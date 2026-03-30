namespace TrainClient.Models
{
    public class WsHeartbeatMessage
    {
        public int Train { get; set; }
        public string Type { get; set; } = "heartbeat";
        public string Timestamp { get; set; } = "";
    }
}