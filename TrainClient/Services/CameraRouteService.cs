namespace TrainClient.Services
{
    public static class CameraRouteService
    {
        private static readonly string[] Train1CarUrls =
        {
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp"
        };

        private static readonly string[] Train2CarUrls =
        {
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp"
        };

        public static string GetRtspUrl(int trainNo, int carNo)
        {
            int index = carNo - 1;

            if (index < 0 || index >= 12)
                return string.Empty;

            return trainNo == 1
                ? Train1CarUrls[index]
                : Train2CarUrls[index];
        }
    }
}