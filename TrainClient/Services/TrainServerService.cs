using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrainClient.Data;
using TrainClient.Models;

namespace TrainClient.Services
{
    public class TrainServerService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly HttpListener _listener;
        private readonly GpsService _gpsService;

        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        private int _currentFrameIndex = 0;
        private int _currentPositionIndex = 0;

        private const bool ForceOutputZero = true;

        public bool IsGpsConnected => _gpsService.IsConnected;
        public double? CurrentLat => _gpsService.CurrentLat;
        public double? CurrentLng => _gpsService.CurrentLng;

        public event Action<string>? LogReceived;

        public TrainServerService(string host, int port, string gpsPort, int gpsBaudRate)
        {
            _host = host;
            _port = port;
            _listener = new HttpListener();
            _gpsService = new GpsService(gpsPort, gpsBaudRate);

            _gpsService.LogReceived += msg => LogReceived?.Invoke(msg);

            TrainDataRepository.Validate();

            string prefix = host == "0.0.0.0"
                ? $"http://+:{port}/"
                : $"http://{host}:{port}/";

            _listener.Prefixes.Add(prefix);
        }

        public async Task StartAsync()
        {
            if (_cts != null)
                return;

            _cts = new CancellationTokenSource();

            _gpsService.Start();
            _listener.Start();

            LogReceived?.Invoke("HTTP 서버 리스너 시작");
            _serverTask = Task.Run(() => ListenLoop(_cts.Token));

            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_listener.IsListening)
                    _listener.Stop();

                if (_serverTask != null)
                    await _serverTask;

                await _gpsService.StopAsync();

                LogReceived?.Invoke("HTTP 서버 리스너 종료");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext? context = null;

                try
                {
                    context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context), token);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"리스너 오류: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            string path = context.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";

            try
            {
                LogReceived?.Invoke($"{context.Request.HttpMethod} {path}");

                switch (path)
                {
                    case "/api/getdata":
                        await HandleGetDataAsync(context);
                        break;

                    case "/api/nextpos":
                        await HandleNextPosAsync(context);
                        break;

                    case "/api/setdata":
                        await HandleSetDataAsync(context);
                        break;

                    default:
                        await WriteJsonAsync(context.Response, 404, new
                        {
                            status = "error",
                            message = "Not Found"
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"요청 처리 오류: {ex.Message}");

                await WriteJsonAsync(context.Response, 500, new
                {
                    status = "error",
                    message = ex.Message
                });
            }
        }

        private async Task HandleGetDataAsync(HttpListenerContext context)
        {
            if (TrainDataRepository.DataA.Count == 0 || TrainDataRepository.DataB.Count == 0)
            {
                await WriteJsonAsync(context.Response, 500, new
                {
                    status = "error",
                    message = "A면/B면 데이터 배열이 비어 있습니다."
                });
                return;
            }

            int[] arr = GetCurrentFrameAndAdvance();
            string argumentStr = string.Join(",", arr);

            await WriteJsonAsync(context.Response, 200, new
            {
                status = "success",
                argument = argumentStr
            });
        }

        private async Task HandleNextPosAsync(HttpListenerContext context)
        {
            if (_gpsService.IsConnected && _gpsService.CurrentLat.HasValue && _gpsService.CurrentLng.HasValue)
            {
                double lat = _gpsService.CurrentLat.Value;
                double lng = _gpsService.CurrentLng.Value;

                LogReceived?.Invoke($"실GPS 반환 lat={lat}, lng={lng}");

                await WriteJsonAsync(context.Response, 200, new
                {
                    lat,
                    lng,
                    source = "real"
                });
            }
            else
            {
                double lat, lng;

                if (_currentPositionIndex < TrainDataRepository.PositionData.Count - 1)
                {
                    lat = TrainDataRepository.PositionData[_currentPositionIndex][0];
                    lng = TrainDataRepository.PositionData[_currentPositionIndex][1];
                    _currentPositionIndex++;
                }
                else
                {
                    var last = TrainDataRepository.PositionData[^1];
                    lat = last[0];
                    lng = last[1];
                }

                LogReceived?.Invoke($"가짜GPS 반환 lat={lat}, lng={lng}");

                await WriteJsonAsync(context.Response, 200, new
                {
                    lat,
                    lng,
                    source = "fake"
                });
            }
        }

        private async Task HandleSetDataAsync(HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 405, new
                {
                    status = "error",
                    message = "Method Not Allowed"
                });
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            SetDataRequest? request = JsonSerializer.Deserialize<SetDataRequest>(body, options);

            if (request == null)
            {
                await WriteJsonAsync(context.Response, 400, new
                {
                    status = "error",
                    message = "잘못된 JSON 요청입니다."
                });
                return;
            }

            LogReceived?.Invoke($"Monitoring System으로부터 수신: status={request.Status}, operation={request.Operation}, value={request.Value}, train={request.Train}");

            if (request.Operation == 1 && request.Value == 1)
                LogReceived?.Invoke($"Train {request.Train}: Forward button pressed");
            else if (request.Operation == 1 && request.Value == 0)
                LogReceived?.Invoke($"Train {request.Train}: Forward button released");

            else if (request.Operation == 2 && request.Value == 1)
                LogReceived?.Invoke($"Train {request.Train}: Backward button pressed");
            else if (request.Operation == 2 && request.Value == 0)
                LogReceived?.Invoke($"Train {request.Train}: Backward button released");

            else if (request.Operation == 3 && request.Value == 1)
                LogReceived?.Invoke($"Train {request.Train}: Brake button pressed");
            else if (request.Operation == 3 && request.Value == 0)
                LogReceived?.Invoke($"Train {request.Train}: Brake button released");

            else if (request.Operation == 4 && request.Value == 1)
                LogReceived?.Invoke($"Train {request.Train}: Emergency button pressed");
            else if (request.Operation == 4 && request.Value == 0)
                LogReceived?.Invoke($"Train {request.Train}: Emergency button released");

            else
                LogReceived?.Invoke($"Train {request.Train}: Unknown event (operation={request.Operation}, value={request.Value})");

            await WriteJsonAsync(context.Response, 200, new
            {
                status = "success",
                message = "Data received successfully"
            });
        }

        private int[] BuildCombinedFrame(int frameIndex)
        {
            int[] aFrame = (int[])TrainDataRepository.DataA[frameIndex].Clone();
            int[] bFrame = (int[])TrainDataRepository.DataB[frameIndex].Clone();

            int[] combined = new int[TrainDataRepository.TotalFrameSize];

            Array.Copy(aFrame, 0, combined, 0, TrainDataRepository.FrameSizePerSide);
            Array.Copy(bFrame, 0, combined, TrainDataRepository.FrameSizePerSide, TrainDataRepository.FrameSizePerSide);

            if (combined.Length != TrainDataRepository.TotalFrameSize)
                throw new InvalidOperationException($"합쳐진 프레임 길이가 94가 아닙니다. 현재 길이: {combined.Length}");

            if (ForceOutputZero)
            {
                // A면 M300~M308 => 38~46
                for (int i = 38; i <= 46; i++)
                    combined[i] = 0;

                // B면 M300~M308 => 85~93
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

        private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            string json = JsonSerializer.Serialize(data, jsonOptions);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }
}