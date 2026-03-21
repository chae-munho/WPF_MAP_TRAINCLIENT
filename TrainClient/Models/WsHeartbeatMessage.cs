namespace TrainClient.Models
{
    public class WsHeartbeatMessage
    {
        public string Type { get; set; } = "heartbeat";
        public string Timestamp { get; set; } = "";
    }
}