namespace TrainClient.Models
{
    public class CameraAlarmItem
    {
        public int TrainNo { get; set; }   //Train 필드와 동일
        public int CarNo { get; set; }
        public string DisplayText => $"[기차{TrainNo}] {CarNo}번 호출";
    }
}