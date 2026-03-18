using System;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace TrainClient.Services
{
    public class GpsService
    {
        private readonly string _portName;
        private readonly int _baudRate;

        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private Task? _readTask;

        public bool IsConnected { get; private set; }
        public double? CurrentLat { get; private set; }
        public double? CurrentLng { get; private set; }

        public event Action<string>? LogReceived;

        public GpsService(string portName, int baudRate)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        public void Start()
        {
            if (_cts != null)
                return;

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_cts.Token));
        }

        public async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_readTask != null)
                    await _readTask;

                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();

                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch
            {
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsConnected = false;
            }
        }

        private void ReadLoop(CancellationToken token)
        {
            try
            {
                _serialPort = new SerialPort(_portName, _baudRate)
                {
                    ReadTimeout = 1000,
                    NewLine = "\r\n"
                };

                _serialPort.Open();
                IsConnected = true;
                LogReceived?.Invoke($"NK-GPS-U 연결 성공 ({_portName}, {_baudRate})");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string line = _serialPort.ReadLine();

                        if (line.StartsWith("$GPGGA", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("$GNGGA", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseGpgga(line);
                        }
                    }
                    catch (TimeoutException)
                    {
                    }
                    catch (Exception ex)
                    {
                        LogReceived?.Invoke($"GPS 읽기 오류: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                LogReceived?.Invoke($"GPS 시리얼 연결 실패: {ex.Message}");
            }
        }

        private void ParseGpgga(string sentence)
        {
            try
            {
                string[] parts = sentence.Split(',');

                if (parts.Length < 6)
                    return;

                string rawLat = parts[2];
                string latDir = parts[3];
                string rawLng = parts[4];
                string lngDir = parts[5];

                if (string.IsNullOrWhiteSpace(rawLat) || string.IsNullOrWhiteSpace(rawLng))
                    return;

                double lat = ConvertNmeaToDecimal(rawLat, latDir, true);
                double lng = ConvertNmeaToDecimal(rawLng, lngDir, false);

                CurrentLat = lat;
                CurrentLng = lng;
            }
            catch
            {
            }
        }

        private static double ConvertNmeaToDecimal(string value, string direction, bool isLatitude)
        {
            double raw = double.Parse(value, CultureInfo.InvariantCulture);

            int degreeDigits = isLatitude ? 2 : 3;
            double degrees = Math.Floor(raw / Math.Pow(10, value.Length - degreeDigits - 3));

            // 더 안전한 방식으로 분리
            string rawString = raw.ToString("F10", CultureInfo.InvariantCulture);
            int dotIndex = rawString.IndexOf('.', StringComparison.Ordinal);
            string integerPart = dotIndex >= 0 ? rawString[..dotIndex] : rawString;

            int degLen = isLatitude ? 2 : 3;
            if (integerPart.Length < degLen)
                throw new FormatException("NMEA 좌표 형식 오류");

            double deg = double.Parse(integerPart[..degLen], CultureInfo.InvariantCulture);
            double min = double.Parse(rawString[degLen..], CultureInfo.InvariantCulture);

            double result = deg + (min / 60.0);

            if (direction.Equals("S", StringComparison.OrdinalIgnoreCase) ||
                direction.Equals("W", StringComparison.OrdinalIgnoreCase))
            {
                result *= -1;
            }

            return result;
        }
    }
}