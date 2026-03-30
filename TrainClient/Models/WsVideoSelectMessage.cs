namespace TrainClient.Models
{
    public class WsVideoSelectMessage
    {
        public string Type { get; set; } = "video_select";
        public int Train { get; set; }
        public int CarNo { get; set; }
        public string? RequestId { get; set; }
        public string Timestamp { get; set; } = "";
    }
}