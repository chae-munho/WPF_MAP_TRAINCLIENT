using System;

namespace TrainClient.Services
{
    public static class CameraRouteService
    {
        private static readonly string[] CarSources =
         {
            "rtsp://admin:%40%40admin7434@192.168.1.100:554/0/onvif/profile1/media.smp",   //인터컴 1
            "rtsp://127.0.0.1:8554/car01",       //인터컴 2
            "rtsp://127.0.0.1:8554/car02",       //인터컴 3
            "rtsp://127.0.0.1:8554/car03",       //인터컴 4
            "rtsp://127.0.0.1:8554/car04",       //인터컴 5
            //"rtsp://127.0.0.1:8554/car05",
            //"rtsp://127.0.0.1:8554/car06",
            //"rtsp://127.0.0.1:8554/car07",
            //"rtsp://127.0.0.1:8554/car08",
            //"rtsp://127.0.0.1:8554/car09",
            //"rtsp://127.0.0.1:8554/car10",
            //"rtsp://127.0.0.1:8554/car11"
        };

        public static string GetRtspUrlByCarNo(int carNo)
        {
            int index = carNo - 1;

            if (index < 0 || index >= CarSources.Length)
                return string.Empty;

            return CarSources[index];
        }
    }
}