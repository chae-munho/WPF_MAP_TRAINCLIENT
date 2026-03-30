namespace TrainClient.Models
{
    public class WsVideoFrameMessage
    {
        public string Type { get; set; } = "video_frame";
        public int Train { get; set; }
        public int CarNo { get; set; }
        public string ImageBase64 { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; } = "jpeg";
        public string Timestamp { get; set; } = "";
    }
}