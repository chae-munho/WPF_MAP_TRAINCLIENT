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
            Stop();

            if (string.IsNullOrWhiteSpace(rtspUrl))
            {
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
                _streamTask?.Wait(500);
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
            try
            {
                using var capture = new VideoCapture(rtspUrl);

                if (!capture.IsOpened())
                {
                    LogReceived?.Invoke($"RTSP 연결 실패: {rtspUrl}");
                    return;
                }

                IsStreaming = true;
                LogReceived?.Invoke($"영상 스트리밍 시작: train={trainNo}, car={carNo}");

                using var frame = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    Cv2.Resize(frame, frame, new OpenCvSharp.Size(640, 360));

                    byte[] jpegBytes = frame.ToBytes(".jpg");
                    string base64 = Convert.ToBase64String(jpegBytes);

                    FrameReady?.Invoke(new WsVideoFrameMessage
                    {
                        Train = trainNo,
                        CarNo = carNo,
                        ImageBase64 = base64,
                        Width = frame.Width,
                        Height = frame.Height,
                        Format = "jpeg",
                        Timestamp = DateTime.UtcNow.ToString("O")
                    });

                    Thread.Sleep(150);
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"영상 스트리밍 오류: {ex.Message}");
            }
            finally
            {
                IsStreaming = false;
                LogReceived?.Invoke("영상 스트리밍 종료");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}