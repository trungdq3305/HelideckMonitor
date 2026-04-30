using System.Drawing;

namespace HelideckVer2.UI.Theme
{
    public static class Palette
    {
        public static bool IsLight { get; set; } = false;

        // ── BACKGROUNDS ──────────────────────────────────────────────────────
        // Dark: tầng rõ ràng AppBg < PanelBg < CardBg (bước ~8-14 mỗi tầng)
        // Light: AppBg xanh đậm ≠ PanelBg ≠ CardBg trắng (bước ~30-50)
        public static Color AppBg        => IsLight ? Color.FromArgb(0xCC, 0xDD, 0xEE) : Color.FromArgb(0x08, 0x0C, 0x14);
        public static Color PanelBg      => IsLight ? Color.FromArgb(0xA4, 0xBE, 0xD8) : Color.FromArgb(0x0E, 0x16, 0x24);
        public static Color CardBg       => IsLight ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x16, 0x22, 0x34);
        public static Color CardFace     => IsLight ? Color.FromArgb(0xEB, 0xF3, 0xFF) : Color.FromArgb(0x1E, 0x2E, 0x46);
        public static Color ChartBg      => IsLight ? Color.FromArgb(0xF4, 0xF9, 0xFF) : Color.FromArgb(0x0A, 0x12, 0x20);
        public static Color SurfaceHi    => IsLight ? Color.FromArgb(0xC0, 0xD4, 0xEC) : Color.FromArgb(0x1C, 0x2E, 0x46);
        public static Color SectionHdrBg => IsLight ? Color.FromArgb(0x3A, 0x68, 0x9C) : Color.FromArgb(0x1A, 0x38, 0x68);
        public static Color InputBg      => IsLight ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x16, 0x22, 0x34);

        // ── BORDERS ──────────────────────────────────────────────────────────
        // Dark: viền phải tương phản rõ với CardBg (~+18-24 mỗi kênh)
        // Light: viền đủ tối để thấy trên nền trắng và xanh nhạt
        public static Color BorderCard   => IsLight ? Color.FromArgb(0x70, 0x9C, 0xC4) : Color.FromArgb(0x2E, 0x4C, 0x78);
        public static Color BorderPanel  => IsLight ? Color.FromArgb(0x7E, 0xA4, 0xC4) : Color.FromArgb(0x20, 0x36, 0x58);
        public static Color GridLine     => IsLight ? Color.FromArgb(0xA0, 0xBE, 0xD8) : Color.FromArgb(0x1C, 0x2E, 0x48);

        // ── TEXT ─────────────────────────────────────────────────────────────
        public static Color TextLabel    => IsLight ? Color.FromArgb(0x1E, 0x44, 0x70) : Color.FromArgb(0x68, 0x8C, 0xB4);
        public static Color TextValue    => IsLight ? Color.FromArgb(0x04, 0x0C, 0x18) : Color.FromArgb(0xDD, 0xEE, 0xFF);
        public static Color TextGps      => IsLight ? Color.FromArgb(0x00, 0x50, 0xA0) : Color.FromArgb(0x50, 0xCC, 0xFF);
        public static Color TextDim      => IsLight ? Color.FromArgb(0x60, 0x80, 0x9C) : Color.FromArgb(0x38, 0x50, 0x68);

        // ── TRẠNG THÁI (ISA-101) ─────────────────────────────────────────────
        public static Color OkBg         => IsLight ? Color.FromArgb(0xB8, 0xEC, 0xCC) : Color.FromArgb(0x06, 0x22, 0x12);
        public static Color OkFg         => IsLight ? Color.FromArgb(0x00, 0x5E, 0x2C) : Color.FromArgb(0x00, 0xCC, 0x66);
        public static Color WaitBg       => IsLight ? Color.FromArgb(0xDC, 0xE8, 0xF6) : Color.FromArgb(0x10, 0x1A, 0x2C);
        public static Color WaitFg       => IsLight ? Color.FromArgb(0x38, 0x64, 0x90) : Color.FromArgb(0x70, 0x98, 0xC2);
        public static Color LostBg       => IsLight ? Color.FromArgb(0xF0, 0xD4, 0xD4) : Color.FromArgb(0x2C, 0x08, 0x08);
        public static Color LostFg       => IsLight ? Color.FromArgb(0xB8, 0x20, 0x00) : Color.FromArgb(0xFF, 0x44, 0x44);

        // ── ALARM (ISA-101) ──────────────────────────────────────────────────
        public static Color AlarmActiveBg => IsLight ? Color.FromArgb(0xFF, 0xD4, 0xD4) : Color.FromArgb(0x3A, 0x00, 0x00);
        public static Color AlarmActiveFg => IsLight ? Color.FromArgb(0xB8, 0x00, 0x00) : Color.FromArgb(0xFF, 0x24, 0x24);
        public static Color AlarmAckBg    => IsLight ? Color.FromArgb(0xFF, 0xEE, 0xC0) : Color.FromArgb(0x2A, 0x14, 0x00);
        public static Color AlarmAckFg    => IsLight ? Color.FromArgb(0x94, 0x5E, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color AlarmNormalBg => IsLight ? Color.FromArgb(0xB8, 0xEC, 0xCC) : Color.FromArgb(0x06, 0x20, 0x12);
        public static Color AlarmNormalFg => IsLight ? Color.FromArgb(0x00, 0x5E, 0x2C) : Color.FromArgb(0x00, 0xCC, 0x66);
        public static Color ValueAlarm    => IsLight ? Color.FromArgb(0xB8, 0x00, 0x00) : Color.FromArgb(0xFF, 0x24, 0x24);
        public static Color ValueAck      => IsLight ? Color.FromArgb(0x94, 0x5E, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── CHART SERIES ─────────────────────────────────────────────────────
        public static Color SeriesRoll   => IsLight ? Color.FromArgb(0x00, 0x78, 0xCC) : Color.FromArgb(0x00, 0xC0, 0xF0);
        public static Color SeriesPitch  => IsLight ? Color.FromArgb(0xCC, 0x88, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color SeriesHeave  => IsLight ? Color.FromArgb(0x88, 0x28, 0xCC) : Color.FromArgb(0xB8, 0x60, 0xFF);
        public static Color SeriesWSpeed => IsLight ? Color.FromArgb(0x00, 0x8A, 0x50) : Color.FromArgb(0x44, 0xE0, 0x9C);
        public static Color SeriesWDir   => IsLight ? Color.FromArgb(0xBB, 0x99, 0x00) : Color.FromArgb(0xFF, 0xD8, 0x40);

        // ── RADAR ────────────────────────────────────────────────────────────
        public static Color RadarBg        => IsLight ? Color.FromArgb(0xEC, 0xF4, 0xFF) : Color.FromArgb(0x08, 0x10, 0x1E);
        public static Color RadarFace      => IsLight ? Color.FromArgb(0xF4, 0xF9, 0xFF) : Color.FromArgb(0x0B, 0x16, 0x2A);
        public static Color RadarBorder    => IsLight ? Color.FromArgb(0x5E, 0x84, 0xB0) : Color.FromArgb(0x22, 0x3C, 0x5C);
        public static Color RadarGrid      => IsLight ? Color.FromArgb(0xAE, 0xC8, 0xE2) : Color.FromArgb(0x16, 0x28, 0x44);
        public static Color RadarTick      => IsLight ? Color.FromArgb(0x7E, 0xA0, 0xC6) : Color.FromArgb(0x2A, 0x46, 0x6A);
        public static Color RadarText      => IsLight ? Color.FromArgb(0x3C, 0x60, 0x90) : Color.FromArgb(0x56, 0x78, 0xAA);
        public static Color RadarNorth     => IsLight ? Color.FromArgb(0x00, 0x6C, 0xCC) : Color.FromArgb(0x00, 0xC4, 0xFF);
        public static Color RadarShipFill  => IsLight ? Color.FromArgb(0x5E, 0x84, 0xB0) : Color.FromArgb(0x28, 0x50, 0x80);
        public static Color RadarShipBdr   => IsLight ? Color.FromArgb(0x24, 0x54, 0x9C) : Color.FromArgb(0x60, 0x98, 0xCC);
        public static Color RadarHeadLine  => IsLight ? Color.FromArgb(0x00, 0x54, 0xCC) : Color.FromArgb(0x00, 0xD4, 0xFF);
        public static Color RadarWindArrow => IsLight ? Color.FromArgb(0xAA, 0x77, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color RadarWindText  => IsLight ? Color.FromArgb(0xAA, 0x77, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── BUTTONS ──────────────────────────────────────────────────────────
        public static Color BtnPrimaryBg  => IsLight ? Color.FromArgb(0xAE, 0xCC, 0xEA) : Color.FromArgb(0x18, 0x2C, 0x4E);
        public static Color BtnPrimaryFg  => IsLight ? Color.FromArgb(0x0C, 0x34, 0x64) : Color.FromArgb(0x80, 0xB6, 0xE2);
        public static Color BtnSettingsBg => IsLight ? Color.FromArgb(0xFF, 0xEC, 0xBC) : Color.FromArgb(0x28, 0x16, 0x00);
        public static Color BtnSettingsFg => IsLight ? Color.FromArgb(0x76, 0x42, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color BtnActiveBg   => IsLight ? Color.FromArgb(0x88, 0xB4, 0xDC) : Color.FromArgb(0x1C, 0x38, 0x64);
        public static Color BtnActiveFg   => IsLight ? Color.FromArgb(0x04, 0x18, 0x38) : Color.FromArgb(0xC0, 0xE2, 0xFF);
    }
}
