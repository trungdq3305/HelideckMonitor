namespace HelideckVer2.Models
{
    public class DeviceTask
    {
        public string TaskName { get; set; }
        public string PortName { get; set; }
        public int BaudRate { get; set; }

        // Nếu set (vd: "MWV", "HDT"), parser chỉ xử lý câu khớp EndsWith(SentenceType) từ port này.
        // Nếu null/empty → auto-detect tất cả (hành vi mặc định).
        public string? SentenceType { get; set; }
    }
}