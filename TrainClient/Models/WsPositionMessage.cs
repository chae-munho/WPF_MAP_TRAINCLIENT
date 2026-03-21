namespace TrainClient.Models
{
    public class WsPositionMessage
    {
        public string Type { get; set; } = "position";
        public int Train { get; set; } = 1;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Source { get; set; } = "fake";
        public string Timestamp { get; set; } = "";
    }
}