namespace TrainClient.Models
{
    public class WsVideoStopMessage
    {
        public string Type { get; set; } = "video_stop";
        public int Train { get; set; }
        public int CarNo { get; set; }
        public string? RequestId { get; set; }
        public string Timestamp { get; set; } = "";
    }
}