using HelideckVer2.Models;
using HelideckVer2.Services;
using HelideckVer2.Services.Parsing;
using HelideckVer2.UI.Theme;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

        private Tag _windTag, _rollTag, _pitchTag, _heaveTag;
        private Label lblAlarmStatus;
        private FlowLayoutPanel _topMenu;
        private TabControl _mainTabControl;

        private const double BufferMinutes    = 20;
        private double _currentViewMinutes    = 2.0;

        // Biến lưu trữ GPS (sẽ đẩy vào DataHub)
        private double _speedKnot;
        private string _currentLat = "NO FIX";
        private string _currentLon = "NO FIX";

        private System.Windows.Forms.Timer _healthTimer, _snapshotTimer, _chartUpdateTimer;
        private System.Windows.Forms.Timer _uiUpdateTimer;

        private double _rollOffset  = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm    = 10.0;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;
        private Label[] _unitLabels = new Label[9];

        private HelideckVer2.UI.Controls.TrendChartControl _trendControl;

        // ── CONSTRUCTOR ───────────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();

            var cfg = ConfigService.Load();
            SystemConfig.Apply(cfg);

            if (cfg.Tasks != null)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = ConfigForm.Tasks.Find(x => x.TaskName == saved.TaskName);
                    if (t != null)
                    {
                        t.PortName = saved.PortName;
                        if (saved.BaudRate > 0) t.BaudRate = saved.BaudRate;
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
            _nmeaParser.OnMotionParsed   += HandleMotion;
            _nmeaParser.OnPositionParsed += HandlePosition;
            _nmeaParser.OnSpeedParsed    += HandleSpeed;

            // COM Engine
            _comEngine = new ComEngine();
            _comEngine.OnDataReceived += OnComDataReceived;

            if (SystemConfig.IsSimulationMode)
            {
                var sim = new SimulationEngine();
                sim.Start(OnComDataReceived);
                this.Text += "  [SIMULATION]";
            }
            else
            {
                _comEngine.Initialize(_taskList);
            }

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

            // Trend chart
            _trendControl = new HelideckVer2.UI.Controls.TrendChartControl { Dock = DockStyle.Fill };
            panelChartHost.Controls.Clear();
            panelChartHost.Controls.Add(_trendControl);

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => _trendControl.Render();
            _chartUpdateTimer.Start();
        }

        // ── LAYOUT SETUP ──────────────────────────────────────────────────────

        private void SetupMainLayout()
        {
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Palette.AppBg };
            this.Controls.Add(root);

            _topMenu = new FlowLayoutPanel
            {
                Dock         = DockStyle.Top,
                Height       = 44,
                BackColor    = Palette.PanelBg,
                Padding      = new Padding(6),
                WrapContents = false
            };

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
            btnSetting.FlatAppearance.BorderSize = 0;
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
            btnDataList.FlatAppearance.BorderSize = 0;
            btnDataList.Click += (s, e) => new DataListForm().Show(this);

            pnlBottom.Controls.Add(btnSetting);
            pnlBottom.Controls.Add(btnDataList);

            Button btnOver = CreateMenuButton("OVERVIEW", Color.Transparent);
            btnOver.Click += (s, e) => _mainTabControl?.SelectTab(0);
            _topMenu.Controls.Add(btnOver);

            _mainTabControl = new TabControl
            {
                Dock        = DockStyle.Fill,
                Appearance  = TabAppearance.FlatButtons,
                ItemSize    = new Size(0, 1),
                SizeMode    = TabSizeMode.Fixed
            };

            var tabOverview = new TabPage("Overview") { BackColor = Palette.AppBg };
            tableLayoutPanel1.Parent = tabOverview;
            tableLayoutPanel1.Dock   = DockStyle.Fill;
            tableLayoutPanel1.Visible = true;
            _mainTabControl.TabPages.Add(tabOverview);

            root.Controls.Add(_mainTabControl);
            root.Controls.Add(pnlBottom);
            root.Controls.Add(_topMenu);
        }

        // Thêm nhãn tên dự án vào đầu panel phải (tableLayoutPanel4)
        private void SetupRightPanelTitle()
        {
            string shipName = SystemConfig.ShipName ?? "HELIDECK MONITOR";
            var lblTitle = new Label
            {
                Text      = $"  PROJECT:  {shipName}",
                Dock      = DockStyle.Top,
                Height    = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 50, 85),
                ForeColor = Color.White,
                Padding   = new Padding(8, 0, 0, 0)
            };

            // Chèn label vào panel chứa ảnh chiếu (tableLayoutPanel4)
            tableLayoutPanel4.Controls.Add(lblTitle, 0, 0);
            tableLayoutPanel4.SetColumnSpan(lblTitle, 2);

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
            tableLayoutPanel2.BackColor       = Color.FromArgb(18, 26, 40);
            tableLayoutPanel2.Padding         = new Padding(4);
            tableLayoutPanel2.Controls.Clear();
            tableLayoutPanel2.ColumnStyles.Clear();
            tableLayoutPanel2.RowStyles.Clear();

            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.RowCount = 5;
            for (int i = 0; i < 5; i++)
                tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));

            // Nhãn và giá trị
            // items[0]=POSITION(full-width), [1-8] = SPEED,HEADING,ROLL,PITCH,HEAVE,H.PERIOD,WIND SPD, WIND DIR
            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9 };
            string[] headers = {
                "POSITION", "SPEED", "HEADING",
                "ROLL", "PITCH", "HEAVE",
                "H.PERIOD", "WIND SPD", "WIND DIR"
            };
            Label[] values = {
                lblPosition, lblSpeed, lblHeading,
                lblRoll, lblPitch, lblHeave,
                lblHeaveCycle, lblWindSpeed, lblWindRelated
            };
            // Đơn vị hiển thị riêng ở label nhỏ bên dưới – không nhúng vào text số
            string[] unitTexts = { "", "kn", "°", "°", "°", "cm", "s", "m/s", "°" };

            Color cardBg  = Color.FromArgb(28, 42, 62);
            Color sepClr  = Color.FromArgb(50, 70, 100);
            Color titleFg = Color.FromArgb(150, 170, 200);

            for (int i = 0; i < 9; i++)
            {
                if (titles[i] == null || values[i] == null) continue;

                titles[i].Text      = headers[i];
                titles[i].AutoSize  = false;
                titles[i].Dock      = DockStyle.Top;
                titles[i].TextAlign = ContentAlignment.MiddleCenter;
                titles[i].ForeColor = titleFg;
                titles[i].BackColor = cardBg;

                values[i].Text      = "---";
                values[i].AutoSize  = false;
                values[i].Dock      = DockStyle.Fill;
                values[i].TextAlign = ContentAlignment.MiddleCenter;
                values[i].BackColor = cardBg;
                values[i].ForeColor = Color.FromArgb(100, 220, 130);

                if (i == 0) values[i].ForeColor = Color.FromArgb(100, 180, 255); // POSITION – blue
                if (i == 5) values[i].ForeColor = Color.FromArgb(255, 160, 60);  // HEAVE – orange

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
                    Height    = hasUnit ? 18 : 0,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = cardBg,
                    ForeColor = Color.FromArgb(80, 120, 160),
                    Font      = new Font("Segoe UI", 9f)
                };
                _unitLabels[i] = unitLbl;

                var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = cardBg };
                EnableDoubleBuffer(card);
                int idx = i;

                card.Resize += (s, e) =>
                {
                    if (card.Height <= 0 || card.Width <= 0) return;

                    titles[idx].Height = Math.Max(20, (int)(card.Height * 0.28f));

                    if (_unitLabels[idx] != null && !string.IsNullOrEmpty(_unitLabels[idx].Text))
                    {
                        int unitH = Math.Max(14, (int)(card.Height * 0.18f));
                        _unitLabels[idx].Height = unitH;
                        float uf = Math.Max(7f, Math.Min(card.Height * 0.09f, 11f));
                        SafeSetFont(_unitLabels[idx], uf);
                    }

                    float tf = Math.Max(8f, Math.Min(Math.Min(card.Height * 0.10f, card.Width * 0.08f), 18f));
                    SafeSetFont(titles[idx], tf);

                    // vf: số thuần (không có unit trong text) → có thể dùng hệ số rộng hơn
                    float vf = idx == 0
                        ? Math.Max(8f,  Math.Min(Math.Min(card.Height * 0.14f, card.Width * 0.055f), 20f))
                        : Math.Max(10f, Math.Min(Math.Min(card.Height * 0.32f, card.Width * 0.22f), 42f));
                    SafeSetFont(values[idx], vf);
                };

                card.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using var path = GetRoundedRect(rect, 10);
                    using var pen  = new Pen(sepClr, 1);
                    g.DrawPath(pen, path);
                    int lineY = titles[idx].Height;
                    using var sp = new Pen(Color.FromArgb(45, 65, 90), 1);
                    g.DrawLine(sp, 8, lineY, card.Width - 8, lineY);
                };

                // Thứ tự Add quan trọng: Fill được dock sau cùng → add trước
                card.Controls.Add(values[i]);
                card.Controls.Add(unitLbl);   // Bottom dock trước Fill
                card.Controls.Add(titles[i]); // Top dock trước Bottom

                if (i == 0)
                {
                    tableLayoutPanel2.Controls.Add(card, 0, 0);
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
            _windTag.Update(wSpeed);
        }

        private void HandleMotion(double r, double p, double h)
        {
            double roll  = r + _rollOffset;
            double pitch = p + _pitchOffset;
            _rawHeaveCm  = h; // giữ dấu để tính zero-crossing
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateNumericData("R/P/H",
                Math.Abs(roll), Math.Abs(pitch), Math.Abs(h));
            _rollTag.Update(Math.Abs(roll));
            _pitchTag.Update(Math.Abs(pitch));
            _heaveTag.Update(Math.Abs(h));
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
            // 1. Đánh giá alarm
            _alarmEngine.Evaluate();

            // 2. Snapshot
            var snap = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();

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
            lblSpeed.Text      = $"{snap.GpsSpeedKnot:0.0}";

            if (snap.GpsLat == "NO FIX")
                lblPosition.Text = "NO FIX";
            else
                lblPosition.Text = $"{snap.GpsLon}\n{snap.GpsLat}";

            lblHeaveCycle.Text = snap.HeavePeriodSec > 0
                ? $"{snap.HeavePeriodSec:0.0}"
                : "---";

            // 5. Radar
            _radarControl.UpdateRadar(snap.Heading, snap.WindDirDeg);

            // 6. Tính chu kỳ Heave (zero-crossing âm→dương trên giá trị CÓ DẤU)
            DateTime now = DateTime.Now;
            if (_lastHeaveValue < 0 && _rawHeaveCm >= 0)
            {
                if (_lastZeroCrossTime.HasValue)
                {
                    double sec = (now - _lastZeroCrossTime.Value).TotalSeconds;
                    if (sec >= 2.0 && sec <= 30.0)
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
                lblAlarmStatus.BackColor = Color.DarkRed;
                lblAlarmStatus.ForeColor = Color.White;
            }
            else if (acked != null)
            {
                lblAlarmStatus.Text      = $"ACK: {acked.Id}";
                lblAlarmStatus.BackColor = Color.DarkOrange;
                lblAlarmStatus.ForeColor = Color.Black;
            }
            else
            {
                lblAlarmStatus.Text      = "✔ NORMAL";
                lblAlarmStatus.BackColor = Color.FromArgb(20, 80, 40);
                lblAlarmStatus.ForeColor = Color.Lime;
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
            SetAlarmColor(a.Id, Color.Red);
            RefreshAlarmBanner();
            SetAlarmRow(a.Id, "Active");
            _logger.LogAlarmEvent("RAISED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider());
        }
        private void OnAlarmCleared(Alarm a)
        {
            SetAlarmColor(a.Id, Color.FromArgb(100, 220, 130));
            RefreshAlarmBanner();
            SetAlarmRow(a.Id, "Normal");
            _logger.LogAlarmEvent("CLEARED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider());
        }
        private void OnAlarmAcked(Alarm a)
        {
            SetAlarmColor(a.Id, Color.Orange);
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

            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.SteelBlue);
            btnTrend1.Click += (s, e) =>
            {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            Button btnTrend2 = CreateMenuButton("TREND WIND", Color.SteelBlue);
            btnTrend2.Click += (s, e) =>
            {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Wind;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.DodgerBlue);
            btnZoom.Click += (s, e) =>
            {
                _currentViewMinutes = _currentViewMinutes == 2.0 ? 20.0 : 2.0;
                btnZoom.Text = $"VIEW: {_currentViewMinutes:0} Min";
                _trendControl.SetViewWindow(_currentViewMinutes);
            };

            Button btnToggleSplit = CreateMenuButton("MODE: COMBINED", Color.ForestGreen);
            btnToggleSplit.Click += (s, e) =>
            {
                _isSeparateTrend       = !_isSeparateTrend;
                btnToggleSplit.Text    = _isSeparateTrend ? "MODE: SPLIT" : "MODE: COMBINED";
                btnToggleSplit.BackColor = _isSeparateTrend ? Color.DarkOrange : Color.ForestGreen;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            panelTrendButtons.Controls.AddRange(new Control[] { btnTrend1, btnTrend2, btnToggleSplit, btnZoom });
        }

        // ── UTILITY ───────────────────────────────────────────────────────────

        private void InitializeTasks()
        {
            _taskList = new List<DeviceTask>();
            foreach (var t in ConfigForm.Tasks)
                _taskList.Add(new DeviceTask { TaskName = t.TaskName, PortName = t.PortName, BaudRate = t.BaudRate });
        }

        private void UpdateBadge(Label lbl, string name, bool isStale, double age)
        {
            if (age > 900)  { lbl.Text = $"{name}: WAIT"; lbl.BackColor = Color.FromArgb(60, 60, 60); }
            else if (isStale){ lbl.Text = $"{name}: LOST"; lbl.BackColor = Color.DarkRed; }
            else             { lbl.Text = $"{name}: OK";   lbl.BackColor = Color.FromArgb(20, 100, 40); }
        }

        private void UpdateLabelStatus(Label lbl, bool isAlive)
        {
            if (lbl == null) return;
            if (!isAlive) lbl.ForeColor = Color.Gray;
            else if (lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.FromArgb(100, 220, 130);
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
                BackColor = Color.FromArgb(20, 80, 40),
                ForeColor = Color.Lime,
                Text      = "✔ NORMAL",
                Margin    = new Padding(16, 7, 5, 5)
            };
            _topMenu.Controls.Add(lblAlarmStatus);
        }

        private void SetupStatusBadges()
        {
            Label CreateBadge(string text) => new Label
            {
                Text        = text,
                AutoSize    = false,
                Size        = new Size(130, 30),
                TextAlign   = ContentAlignment.MiddleCenter,
                Font        = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor   = Color.FromArgb(60, 60, 60),
                ForeColor   = Color.White,
                Margin      = new Padding(4, 7, 0, 5),
                BorderStyle = BorderStyle.None
            };

            lblStatGPS     = CreateBadge("GPS: WAIT");
            lblStatWind    = CreateBadge("WIND: WAIT");
            lblStatMotion  = CreateBadge("R/P/H: WAIT");
            lblStatHeading = CreateBadge("HDG: WAIT");
            _topMenu.Controls.AddRange(new Control[] { lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading });
        }

        private Button CreateMenuButton(string text, Color bg) => new Button
        {
            Text        = text,
            BackColor   = bg,
            ForeColor   = Color.White,
            FlatStyle   = FlatStyle.Flat,
            AutoSize    = true,
            MinimumSize = new Size(0, 30),
            MaximumSize = new Size(0, 30),
            Padding     = new Padding(10, 0, 10, 0),
            Margin      = new Padding(4, 2, 4, 2),
            Font        = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        private void LoadImageFromFile(PictureBox picBox, string fileName)
        {
            try
            {
                string folder = Path.Combine(Application.StartupPath, "Images");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                    picBox.Image = Image.FromStream(stream);
                }
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
                new ConfigForm().ShowDialog();
                SystemConfig.Apply(ConfigService.Load());
            }
        }

        // Stubs giữ tương thích Designer
        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) { }
    }
}
