namespace TrainClient.Models
{
    public class WsHelloMessage
    {
        public string Type { get; set; } = "hello";
        public string Role { get; set; } = "train";
        public string ClientName { get; set; } = "TrainClient";
        public string Timestamp { get; set; } = "";
    }
}