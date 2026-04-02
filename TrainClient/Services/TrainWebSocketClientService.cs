using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrainClient.Data;
using TrainClient.Models;

namespace TrainClient.Services
{
    public class TrainWebSocketClientService
    {
        private readonly Uri _mainServerUri;
        private readonly Uri _videoServerUri;
        private readonly GpsService _gpsService;

        private CancellationTokenSource? _cts;
        private Task? _mainRunTask;
        private Task? _videoRunTask;

        private ClientWebSocket? _mainSocket;
        private ClientWebSocket? _videoSocket;

        private readonly SemaphoreSlim _mainSendLock = new(1, 1);
        private readonly SemaphoreSlim _videoSendLock = new(1, 1);

        private int _currentFrameIndex = 0;
        private int _currentPositionIndex = 0;
        private readonly int _trainId = 1;

        private const bool ForceOutputZero = true;

        private int _videoFrameSending = 0;

        private readonly VideoStreamingService _videoStreamingService = new();

        public bool IsGpsConnected => _gpsService.IsConnected;
        public double? CurrentLat => _gpsService.CurrentLat;
        public double? CurrentLng => _gpsService.CurrentLng;

        public bool IsMainConnected =>
            _mainSocket != null && _mainSocket.State == WebSocketState.Open;

        public bool IsVideoConnected =>
            _videoSocket != null && _videoSocket.State == WebSocketState.Open;

        public bool IsConnected => IsMainConnected;

        public int CurrentFrameIndex => _currentFrameIndex;
        public int CurrentPositionIndex => _currentPositionIndex;

        public event Action<string>? LogReceived;
        public event Action<WsControlMessage>? ControlCommandReceived;
        public event Action<int[]>? TelemetryReceived;

        public TrainWebSocketClientService(string serverUrl, string videoServerUrl, string gpsPort, int gpsBaudRate)
        {
            _mainServerUri = new Uri(serverUrl);
            _videoServerUri = new Uri(videoServerUrl);
            _gpsService = new GpsService(gpsPort, gpsBaudRate);
            _gpsService.LogReceived += msg => LogReceived?.Invoke(msg);

            _videoStreamingService.LogReceived += msg => LogReceived?.Invoke(msg);
            _videoStreamingService.FrameReady += OnVideoFrameReady;

            TrainDataRepository.Validate();
        }

        public async Task StartAsync(bool resetProgress)
        {
            if (_cts != null)
                return;

            if (resetProgress)
            {
                ResetProgress();
                LogReceived?.Invoke("전송 위치를 처음으로 초기화했습니다.");
            }
            else
            {
                LogReceived?.Invoke($"이전 중단 시점부터 이어서 시작합니다. frame={_currentFrameIndex}, position={_currentPositionIndex}");
            }

            _cts = new CancellationTokenSource();

            _gpsService.Start();

            _mainRunTask = Task.Run(() => RunMainSocketLoopAsync(_cts.Token));
            _videoRunTask = Task.Run(() => RunVideoSocketLoopAsync(_cts.Token));

            await Task.CompletedTask;
        }

        public void ResetProgress()
        {
            _currentFrameIndex = 0;
            _currentPositionIndex = 0;
        }

        public async Task StopAsync()
        {
            try
            {
                StopVideoStreaming();

                _cts?.Cancel();

                if (_mainSocket != null)
                {
                    try
                    {
                        if (_mainSocket.State == WebSocketState.Open)
                        {
                            await _mainSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "client stop",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _mainSocket.Dispose();
                    _mainSocket = null;
                }

                if (_videoSocket != null)
                {
                    try
                    {
                        if (_videoSocket.State == WebSocketState.Open)
                        {
                            await _videoSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "client stop",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _videoSocket.Dispose();
                    _videoSocket = null;
                }

                if (_mainRunTask != null)
                    await _mainRunTask;

                if (_videoRunTask != null)
                    await _videoRunTask;

                await _gpsService.StopAsync();

                LogReceived?.Invoke($"WebSocket 클라이언트 종료 (진행상태 유지: frame={_currentFrameIndex}, position={_currentPositionIndex})");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _mainRunTask = null;
                _videoRunTask = null;
            }
        }

        public void StartVideoStreaming(int carNo)
        {
            try
            {
                string rtspUrl = CameraRouteService.GetRtspUrlByCarNo(carNo);

                if (string.IsNullOrWhiteSpace(rtspUrl))
                {
                    LogReceived?.Invoke($"영상 스트리밍 시작 실패: 객차 {carNo} RTSP 주소가 비어 있습니다.");
                    return;
                }

                if (_videoStreamingService.IsStreaming &&
                    _videoStreamingService.CurrentTrain == _trainId &&
                    _videoStreamingService.CurrentCarNo == carNo)
                {
                    return;
                }

                _videoStreamingService.Start(_trainId, carNo, rtspUrl);
                LogReceived?.Invoke($"영상 스트리밍 요청: train={_trainId}, car={carNo}");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"영상 스트리밍 시작 실패: {ex.Message}");
            }
        }

        public void StopVideoStreaming()
        {
            int stoppedTrain = _videoStreamingService.CurrentTrain;

            _videoStreamingService.Stop();

            _ = SafeSendVideoStopAsync(stoppedTrain <= 0 ? _trainId : stoppedTrain);
        }

        private async Task SafeSendVideoStopAsync(int trainNo)
        {
            try
            {
                await SendVideoAsync(new WsVideoStopMessage
                {
                    Train = trainNo,
                    Timestamp = DateTime.UtcNow.ToString("O")
                }, CancellationToken.None);
            }
            catch
            {
            }
        }

        private void OnVideoFrameReady(WsVideoFrameMessage frame)
        {
            _ = SafeSendVideoFrameAsync(frame);
        }

        private async Task SafeSendVideoFrameAsync(WsVideoFrameMessage frame)
        {
            if (Interlocked.Exchange(ref _videoFrameSending, 1) == 1)
                return;

            try
            {
                await SendVideoAsync(frame, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"video_frame 전송 실패: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _videoFrameSending, 0);
            }
        }

        private async Task RunMainSocketLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var mainSocket = new ClientWebSocket();
                _mainSocket = mainSocket;
                mainSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                try
                {
                    LogReceived?.Invoke($"관제 서버(일반) 접속 시도: {_mainServerUri}");
                    await mainSocket.ConnectAsync(_mainServerUri, token);
                    LogReceived?.Invoke("관제 서버(일반) WebSocket 연결 성공");

                    await SendMainAsync(new WsHelloMessage
                    {
                        Train = _trainId,
                        ClientName = Environment.MachineName,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    CancellationToken linkedToken = linkedCts.Token;

                    Task receiveTask = ReceiveLoopAsync(mainSocket, linkedToken);
                    Task telemetryTask = TelemetryLoopAsync(linkedToken);
                    Task positionTask = PositionLoopAsync(linkedToken);
                    Task heartbeatTask = HeartbeatLoopAsync(linkedToken);

                    _ = await Task.WhenAny(receiveTask, telemetryTask, positionTask, heartbeatTask);

                    linkedCts.Cancel();

                    await SafeAwait(receiveTask);
                    await SafeAwait(telemetryTask);
                    await SafeAwait(positionTask);
                    await SafeAwait(heartbeatTask);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"일반 WebSocket 연결/통신 오류: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (mainSocket.State == WebSocketState.Open)
                        {
                            await mainSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "reconnect",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _mainSocket = null;
                }

                if (!token.IsCancellationRequested)
                {
                    LogReceived?.Invoke("일반 WS 3초 후 재접속 시도...");
                    try
                    {
                        await Task.Delay(3000, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private async Task RunVideoSocketLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var videoSocket = new ClientWebSocket();
                _videoSocket = videoSocket;
                videoSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                try
                {
                    LogReceived?.Invoke($"관제 서버(영상) 접속 시도: {_videoServerUri}");
                    await videoSocket.ConnectAsync(_videoServerUri, token);
                    LogReceived?.Invoke("관제 서버(영상) WebSocket 연결 성공");

                    await SendVideoAsync(new WsHelloMessage
                    {
                        Train = _trainId,
                        ClientName = Environment.MachineName,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    while (!token.IsCancellationRequested && videoSocket.State == WebSocketState.Open)
                    {
                        await Task.Delay(1000, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"영상 WebSocket 연결/통신 오류: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (videoSocket.State == WebSocketState.Open)
                        {
                            await videoSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "reconnect",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _videoSocket = null;
                }

                if (!token.IsCancellationRequested)
                {
                    LogReceived?.Invoke("영상 WS 3초 후 재접속 시도...");
                    try
                    {
                        await Task.Delay(3000, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string json = await ReceiveTextMessageAsync(socket, buffer, token);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                LogReceived?.Invoke($"수신: {json}");

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);

                    string type = "";
                    if (doc.RootElement.TryGetProperty("type", out JsonElement typeEl) ||
                        doc.RootElement.TryGetProperty("Type", out typeEl))
                    {
                        type = typeEl.GetString() ?? "";
                    }

                    if (type == "control")
                    {
                        WsControlMessage? cmd = JsonSerializer.Deserialize<WsControlMessage>(json);
                        if (cmd != null)
                        {
                            ControlCommandReceived?.Invoke(cmd);

                            await SendMainAsync(new WsControlAckMessage
                            {
                                Train = cmd.Train,
                                Operation = cmd.Operation,
                                Value = cmd.Value,
                                CommandId = cmd.CommandId,
                                Result = "ok",
                                Timestamp = DateTime.UtcNow.ToString("O")
                            }, token);
                        }
                    }
                    else if (type == "ping")
                    {
                        await SendMainAsync(new
                        {
                            type = "pong",
                            timestamp = DateTime.UtcNow.ToString("O")
                        }, token);
                    }
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"수신 메시지 처리 실패: {ex.Message}");
                }
            }
        }

        private async Task TelemetryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsMainConnected)
                {
                    int[] data = GetCurrentFrameAndAdvance();

                    await SendMainAsync(new WsTelemetryMessage
                    {
                        Train = _trainId,
                        Data = data,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    TelemetryReceived?.Invoke((int[])data.Clone());

                    if (data.Length > 0)
                        LogReceived?.Invoke($"telemetry 전송 완료 (첫번째 열차ID={data[0]}, 현재 frame={_currentFrameIndex})");
                    else
                        LogReceived?.Invoke("telemetry 전송 완료");
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task PositionLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsMainConnected)
                {
                    double lat;
                    double lng;
                    string source;

                    if (_gpsService.IsConnected &&
                        _gpsService.CurrentLat.HasValue &&
                        _gpsService.CurrentLng.HasValue)
                    {
                        lat = _gpsService.CurrentLat.Value;
                        lng = _gpsService.CurrentLng.Value;
                        source = "real";
                    }
                    else
                    {
                        (lat, lng) = GetFakePositionAndAdvance();
                        source = "fake";
                    }

                    await SendMainAsync(new WsPositionMessage
                    {
                        Train = _trainId,
                        Lat = lat,
                        Lng = lng,
                        Source = source,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    LogReceived?.Invoke($"position 전송 완료 train={_trainId}, lat={lat}, lng={lng}, source={source}, 현재 position={_currentPositionIndex}");
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsMainConnected)
                {
                    await SendMainAsync(new WsHeartbeatMessage
                    {
                        Train = _trainId,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);
                }

                await Task.Delay(10000, token);
            }
        }

        private async Task SendMainAsync<T>(T payload, CancellationToken token)
        {
            if (_mainSocket == null || _mainSocket.State != WebSocketState.Open)
                return;

            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _mainSendLock.WaitAsync(token);
            try
            {
                if (_mainSocket != null && _mainSocket.State == WebSocketState.Open)
                {
                    await _mainSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        token);
                }
            }
            finally
            {
                _mainSendLock.Release();
            }
        }

        private async Task SendVideoAsync<T>(T payload, CancellationToken token)
        {
            if (_videoSocket == null || _videoSocket.State != WebSocketState.Open)
                return;

            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _videoSendLock.WaitAsync(token);
            try
            {
                if (_videoSocket != null && _videoSocket.State == WebSocketState.Open)
                {
                    await _videoSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        token);
                }
            }
            finally
            {
                _videoSendLock.Release();
            }
        }

        private static async Task<string> ReceiveTextMessageAsync(
            ClientWebSocket socket,
            byte[] buffer,
            CancellationToken token)
        {
            using MemoryStream ms = new();

            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return string.Empty;

                ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    break;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private (double lat, double lng) GetFakePositionAndAdvance()
        {
            if (TrainDataRepository.PositionData.Count == 0)
                return (0, 0);

            double lat;
            double lng;

            if (_currentPositionIndex < TrainDataRepository.PositionData.Count - 1)
            {
                lat = TrainDataRepository.PositionData[_currentPositionIndex][0];
                lng = TrainDataRepository.PositionData[_currentPositionIndex][1];
                _currentPositionIndex++;
            }
            else
            {
                double[] last = TrainDataRepository.PositionData[^1];
                lat = last[0];
                lng = last[1];
            }

            return (lat, lng);
        }

        private int[] BuildCombinedFrame(int frameIndex)
        {
            int[] aFrame = (int[])TrainDataRepository.DataA[frameIndex].Clone();
            int[] bFrame = (int[])TrainDataRepository.DataB[frameIndex].Clone();

            int[] combined = new int[TrainDataRepository.TotalFrameSize];

            Array.Copy(aFrame, 0, combined, 0, TrainDataRepository.FrameSizePerSide);
            Array.Copy(bFrame, 0, combined, TrainDataRepository.FrameSizePerSide, TrainDataRepository.FrameSizePerSide);

            if (combined.Length != TrainDataRepository.TotalFrameSize)
            {
                throw new InvalidOperationException(
                    $"합쳐진 프레임 길이가 {TrainDataRepository.TotalFrameSize}가 아닙니다. 현재 길이: {combined.Length}");
            }

            if (ForceOutputZero)
            {
                for (int i = 38; i <= 46; i++)
                    combined[i] = 0;

                for (int i = 85; i <= 93; i++)
                    combined[i] = 0;
            }

            return combined;
        }

        private int[] GetCurrentFrameAndAdvance()
        {
            int[] frame = BuildCombinedFrame(_currentFrameIndex);

            if (_currentFrameIndex < TrainDataRepository.DataA.Count - 1)
                _currentFrameIndex++;

            return frame;
        }

        private static async Task SafeAwait(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }
    }
}