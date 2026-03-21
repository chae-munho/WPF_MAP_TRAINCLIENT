namespace TrainClient.Models
{
    public class WsControlMessage
    {
        public string Type { get; set; } = "";
        public int Train { get; set; }
        public int Operation { get; set; }
        public int Value { get; set; }
        public string? CommandId { get; set; }
        public string? Timestamp { get; set; }
    }
}