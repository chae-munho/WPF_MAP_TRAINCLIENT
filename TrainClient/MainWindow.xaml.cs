using System;
using System.Windows;
using System.Windows.Threading;
using TrainClient.Services;

namespace TrainClient
{
    public partial class MainWindow : Window
    {
        private TrainServerService? _serverService;
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
            if (_serverService == null)
            {
                txtGpsStatus.Text = "미연결";
                txtLat.Text = "-";
                txtLng.Text = "-";
                return;
            }

            txtGpsStatus.Text = _serverService.IsGpsConnected ? "연결됨" : "미연결";
            txtLat.Text = _serverService.CurrentLat?.ToString("F10") ?? "-";
            txtLng.Text = _serverService.CurrentLng?.ToString("F10") ?? "-";
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_serverService != null)
                {
                    AppendLog("이미 서버가 실행 중입니다.");
                    return;
                }

                string host = txtHost.Text.Trim();
                int port = int.Parse(txtPort.Text.Trim());
                string gpsPort = txtGpsPort.Text.Trim();
                int baudRate = int.Parse(txtBaudRate.Text.Trim());

                _serverService = new TrainServerService(host, port, gpsPort, baudRate);
                _serverService.LogReceived += AppendLog;

                await _serverService.StartAsync();

                AppendLog($"서버 시작 완료: http://{host}:{port}/");
            }
            catch (Exception ex)
            {
                AppendLog($"서버 시작 실패: {ex.Message}");
                MessageBox.Show(ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_serverService == null)
                {
                    AppendLog("실행 중인 서버가 없습니다.");
                    return;
                }

                await _serverService.StopAsync();
                _serverService.LogReceived -= AppendLog;
                _serverService = null;

                AppendLog("서버 중지 완료");
            }
            catch (Exception ex)
            {
                AppendLog($"서버 중지 실패: {ex.Message}");
            }
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
                if (_serverService != null)
                {
                    await _serverService.StopAsync();
                }
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}