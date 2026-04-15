using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using TrainClient.Models;

namespace TrainClient.Services
{
    public class VideoStreamingService : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _streamTask;

        public bool IsStreaming { get; private set; }
        public int CurrentTrain { get; private set; }
        public int CurrentCarNo { get; private set; }

        public event Action<string>? LogReceived;
        public event Action<VideoFramePacket>? FrameReady;

        public void Start(int trainNo, int carNo, string rtspUrl)
        {
            System.Diagnostics.Debug.WriteLine($"[VIDEO] Start train={trainNo}, car={carNo}, url={rtspUrl}");

            Stop();

            if (string.IsNullOrWhiteSpace(rtspUrl))
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] empty url");
                LogReceived?.Invoke("영상 스트리밍 시작 실패: RTSP URL이 비어 있습니다.");
                return;
            }

            CurrentTrain = trainNo;
            CurrentCarNo = carNo;

            _cts = new CancellationTokenSource();
            _streamTask = Task.Run(() => StreamLoop(trainNo, carNo, rtspUrl, _cts.Token));
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _streamTask?.Wait(1500);
            }
            catch
            {
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _streamTask = null;
                IsStreaming = false;
                CurrentTrain = 0;
                CurrentCarNo = 0;
            }
        }

        private void StreamLoop(int trainNo, int carNo, string rtspUrl, CancellationToken token)
        {
            System.Diagnostics.Debug.WriteLine($"[VIDEO] StreamLoop begin train={trainNo}, car={carNo}");

            const int maxConsecutiveReadFails = 10;
            const int readFailDelayMs = 100;
            const int reconnectDelayMs = 1000;
            const int frameIntervalMs = 100;

            bool wasStreamingLogged = false;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var capture = new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);

                        if (!capture.IsOpened())
                        {
                            IsStreaming = false;

                            LogReceived?.Invoke($"RTSP 연결 실패: train={trainNo}, car={carNo}, url={rtspUrl}");

                            if (token.IsCancellationRequested)
                                break;

                            Thread.Sleep(reconnectDelayMs);
                            continue;
                        }

                        if (!wasStreamingLogged)
                        {
                            LogReceived?.Invoke($"영상 스트리밍 시작: train={trainNo}, car={carNo}");
                            wasStreamingLogged = true;
                        }
                        else
                        {
                            LogReceived?.Invoke($"영상 스트리밍 재연결 성공: train={trainNo}, car={carNo}");
                        }

                        IsStreaming = true;

                        using var frame = new Mat();
                        using var resized = new Mat();

                        int consecutiveFailCount = 0;

                        while (!token.IsCancellationRequested)
                        {
                            bool ok = capture.Read(frame);

                            if (!ok || frame.Empty())
                            {
                                consecutiveFailCount++;

                                if (consecutiveFailCount == 1 || consecutiveFailCount % 5 == 0)
                                {
                                    LogReceived?.Invoke(
                                        $"프레임 읽기 실패: train={trainNo}, car={carNo}, fail={consecutiveFailCount}");
                                }

                                if (consecutiveFailCount >= maxConsecutiveReadFails)
                                {
                                    LogReceived?.Invoke(
                                        $"프레임 연속 실패로 재연결 시도: train={trainNo}, car={carNo}, fail={consecutiveFailCount}");
                                    break;
                                }

                                Thread.Sleep(readFailDelayMs);
                                continue;
                            }

                            consecutiveFailCount = 0;

                            Cv2.Resize(frame, resized, new OpenCvSharp.Size(400, 225));

                            int[] jpegParams =
                            {
                                (int)ImwriteFlags.JpegQuality, 60
                            };

                            byte[] jpegBytes = resized.ToBytes(".jpg", jpegParams);

                            FrameReady?.Invoke(new VideoFramePacket
                            {
                                Train = trainNo,
                                CarNo = carNo,
                                Width = resized.Width,
                                Height = resized.Height,
                                TimestampTicksUtc = DateTime.UtcNow.Ticks,
                                JpegBytes = jpegBytes
                            });

                            Thread.Sleep(frameIntervalMs);
                        }

                        IsStreaming = false;

                        try
                        {
                            capture.Release();
                        }
                        catch
                        {
                        }

                        if (token.IsCancellationRequested)
                            break;

                        Thread.Sleep(reconnectDelayMs);
                    }
                    catch (Exception ex)
                    {
                        IsStreaming = false;
                        LogReceived?.Invoke($"영상 스트리밍 루프 오류: train={trainNo}, car={carNo}, error={ex.Message}");

                        if (token.IsCancellationRequested)
                            break;

                        Thread.Sleep(reconnectDelayMs);
                    }
                }
            }
            catch (Exception ex)
            {
                IsStreaming = false;
                LogReceived?.Invoke($"영상 스트리밍 오류: {ex.Message}");
            }
            finally
            {
                IsStreaming = false;
                LogReceived?.Invoke($"영상 스트리밍 종료: train={trainNo}, car={carNo}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}