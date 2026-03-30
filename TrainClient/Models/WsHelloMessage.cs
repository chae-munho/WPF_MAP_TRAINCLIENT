namespace TrainClient.Models
{
    public class WsHelloMessage
    {
        public int Train { get; set; }
        public string Type { get; set; } = "hello";
        public string Role { get; set; } = "train";
        public string ClientName { get; set; } = "TrainClient";
        public string Timestamp { get; set; } = "";
    }
}