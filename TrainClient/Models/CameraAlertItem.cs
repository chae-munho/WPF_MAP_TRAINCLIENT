namespace TrainClient.Models
{
    public class CameraAlarmItem
    {
        public int TrainNo { get; set; }
        public int CarNo { get; set; }
        public string DisplayText => $"[기차{TrainNo}] {CarNo}번 호출";
    }
}