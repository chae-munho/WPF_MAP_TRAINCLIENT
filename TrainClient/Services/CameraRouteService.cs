namespace TrainClient.Services
{
    public static class CameraRouteService
    {
        private static readonly string[] CarUrls =
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

        public static string GetRtspUrlByCarNo(int carNo)
        {
            int index = carNo - 1;

            if (index < 0 || index >= CarUrls.Length)
                return string.Empty;

            return CarUrls[index];
        }
    }
}