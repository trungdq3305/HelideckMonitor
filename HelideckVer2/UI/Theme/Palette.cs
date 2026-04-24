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
        // Charcoal trung tính – gần với real HMS console (Kongsberg/Observator style)
        public static readonly Color AppBg      = Color.FromArgb(0x09, 0x0C, 0x11); // #090C11 – near-black, trung tính
        public static readonly Color PanelBg    = Color.FromArgb(0x0D, 0x13, 0x1C); // #0D131C – bar trên/dưới, panel
        public static readonly Color CardBg     = Color.FromArgb(0x11, 0x1A, 0x28); // #111A28 – card dữ liệu
        public static readonly Color ChartBg    = Color.FromArgb(0x0A, 0x12, 0x1C); // #0A121C – chart area
        public static readonly Color SurfaceHi  = Color.FromArgb(0x16, 0x22, 0x36); // #162236 – hover/selected row

        // ── BORDERS ──────────────────────────────────────────────────────────
        public static readonly Color BorderCard  = Color.FromArgb(0x1C, 0x2C, 0x46); // #1C2C46
        public static readonly Color BorderPanel = Color.FromArgb(0x16, 0x24, 0x3C); // #16243C
        public static readonly Color GridLine    = Color.FromArgb(0x16, 0x26, 0x3C); // chart grid

        // ── TEXT ─────────────────────────────────────────────────────────────
        /// Tên nhãn (POSITION, SPEED…) – muted, không cạnh tranh với giá trị
        public static readonly Color TextLabel  = Color.FromArgb(0x64, 0x88, 0xB4); // #6488B4 – steel blue
        /// Giá trị số LIVE – sáng hơn để đọc rõ trong điều kiện ánh sáng biến đổi trên tàu
        public static readonly Color TextValue  = Color.FromArgb(0xDE, 0xEC, 0xFF); // #DEECFF – near-white
        /// GPS/Position – cyan rõ hơn để phân biệt loại dữ liệu
        public static readonly Color TextGps    = Color.FromArgb(0x5C, 0xCC, 0xFF); // #5CCFFF – cyan
        /// Không có dữ liệu / stale
        public static readonly Color TextDim    = Color.FromArgb(0x32, 0x4C, 0x66); // #324C66

        // ── TRẠNG THÁI (ISA-101) ─────────────────────────────────────────────
        // OK
        public static readonly Color OkBg       = Color.FromArgb(0x08, 0x22, 0x14); // #082214
        public static readonly Color OkFg       = Color.FromArgb(0x00, 0xCC, 0x66); // #00CC66 – vivid green (IEC 62288)
        // WAIT / Không có dữ liệu
        public static readonly Color WaitBg     = Color.FromArgb(0x10, 0x18, 0x26); // #101826
        public static readonly Color WaitFg     = Color.FromArgb(0x48, 0x60, 0x80); // #486080
        // LOST / Stale
        public static readonly Color LostBg     = Color.FromArgb(0x2C, 0x08, 0x08); // #2C0808
        public static readonly Color LostFg     = Color.FromArgb(0xFF, 0x40, 0x40); // #FF4040

        // ── ALARM (ISA-101 màu chuẩn) ────────────────────────────────────────
        /// Active unacked alarm – ĐỎ (phải cực kỳ nổi bật, đây là safety-critical)
        public static readonly Color AlarmActiveBg = Color.FromArgb(0x3C, 0x00, 0x00);
        public static readonly Color AlarmActiveFg = Color.FromArgb(0xFF, 0x22, 0x22); // #FF2222 – alarm red sáng nhất
        /// Acknowledged alarm – VÀNG AMBER
        public static readonly Color AlarmAckBg    = Color.FromArgb(0x2C, 0x18, 0x00);
        public static readonly Color AlarmAckFg    = Color.FromArgb(0xFF, 0xB8, 0x00); // #FFB800
        /// Normal – xanh lá tối
        public static readonly Color AlarmNormalBg = Color.FromArgb(0x08, 0x1C, 0x0E);
        public static readonly Color AlarmNormalFg = Color.FromArgb(0x00, 0xCC, 0x66); // đồng bộ với OkFg
        /// Màu giá trị đang ở trạng thái alarm (đỏ sáng)
        public static readonly Color ValueAlarm    = Color.FromArgb(0xFF, 0x22, 0x22);
        /// Màu giá trị đang ở trạng thái acked (amber)
        public static readonly Color ValueAck      = Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── CHART SERIES ─────────────────────────────────────────────────────
        // Motion: 3 màu phân biệt rõ – quan trọng cho helideck landing assessment
        public static readonly Color SeriesRoll    = Color.FromArgb(0x00, 0xC0, 0xF0); // #00C0F0 – cyan (roll)
        public static readonly Color SeriesPitch   = Color.FromArgb(0xFF, 0xB8, 0x00); // #FFB800 – amber (pitch)
        public static readonly Color SeriesHeave   = Color.FromArgb(0xFF, 0x60, 0x60); // #FF6060 – coral (heave)
        // Wind: seafoam + gold – khác hẳn với motion series
        public static readonly Color SeriesWSpeed  = Color.FromArgb(0x48, 0xE0, 0xA0); // #48E0A0 – seafoam (wind speed)
        public static readonly Color SeriesWDir    = Color.FromArgb(0xFF, 0xD8, 0x40); // #FFD840 – gold (wind dir)

        // ── RADAR ────────────────────────────────────────────────────────────
        public static readonly Color RadarBg       = Color.FromArgb(0x08, 0x10, 0x1E); // #08101E
        public static readonly Color RadarFace     = Color.FromArgb(0x0B, 0x16, 0x2A); // #0B162A
        public static readonly Color RadarBorder   = Color.FromArgb(0x20, 0x38, 0x56); // #203856 – sáng hơn để frame rõ
        public static readonly Color RadarGrid     = Color.FromArgb(0x16, 0x28, 0x40); // #162840
        public static readonly Color RadarTick     = Color.FromArgb(0x2A, 0x44, 0x66); // #2A4466
        public static readonly Color RadarText     = Color.FromArgb(0x56, 0x78, 0xA8); // #5678A8
        public static readonly Color RadarNorth    = Color.FromArgb(0x00, 0xC0, 0xF0); // #00C0F0 – north marker cyan
        public static readonly Color RadarShipFill = Color.FromArgb(0x28, 0x4E, 0x7E); // #284E7E – ship body
        public static readonly Color RadarShipBdr  = Color.FromArgb(0x60, 0x96, 0xCC); // #6096CC – ship outline
        public static readonly Color RadarHeadLine = Color.FromArgb(0x00, 0xD4, 0xFF); // #00D4FF – heading line (rõ nhất)
        public static readonly Color RadarWindArrow= Color.FromArgb(0xFF, 0xB8, 0x00); // #FFB800 – amber wind arrow
        public static readonly Color RadarWindText = Color.FromArgb(0xFF, 0xB8, 0x00); // #FFB800

        // ── BUTTONS ─────────────────────────────────────────────────────────
        public static readonly Color BtnPrimaryBg  = Color.FromArgb(0x12, 0x24, 0x42); // #122442
        public static readonly Color BtnPrimaryFg  = Color.FromArgb(0x80, 0xB4, 0xE0); // #80B4E0
        public static readonly Color BtnSettingsBg = Color.FromArgb(0x26, 0x16, 0x00); // #261600
        public static readonly Color BtnSettingsFg = Color.FromArgb(0xFF, 0xB8, 0x00); // #FFB800
        public static readonly Color BtnActiveBg   = Color.FromArgb(0x18, 0x38, 0x60); // #183860
        public static readonly Color BtnActiveFg   = Color.FromArgb(0xC0, 0xE2, 0xFF); // #C0E2FF
    }
}
