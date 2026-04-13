namespace TrainClient.Models
{
    public sealed class VideoFramePacket
    {
        public int Train { get; set; }
        public int CarNo { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long TimestampTicksUtc { get; set; }
        public byte[] JpegBytes { get; set; } = System.Array.Empty<byte>();
    }
}