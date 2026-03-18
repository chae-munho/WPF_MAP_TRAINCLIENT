namespace TrainClient.Models
{
    public class SetDataRequest
    {
        public string Status { get; set; } = "";
        public int Operation { get; set; }
        public int Value { get; set; }
        public int Train { get; set; }
    }
}