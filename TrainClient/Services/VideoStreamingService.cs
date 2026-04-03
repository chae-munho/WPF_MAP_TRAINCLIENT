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
        public event Action<WsVideoFrameMessage>? FrameReady;

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
                _streamTask?.Wait(1000);
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

            try
            {
                using var capture = new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);

                if (!capture.IsOpened())
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] RTSP open failed: {rtspUrl}");
                    
                    LogReceived?.Invoke($"RTSP 연결 실패: train={trainNo}, car={carNo}, url={rtspUrl}");
                    return;
                }

                IsStreaming = true;
                LogReceived?.Invoke($"영상 스트리밍 시작: train={trainNo}, car={carNo}");

                using var frame = new Mat();
                using var resized = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        LogReceived?.Invoke($"프레임 읽기 실패: train={trainNo}, car={carNo}");
                        Thread.Sleep(30);
                        continue;
                    }

                    Cv2.Resize(frame, resized, new OpenCvSharp.Size(400, 225));

                    int[] jpegParams =
                    {
                        (int)ImwriteFlags.JpegQuality, 60
                    };

                    byte[] jpegBytes = resized.ToBytes(".jpg", jpegParams);
                    string base64 = Convert.ToBase64String(jpegBytes);

                    FrameReady?.Invoke(new WsVideoFrameMessage
                    {
                        Train = trainNo,
                        CarNo = carNo,
                        ImageBase64 = base64,
                        Width = resized.Width,
                        Height = resized.Height,
                        Format = "jpeg",
                        Timestamp = DateTime.UtcNow.ToString("O")
                    });

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
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