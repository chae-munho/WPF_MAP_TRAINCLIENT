namespace TrainClient.Models
{
    public class WsTelemetryMessage
    {
        public string Type { get; set; } = "telemetry";
        public int Train { get; set; }
        public int[] Data { get; set; } = System.Array.Empty<int>();
        public string Timestamp { get; set; } = "";
    }
}