using System.Drawing;

namespace HelideckVer2.UI.Theme
{
    public static class Palette
    {
        public static bool IsLight { get; set; } = false;

        // ── BACKGROUNDS ──────────────────────────────────────────────────────
        public static Color AppBg       => IsLight ? Color.FromArgb(0xEE, 0xF2, 0xF8) : Color.FromArgb(0x09, 0x0C, 0x11);
        public static Color PanelBg     => IsLight ? Color.FromArgb(0xD6, 0xE4, 0xF0) : Color.FromArgb(0x0D, 0x13, 0x1C);
        public static Color CardBg      => IsLight ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x11, 0x1A, 0x28);
        public static Color CardFace    => IsLight ? Color.FromArgb(0xF0, 0xF6, 0xFF) : Color.FromArgb(0x1C, 0x2A, 0x3E);
        public static Color ChartBg     => IsLight ? Color.FromArgb(0xF5, 0xF9, 0xFF) : Color.FromArgb(0x0A, 0x12, 0x1C);
        public static Color SurfaceHi   => IsLight ? Color.FromArgb(0xDC, 0xE8, 0xF4) : Color.FromArgb(0x16, 0x22, 0x36);
        public static Color SectionHdrBg=> IsLight ? Color.FromArgb(0xB8, 0xD0, 0xE8) : Color.FromArgb(0x1E, 0x32, 0x55);
        public static Color InputBg     => IsLight ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x11, 0x1A, 0x28);

        // ── BORDERS ──────────────────────────────────────────────────────────
        public static Color BorderCard  => IsLight ? Color.FromArgb(0x9A, 0xB8, 0xD4) : Color.FromArgb(0x1C, 0x2C, 0x46);
        public static Color BorderPanel => IsLight ? Color.FromArgb(0xB0, 0xCC, 0xDE) : Color.FromArgb(0x16, 0x24, 0x3C);
        public static Color GridLine    => IsLight ? Color.FromArgb(0xCC, 0xDE, 0xEE) : Color.FromArgb(0x16, 0x26, 0x3C);

        // ── TEXT ─────────────────────────────────────────────────────────────
        public static Color TextLabel   => IsLight ? Color.FromArgb(0x3A, 0x5A, 0x84) : Color.FromArgb(0x64, 0x88, 0xB4);
        public static Color TextValue   => IsLight ? Color.FromArgb(0x0A, 0x18, 0x28) : Color.FromArgb(0xDE, 0xEC, 0xFF);
        public static Color TextGps     => IsLight ? Color.FromArgb(0x00, 0x66, 0xAA) : Color.FromArgb(0x5C, 0xCC, 0xFF);
        public static Color TextDim     => IsLight ? Color.FromArgb(0x8A, 0xAA, 0xBF) : Color.FromArgb(0x32, 0x4C, 0x66);

        // ── TRẠNG THÁI (ISA-101) ─────────────────────────────────────────────
        public static Color OkBg        => IsLight ? Color.FromArgb(0xD0, 0xF0, 0xDC) : Color.FromArgb(0x08, 0x22, 0x14);
        public static Color OkFg        => IsLight ? Color.FromArgb(0x00, 0x66, 0x33) : Color.FromArgb(0x00, 0xCC, 0x66);
        public static Color WaitBg      => IsLight ? Color.FromArgb(0xE0, 0xEA, 0xF4) : Color.FromArgb(0x10, 0x18, 0x26);
        public static Color WaitFg      => IsLight ? Color.FromArgb(0x50, 0x70, 0xA0) : Color.FromArgb(0x48, 0x60, 0x80);
        public static Color LostBg      => IsLight ? Color.FromArgb(0xF4, 0xDC, 0xDC) : Color.FromArgb(0x2C, 0x08, 0x08);
        public static Color LostFg      => IsLight ? Color.FromArgb(0xBB, 0x22, 0x00) : Color.FromArgb(0xFF, 0x40, 0x40);

        // ── ALARM (ISA-101) ──────────────────────────────────────────────────
        public static Color AlarmActiveBg => IsLight ? Color.FromArgb(0xFF, 0xDD, 0xDD) : Color.FromArgb(0x3C, 0x00, 0x00);
        public static Color AlarmActiveFg => IsLight ? Color.FromArgb(0xBB, 0x00, 0x00) : Color.FromArgb(0xFF, 0x22, 0x22);
        public static Color AlarmAckBg    => IsLight ? Color.FromArgb(0xFF, 0xF0, 0xCC) : Color.FromArgb(0x2C, 0x18, 0x00);
        public static Color AlarmAckFg    => IsLight ? Color.FromArgb(0x99, 0x66, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color AlarmNormalBg => IsLight ? Color.FromArgb(0xD0, 0xF0, 0xDC) : Color.FromArgb(0x08, 0x1C, 0x0E);
        public static Color AlarmNormalFg => IsLight ? Color.FromArgb(0x00, 0x66, 0x33) : Color.FromArgb(0x00, 0xCC, 0x66);
        public static Color ValueAlarm    => IsLight ? Color.FromArgb(0xBB, 0x00, 0x00) : Color.FromArgb(0xFF, 0x22, 0x22);
        public static Color ValueAck      => IsLight ? Color.FromArgb(0x99, 0x66, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── CHART SERIES ─────────────────────────────────────────────────────
        public static Color SeriesRoll   => IsLight ? Color.FromArgb(0x00, 0x80, 0xC0) : Color.FromArgb(0x00, 0xC0, 0xF0);
        public static Color SeriesPitch  => IsLight ? Color.FromArgb(0xCC, 0x88, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color SeriesHeave  => IsLight ? Color.FromArgb(0x80, 0x30, 0xCC) : Color.FromArgb(0xB0, 0x60, 0xF0);
        public static Color SeriesWSpeed => IsLight ? Color.FromArgb(0x00, 0x88, 0x55) : Color.FromArgb(0x48, 0xE0, 0xA0);
        public static Color SeriesWDir   => IsLight ? Color.FromArgb(0xBB, 0x99, 0x00) : Color.FromArgb(0xFF, 0xD8, 0x40);

        // ── RADAR ────────────────────────────────────────────────────────────
        public static Color RadarBg        => IsLight ? Color.FromArgb(0xED, 0xF3, 0xFA) : Color.FromArgb(0x08, 0x10, 0x1E);
        public static Color RadarFace      => IsLight ? Color.FromArgb(0xF5, 0xF9, 0xFF) : Color.FromArgb(0x0B, 0x16, 0x2A);
        public static Color RadarBorder    => IsLight ? Color.FromArgb(0x70, 0x90, 0xB8) : Color.FromArgb(0x20, 0x38, 0x56);
        public static Color RadarGrid      => IsLight ? Color.FromArgb(0xC0, 0xD4, 0xE8) : Color.FromArgb(0x16, 0x28, 0x40);
        public static Color RadarTick      => IsLight ? Color.FromArgb(0x8A, 0xAB, 0xCC) : Color.FromArgb(0x2A, 0x44, 0x66);
        public static Color RadarText      => IsLight ? Color.FromArgb(0x4A, 0x6A, 0x9A) : Color.FromArgb(0x56, 0x78, 0xA8);
        public static Color RadarNorth     => IsLight ? Color.FromArgb(0x00, 0x80, 0xCC) : Color.FromArgb(0x00, 0xC0, 0xF0);
        public static Color RadarShipFill  => IsLight ? Color.FromArgb(0x70, 0x90, 0xB8) : Color.FromArgb(0x28, 0x4E, 0x7E);
        public static Color RadarShipBdr   => IsLight ? Color.FromArgb(0x30, 0x60, 0xA0) : Color.FromArgb(0x60, 0x96, 0xCC);
        public static Color RadarHeadLine  => IsLight ? Color.FromArgb(0x00, 0x60, 0xCC) : Color.FromArgb(0x00, 0xD4, 0xFF);
        public static Color RadarWindArrow => IsLight ? Color.FromArgb(0xAA, 0x77, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color RadarWindText  => IsLight ? Color.FromArgb(0xAA, 0x77, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);

        // ── BUTTONS ──────────────────────────────────────────────────────────
        public static Color BtnPrimaryBg  => IsLight ? Color.FromArgb(0xC8, 0xDC, 0xF0) : Color.FromArgb(0x12, 0x24, 0x42);
        public static Color BtnPrimaryFg  => IsLight ? Color.FromArgb(0x1A, 0x3C, 0x6A) : Color.FromArgb(0x80, 0xB4, 0xE0);
        public static Color BtnSettingsBg => IsLight ? Color.FromArgb(0xFF, 0xF0, 0xCC) : Color.FromArgb(0x26, 0x16, 0x00);
        public static Color BtnSettingsFg => IsLight ? Color.FromArgb(0x7A, 0x44, 0x00) : Color.FromArgb(0xFF, 0xB8, 0x00);
        public static Color BtnActiveBg   => IsLight ? Color.FromArgb(0xA8, 0xC8, 0xE8) : Color.FromArgb(0x18, 0x38, 0x60);
        public static Color BtnActiveFg   => IsLight ? Color.FromArgb(0x0A, 0x1E, 0x40) : Color.FromArgb(0xC0, 0xE2, 0xFF);
    }
}
