using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Windows;
using TrainClient.Services;

namespace TrainClient.Popups
{
    public partial class CameraPopup : Window
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private VideoView? _videoView;
        private string _currentUrl = "";

        public int CurrentCarNo { get; private set; }

        public CameraPopup()
        {
            InitializeComponent();
            InitializeVlc();
        }

        private void InitializeVlc()
        {
            try
            {
                Core.Initialize();

                _libVLC = new LibVLC(
                    "--no-audio",
                    "--rtsp-tcp",
                    "--network-caching=500",
                    "--live-caching=500"
                );

                _mediaPlayer = new MediaPlayer(_libVLC);

                _videoView = new VideoView
                {
                    MediaPlayer = _mediaPlayer
                };

                VideoHost.Content = _videoView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"VLC 초기화 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ShowIntercom(int carNo)
        {
            if (carNo < 1 || carNo > 12)
            {
                txtSelectedTitle.Text = "잘못된 객차 호출";
                txtSelectedUrl.Text = "-";
                txtVideoPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            CurrentCarNo = carNo;

            string url = GetRtspUrl(carNo);

            txtSelectedTitle.Text = $"{carNo}번 객차 호출";
            txtSelectedUrl.Text = url;

            PlayUrl(url);
        }

        private string GetRtspUrl(int carNo)
        {
            return CameraRouteService.GetRtspUrlByCarNo(carNo);
        }

        private void PlayUrl(string url)
        {
            try
            {
                if (_libVLC == null || _mediaPlayer == null || string.IsNullOrWhiteSpace(url))
                {
                    txtVideoPlaceholder.Visibility = Visibility.Visible;
                    txtSelectedUrl.Text = "VLC 또는 URL이 비정상입니다.";
                    return;
                }

                if (string.Equals(_currentUrl, url, StringComparison.OrdinalIgnoreCase))
                {
                    txtVideoPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }

                _currentUrl = url;

                using Media media = new(_libVLC, new Uri(url));
                bool started = _mediaPlayer.Play(media);

                txtSelectedUrl.Text = started
                    ? url
                    : $"재생 시작 실패: {url}";

                txtVideoPlaceholder.Visibility = started
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            catch (Exception ex)
            {
                txtVideoPlaceholder.Visibility = Visibility.Visible;
                txtSelectedUrl.Text = $"재생 오류: {ex.Message}";
                MessageBox.Show($"카메라 재생 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _mediaPlayer?.Stop();
                _mediaPlayer?.Dispose();
                _libVLC?.Dispose();

                _mediaPlayer = null;
                _libVLC = null;
                _videoView = null;
                _currentUrl = "";
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}