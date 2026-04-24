using System.Drawing;

namespace HelideckVer2.UI.Theme
{
    /// <summary>
    /// Bảng màu chuẩn ISA-101 / IEC 62288 cho hệ thống giám sát maritime.
    /// Tất cả màu dùng trong toàn bộ app phải lấy từ đây – không hard-code rải rác.
    /// </summary>
    public static class Palette
    {
        // ── BACKGROUNDS ──────────────────────────────────────────────────────
        public static readonly Color AppBg      = Color.FromArgb(0x0B, 0x0F, 0x1A); // #0B0F1A – nền app
        public static readonly Color PanelBg    = Color.FromArgb(0x0F, 0x15, 0x22); // #0F1522 – bar trên/dưới, panel
        public static readonly Color CardBg     = Color.FromArgb(0x13, 0x1C, 0x2E); // #131C2E – card dữ liệu
        public static readonly Color ChartBg    = Color.FromArgb(0x0C, 0x14, 0x22); // #0C1422 – chart area
        public static readonly Color SurfaceHi  = Color.FromArgb(0x18, 0x24, 0x3A); // #18243A – hover/selected row

        // ── BORDERS ──────────────────────────────────────────────────────────
        public static readonly Color BorderCard  = Color.FromArgb(0x1E, 0x2D, 0x48); // #1E2D48
        public static readonly Color BorderPanel = Color.FromArgb(0x18, 0x26, 0x40); // #182640
        public static readonly Color GridLine    = Color.FromArgb(0x18, 0x28, 0x40); // chart grid

        // ── TEXT ─────────────────────────────────────────────────────────────
        /// Tên nhãn (POSITION, SPEED…) – muted, không cạnh tranh với giá trị
        public static readonly Color TextLabel  = Color.FromArgb(0x5B, 0x7B, 0xA8); // #5B7BA8
        /// Giá trị số LIVE – tất cả dùng 1 màu này (trừ khi alarm)
        public static readonly Color TextValue  = Color.FromArgb(0xD0, 0xDC, 0xF0); // #D0DCF0
        /// GPS/Position – hơi xanh hơn để phân biệt loại dữ liệu
        public static readonly Color TextGps    = Color.FromArgb(0x7E, 0xC8, 0xFF); // #7EC8FF
        /// Không có dữ liệu / stale
        public static readonly Color TextDim    = Color.FromArgb(0x35, 0x4F, 0x6A); // #354F6A

        // ── TRẠNG THÁI (ISA-101) ─────────────────────────────────────────────
        // OK
        public static readonly Color OkBg       = Color.FromArgb(0x0B, 0x26, 0x18); // #0B2618
        public static readonly Color OkFg       = Color.FromArgb(0x2E, 0xCC, 0x71); // #2ECC71 – xanh lá
        // WAIT / Không có dữ liệu
        public static readonly Color WaitBg     = Color.FromArgb(0x12, 0x18, 0x27); // #121827
        public static readonly Color WaitFg     = Color.FromArgb(0x4B, 0x60, 0x80); // #4B6080
        // LOST / Stale
        public static readonly Color LostBg     = Color.FromArgb(0x2D, 0x0D, 0x0D); // #2D0D0D
        public static readonly Color LostFg     = Color.FromArgb(0xFF, 0x44, 0x44); // #FF4444

        // ── ALARM (ISA-101 màu chuẩn) ────────────────────────────────────────
        /// Active unacked alarm – ĐỎ
        public static readonly Color AlarmActiveBg = Color.FromArgb(0x3D, 0x00, 0x00);
        public static readonly Color AlarmActiveFg = Color.FromArgb(0xFF, 0x33, 0x33);
        /// Acknowledged alarm – VÀNG AMBER
        public static readonly Color AlarmAckBg    = Color.FromArgb(0x2D, 0x1A, 0x00);
        public static readonly Color AlarmAckFg    = Color.FromArgb(0xFF, 0xB8, 0x00);
        /// Normal – xanh lá tối
        public static readonly Color AlarmNormalBg = Color.FromArgb(0x0B, 0x1E, 0x10);
        public static readonly Color AlarmNormalFg = Color.FromArgb(0x2E, 0xCC, 0x71);
        /// Màu giá trị đang ở trạng thái alarm (đỏ sáng)
        public static readonly Color ValueAlarm    = Color.FromArgb(0xFF, 0x44, 0x44);
        /// Màu giá trị đang ở trạng thái acked (amber)
        public static readonly Color ValueAck      = Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── CHART SERIES ─────────────────────────────────────────────────────
        public static readonly Color SeriesRoll    = Color.FromArgb(0x4F, 0xC3, 0xF7); // cyan
        public static readonly Color SeriesPitch   = Color.FromArgb(0xFF, 0xB8, 0x00); // amber
        public static readonly Color SeriesHeave   = Color.FromArgb(0xFF, 0x6B, 0x6B); // coral
        public static readonly Color SeriesWSpeed  = Color.FromArgb(0x4F, 0xC3, 0xF7); // cyan
        public static readonly Color SeriesWDir    = Color.FromArgb(0xFF, 0xD7, 0x00); // gold

        // ── RADAR ────────────────────────────────────────────────────────────
        public static readonly Color RadarBg       = Color.FromArgb(0x0A, 0x12, 0x20);
        public static readonly Color RadarFace     = Color.FromArgb(0x0D, 0x18, 0x2E);
        public static readonly Color RadarBorder   = Color.FromArgb(0x24, 0x3A, 0x58);
        public static readonly Color RadarGrid     = Color.FromArgb(0x1A, 0x2A, 0x42);
        public static readonly Color RadarTick     = Color.FromArgb(0x2E, 0x48, 0x6A);
        public static readonly Color RadarText     = Color.FromArgb(0x58, 0x7A, 0xA8);
        public static readonly Color RadarNorth    = Color.FromArgb(0x4F, 0xC3, 0xF7);
        public static readonly Color RadarShipFill = Color.FromArgb(0x5E, 0x88, 0xBB);
        public static readonly Color RadarShipBdr  = Color.FromArgb(0x9E, 0xC6, 0xEE);
        public static readonly Color RadarHeadLine = Color.FromArgb(0x9E, 0xC6, 0xEE);
        public static readonly Color RadarWindArrow= Color.FromArgb(0xFF, 0xB8, 0x00);  // amber
        public static readonly Color RadarWindText = Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── BUTTONS ─────────────────────────────────────────────────────────
        public static readonly Color BtnPrimaryBg  = Color.FromArgb(0x14, 0x26, 0x42);
        public static readonly Color BtnPrimaryFg  = Color.FromArgb(0x7E, 0xAC, 0xD8);
        public static readonly Color BtnSettingsBg = Color.FromArgb(0x28, 0x1B, 0x00);
        public static readonly Color BtnSettingsFg = Color.FromArgb(0xFF, 0xB8, 0x00);
        public static readonly Color BtnActiveBg   = Color.FromArgb(0x1A, 0x36, 0x58);
        public static readonly Color BtnActiveFg   = Color.FromArgb(0xC8, 0xE4, 0xFF);
    }
}
