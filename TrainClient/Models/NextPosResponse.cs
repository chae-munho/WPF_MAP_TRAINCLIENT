namespace TrainClient.Models
{
    public class NextPosResponse
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Source { get; set; } = "fake";
    }
}