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
        private readonly Uri _serverUri;
        private readonly GpsService _gpsService;

        private CancellationTokenSource? _cts;
        private Task? _runTask;

        private ClientWebSocket? _socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private int _currentFrameIndex = 0;
        private int _currentPositionIndex = 0;
        private readonly int _trainId = 1;

        private const bool ForceOutputZero = true;

        public bool IsGpsConnected => _gpsService.IsConnected;
        public double? CurrentLat => _gpsService.CurrentLat;
        public double? CurrentLng => _gpsService.CurrentLng;

        public bool IsConnected =>
            _socket != null && _socket.State == WebSocketState.Open;

        public event Action<string>? LogReceived;
        public event Action<WsControlMessage>? ControlCommandReceived;

        public TrainWebSocketClientService(string serverUrl, string gpsPort, int gpsBaudRate)
        {
            _serverUri = new Uri(serverUrl);
            _gpsService = new GpsService(gpsPort, gpsBaudRate);
            _gpsService.LogReceived += msg => LogReceived?.Invoke(msg);

            TrainDataRepository.Validate();
        }

        public async Task StartAsync()
        {
            if (_cts != null)
                return;

            _cts = new CancellationTokenSource();

            _gpsService.Start();

            _runTask = Task.Run(() => RunClientLoopAsync(_cts.Token));
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_socket != null)
                {
                    try
                    {
                        if (_socket.State == WebSocketState.Open)
                        {
                            await _socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "client stop",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _socket.Dispose();
                    _socket = null;
                }

                if (_runTask != null)
                    await _runTask;

                await _gpsService.StopAsync();

                LogReceived?.Invoke("WebSocket 클라이언트 종료");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RunClientLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var socket = new ClientWebSocket();
                _socket = socket;
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                try
                {
                    LogReceived?.Invoke($"관제 서버 접속 시도: {_serverUri}");
                    await socket.ConnectAsync(_serverUri, token);
                    LogReceived?.Invoke("관제 서버 WebSocket 연결 성공");

                    await SendAsync(new WsHelloMessage
                    {
                        ClientName = Environment.MachineName,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    CancellationToken linkedToken = linkedCts.Token;

                    Task receiveTask = ReceiveLoopAsync(socket, linkedToken);
                    Task telemetryTask = TelemetryLoopAsync(linkedToken);
                    Task positionTask = PositionLoopAsync(linkedToken);
                    Task heartbeatTask = HeartbeatLoopAsync(linkedToken);

                    Task completed = await Task.WhenAny(receiveTask, telemetryTask, positionTask, heartbeatTask);

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
                    LogReceived?.Invoke($"WebSocket 연결/통신 오류: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "reconnect",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                    }

                    _socket = null;
                }

                if (!token.IsCancellationRequested)
                {
                    LogReceived?.Invoke("3초 후 재접속 시도...");
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
                    string type = doc.RootElement.TryGetProperty("type", out JsonElement typeEl)
                        ? typeEl.GetString() ?? ""
                        : "";

                    if (type == "control")
                    {
                        WsControlMessage? cmd = JsonSerializer.Deserialize<WsControlMessage>(json);
                        if (cmd != null)
                        {
                            ControlCommandReceived?.Invoke(cmd);

                            await SendAsync(new WsControlAckMessage
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
                        await SendAsync(new
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
                if (IsConnected)
                {
                    int[] data = GetCurrentFrameAndAdvance();

                    await SendAsync(new WsTelemetryMessage
                    {
                        Data = data,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    if (data.Length > 0)
                    {
                        LogReceived?.Invoke($"telemetry 전송 완료 (첫번째 열차ID={data[0]})");
                    }
                    else
                    {
                        LogReceived?.Invoke("telemetry 전송 완료");
                    }
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task PositionLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsConnected)
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

                    await SendAsync(new WsPositionMessage
                    {
                        Train = _trainId,
                        Lat = lat,
                        Lng = lng,
                        Source = source,
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);

                    LogReceived?.Invoke($"position 전송 완료 train={_trainId}, lat={lat}, lng={lng}, source={source}");
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsConnected)
                {
                    await SendAsync(new WsHeartbeatMessage
                    {
                        Timestamp = DateTime.UtcNow.ToString("O")
                    }, token);
                }

                await Task.Delay(10000, token);
            }
        }

        private async Task SendAsync<T>(T payload, CancellationToken token)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
                return;

            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _sendLock.WaitAsync(token);
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    await _socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        token);
                }
            }
            finally
            {
                _sendLock.Release();
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
                throw new InvalidOperationException($"합쳐진 프레임 길이가 {TrainDataRepository.TotalFrameSize}가 아닙니다. 현재 길이: {combined.Length}");

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