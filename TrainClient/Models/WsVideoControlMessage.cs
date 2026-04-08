namespace TrainClient.Models
{
    public class WsVideoControlMessage
    {
        public string Type { get; set; } = "video_control";
        public string Action { get; set; } = "";   // "stop"
        public int Train { get; set; }
        public int CarNo { get; set; }
        public string? RequestId { get; set; }
        public string Timestamp { get; set; } = "";
    }
}