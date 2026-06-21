using HelideckVer2.Models;
using HelideckVer2.Services;
using HelideckVer2.Services.Parsing;
using HelideckVer2.UI.Theme;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;
using Font = System.Drawing.Font;
using Image = System.Drawing.Image;

namespace HelideckVer2
{
    public partial class MainForm : Form
    {
        private ComEngine             _comEngine;
        private NmeaParserService     _nmeaParser;
        private HelideckVer2.UI.Controls.RadarControl _radarControl;
        private HelideckVer2.UI.Controls.TrendChartControl.TrendMode _currentTrendMode
            = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion;
        private DataLogger _logger;

        private List<DeviceTask> _taskList;

        private double _lastHeaveValue   = 0;
        private double _rawHeaveCm       = 0; // giá trị có dấu, chỉ dùng để tính chu kỳ
        private DateTime? _lastZeroCrossTime = null;

        private AlarmEngine _alarmEngine;
        private bool _isSeparateTrend = false;
        private Button[] _trendModeButtons;

        private Tag _windTag, _rollTag, _pitchTag, _heaveTag;
        private Label lblAlarmStatus;
        private Label _lblClock;
        private Label _lblProjectTitle;
        private FlowLayoutPanel _topMenu;

        private const double BufferMinutes    = 20;
        private double _currentViewMinutes    = 2.0;

        // Biến lưu trữ GPS (sẽ đẩy vào DataHub)
        private double _speedKnot;
        private string _currentLat = "NO FIX";
        private string _currentLon = "NO FIX";

        private System.Windows.Forms.Timer _healthTimer, _snapshotTimer, _chartUpdateTimer;
        private System.Windows.Forms.Timer _uiUpdateTimer;
        private MeteoService    _meteoService;
        private MruService      _mruService;
        private SimulationEngine _simEngine;

        private double _rollOffset  = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm    = 10.0;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading, lblStatMeteo;
        private Label _lblTemp, _lblHumidity, _lblPressure;
        private Label[] _unitLabels = new Label[12];

        // ── HEAVE MIN/MAX ROLLING WINDOW ─────────────────────────────────────
        private const double HeaveMinMaxWindowSec = 60.0;
        private readonly Queue<(DateTime ts, double val)> _heaveHistory = new Queue<(DateTime ts, double val)>();
        private Label _lblHeaveMax, _lblHeaveMin;

        // ── APPLICATION WATCHDOG ──────────────────────────────────────────────
        // Tracks last UI timer heartbeat. Background watchdog exits the process
        // if the UI thread freezes for >15s (silent hang detection).
        private long _lastUiTickTicks = DateTime.Now.Ticks;

        private HelideckVer2.UI.Controls.TrendChartControl _trendControl;

        // ── CONSTRUCTOR ───────────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();

            var cfg = ConfigService.Load();
            SystemConfig.Apply(cfg);
            Palette.IsLight = SystemConfig.IsLightTheme; // phải set trước khi build UI
            SystemConfig.ThemeChanged += ApplyTheme;
            SystemConfig.VesselImageChanged += () => LoadImageFromFile(pictureBox1, "picture1.png");

            if (cfg.Tasks != null)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = ConfigForm.Tasks.Find(x => x.TaskName == saved.TaskName);
                    if (t != null)
                    {
                        t.PortName = saved.PortName;
                        if (saved.BaudRate > 0) t.BaudRate = saved.BaudRate;
                        t.SentenceType = saved.SentenceType;
                    }
                }
            }

            InitializeTasks();

            // Xây dựng layout
            SetupMainLayout();
            ApplyLeftPanelStyles();
            SetupRightPanelTitle();
            EnsureAlarmBadge();
            SetupStatusBadges();
            SetupTrendButtonsNearChart();

            // Radar thay thế pictureBox2
            _radarControl = new HelideckVer2.UI.Controls.RadarControl { Dock = DockStyle.Fill };
            pictureBox2.Parent.Controls.Add(_radarControl);
            _radarControl.BringToFront();
            pictureBox2.Visible = false;

            // Ảnh chiếu tàu
            LoadImageFromFile(pictureBox1, "picture1.png");
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

            // Logger
            _logger = new DataLogger();

            _snapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _snapshotTimer.Tick += SnapshotTimer_Tick;
            _snapshotTimer.Start();

            // NMEA Parser
            _nmeaParser = new NmeaParserService { HeaveArm = _heaveArm };
            _nmeaParser.SetPortTasks(ConfigForm.Tasks);
            _nmeaParser.OnHeadingParsed  += HandleHeading;
            _nmeaParser.OnWindParsed     += HandleWind;
            _nmeaParser.OnPositionParsed += HandlePosition;
            _nmeaParser.OnSpeedParsed    += HandleSpeed;

            // COM Engine
            _comEngine = new ComEngine();
            _comEngine.OnDataReceived += OnComDataReceived;

            this.FormClosing += (s, e) =>
            {
                e.Cancel = true;       // prevent framework close; we exit via Environment.Exit
                this.Visible = false;  // hide immediately — user sees app disappear at once

                // Stop UI timers so no more ticks fire during cleanup
                _uiUpdateTimer?.Stop();
                _snapshotTimer?.Stop();
                _healthTimer?.Stop();
                _chartUpdateTimer?.Stop();

                var mru   = _mruService;
                var sim   = _simEngine;
                var meteo = _meteoService;
                var com   = _comEngine;
                var log   = _logger;

                // Safety: force-exit after 4s if any cleanup step hangs
                Task.Delay(4000).ContinueWith(_ => Environment.Exit(0));

                Task.Run(() =>
                {
                    try { mru?.SendGoToConfig(); } catch { }  // joins read thread (max 1.5s)
                    try { sim?.Stop();    } catch { }
                    try { mru?.Stop();    } catch { }
                    try { meteo?.Stop();  } catch { }
                    try { com?.Dispose(); } catch { }
                    try { log?.Dispose(); } catch { }  // flush remaining log data
                    Environment.Exit(0);
                });
            };

            StartDataPipeline();
            Task.Run(CheckNtpSync);

            // Alarm Engine
            _alarmEngine = new AlarmEngine();
            _windTag  = new Tag("WindSpeed");
            _rollTag  = new Tag("Roll");
            _pitchTag = new Tag("Pitch");
            _heaveTag = new Tag("Heave");

            _alarmEngine.Register(new Alarm("AL_WIND",  _windTag,  () => SystemConfig.WindMax));
            _alarmEngine.Register(new Alarm("AL_ROLL",  _rollTag,  () => SystemConfig.RMax));
            _alarmEngine.Register(new Alarm("AL_PITCH", _pitchTag, () => SystemConfig.PMax));
            _alarmEngine.Register(new Alarm("AL_HEAVE", _heaveTag, () => SystemConfig.HMax));

            _alarmEngine.AlarmRaised  += OnAlarmRaised;
            _alarmEngine.AlarmCleared += OnAlarmCleared;
            _alarmEngine.AlarmAcked   += OnAlarmAcked;

            RefreshAlarmBanner();
            StartHealthTimer();

            // Application-level watchdog: if the UI timer stops firing for >15s the process is
            // unrecoverable — force exit so the OS/supervisor can restart the application.
            var watchdogThread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(5000);
                    double ageSec = (DateTime.Now.Ticks - Interlocked.Read(ref _lastUiTickTicks))
                                    / (double)TimeSpan.TicksPerSecond;
                    if (ageSec > 15.0)
                    {
                        SystemLogger.LogInfo($"[Watchdog] UI timer frozen for {ageSec:0}s — restarting application.");
                        Program.RestartApp(); // starts new instance before exiting
                    }
                }
            }) { IsBackground = true, Name = "AppWatchdog" };
            watchdogThread.Start();

            // Trend chart
            _trendControl = new HelideckVer2.UI.Controls.TrendChartControl { Dock = DockStyle.Fill };
            panelChartHost.Controls.Clear();
            panelChartHost.Controls.Add(_trendControl);

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => _trendControl.Render();
            _chartUpdateTimer.Start();
        }

        // ── LIVE THEME SWITCH ─────────────────────────────────────────────────

        // Builds old→new color map then walks every control in the form.
        // Custom-drawn controls (radar, chart) are invalidated / rebuilt so they
        // repaint themselves with the new Palette colours.
        private void ApplyTheme()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyTheme)); return; }

            // Snapshot old palette (before switching IsLight)
            Color[] oldC = SnapshotPalette();

            // Switch theme
            Palette.IsLight = SystemConfig.IsLightTheme;

            // Snapshot new palette
            Color[] newC = SnapshotPalette();

            // Build colour-swap map (old → new); skip duplicates (first entry wins)
            var map = new Dictionary<Color, Color>();
            for (int i = 0; i < oldC.Length; i++)
                if (!map.ContainsKey(oldC[i]))
                    map[oldC[i]] = newC[i];

            // Store map so other open forms (ConfigForm, DataListForm) can apply the same swap
            Palette.CurrentSwapMap = map;

            this.SuspendLayout();
            SwapControlColors(this, map);

            // Explicitly repaint dynamic card panels (created after InitializeComponent).
            // SwapControlColors may miss them because EnableDoubleBuffer/UserPaint panels
            // don't always honour BackColor changes set from outside their own paint cycle.
            tableLayoutPanel2.BackColor = Palette.CardBg;
            foreach (Control card in tableLayoutPanel2.Controls)
                card.Invalidate(true);

            this.ResumeLayout(false);

            // Rebuild chart areas/series with new palette colours
            _trendControl?.SetMode(_currentTrendMode, _isSeparateTrend);

            RefreshTrendButtonColors();

            // Sync title bar chrome with the new theme
            Palette.ApplyTitleBarTheme(Handle);

            // Force repaint on GDI+ custom controls
            _radarControl?.Invalidate();
            this.Refresh();
        }

        // Returns all Palette colour values in a fixed order (must match between two calls)
        private static Color[] SnapshotPalette() => new[] {
            Palette.AppBg,        Palette.PanelBg,      Palette.CardBg,
            Palette.CardFace,     Palette.SectionHdrBg, Palette.SurfaceHi,
            Palette.BorderCard,   Palette.BorderPanel,  Palette.GridLine,
            Palette.TextLabel,    Palette.TextValue,    Palette.TextDim,
            Palette.TextGps,      Palette.OkBg,         Palette.OkFg,
            Palette.LostBg,       Palette.LostFg,       Palette.WaitBg,
            Palette.WaitFg,       Palette.AlarmActiveBg,Palette.AlarmActiveFg,
            Palette.AlarmAckBg,   Palette.AlarmAckFg,   Palette.AlarmNormalBg,
            Palette.AlarmNormalFg,Palette.BtnPrimaryBg, Palette.BtnPrimaryFg,
            Palette.BtnSettingsBg,Palette.BtnSettingsFg,Palette.BtnActiveBg,
            Palette.BtnActiveFg,  Palette.ChartBg,      Palette.SeriesRoll,
            Palette.SeriesPitch,  Palette.SeriesHeave,  Palette.SeriesWSpeed,
            Palette.SeriesWDir,   Palette.RadarBg,      Palette.RadarFace,
        };

        private static void SwapControlColors(Control root, Dictionary<Color, Color> map)
        {
            foreach (Control c in root.Controls)
            {
                if (map.TryGetValue(c.BackColor, out Color nb)) c.BackColor = nb;
                if (map.TryGetValue(c.ForeColor, out Color nf)) c.ForeColor = nf;

                // Buttons also have FlatAppearance border colours
                if (c is Button btn && map.TryGetValue(btn.FlatAppearance.BorderColor, out Color nbdr))
                    btn.FlatAppearance.BorderColor = nbdr;

                if (c.Controls.Count > 0)
                    SwapControlColors(c, map);
            }
        }

        // ── LAYOUT SETUP ──────────────────────────────────────────────────────

        private void SetupMainLayout()
        {
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Palette.AppBg };
            this.Controls.Add(root);

            _topMenu = new FlowLayoutPanel
            {
                Dock         = DockStyle.Fill,
                BackColor    = Palette.PanelBg,
                Padding      = new Padding(6),
                WrapContents = false
            };

            _lblClock = new Label
            {
                AutoSize  = false,
                Width     = 150,
                Dock      = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Palette.PanelBg,
                ForeColor = Palette.TextValue,
                Text      = DateTime.Now.ToString("HH:mm:ss"),
                Padding   = new Padding(0, 0, 10, 0)
            };

            var headerPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = Palette.PanelBg
            };
            headerPanel.Controls.Add(_topMenu);
            headerPanel.Controls.Add(_lblClock);

            Panel pnlBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 52,
                BackColor = Palette.PanelBg,
                Padding   = new Padding(8, 6, 8, 6)
            };

            Button btnSetting = new Button
            {
                Text      = "⚙  SETTINGS",
                Width     = 150, Height = 38,
                Location  = new Point(10, 7),
                BackColor = Palette.BtnSettingsBg,
                ForeColor = Palette.BtnSettingsFg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnSetting.FlatAppearance.BorderColor = Palette.BorderCard;
            btnSetting.FlatAppearance.BorderSize  = 1;
            btnSetting.Click += BtnSettings_Click;

            Button btnDataList = new Button
            {
                Text      = "📋  RAW SCAN",
                Width     = 160, Height = 38,
                Location  = new Point(170, 7),
                BackColor = Palette.BtnPrimaryBg,
                ForeColor = Palette.BtnPrimaryFg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnDataList.FlatAppearance.BorderColor = Palette.BorderCard;
            btnDataList.FlatAppearance.BorderSize  = 1;
            btnDataList.Click += (s, e) => new DataListForm().Show(this);

            pnlBottom.Controls.Add(btnSetting);
            pnlBottom.Controls.Add(btnDataList);

            // Đặt màu cho các designer panel (override Color.White / BorderStyle từ .Designer.cs)
            tableLayoutPanel1.BackColor    = Palette.AppBg;
            tableLayoutPanel3.BackColor    = Palette.AppBg;
            tableLayoutPanel4.BackColor    = Palette.AppBg;
            tableLayoutPanelTrend.BackColor = Palette.AppBg;
            panelTrendButtons.BackColor    = Palette.PanelBg;
            panelChartHost.BackColor       = Palette.AppBg;
            pictureBox1.BorderStyle        = BorderStyle.None;
            pictureBox2.BorderStyle        = BorderStyle.None;

            tableLayoutPanel1.Dock    = DockStyle.Fill;
            tableLayoutPanel1.Visible = true;

            root.Controls.Add(tableLayoutPanel1);
            root.Controls.Add(pnlBottom);
            root.Controls.Add(headerPanel);
        }

        // Thêm nhãn tên dự án vào đầu panel phải (tableLayoutPanel4)
        private void SetupRightPanelTitle()
        {
            string shipName = SystemConfig.ShipName ?? "HELIDECK MONITOR";
            _lblProjectTitle = new Label
            {
                Text      = $"  {shipName}",
                Dock      = DockStyle.Top,
                Height    = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = Palette.SectionHdrBg,
                ForeColor = Palette.TextValue,
                Padding   = new Padding(8, 0, 0, 0)
            };

            // Ảnh vessel 50%, radar 50%
            tableLayoutPanel4.ColumnStyles.Clear();
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            tableLayoutPanel4.Controls.Add(_lblProjectTitle, 0, 0);
            tableLayoutPanel4.SetColumnSpan(_lblProjectTitle, 2);

            // Dịch pictureBox1 và pictureBox2 xuống row 1
            tableLayoutPanel4.RowCount = 2;
            tableLayoutPanel4.RowStyles.Clear();
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tableLayoutPanel4.SetRow(pictureBox1, 1);
            tableLayoutPanel4.SetRow(pictureBox2, 1);
        }

        // ── LEFT PANEL CARDS (8 data items) ──────────────────────────────────

        private void ApplyLeftPanelStyles()
        {
            // 25% left / 75% right
            tableLayoutPanel1.ColumnStyles.Clear();
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));

            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
            tableLayoutPanel2.BackColor       = Palette.CardBg;
            tableLayoutPanel2.Padding         = new Padding(4);
            tableLayoutPanel2.Controls.Clear();
            tableLayoutPanel2.ColumnStyles.Clear();
            tableLayoutPanel2.RowStyles.Clear();

            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.RowCount = 7;
            for (int i = 0; i < 7; i++)
                tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 7));

            // Nhãn và giá trị
            // items[0]=POSITION(full-width), [1-8]=SPEED..WIND DIR, [9-10]=TEMP,HUMIDITY, [11]=PRESSURE (METEO COM5)
            var lblTitleTemp     = new Label();
            var lblTitleHumidity = new Label();
            var lblTitlePressure = new Label();
            _lblTemp     = new Label();
            _lblHumidity = new Label();
            _lblPressure = new Label();

            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9, lblTitleTemp, lblTitleHumidity, lblTitlePressure };
            string[] headers = {
                "POSITION", "SPEED", "HEADING",
                "ROLL", "PITCH", "HEAVE",
                "H.PERIOD", "WIND SPD", "WIND DIR",
                "TEMP", "HUMIDITY", "PRESSURE"
            };
            Label[] values = {
                lblPosition, lblSpeed, lblHeading,
                lblRoll, lblPitch, lblHeave,
                lblHeaveCycle, lblWindSpeed, lblWindRelated,
                _lblTemp, _lblHumidity, _lblPressure
            };
            // Đơn vị hiển thị riêng ở label nhỏ bên dưới – không nhúng vào text số
            string[] unitTexts = { "", "kn", "°", "°", "°", "cm", "s", "m/s", "°", "°C", "%", "hPa" };

            Color titleFg = Palette.TextLabel;

            for (int i = 0; i < 12; i++)
            {
                if (titles[i] == null || values[i] == null) continue;

                titles[i].Text      = headers[i];
                titles[i].AutoSize  = false;
                titles[i].Dock      = DockStyle.Top;
                titles[i].TextAlign = ContentAlignment.MiddleCenter;
                titles[i].ForeColor = titleFg;
                titles[i].BackColor = Color.Transparent;

                values[i].Text      = "---";
                values[i].AutoSize  = false;
                values[i].Dock      = DockStyle.Fill;
                values[i].TextAlign = ContentAlignment.MiddleCenter;
                values[i].Padding   = new Padding(0);
                values[i].BackColor = Color.Transparent;
                values[i].ForeColor = Palette.OkFg;

                if (i == 0)  values[i].ForeColor = Palette.TextGps;        // POSITION
                if (i == 9)  values[i].ForeColor = Palette.SeriesRoll;    // TEMP
                if (i == 10) values[i].ForeColor = Palette.SeriesWSpeed;  // HUMIDITY
                if (i == 11) values[i].ForeColor = Palette.SeriesHeave;   // PRESSURE

                if (i == 7) values[i].Click += lblWindSpeed_Click;
                if (i == 3) values[i].Click += lblRoll_Click;
                if (i == 4) values[i].Click += lblPitch_Click;
                if (i == 5) values[i].Click += lblHeave_Click;

                bool hasUnit = !string.IsNullOrEmpty(unitTexts[i]);
                var unitLbl = new Label
                {
                    Text      = unitTexts[i],
                    AutoSize  = false,
                    Dock      = DockStyle.Bottom,
                    Height    = hasUnit ? 26 : 0,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent,
                    ForeColor = Palette.TextLabel,
                    Font      = new Font("Segoe UI", 12f, FontStyle.Bold)
                };
                _unitLabels[i] = unitLbl;

                var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(4) };
                EnableDoubleBuffer(card);
                int idx = i;

                card.Resize += (s, e) =>
                {
                    if (card.Height <= 0 || card.Width <= 0) return;

                    // Min nhỏ hơn để card nhỏ vẫn có chỗ cho số
                    int titleH = Math.Max(12, (int)(card.Height * 0.25f));
                    titles[idx].Height = titleH;

                    int unitH = 0;
                    if (_unitLabels[idx] != null && !string.IsNullOrEmpty(_unitLabels[idx].Text))
                    {
                        unitH = Math.Max(14, (int)(card.Height * 0.20f));
                        _unitLabels[idx].Height = unitH;
                        float uf = Math.Max(7f, Math.Min(card.Height * 0.12f, 14f));
                        SafeSetFont(_unitLabels[idx], uf);
                    }

                    float tf = Math.Max(7f, Math.Min(Math.Min(card.Height * 0.10f, card.Width * 0.08f), 18f));
                    SafeSetFont(titles[idx], tf);

                    // fillH = phần còn lại dành cho số — phải đủ để font không bị clip
                    int fillH = Math.Max(1, card.Height - titleH - unitH);

                    float vf;
                    if (idx == 0)
                    {
                        vf = Math.Max(7f, Math.Min(Math.Min(card.Height * 0.14f, card.Width * 0.055f), 20f));
                    }
                    else
                    {
                        // pt → px: pt * DpiX/72 * lineSpacingRatio(≈1.21 Segoe UI)
                        // Đảo ngược: safe_pt = fillH / (DpiX/72 * 1.21) * 0.82 (safety margin)
                        float dpiScale   = Math.Max(96f, card.DeviceDpi) / 96f;
                        float maxByFill  = fillH * 0.53f / dpiScale;
                        float maxByWidth = card.Width * 0.22f;
                        vf = Math.Max(7f, Math.Min(Math.Min(maxByFill, maxByWidth), 36f));
                    }
                    SafeSetFont(values[idx], vf);
                };

                card.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using var path = GetRoundedRect(rect, 10);
                    using var pen  = new Pen(Palette.BorderCard, 1);
                    g.DrawPath(pen, path);
                    int lineY = titles[idx].Height;
                    using var sp = new Pen(Palette.BorderPanel, 1);
                    g.DrawLine(sp, 8, lineY, card.Width - 8, lineY);
                };

                // Thứ tự Add quan trọng: Fill được dock sau cùng → add trước
                card.Controls.Add(values[i]);
                card.Controls.Add(unitLbl);   // Bottom dock trước Fill
                card.Controls.Add(titles[i]); // Top dock trước Bottom

                // HEAVE card: hai label góc trái/phải trên vùng unit
                if (i == 5)
                {
                    _lblHeaveMax = new Label
                    {
                        AutoSize  = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = Color.Transparent,
                        ForeColor = Color.FromArgb(80, 200, 100),
                        Font      = new Font("Consolas", 7.5f, FontStyle.Regular),
                        Text      = "▲---",
                        Padding   = new Padding(2, 0, 0, 0)
                    };
                    _lblHeaveMin = new Label
                    {
                        AutoSize  = false,
                        TextAlign = ContentAlignment.MiddleRight,
                        BackColor = Color.Transparent,
                        ForeColor = Color.FromArgb(255, 130, 50),
                        Font      = new Font("Consolas", 7.5f, FontStyle.Regular),
                        Text      = "▼---",
                        Padding   = new Padding(0, 0, 2, 0)
                    };
                    card.Controls.Add(_lblHeaveMax);
                    card.Controls.Add(_lblHeaveMin);
                    _lblHeaveMax.BringToFront();
                    _lblHeaveMin.BringToFront();

                    card.Resize += (s2, e2) =>
                    {
                        if (card.Height <= 0) return;
                        int uH = (_unitLabels[5] != null) ? _unitLabels[5].Height : 18;
                        int y  = card.Height - uH;
                        int w  = (card.Width - 4) * 2 / 5;
                        _lblHeaveMax.Bounds = new Rectangle(2, y, w, uH);
                        _lblHeaveMin.Bounds = new Rectangle(card.Width - w - 2, y, w, uH);
                        float f = Math.Max(6f, Math.Min(uH * 0.55f, 10f));
                        SafeSetFont(_lblHeaveMax, f);
                        SafeSetFont(_lblHeaveMin, f);
                    };
                }

                if (i == 0)
                {
                    tableLayoutPanel2.Controls.Add(card, 0, 0);
                    tableLayoutPanel2.SetColumnSpan(card, 2);
                }
                else if (i == 11)
                {
                    tableLayoutPanel2.Controls.Add(card, 0, 6);
                    tableLayoutPanel2.SetColumnSpan(card, 2);
                }
                else
                {
                    int row = (i + 1) / 2;
                    int col = (i + 1) % 2;
                    tableLayoutPanel2.Controls.Add(card, col, row);
                }
            }
        }

        private static void SafeSetFont(Label lbl, float size)
        {
            if (lbl.Font != null && Math.Abs(lbl.Font.Size - size) < 0.5f) return;
            Font old = lbl.Font;
            lbl.Font = new Font("Segoe UI", size, FontStyle.Bold);
            old?.Dispose();
        }

        // ── NMEA HANDLERS ─────────────────────────────────────────────────────

        private void HandleHeading(double heading)
        {
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateNumericData("HEADING", heading);
        }

        private void HandleWind(double wSpeed, double wDir)
        {
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateNumericData("WIND", wSpeed, wDir);
            // Tag.Update() is intentionally NOT called here (ThreadPool thread).
            // All alarm tags are updated exclusively from UiUpdateTimer_Tick (UI thread)
            // to avoid a data race on Tag.Value.
        }

        private void HandleMotion(double r, double p, double h)
        {
            double roll  = r + _rollOffset;
            double pitch = p + _pitchOffset;
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateNumericData("R/P/H", roll, pitch, h);
            // Tag.Update() is intentionally NOT called here — see HandleWind comment.
        }

        private void HandlePosition(string fLat, string fLon)
        {
            _currentLat = fLat;
            _currentLon = fLon;
            // Cập nhật DataHub GPS string (raw cũng được cập nhật qua OnComDataReceived)
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateGpsData(_speedKnot, fLat, fLon);
        }

        private void HandleSpeed(double k)
        {
            _speedKnot = k;
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateGpsData(k, _currentLat, _currentLon);
        }

        // ── NTP SYNC CHECK ────────────────────────────────────────────────────

        private static void CheckNtpSync()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("w32tm", "/query /status")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                var match = System.Text.RegularExpressions.Regex.Match(
                    output, @"Last Successful Sync Time:\s*(.+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success && DateTime.TryParse(match.Groups[1].Value.Trim(), out DateTime lastSync))
                {
                    double hours = (DateTime.Now - lastSync).TotalHours;
                    if (hours > 24.0)
                        SystemLogger.LogInfo($"[NTP] WARNING: Clock last synced {hours:0.0}h ago — log timestamps may drift. Run: w32tm /resync");
                    else
                        SystemLogger.LogInfo($"[NTP] Clock synced {hours:0.1}h ago — OK.");
                }
                else
                {
                    SystemLogger.LogInfo("[NTP] WARNING: Cannot determine last NTP sync time — log timestamps may be inaccurate.");
                }
            }
            catch (Exception ex)
            {
                SystemLogger.LogInfo($"[NTP] Clock sync check failed: {ex.Message}");
            }
        }

        // ── COM DATA RECEIVED ─────────────────────────────────────────────────

        private void OnComDataReceived(string portName, string rawData)
        {
            // Lưu chuỗi raw vào DataHub cho Bảng quét thô
            var task = _taskList.Find(t => t.PortName == portName);
            if (task != null)
                HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateRawString(task.TaskName, rawData);

            _nmeaParser.Parse(portName, rawData);
        }

        // ── HEALTH + UI TIMERS ────────────────────────────────────────────────

        private void StartHealthTimer()
        {
            if (_healthTimer != null) return;

            // 1 Hz – kiểm tra stale / badge
            _healthTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _healthTimer.Tick += (s, e) =>
            {
                _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
                var snap = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();
                foreach (var row in snap.TaskRows)
                {
                    switch (row.TaskName)
                    {
                        case "GPS":
                            UpdateBadge(lblStatGPS, "GPS", row.IsStale, row.Age);
                            UpdateLabelStatus(lblPosition, !row.IsStale);
                            UpdateLabelStatus(lblSpeed, !row.IsStale);
                            break;
                        case "WIND":
                            UpdateBadge(lblStatWind, "WIND", row.IsStale, row.Age);
                            UpdateLabelStatus(lblWindSpeed, !row.IsStale);
                            UpdateLabelStatus(lblWindRelated, !row.IsStale);
                            break;
                        case "R/P/H":
                            UpdateBadge(lblStatMotion, "R/P/H", row.IsStale, row.Age);
                            UpdateLabelStatus(lblRoll,      !row.IsStale);
                            UpdateLabelStatus(lblPitch,     !row.IsStale);
                            UpdateLabelStatus(lblHeave,     !row.IsStale);
                            UpdateLabelStatus(lblHeaveCycle,!row.IsStale);
                            break;
                        case "HEADING":
                            UpdateBadge(lblStatHeading, "HDG", row.IsStale, row.Age);
                            UpdateLabelStatus(lblHeading, !row.IsStale);
                            break;
                        case "METEO":
                            UpdateBadge(lblStatMeteo, "METEO", row.IsStale, row.Age);
                            UpdateLabelStatus(_lblTemp,     !row.IsStale);
                            UpdateLabelStatus(_lblHumidity, !row.IsStale);
                            break;
                    }
                }
            };
            _healthTimer.Start();

            // 100 ms – vẽ UI, chart, tính heave period
            _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            _uiUpdateTimer.Start();
        }

        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            // Heartbeat for application watchdog
            Interlocked.Exchange(ref _lastUiTickTicks, DateTime.Now.Ticks);

            // 1. Snapshot + update alarm tags BEFORE Evaluate so alarms see current data
            var snap = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();
            _windTag.Update(snap.WindSpeedMs);
            _rollTag.Update(Math.Abs(snap.RollDeg));
            _pitchTag.Update(Math.Abs(snap.PitchDeg));
            _heaveTag.Update(Math.Abs(snap.HeaveCm));
            _rawHeaveCm = snap.HeaveCm;

            // 2. Đánh giá alarm với data hiện tại
            _alarmEngine.Evaluate();

            // 3. Đẩy vào trend chart
            _trendControl.PushMotionData(snap.RollDeg, snap.PitchDeg, snap.HeaveCm);
            _trendControl.PushWindData(snap.WindSpeedMs, snap.WindDirDeg);

            // 4. Cập nhật nhãn – chỉ số thuần, đơn vị nằm ở _unitLabels riêng
            lblHeading.Text    = $"{snap.Heading:0.0}";
            lblWindSpeed.Text  = $"{snap.WindSpeedMs:0.0}";
            lblWindRelated.Text= $"{snap.WindDirDeg:0}";
            lblRoll.Text       = $"{snap.RollDeg:0.0}";
            lblPitch.Text      = $"{snap.PitchDeg:0.0}";
            lblHeave.Text      = $"{snap.HeaveCm:0.0}";

            // Rolling min/max for heave
            _heaveHistory.Enqueue((DateTime.Now, snap.HeaveCm));
            var cutoff = DateTime.Now.AddSeconds(-HeaveMinMaxWindowSec);
            while (_heaveHistory.Count > 0 && _heaveHistory.Peek().ts < cutoff)
                _heaveHistory.Dequeue();
            if (_lblHeaveMax != null && _heaveHistory.Count > 0)
            {
                double hMax = double.MinValue, hMin = double.MaxValue;
                foreach (var entry in _heaveHistory)
                {
                    if (entry.val > hMax) hMax = entry.val;
                    if (entry.val < hMin) hMin = entry.val;
                }
                _lblHeaveMax.Text = $"▲{hMax:+0.0;-0.0;0.0}";
                _lblHeaveMin.Text = $"▼{hMin:+0.0;-0.0;0.0}";
            }

            lblSpeed.Text      = $"{snap.GpsSpeedKnot:0.0}";

            if (snap.GpsLat == "NO FIX")
                lblPosition.Text = "NO FIX";
            else
                lblPosition.Text = $"{snap.GpsLon}\n{snap.GpsLat}";

            lblHeaveCycle.Text = snap.HeavePeriodSec > 0
                ? $"{snap.HeavePeriodSec:0.0}"
                : "---";

            // 5. METEO labels + ENV chart
            // Use 6s freshness window (poll=2s, Modbus timeout=600ms) to prevent flicker
            // when a poll is slow and Age briefly crosses the 2s IsStale threshold.
            var meteoRow = snap.TaskRows.Find(r => r.TaskName == "METEO");
            bool meteoFresh = meteoRow != null && meteoRow.Age < 6.0;
            SetLabelText(_lblTemp,     meteoFresh ? $"{snap.TempCelsius:0.00}" : "---");
            SetLabelText(_lblHumidity, meteoFresh ? $"{snap.HumidityPct:0.00}"  : "---");
            SetLabelText(_lblPressure, meteoFresh ? $"{snap.PressureHPa:0.0}"  : "---");
            if (meteoFresh)
                _trendControl.PushEnvData(snap.TempCelsius, snap.HumidityPct);

            // 6. Radar
            _radarControl.UpdateRadar(snap.Heading, snap.WindDirDeg);


            // 6. Tính chu kỳ Heave (zero-crossing âm→dương trên giá trị CÓ DẤU)
            DateTime now = DateTime.Now;
            if (_lastHeaveValue < 0 && _rawHeaveCm >= 0)
            {
                if (_lastZeroCrossTime.HasValue)
                {
                    double sec = (now - _lastZeroCrossTime.Value).TotalSeconds;
                    if (sec >= 1.0 && sec <= 30.0)
                        HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateHeavePeriod(sec);
                }
                _lastZeroCrossTime = now;
            }
            _lastHeaveValue = _rawHeaveCm;
        }

        private void SnapshotTimer_Tick(object sender, EventArgs e)
        {
            var snap = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();
            _logger.LogSnapshot(
                snap.GpsSpeedKnot, snap.Heading,
                snap.RollDeg, snap.PitchDeg, snap.HeaveCm,
                snap.HeavePeriodSec, snap.WindSpeedMs, snap.WindDirDeg,
                snap.TempCelsius, snap.HumidityPct, snap.PressureHPa,
                snap.GpsLat, snap.GpsLon);
        }

        // ── ALARM ─────────────────────────────────────────────────────────────

        private bool IsAlarmActive(string id)
        {
            foreach (var a in _alarmEngine.GetAll())
                if (a.Id == id && a.IsActive) return true;
            return false;
        }

        private void RefreshAlarmBanner()
        {
            if (lblAlarmStatus == null) return;
            var all = new List<Alarm>(_alarmEngine.GetAll());
            var unacked = all.Find(a => a.IsActive && !a.IsAcked);
            var acked   = all.Find(a => a.IsActive && a.IsAcked);

            if (unacked != null)
            {
                lblAlarmStatus.Text      = $"⚠ ALARM: {unacked.Id}";
                lblAlarmStatus.BackColor = Palette.AlarmActiveBg;
                lblAlarmStatus.ForeColor = Palette.AlarmActiveFg;
            }
            else if (acked != null)
            {
                lblAlarmStatus.Text      = $"ACK: {acked.Id}";
                lblAlarmStatus.BackColor = Palette.AlarmAckBg;
                lblAlarmStatus.ForeColor = Palette.AlarmAckFg;
            }
            else
            {
                lblAlarmStatus.Text      = "✔ NORMAL";
                lblAlarmStatus.BackColor = Palette.AlarmNormalBg;
                lblAlarmStatus.ForeColor = Palette.AlarmNormalFg;
            }
        }

        private void SetAlarmRow(string id, string state)
        {
            var rphActive = new List<string>();
            if (IsAlarmActive("AL_ROLL"))  rphActive.Add("ROLL");
            if (IsAlarmActive("AL_PITCH")) rphActive.Add("PITCH");
            if (IsAlarmActive("AL_HEAVE")) rphActive.Add("HEAVE");

            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateAlarmState("R/P/H",
                rphActive.Count > 0 ? string.Join(",", rphActive) : "Normal");
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateAlarmState("WIND",
                IsAlarmActive("AL_WIND") ? "WIND" : "Normal");
        }

        private void OnAlarmRaised(Alarm a)
        {
            SetAlarmColor(a.Id, Palette.AlarmActiveFg);
            RefreshAlarmBanner();
            SetAlarmRow(a.Id, "Active");
            _logger.LogAlarmEvent("RAISED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider());

        }
        private void OnAlarmCleared(Alarm a)
        {
            SetAlarmColor(a.Id, Palette.OkFg);
            RefreshAlarmBanner();
            SetAlarmRow(a.Id, "Normal");
            _logger.LogAlarmEvent("CLEARED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider());
        }
        private void OnAlarmAcked(Alarm a)
        {
            SetAlarmColor(a.Id, Palette.AlarmAckFg);
            RefreshAlarmBanner();
            SetAlarmRow(a.Id, "Ack");
            _logger.LogAlarmEvent("ACKED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider());
        }

        private void SetAlarmColor(string id, Color c)
        {
            if (id == "AL_WIND")  { lblWindSpeed.ForeColor  = c; lblWindRelated.ForeColor = c; }
            else if (id == "AL_ROLL")  lblRoll.ForeColor  = c;
            else if (id == "AL_PITCH") lblPitch.ForeColor = c;
            else if (id == "AL_HEAVE") lblHeave.ForeColor = c;
        }

        private void lblWindSpeed_Click(object s, EventArgs e)  => _alarmEngine.Ack("AL_WIND");
        private void lblRoll_Click(object s, EventArgs e)        => _alarmEngine.Ack("AL_ROLL");
        private void lblPitch_Click(object s, EventArgs e)       => _alarmEngine.Ack("AL_PITCH");
        private void lblHeave_Click(object s, EventArgs e)       => _alarmEngine.Ack("AL_HEAVE");

        // ── TREND BUTTONS ─────────────────────────────────────────────────────

        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();

            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Palette.BtnPrimaryBg, Palette.BtnPrimaryFg);
            btnTrend1.Click += (s, e) =>
            {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
                RefreshTrendButtonColors();
            };

            Button btnTrend2 = CreateMenuButton("TREND WIND", Palette.BtnPrimaryBg, Palette.BtnPrimaryFg);
            btnTrend2.Click += (s, e) =>
            {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Wind;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
                RefreshTrendButtonColors();
            };

            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Palette.BtnActiveBg, Palette.BtnActiveFg);
            btnZoom.Click += (s, e) =>
            {
                // Cycle: 30s → 2 min → 20 min → 30s
                if      (_currentViewMinutes <= 0.5) _currentViewMinutes = 2.0;
                else if (_currentViewMinutes <= 2.0) _currentViewMinutes = 20.0;
                else                                  _currentViewMinutes = 0.5;

                btnZoom.Text = _currentViewMinutes < 1.0
                    ? $"VIEW: {(int)(_currentViewMinutes * 60)}s"
                    : $"VIEW: {_currentViewMinutes:0} Min";
                _trendControl.SetViewWindow(_currentViewMinutes);
            };

            Button btnToggleSplit = CreateMenuButton("MODE: COMBINED", Palette.OkBg, Palette.OkFg);
            btnToggleSplit.Click += (s, e) =>
            {
                _isSeparateTrend         = !_isSeparateTrend;
                btnToggleSplit.Text      = _isSeparateTrend ? "MODE: SPLIT" : "MODE: COMBINED";
                btnToggleSplit.BackColor = _isSeparateTrend ? Palette.BtnSettingsBg : Palette.OkBg;
                btnToggleSplit.ForeColor = _isSeparateTrend ? Palette.BtnSettingsFg : Palette.OkFg;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            Button btnTrend3 = CreateMenuButton("TREND ENV", Palette.BtnPrimaryBg, Palette.BtnPrimaryFg);
            btnTrend3.Click += (s, e) =>
            {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Env;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
                RefreshTrendButtonColors();
            };

            _trendModeButtons = new[] { btnTrend1, btnTrend2, btnTrend3 };
            RefreshTrendButtonColors();

            panelTrendButtons.Controls.AddRange(new Control[] { btnTrend1, btnTrend2, btnTrend3, btnToggleSplit, btnZoom });
        }

        private void RefreshTrendButtonColors()
        {
            if (_trendModeButtons == null) return;
            var modes = new[]
            {
                HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion,
                HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Wind,
                HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Env
            };
            for (int i = 0; i < _trendModeButtons.Length; i++)
            {
                bool active = modes[i] == _currentTrendMode;
                _trendModeButtons[i].BackColor = active ? Palette.BtnActiveBg     : Palette.BtnPrimaryBg;
                _trendModeButtons[i].ForeColor = active ? Palette.BtnActiveFg     : Palette.BtnPrimaryFg;
                _trendModeButtons[i].FlatAppearance.BorderColor = active ? Palette.BtnActiveBorder : Palette.BorderCard;
                _trendModeButtons[i].FlatAppearance.BorderSize  = active ? 2 : 1;
            }
        }

        // ── UTILITY ───────────────────────────────────────────────────────────

        private void InitializeTasks()
        {
            _taskList = new List<DeviceTask>();
            foreach (var t in ConfigForm.Tasks)
                _taskList.Add(new DeviceTask { TaskName = t.TaskName, PortName = t.PortName, BaudRate = t.BaudRate, SentenceType = t.SentenceType });
        }

        private void UpdateBadge(Label lbl, string name, bool isStale, double age)
        {
            if (age > 900)   { lbl.Text = $"{name}: WAIT"; lbl.BackColor = Palette.WaitBg; lbl.ForeColor = Palette.WaitFg; }
            else if (isStale){ lbl.Text = $"{name}: LOST"; lbl.BackColor = Palette.LostBg; lbl.ForeColor = Palette.LostFg; }
            else             { lbl.Text = $"{name}: OK";   lbl.BackColor = Palette.OkBg;   lbl.ForeColor = Palette.OkFg; }
        }

        private static void SetLabelText(Label lbl, string text)
        {
            if (lbl != null && lbl.Text != text) lbl.Text = text;
        }

        private void UpdateLabelStatus(Label lbl, bool isAlive)
        {
            if (lbl == null) return;
            if (!isAlive) lbl.ForeColor = Palette.TextDim;
            else if (lbl.ForeColor == Palette.TextDim) lbl.ForeColor = Palette.OkFg;
        }

        private static void AddBadgeBorder(Label lbl)
        {
            lbl.Paint += (s, e) =>
            {
                using var pen = new Pen(Palette.BorderCard, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, lbl.Width - 1, lbl.Height - 1);
            };
        }

        private void EnsureAlarmBadge()
        {
            if (lblAlarmStatus != null) return;
            lblAlarmStatus = new Label
            {
                AutoSize  = false,
                Width     = 220, Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Palette.AlarmNormalBg,
                ForeColor = Palette.AlarmNormalFg,
                Text      = "✔ NORMAL",
                Margin    = new Padding(16, 7, 5, 5)
            };
            AddBadgeBorder(lblAlarmStatus);
            _topMenu.Controls.Add(lblAlarmStatus);
        }

        private void SetupStatusBadges()
        {
            Label CreateBadge(string text)
            {
                var lbl = new Label
                {
                    Text        = text,
                    AutoSize    = false,
                    Size        = new Size(130, 30),
                    TextAlign   = ContentAlignment.MiddleCenter,
                    Font        = new Font("Segoe UI", 9, FontStyle.Bold),
                    BackColor   = Palette.WaitBg,
                    ForeColor   = Palette.WaitFg,
                    Margin      = new Padding(4, 7, 0, 5),
                    BorderStyle = BorderStyle.None
                };
                AddBadgeBorder(lbl);
                return lbl;
            }

            lblStatGPS     = CreateBadge("GPS: WAIT");
            lblStatWind    = CreateBadge("WIND: WAIT");
            lblStatMotion  = CreateBadge("R/P/H: WAIT");
            lblStatHeading = CreateBadge("HDG: WAIT");
            lblStatMeteo   = CreateBadge("METEO: WAIT");
            _topMenu.Controls.AddRange(new Control[] { lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading, lblStatMeteo });
        }

        private Button CreateMenuButton(string text, Color bg, Color? fg = null)
        {
            var btn = new Button
            {
                Text        = text,
                BackColor   = bg,
                ForeColor   = fg ?? Color.White,
                FlatStyle   = FlatStyle.Flat,
                AutoSize    = true,
                MinimumSize = new Size(0, 30),
                MaximumSize = new Size(0, 30),
                Padding     = new Padding(10, 0, 10, 0),
                Margin      = new Padding(4, 2, 4, 2),
                Font        = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderColor = Palette.BorderCard;
            btn.FlatAppearance.BorderSize  = 1;
            return btn;
        }

        private void LoadImageFromFile(PictureBox picBox, string fileName)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "Images", fileName);
                if (!File.Exists(path)) return;
                Image old = picBox.Image;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var tmp = Image.FromStream(stream);
                picBox.Image = (Image)tmp.Clone(); // clone tách khỏi stream trước khi stream đóng
                old?.Dispose();
            }
            catch { }
        }

        private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (radius == 0) { path.AddRectangle(bounds); return path; }
            var arc = new Rectangle(bounds.Location, new Size(d, d));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Palette.ApplyTitleBarTheme(Handle);
            this.DoubleBuffered = true;
            EnableDoubleBuffer(tableLayoutPanel1);
            EnableDoubleBuffer(tableLayoutPanel2);
            EnableDoubleBuffer(tableLayoutPanel3);
            EnableDoubleBuffer(tableLayoutPanelTrend);
        }

        private void EnableDoubleBuffer(Control c)
        {
            if (c == null) return;
            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(c, true, null);
        }

        private void BtnSettings_Click(object s, EventArgs e)
        {
            if (new LoginForm().ShowDialog() == DialogResult.OK)
            {
                var result = new ConfigForm().ShowDialog();
                SystemConfig.Apply(ConfigService.Load());
                if (result == DialogResult.OK)
                {
                    _lblProjectTitle.Text = $"  {SystemConfig.ShipName ?? "HELIDECK MONITOR"}";
                    RestartDataPipeline();
                }
            }
        }

        private void RestartDataPipeline()
        {
            var oldSim   = _simEngine;
            var oldMru   = _mruService;
            var oldMeteo = _meteoService;
            var oldCom   = _comEngine;

            _simEngine    = null;
            _mruService   = null;
            _meteoService = null;
            _comEngine    = null;

            // WinForms timer — dừng trên UI thread trước
            try { oldSim?.Stop(); } catch { }

            // SerialPort.Close / thread join có thể block → background thread
            Task.Run(() =>
            {
                try { oldMru?.SendGoToConfig(); } catch { }  // sets _running=false, joins thread (max 1.5s)
                try { oldMru?.Stop();           } catch { }
                try { oldMeteo?.Stop();         } catch { }
                try { oldCom?.Dispose();        } catch { }
            }).ContinueWith(_ =>
            {
                Invoke(new Action(() =>
                {
                    InitializeTasks();
                    _nmeaParser.SetPortTasks(ConfigForm.Tasks);
                    _comEngine = new ComEngine();
                    _comEngine.OnDataReceived += OnComDataReceived;
                    StartDataPipeline();
                }));
            });
        }

        // Stubs giữ tương thích Designer
        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {
            var widths = tableLayoutPanel1.GetColumnWidths();
            if (widths.Length < 2) return;
            int x = widths[0];
            using var pen = new Pen(Palette.BorderCard, 2);
            e.Graphics.DrawLine(pen, x, 0, x, tableLayoutPanel1.Height);
        }

        // ── DATA PIPELINE START/STOP ─────────────────────────────────────────

        private void StartDataPipeline()
        {
            if (SystemConfig.IsSimulationMode)
            {
                _simEngine = new SimulationEngine();
                _simEngine.Start(OnComDataReceived);
            }
            else
            {
                _comEngine.Initialize(_taskList);
            }

            var meteoTask = ConfigForm.Tasks.Find(t => t.TaskName == "METEO");
            if (meteoTask != null)
            {
                _meteoService = new MeteoService(meteoTask.PortName, meteoTask.BaudRate, _comEngine);
                _meteoService.Start();
            }

            var mruTask = ConfigForm.Tasks.Find(t => t.TaskName == "MRU");
            if (mruTask != null)
            {
                _mruService = new MruService(mruTask.PortName ?? "", mruTask.BaudRate, _comEngine);
                _mruService.Start();
            }

            UpdateSimTitle();
        }

        private void UpdateSimTitle()
        {
            string baseTitle = this.Text.Replace("  [SIMULATION]", "").TrimEnd();
            this.Text = SystemConfig.IsSimulationMode ? baseTitle + "  [SIMULATION]" : baseTitle;
        }

        // Panel that always fills its background from the current Palette,
        // so theme switches take effect without any BackColor bookkeeping.
        private sealed class CardPanel : Panel
        {
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using var brush = new SolidBrush(Palette.CardFace);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }
}
