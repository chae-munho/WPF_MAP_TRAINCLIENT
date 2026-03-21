using System;
using System.Windows;
using System.Windows.Threading;
using TrainClient.Models;
using TrainClient.Services;

namespace TrainClient
{
    public partial class MainWindow : Window
    {
        private TrainWebSocketClientService? _clientService;
        private DispatcherTimer? _uiTimer;

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
                txtGpsStatus.Text = "미연결";
                txtLat.Text = "-";
                txtLng.Text = "-";
                return;
            }

            txtWsStatus.Text = _clientService.IsConnected ? "연결됨" : "미연결";
            txtGpsStatus.Text = _clientService.IsGpsConnected ? "연결됨" : "미연결";
            txtLat.Text = _clientService.CurrentLat?.ToString("F10") ?? "-";
            txtLng.Text = _clientService.CurrentLng?.ToString("F10") ?? "-";
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_clientService != null)
                {
                    AppendLog("이미 실행 중입니다.");
                    return;
                }

                string wsUrl = txtServerUrl.Text.Trim();
                string gpsPort = txtGpsPort.Text.Trim();
                int baudRate = int.Parse(txtBaudRate.Text.Trim());

                _clientService = new TrainWebSocketClientService(wsUrl, gpsPort, baudRate);
                _clientService.LogReceived += AppendLog;
                _clientService.ControlCommandReceived += OnControlCommandReceived;

                await _clientService.StartAsync();

                AppendLog($"관제 접속 시작: {wsUrl}");
            }
            catch (Exception ex)
            {
                AppendLog($"시작 실패: {ex.Message}");
                MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);

                if (_clientService != null)
                {
                    _clientService.LogReceived -= AppendLog;
                    _clientService.ControlCommandReceived -= OnControlCommandReceived;
                    _clientService = null;
                }
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_clientService == null)
                {
                    AppendLog("실행 중인 클라이언트가 없습니다.");
                    return;
                }

                await _clientService.StopAsync();
                _clientService.LogReceived -= AppendLog;
                _clientService.ControlCommandReceived -= OnControlCommandReceived;
                _clientService = null;

                AppendLog("연결 종료 완료");
            }
            catch (Exception ex)
            {
                AppendLog($"종료 실패: {ex.Message}");
            }
        }

        private void OnControlCommandReceived(WsControlMessage cmd)
        {
            Dispatcher.Invoke(() =>
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
            });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }

        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                if (_clientService != null)
                {
                    await _clientService.StopAsync();
                }
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}