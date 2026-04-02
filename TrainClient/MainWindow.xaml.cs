using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TrainClient.Models;
using TrainClient.Popups;
using TrainClient.Services;

namespace TrainClient
{
    public partial class MainWindow : Window
    {
        private TrainWebSocketClientService? _clientService;
        private DispatcherTimer? _uiTimer;

        private readonly Dictionary<int, CameraPopup> _cameraPopups = new();
        private int _previousActiveIntercomCar = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitializeUiTimer();
        }

        private void InitializeUiTimer()
        {
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_clientService == null)
            {
                txtWsStatus.Text = "미연결";
                txtVideoWsStatus.Text = "미연결";
                txtGpsStatus.Text = "미연결";
                txtLat.Text = "-";
                txtLng.Text = "-";
                return;
            }

            txtWsStatus.Text = _clientService.IsMainConnected ? "연결됨" : "미연결";
            txtVideoWsStatus.Text = _clientService.IsVideoConnected ? "연결됨" : "미연결";
            txtGpsStatus.Text = _clientService.IsGpsConnected ? "연결됨" : "미연결";
            txtLat.Text = _clientService.CurrentLat?.ToString("F10") ?? "-";
            txtLng.Text = _clientService.CurrentLng?.ToString("F10") ?? "-";
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            await StartClientAsync(resetProgress: true);
        }

        private async void btnResumeConnect_Click(object sender, RoutedEventArgs e)
        {
            await StartClientAsync(resetProgress: false);
        }

        private async Task StartClientAsync(bool resetProgress)
        {
            try
            {
                string wsUrl = txtServerUrl.Text.Trim();
                string videoWsUrl = txtVideoServerUrl.Text.Trim();
                string gpsPort = txtGpsPort.Text.Trim();
                int baudRate = int.Parse(txtBaudRate.Text.Trim());

                // 기존 로직 유지:
                // 서비스가 없을 때만 생성해야 진행 상태(frame/position)가 유지됨
                if (_clientService == null)
                {
                    _clientService = new TrainWebSocketClientService(wsUrl, videoWsUrl, gpsPort, baudRate);
                    _clientService.LogReceived += AppendLog;
                    _clientService.ControlCommandReceived += OnControlCommandReceived;
                    _clientService.TelemetryReceived += OnTelemetryReceived;
                }

                if (_clientService.IsMainConnected)
                {
                    AppendLog("이미 관제 서버에 연결되어 있습니다.");
                    return;
                }

                await _clientService.StartAsync(resetProgress);

                if (resetProgress)
                    AppendLog("관제 접속 시작 (처음부터)");
                else
                    AppendLog("관제 접속 시작 (중단 지점부터 이어서)");
            }
            catch (Exception ex)
            {
                AppendLog($"시작 실패: {ex.Message}");
                MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_clientService == null)
                {
                    AppendLog("클라이언트가 아직 생성되지 않았습니다.");
                    return;
                }

                if (!_clientService.IsMainConnected && !_clientService.IsVideoConnected)
                {
                    AppendLog("현재 연결되어 있지 않습니다.");
                    return;
                }

                await _clientService.StopAsync();

                CloseAllCameraPopups();

                AppendLog("연결 종료 완료 (진행 상태 유지)");
            }
            catch (Exception ex)
            {
                AppendLog($"종료 실패: {ex.Message}");
            }
        }

        private void OnControlCommandReceived(WsControlMessage cmd)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string text = $"제어명령 수신: train={cmd.Train}, op={cmd.Operation}, value={cmd.Value}";
                AppendLog(text);

                if (cmd.Operation == 1 && cmd.Value == 1)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 가속 버튼 누름";
                else if (cmd.Operation == 1 && cmd.Value == 0)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 가속 버튼 뗌";
                else if (cmd.Operation == 2 && cmd.Value == 1)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 감속 버튼 누름";
                else if (cmd.Operation == 2 && cmd.Value == 0)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 감속 버튼 뗌";
                else if (cmd.Operation == 3 && cmd.Value == 1)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 정지 버튼 누름";
                else if (cmd.Operation == 3 && cmd.Value == 0)
                    txtLastCommand.Text = $"열차 {cmd.Train}: 정지 버튼 뗌";
                else
                    txtLastCommand.Text = $"열차 {cmd.Train}: 알 수 없는 명령";
            }));
        }

        private void OnTelemetryReceived(int[] data)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    int activeCarNo = GetActiveIntercomCarNo(data);
                    HandleActiveIntercomTransition(activeCarNo);
                }
                catch (Exception ex)
                {
                    AppendLog($"인터컴 처리 실패: {ex.Message}");
                }
            }));
        }

        private int GetActiveIntercomCarNo(int[] data)
        {
            int aIntercomCar = GetSafeValue(data, 12);
            int aMaster = GetSafeValue(data, 14);

            int bIntercomCar = GetSafeValue(data, 59);
            int bMaster = GetSafeValue(data, 61);

            if (aMaster == 1)
                return aIntercomCar;

            if (aMaster == 0 && bMaster == 1)
                return bIntercomCar;

            return 0;
        }

        private void HandleActiveIntercomTransition(int currentCarNo)
        {
            if (currentCarNo <= 0)
                return;

            if (_previousActiveIntercomCar == 0)
            {
                AppendLog($"인터컴 호출 감지: {currentCarNo}번 객차");
                ShowNewCameraPopup(currentCarNo);
                _previousActiveIntercomCar = currentCarNo;
                return;
            }

            if (currentCarNo != _previousActiveIntercomCar)
            {
                AppendLog($"인터컴 호출 추가 감지: {_previousActiveIntercomCar}번 이후 {currentCarNo}번 객차");
                ShowNewCameraPopup(currentCarNo);
                _previousActiveIntercomCar = currentCarNo;
            }
        }

        private void ShowNewCameraPopup(int carNo)
        {
            try
            {
                if (_cameraPopups.TryGetValue(carNo, out CameraPopup? existingPopup))
                {
                    if (existingPopup.IsVisible)
                    {
                        existingPopup.Activate();
                        existingPopup.Topmost = true;
                        existingPopup.Topmost = false;
                        existingPopup.Focus();
                        return;
                    }

                    _cameraPopups.Remove(carNo);
                }

                var popup = new CameraPopup
                {
                    Owner = this
                };

                popup.Closed += (_, _) =>
                {
                    if (_cameraPopups.ContainsKey(carNo) && ReferenceEquals(_cameraPopups[carNo], popup))
                        _cameraPopups.Remove(carNo);
                };

                _cameraPopups[carNo] = popup;
                popup.ShowIntercom(carNo);
                popup.Show();
                popup.Activate();

                _clientService?.StartVideoStreaming(carNo);
            }
            catch (Exception ex)
            {
                AppendLog($"카메라 팝업 표시 실패: {ex.Message}");
            }
        }

        private void CloseAllCameraPopups()
        {
            try
            {
                var popups = new List<CameraPopup>(_cameraPopups.Values);
                _cameraPopups.Clear();

                foreach (var popup in popups)
                {
                    try
                    {
                        popup.Close();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static int GetSafeValue(int[] data, int index)
        {
            if (data == null || index < 0 || index >= data.Length)
                return 0;

            return data[index];
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }

        private void AppendLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            }));
        }

        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                CloseAllCameraPopups();

                if (_clientService != null)
                {
                    _clientService.LogReceived -= AppendLog;
                    _clientService.ControlCommandReceived -= OnControlCommandReceived;
                    _clientService.TelemetryReceived -= OnTelemetryReceived;

                    await _clientService.StopAsync();
                    _clientService = null;
                }
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}