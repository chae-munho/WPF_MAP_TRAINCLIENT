using System;

namespace TrainClient.Models
{
    public class IntercomDetectedEventArgs : EventArgs
    {
        public int Train { get; set; }
        public int CarNumber { get; set; }
        public string CameraUrl { get; set; } = "";
        public DateTime OccurredAt { get; set; } = DateTime.Now;
    }
}