namespace TrainClient.Models
{
    public class WsControlAckMessage
    {
        public string Type { get; set; } = "control_ack";
        public int Train { get; set; }
        public int Operation { get; set; }
        public int Value { get; set; }
        public string Result { get; set; } = "ok";
        public string? CommandId { get; set; }
        public string Timestamp { get; set; } = "";
    }
}