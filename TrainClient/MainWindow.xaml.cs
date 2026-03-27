using System;
using System.Threading.Tasks;
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
                string gpsPort = txtGpsPort.Text.Trim();
                int baudRate = int.Parse(txtBaudRate.Text.Trim());

                if (_clientService == null)
                {
                    _clientService = new TrainWebSocketClientService(wsUrl, gpsPort, baudRate);
                    _clientService.LogReceived += AppendLog;
                    _clientService.ControlCommandReceived += OnControlCommandReceived;
                }

                if (_clientService.IsConnected)
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

                if (!_clientService.IsConnected)
                {
                    AppendLog("현재 연결되어 있지 않습니다.");
                    return;
                }

                await _clientService.StopAsync();

                AppendLog("연결 종료 완료 (진행 상태 유지)");
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
                    _clientService.LogReceived -= AppendLog;
                    _clientService.ControlCommandReceived -= OnControlCommandReceived;
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