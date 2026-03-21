using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using HelideckVer2.Services;
using HelideckVer2.Models;

namespace HelideckVer2
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. DATA DÙNG CHUNG CHO CỬA SỔ DATALIST POP-UP
        // ==========================================
        public static string[] GridValues = new string[4] { "", "", "", "" };
        public static double[] GridAges = new double[4] { 999, 999, 999, 999 };
        public static bool[] GridStales = new bool[4] { true, true, true, true };
        public static string[] GridLimits = new string[4] { "", "", "", "" };
        public static string[] GridAlarms = new string[4] { "Normal", "Normal", "Normal", "Normal" };
        private DateTime?[] _lastUpdate = new DateTime?[4];

        // ==========================================
        // 2. KHAI BÁO BIẾN TOÀN CỤC
        // ==========================================
        private bool _isSimulationMode = true; // Đổi thành FALSE khi ra tàu thực tế
        private System.Windows.Forms.Timer _simTimer;
        private double _simTimeCounter = 0;

        private ComEngine _comEngine;
        private DataLogger _logger;
        private Chart _trendChart;
        private List<DeviceTask> _taskList;

        private double _lastHeaveValue = 0;
        private DateTime? _lastZeroCrossTime = null;

        private AlarmEngine _alarmEngine;
        private bool _isLiveMode = true;

        private Tag _windTag, _rollTag, _pitchTag, _heaveTag;
        private Label lblAlarmStatus;
        private FlowLayoutPanel _topMenu;
        private TabControl _mainTabControl;

        private const int BufferMinutes = 20;
        private double _currentViewMinutes = 2.0;

        private enum TrendMode { Motion, Wind }
        private TrendMode _trendMode = TrendMode.Motion;

        private readonly List<TrendPoint> _motionBuffer = new();
        private readonly List<TrendPoint> _windBuffer = new();

        private struct TrendPoint { public double X, V1, V2, V3; }

        private double _speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm, _heavePeriodSec, _windSpeedMs, _windDirDeg;

        private System.Windows.Forms.Timer _healthTimer, _snapshotTimer, _chartUpdateTimer;
        private const double StaleSeconds = 2.0;

        private double _rollOffset = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm = 10.0;
        private bool _isProgrammaticScroll = false;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;

        // ===== BIẾN CHO HMI TAB (RADAR VIEW) =====
        private Panel _panelHmiGraphic;
        private Label _lblHmiPosVal, _lblHmiSpeedVal, _lblHmiHeadingVal, _lblHmiRollVal, _lblHmiPitchVal, _lblHmiHeaveVal, _lblHmiCycleVal, _lblHmiWindSpdVal, _lblHmiWindDirVal;
        private double _drawHeading = 0, _drawWindDir = 0;
        private readonly PointF[] _shipPoly = { new PointF(0, -100), new PointF(30, -30), new PointF(30, 90), new PointF(-30, 90), new PointF(-30, -30) };

        // ==========================================
        // 3. CONSTRUCTOR
        // ==========================================
        public Form1()
        {
            InitializeComponent();
            ApplyLeftPanelStyles();

            LoadImageFromFile(pictureBox1, "elevation_view.png");
            LoadImageFromFile(pictureBox2, "plan_view.png");
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;

            var cfg = ConfigService.Load();
            SystemConfig.Apply(cfg);

            if (cfg.Tasks != null && cfg.Tasks.Count > 0)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = ConfigForm.Tasks.Find(x => x.PortName == saved.PortName);
                    if (t != null) t.BaudRate = saved.BaudRate; // Hoặc ép 4800 nếu cần
                }
            }

            InitializeTasks();

            SetupMainLayout();
            EnsureAlarmBadge();
            SetupStatusBadges();
            SetupTrendButtonsNearChart();
            SetupIndustrialChart();

            _logger = new DataLogger();

            _snapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _snapshotTimer.Tick += (s, e) => _logger.LogSnapshot(_speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm, _heavePeriodSec, _windSpeedMs, _windDirDeg);
            _snapshotTimer.Start();

            _comEngine = new ComEngine();
            _comEngine.OnDataReceived += OnComDataReceived;

            if (_isSimulationMode)
            {
                StartSimulation();
                this.Text += " [SIMULATION MODE]";
            }
            else
            {
                _comEngine.Initialize(_taskList);
            }

            _alarmEngine = new AlarmEngine();
            _windTag = new Tag("WindSpeed"); _rollTag = new Tag("Roll"); _pitchTag = new Tag("Pitch"); _heaveTag = new Tag("Heave");

            _alarmEngine.Register(new Alarm("AL_WIND", _windTag, () => SystemConfig.WindMax));
            _alarmEngine.Register(new Alarm("AL_ROLL", _rollTag, () => SystemConfig.RMax));
            _alarmEngine.Register(new Alarm("AL_PITCH", _pitchTag, () => SystemConfig.PMax));
            _alarmEngine.Register(new Alarm("AL_HEAVE", _heaveTag, () => SystemConfig.HMax));

            _alarmEngine.AlarmRaised += OnAlarmRaised;
            _alarmEngine.AlarmCleared += OnAlarmCleared;
            _alarmEngine.AlarmAcked += OnAlarmAcked;

            RefreshAlarmBanner();
            RefreshDataListLimits();
            StartHealthTimer();

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => RenderChartFromBuffer();
            _chartUpdateTimer.Start();
        }

        // ==========================================
        // 4. UI SETUP & NAVIGATION
        // ==========================================
        private void SetupMainLayout()
        {
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };
            this.Controls.Add(root);

            _topMenu = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Color.LightGray, Padding = new Padding(6), WrapContents = false };
            Panel pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.WhiteSmoke };

            Button btnSetting = new Button { Text = "⚙ SETTINGS", Width = 150, Height = 40, Location = new Point(20, 10), BackColor = Color.Orange, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnSetting.Click += BtnSettings_Click;

            Button btnDataList = new Button { Text = "📋 VIEW DATA LIST", Width = 250, Height = 40, Location = new Point(190, 10), BackColor = Color.DodgerBlue, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnDataList.Click += (s, e) => { new DataListForm().ShowDialog(this); };

            pnlBottom.Controls.Add(btnSetting);
            pnlBottom.Controls.Add(btnDataList);

            Button btnOver = CreateMenuButton("OVERVIEW", Color.White);
            btnOver.Click += (s, e) => _mainTabControl.SelectTab(0);

            Button btnHMI = CreateMenuButton("HMI VIEW", Color.LightSkyBlue);
            btnHMI.Click += (s, e) => _mainTabControl.SelectTab(1);

            _topMenu.Controls.Add(btnOver);
            _topMenu.Controls.Add(btnHMI);

            _mainTabControl = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };

            var tabOverview = new TabPage("Overview") { BackColor = Color.WhiteSmoke };
            this.tableLayoutPanel1.Parent = tabOverview;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Visible = true;

            var tabHmi = new TabPage("HMI") { BackColor = Color.White };
            SetupHmiTab(tabHmi);

            _mainTabControl.TabPages.Add(tabOverview);
            _mainTabControl.TabPages.Add(tabHmi);

            root.Controls.Add(_mainTabControl);
            root.Controls.Add(pnlBottom);
            root.Controls.Add(_topMenu);
        }

        private void SetupHmiTab(TabPage tab)
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            tab.Controls.Add(layout);

            TableLayoutPanel tblData = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 9, ColumnCount = 2, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, BackColor = Color.White };
            tblData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            tblData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            EnableDoubleBuffer(tblData);

            void AddRow(int row, string title, ref Label valLabel, bool isGps = false)
            {
                tblData.RowStyles.Add(new RowStyle(SizeType.Percent, 11.1111f));
                Label lblTitle = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.DimGray, BackColor = Color.FromArgb(240, 240, 240), Padding = new Padding(10, 0, 0, 0) };
                valLabel = new Label { Text = "---", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Black, BackColor = Color.White };

                // === CHỈNH LẠI FONT BẰNG Y HỆT OVERVIEW Ở ĐÂY ===
                if (isGps)
                {
                    valLabel.Font = new Font("Segoe UI", 20, FontStyle.Bold); // GPS cỡ 20
                    valLabel.ForeColor = Color.DarkBlue;
                }
                else
                {
                    valLabel.Font = new Font("Segoe UI", 36, FontStyle.Bold); // Số liệu cỡ 36
                }

                tblData.Controls.Add(lblTitle, 0, row);
                tblData.Controls.Add(valLabel, 1, row);
            }

            AddRow(0, "POSITION", ref _lblHmiPosVal, true);
            AddRow(1, "SPEED", ref _lblHmiSpeedVal);
            AddRow(2, "HEADING", ref _lblHmiHeadingVal);
            AddRow(3, "ROLL", ref _lblHmiRollVal);
            AddRow(4, "PITCH", ref _lblHmiPitchVal);
            AddRow(5, "HEAVE", ref _lblHmiHeaveVal);
            AddRow(6, "H.PERIOD", ref _lblHmiCycleVal);
            AddRow(7, "WIND SPD", ref _lblHmiWindSpdVal);
            AddRow(8, "WIND DIR", ref _lblHmiWindDirVal);
            _lblHmiHeaveVal.ForeColor = Color.Red;

            layout.Controls.Add(tblData, 0, 0);

            _panelHmiGraphic = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, 235, 240) };
            EnableDoubleBuffer(_panelHmiGraphic);
            _panelHmiGraphic.Paint += HmiPanel_Paint;
            _panelHmiGraphic.Resize += (s, e) => _panelHmiGraphic.Invalidate();
            layout.Controls.Add(_panelHmiGraphic, 1, 0);
        }

        private void HmiPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _panelHmiGraphic.Width, h = _panelHmiGraphic.Height;
            Point center = new Point(w / 2, h / 2);
            int radius = Math.Min(w, h) / 2 - 40;
            if (radius < 50) return;

            using (SolidBrush bgBrush = new SolidBrush(Color.White)) g.FillEllipse(bgBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (Pen pBorder = new Pen(Color.Gray, 2)) g.DrawEllipse(pBorder, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            using (Pen pGrid = new Pen(Color.FromArgb(180, 200, 220), 1) { DashStyle = DashStyle.Dot })
            using (Pen pMain = new Pen(Color.LightSlateGray, 2))
            using (Font font = new Font("Segoe UI", 10, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.DarkSlateGray))
            {
                g.DrawEllipse(pGrid, center.X - radius * 0.66f, center.Y - radius * 0.66f, radius * 1.33f, radius * 1.33f);
                g.DrawEllipse(pGrid, center.X - radius * 0.33f, center.Y - radius * 0.33f, radius * 0.66f, radius * 0.66f);
                g.DrawLine(pGrid, center.X - radius, center.Y, center.X + radius, center.Y);
                g.DrawLine(pGrid, center.X, center.Y - radius, center.X, center.Y + radius);

                for (int i = 0; i < 360; i += 10)
                {
                    double rad = (i - 90) * Math.PI / 180.0;
                    int len = (i % 90 == 0) ? 15 : 8;
                    float x1 = center.X + (float)(Math.Cos(rad) * radius), y1 = center.Y + (float)(Math.Sin(rad) * radius);
                    float x2 = center.X + (float)(Math.Cos(rad) * (radius - len)), y2 = center.Y + (float)(Math.Sin(rad) * (radius - len));
                    g.DrawLine((i % 90 == 0) ? pMain : pGrid, x1, y1, x2, y2);
                    string txt = i.ToString();
                    float xT = center.X + (float)(Math.Cos(rad) * (radius + 28)), yT = center.Y + (float)(Math.Sin(rad) * (radius + 28));
                    var stateText = g.Save();
                    g.TranslateTransform(xT, yT); g.RotateTransform(i);
                    SizeF s = g.MeasureString(txt, font);
                    g.DrawString(txt, font, brush, -s.Width / 2, -s.Height / 2);
                    g.Restore(stateText);
                }
                g.DrawString("N", new Font("Segoe UI", 16, FontStyle.Bold), Brushes.DarkBlue, center.X - 11, center.Y - radius - 50);
            }

            var stateShip = g.Save();
            g.TranslateTransform(center.X, center.Y); g.RotateTransform((float)_drawHeading);
            using (SolidBrush b = new SolidBrush(Color.DarkGray)) g.FillPolygon(b, _shipPoly);
            using (Pen p = new Pen(Color.DimGray, 2)) g.DrawPolygon(p, _shipPoly);
            using (Pen pHead = new Pen(Color.DarkGoldenrod, 2) { DashStyle = DashStyle.Dash }) g.DrawLine(pHead, 0, -100, 0, -radius + 20);
            g.Restore(stateShip);

            var stateWind = g.Save();
            g.TranslateTransform(center.X, center.Y); g.RotateTransform((float)_drawWindDir + 180);
            int arrowPos = radius - 40;
            PointF[] arrow = { new PointF(0, -arrowPos - 30), new PointF(-10, -arrowPos), new PointF(10, -arrowPos) };
            using (SolidBrush b = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(b, arrow);
            using (Pen p = new Pen(Color.RoyalBlue, 3)) g.DrawLine(p, 0, -arrowPos, 0, -arrowPos + 50);
            g.Restore(stateWind);
        }

        private void ApplyLeftPanelStyles()
        {
            if (tableLayoutPanel1 != null)
            {
                tableLayoutPanel1.ColumnStyles.Clear();
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            }

            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel2.BackColor = Color.White;
            tableLayoutPanel2.ColumnStyles.Clear();
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            tableLayoutPanel2.RowStyles.Clear();
            tableLayoutPanel2.RowCount = 9;
            for (int i = 0; i < 9; i++) tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 11.1111f));

            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9 };
            string[] headers = { "POSITION", "SPEED", "HEADING", "ROLL", "PITCH", "HEAVE", "H.PERIOD", "WIND SPD", "WIND DIR" };
            for (int i = 0; i < 9; i++)
            {
                if (titles[i] == null) continue;
                titles[i].Text = headers[i];
                titles[i].AutoSize = false; titles[i].Dock = DockStyle.Fill; titles[i].TextAlign = ContentAlignment.MiddleLeft;
                titles[i].Padding = new Padding(10, 0, 0, 0); titles[i].Font = new Font("Segoe UI", 18, FontStyle.Bold);
                titles[i].ForeColor = Color.DimGray; titles[i].BackColor = Color.FromArgb(240, 240, 240); titles[i].Margin = new Padding(0);
            }

            Label[] values = { lblPosition, lblSpeed, lblHeading, lblRoll, lblPitch, lblHeave, lblHeaveCycle, lblWindSpeed, lblWindRelated };
            foreach (var v in values)
            {
                if (v == null) continue;
                v.Text = "---"; v.AutoSize = false; v.Dock = DockStyle.Fill; v.TextAlign = ContentAlignment.MiddleCenter;
                v.BackColor = Color.White; v.ForeColor = Color.Black; v.Margin = new Padding(0);

                // === CHỈNH LẠI FONT Ở ĐÂY ===
                if (v == lblPosition)
                {
                    v.Font = new Font("Segoe UI", 20, FontStyle.Bold); // Phóng to GPS lên 20
                    v.ForeColor = Color.DarkBlue;
                }
                else
                {
                    v.Font = new Font("Segoe UI", 36, FontStyle.Bold); // Phóng to số liệu khác lên 36
                }
            }
            lblHeave.ForeColor = Color.Red;
        }

        // ==========================================
        // 5. CẬP NHẬT TÍN HIỆU & ALARM CHO POP-UP
        // ==========================================
        private int FindRowIndex(string taskName)
        {
            return taskName switch { "GPS" => 0, "WIND" => 1, "R/P/H" => 2, "HEADING" => 3, _ => -1 };
        }

        private void StartHealthTimer()
        {
            if (_healthTimer != null) return;
            _healthTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _healthTimer.Tick += (s, e) =>
            {
                string[] tasks = { "GPS", "WIND", "R/P/H", "HEADING" };
                for (int i = 0; i < 4; i++)
                {
                    string taskName = tasks[i];
                    bool isStale = true;
                    double age = 999.0;

                    if (_lastUpdate[i] != null)
                    {
                        age = (DateTime.Now - _lastUpdate[i].Value).TotalSeconds;
                        if (age <= StaleSeconds) isStale = false;
                    }

                    GridAges[i] = age;
                    GridStales[i] = isStale;

                    switch (taskName)
                    {
                        case "GPS": UpdateBadge(lblStatGPS, "GPS", isStale, age); UpdateLabelStatus(lblPosition, !isStale); UpdateLabelStatus(lblSpeed, !isStale); break;
                        case "WIND": UpdateBadge(lblStatWind, "WIND", isStale, age); UpdateLabelStatus(lblWindSpeed, !isStale); UpdateLabelStatus(lblWindRelated, !isStale); break;
                        case "R/P/H": UpdateBadge(lblStatMotion, "R/P/H", isStale, age); UpdateLabelStatus(lblRoll, !isStale); UpdateLabelStatus(lblPitch, !isStale); UpdateLabelStatus(lblHeave, !isStale); UpdateLabelStatus(lblHeaveCycle, !isStale); break;
                        case "HEADING": UpdateBadge(lblStatHeading, "HEADING", isStale, age); UpdateLabelStatus(lblHeading, !isStale); break;
                    }
                }
            };
            _healthTimer.Start();
        }

        private void UpdateGridByTask(string task, string val)
        {
            int r = FindRowIndex(task);
            if (r >= 0)
            {
                GridValues[r] = val;
                _lastUpdate[r] = DateTime.Now;
            }
        }

        private void RefreshDataListLimits()
        {
            GridLimits[1] = $"{SystemConfig.WindMax:0.0}";
            GridLimits[2] = $"R:{SystemConfig.RMax:0.0} P:{SystemConfig.PMax:0.0} H:{SystemConfig.HMax:0.0}";
            GridLimits[0] = "N/A";
            GridLimits[3] = "N/A";
        }

        private bool IsAlarmActive(string alarmId)
        {
            foreach (var alarm in _alarmEngine.GetAll()) { if (alarm.Id == alarmId && alarm.IsActive) return true; }
            return false;
        }

        private void SetAlarmRow(string changedAlarmId, string state)
        {
            List<string> activeRPH = new List<string>();
            if (IsAlarmActive("AL_ROLL")) activeRPH.Add("ROLL");
            if (IsAlarmActive("AL_PITCH")) activeRPH.Add("PITCH");
            if (IsAlarmActive("AL_HEAVE")) activeRPH.Add("HEAVE");

            if (activeRPH.Count > 0) GridAlarms[2] = string.Join(", ", activeRPH);
            else GridAlarms[2] = "Normal";

            if (IsAlarmActive("AL_WIND")) GridAlarms[1] = "WIND";
            else GridAlarms[1] = "Normal";
        }

        private void RefreshAlarmBanner()
        {
            if (lblAlarmStatus == null) return;
            var all = new List<Alarm>(_alarmEngine.GetAll());
            var activeUnacked = all.Find(a => a.IsActive && !a.IsAcked);
            var activeAcked = all.Find(a => a.IsActive && a.IsAcked);

            if (activeUnacked != null) { lblAlarmStatus.Text = $"ALARM: {activeUnacked.Id}"; lblAlarmStatus.BackColor = Color.DarkRed; lblAlarmStatus.ForeColor = Color.White; return; }
            if (activeAcked != null) { lblAlarmStatus.Text = $"ACK: {activeAcked.Id}"; lblAlarmStatus.BackColor = Color.Orange; lblAlarmStatus.ForeColor = Color.Black; return; }

            lblAlarmStatus.Text = "NORMAL"; lblAlarmStatus.BackColor = Color.Black; lblAlarmStatus.ForeColor = Color.Lime;
        }

        private void OnAlarmRaised(Alarm a) { SetAlarmColor(a.Id, Color.Red); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Active"); _logger.LogAlarmEvent("RAISED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void OnAlarmCleared(Alarm a) { SetAlarmColor(a.Id, Color.Black); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Normal"); _logger.LogAlarmEvent("CLEARED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void OnAlarmAcked(Alarm a) { SetAlarmColor(a.Id, Color.Orange); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Ack"); _logger.LogAlarmEvent("ACKED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void SetAlarmColor(string id, Color c) { if (id == "AL_WIND") { lblWindSpeed.ForeColor = c; lblWindRelated.ForeColor = c; } else if (id == "AL_ROLL") lblRoll.ForeColor = c; else if (id == "AL_PITCH") lblPitch.ForeColor = c; else if (id == "AL_HEAVE") lblHeave.ForeColor = c; }

        private void lblWindSpeed_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_WIND");
        private void lblRoll_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_ROLL");
        private void lblPitch_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_PITCH");
        private void lblHeave_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_HEAVE");

        // ==========================================
        // 6. XỬ LÝ COM PORT, PARSE VÀ TREND CHART
        // ==========================================
        private void StartSimulation()
        {
            _simTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            Random rnd = new Random();
            _simTimer.Tick += (s, e) =>
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                _simTimeCounter += 1.0;
                string lat = "1045." + rnd.Next(100, 999), lon = "10640." + rnd.Next(100, 999);
                double speed = 5.0 + rnd.NextDouble() * 2, windSpeed = 15.0 + (rnd.NextDouble() * 10 - 5), windDir = 120.0 + (rnd.NextDouble() * 20 - 10);
                double roll = 2.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 8.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double pitch = 1.5 * Math.Cos(_simTimeCounter * 2 * Math.PI / 6.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double heave = 30.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 5.0) + (rnd.NextDouble() * 2 - 1);
                double heading = 180.0 + (rnd.NextDouble() * 4 - 2);

                OnComDataReceived("COM1", $"$GPGGA,083226.00,{lat},N,{lon},E,1,08,1.0,10.0,M,0.0,M,,*XX");
                OnComDataReceived("COM1", $"$GPVTG,360.0,T,348.7,M,0.0,N,{speed.ToString("0.0", ci)},K*XX");
                OnComDataReceived("COM2", $"$WIMWV,{windDir.ToString("0.0", ci)},R,{windSpeed.ToString("0.0", ci)},M,A*XX");
                OnComDataReceived("COM3", $"$CNTB,{roll.ToString("0.00", ci)},{pitch.ToString("0.00", ci)},{heave.ToString("0.0", ci)}");
                OnComDataReceived("COM4", $"$HEHDT,{heading.ToString("0.0", ci)},T*XX");
            };
            _simTimer.Start();
        }

        private void OnComDataReceived(string portName, string rawData) { this.Invoke(new Action(() => ParseAndDisplay(portName, rawData))); }

        private void ParseAndDisplay(string portName, string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data) || !data.StartsWith("$")) return;
                int starIndex = data.IndexOf('*');
                if (starIndex > -1) data = data.Substring(0, starIndex);
                string[] p = data.Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var style = System.Globalization.NumberStyles.Any;

                if (p[0].EndsWith("HDT") && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                    {
                        _headingDeg = heading; lblHeading.Text = $"{heading:0.0}°"; UpdateGridByTask("HEADING", $"{heading:0.0}");
                        _drawHeading = heading; SetLabelText(_lblHmiHeadingVal, $"{heading:0.0}°"); _panelHmiGraphic?.Invalidate();
                    }
                    return;
                }
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) && double.TryParse(p[3], style, culture, out double wSpeed))
                    {
                        _windSpeedMs = wSpeed; _windDirDeg = wDir;
                        lblWindSpeed.Text = $"{wSpeed:0.0} m/s"; lblWindRelated.Text = $"{wDir:0}°"; UpdateGridByTask("WIND", $"{wSpeed:0.0} / {wDir:0}");
                        _windTag.Update(wSpeed); _alarmEngine.Evaluate();
                        lock (_windBuffer) { _windBuffer.Add(new TrendPoint { X = DateTime.Now.ToOADate(), V1 = wSpeed, V2 = wDir }); if (_windBuffer.Count > 0 && _windBuffer[0].X < DateTime.Now.AddMinutes(-BufferMinutes - 1).ToOADate()) _windBuffer.RemoveAt(0); }
                        _drawWindDir = wDir; SetLabelText(_lblHmiWindSpdVal, $"{wSpeed:0.0} m/s"); SetLabelText(_lblHmiWindDirVal, $"{wDir:0}°"); _panelHmiGraphic?.Invalidate();
                    }
                    return;
                }
                if ((p[0].EndsWith("CNTB") && p.Length >= 4) || (p[0] == "$PRDID" && p.Length >= 3))
                {
                    double r = 0, pi = 0, h = 0; bool ok = false;
                    if (p[0].EndsWith("CNTB")) { ok = double.TryParse(p[1], style, culture, out r) && double.TryParse(p[2], style, culture, out pi) && double.TryParse(p[3], style, culture, out h); }
                    else { if (double.TryParse(p[1], style, culture, out double rawP) && double.TryParse(p[2], style, culture, out double rawR)) { r = rawR; pi = rawP; h = _heaveArm * Math.Sin(rawP * Math.PI / 180.0); ok = true; } }
                    if (ok) DisplayMotionData(r + _rollOffset, pi + _pitchOffset, h);
                    return;
                }
                if (p[0].EndsWith("GGA") && p.Length >= 6)
                {
                    string lat = p[2], latD = p[3], lon = p[4], lonD = p[5];
                    if (lat.Length > 4 && lon.Length > 5)
                    {
                        string fLat = $"{lat.Substring(0, 2)}°{lat.Substring(2)}'{latD}", fLon = $"{lon.Substring(0, 3)}°{lon.Substring(3)}'{lonD}";
                        lblPosition.Text = $"{fLon}\r\n{fLat}"; UpdateGridByTask("GPS", $"{fLat} {fLon}"); SetLabelText(_lblHmiPosVal, $"{fLon}\r\n{fLat}");
                    }
                    else lblPosition.Text = "NO FIX";
                    return;
                }
                if (p[0] == "$GPVTG")
                {
                    double k = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(k)) { _speedKnot = k; lblSpeed.Text = $"{k:0.0} kn"; SetLabelText(_lblHmiSpeedVal, $"{k:0.0} kn"); }
                }
            }
            catch { }
        }

        private void DisplayMotionData(double r, double p, double h)
        {
            _rollDeg = r; _pitchDeg = p; _heaveCm = h;
            lblRoll.Text = $"{r:0.00}°"; lblPitch.Text = $"{p:0.00}°"; lblHeave.Text = $"{h:0.0} cm";

            DateTime now = DateTime.Now;
            if (_lastHeaveValue < 0 && h >= 0)
            {
                if (_lastZeroCrossTime != null)
                {
                    double sec = (now - _lastZeroCrossTime.Value).TotalSeconds;
                    if (sec > 2.0) { lblHeaveCycle.Text = $"{sec:0.0} s"; _heavePeriodSec = sec; }
                }
                _lastZeroCrossTime = now;
            }
            _lastHeaveValue = h;

            UpdateGridByTask("R/P/H", $"R:{r:0.0} P:{p:0.0} H:{h:0.0}");
            lock (_motionBuffer)
            {
                _motionBuffer.Add(new TrendPoint { X = now.ToOADate(), V1 = r, V2 = p, V3 = h });
                if (_motionBuffer.Count > 0 && _motionBuffer[0].X < now.AddMinutes(-BufferMinutes).ToOADate()) _motionBuffer.RemoveAt(0);
            }
            _rollTag.Update(Math.Abs(r)); _pitchTag.Update(Math.Abs(p)); _heaveTag.Update(Math.Abs(h)); _alarmEngine.Evaluate();

            SetLabelText(_lblHmiRollVal, $"{r:0.00}°"); SetLabelText(_lblHmiPitchVal, $"{p:0.00}°"); SetLabelText(_lblHmiHeaveVal, $"{h:0.0} cm"); SetLabelText(_lblHmiCycleVal, $"{_heavePeriodSec:0.0} s");
        }

        // ==========================================
        // 7. CÁC HÀM TIỆN ÍCH, VẼ GIAO DIỆN, CHART
        // ==========================================
        private void InitializeTasks() { _taskList = new List<DeviceTask>(); foreach (var t in ConfigForm.Tasks) _taskList.Add(new DeviceTask { TaskName = t.TaskName, PortName = t.PortName, BaudRate = t.BaudRate, Value1 = 0, HighLimit = t.HighLimit }); }
        private void UpdateBadge(Label lbl, string name, bool isStale, double age) { if (age > 900) { lbl.Text = $"{name}: WAIT"; lbl.BackColor = Color.DimGray; } else if (isStale) { lbl.Text = $"{name}: LOST"; lbl.BackColor = Color.Red; } else { lbl.Text = $"{name}: OK"; lbl.BackColor = Color.ForestGreen; } }
        private void UpdateLabelStatus(Label lbl, bool isAlive) { if (lbl != null) { if (isAlive && lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.Black; else if (!isAlive) lbl.ForeColor = Color.Gray; } }
        private void SetLabelText(Label lbl, string newVal) { if (lbl != null && lbl.Text != newVal) lbl.Text = newVal; }
        private void EnsureAlarmBadge() { if (lblAlarmStatus == null) { lblAlarmStatus = new Label { AutoSize = false, Width = 240, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.Black, ForeColor = Color.Lime, Text = "NORMAL", Margin = new Padding(20, 7, 5, 5) }; _topMenu.Controls.Add(lblAlarmStatus); } }
        private void SetupStatusBadges() { Label CreateBadge(string text) => new Label { Text = text, AutoSize = false, Size = new Size(140, 30), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.DimGray, ForeColor = Color.White, Margin = new Padding(5, 7, 0, 5), BorderStyle = BorderStyle.FixedSingle }; lblStatGPS = CreateBadge("GPS: WAIT"); lblStatWind = CreateBadge("WIND: WAIT"); lblStatMotion = CreateBadge("R/P/H: WAIT"); lblStatHeading = CreateBadge("HEADING: WAIT"); _topMenu.Controls.AddRange(new Control[] { lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading }); }
        private Button CreateMenuButton(string text, Color bg) => new Button { Text = text, BackColor = bg, FlatStyle = FlatStyle.Flat, Size = new Size(120, 30), Margin = new Padding(5, 2, 5, 2) };
        private void SetupHMILabel(Label lbl) { if (lbl != null) { lbl.Font = new Font("Segoe UI", 42, FontStyle.Bold); lbl.ForeColor = Color.Black; lbl.BackColor = Color.White; lbl.TextAlign = ContentAlignment.MiddleCenter; } }
        private double TryGetNumberAfterToken(string[] p, string t) { for (int i = 0; i < p.Length - 1; i++) if (string.Equals(p[i], t, StringComparison.OrdinalIgnoreCase) && double.TryParse(p[i + 1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v)) return v; return double.NaN; }
        private void LoadImageFromFile(PictureBox picBox, string fileName) { try { string path = Path.Combine(Application.StartupPath, "Images", fileName); if (File.Exists(path)) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) picBox.Image = Image.FromStream(stream); } catch { } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); this.DoubleBuffered = true; EnableDoubleBuffer(tableLayoutPanel1); EnableDoubleBuffer(tableLayoutPanel2); EnableDoubleBuffer(tableLayoutPanel3); EnableDoubleBuffer(tableLayoutPanelTrend); }
        private void EnableDoubleBuffer(Control c) { if (c != null) typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true, null); }
        private void BtnSettings_Click(object s, EventArgs e) { if (new LoginForm().ShowDialog() == DialogResult.OK) { new ConfigForm().ShowDialog(); SystemConfig.Apply(ConfigService.Load()); RefreshDataListLimits(); } }

        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();
            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.White); btnTrend1.Click += (s, e) => SetTrendMode(TrendMode.Motion);
            Button btnTrend2 = CreateMenuButton("TREND Wind", Color.White); btnTrend2.Click += (s, e) => SetTrendMode(TrendMode.Wind);
            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btnZoom.Click += (s, e) => { if (_currentViewMinutes == 2.0) { _currentViewMinutes = 20.0; btnZoom.Text = "VIEW: 20 Min"; btnZoom.BackColor = Color.LightSalmon; } else { _currentViewMinutes = 2.0; btnZoom.Text = "VIEW: 2 Min"; btnZoom.BackColor = Color.LightBlue; } _isLiveMode = true; ApplyViewportToNow(); };
            panelTrendButtons.Controls.AddRange(new Control[] { btnTrend1, btnTrend2, btnZoom });
        }

        private void SetupIndustrialChart()
        {
            if (panelChartHost == null) return;
            if (_trendChart != null) { panelChartHost.Controls.Remove(_trendChart); _trendChart.Dispose(); }

            _trendChart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            var area = new ChartArea("Main") { BackColor = Color.White };

            area.AxisX.LabelStyle.Format = "HH:mm:ss";
            area.AxisX.IntervalType = DateTimeIntervalType.Minutes;
            area.AxisX.Interval = 1;
            area.AxisX.MajorGrid.LineColor = Color.LightGray;
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisX.IsMarginVisible = false;

            area.AxisX.ScaleView.Zoomable = true;
            area.CursorX.IsUserEnabled = true;
            area.CursorX.IsUserSelectionEnabled = true;

            area.AxisX.ScrollBar.Enabled = true;
            area.AxisX.ScrollBar.IsPositionedInside = false;
            area.AxisX.ScrollBar.ButtonStyle = ScrollBarButtonStyles.All;
            area.AxisX.ScrollBar.Size = 14;

            area.AxisY.IsStartedFromZero = false;
            area.AxisY.MajorGrid.LineColor = Color.LightGray;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;

            _trendChart.ChartAreas.Add(area);
            _trendChart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Center, IsTextAutoFit = false, Font = new Font("Segoe UI", 14, FontStyle.Bold) });

            SetTrendMode(TrendMode.Motion);

            // Dòng đã được sửa lỗi CS1061
            _trendChart.AxisViewChanged += (s, e) => {
                if (!_isProgrammaticScroll)
                    _isLiveMode = (Math.Abs(_trendChart.ChartAreas[0].AxisX.Maximum - (_trendChart.ChartAreas[0].AxisX.ScaleView.Position + _trendChart.ChartAreas[0].AxisX.ScaleView.Size)) < (5.0 / 86400.0));
            };

            panelChartHost.Controls.Add(_trendChart);
            ApplyViewportToNow();
        }

        private void SetTrendMode(TrendMode mode)
        {
            _trendMode = mode; _trendChart?.Series.Clear(); if (_trendChart == null) return;
            if (mode == TrendMode.Motion) { AddSeries("Roll"); AddSeries("Pitch"); AddSeries("Heave"); }
            else { AddSeries("WindSpeed"); AddSeries("WindDir"); }
            ApplyViewportToNow();
        }

        private void AddSeries(string name)
        {
            string legendText = name switch { "Roll" => "R", "Pitch" => "P", "Heave" => "H", "WindSpeed" => "Wind speed", "WindDir" => "Direction", _ => name };
            _trendChart.Series.Add(new Series(name) { ChartType = SeriesChartType.FastLine, BorderWidth = 3, XValueType = ChartValueType.DateTime, IsVisibleInLegend = true, LegendText = legendText });
        }

        private void ApplyViewportToNow()
        {
            if (_trendChart == null) return;
            double nowX = DateTime.Now.ToOADate(), viewSize = _currentViewMinutes / 1440.0, bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();
            _trendChart.ChartAreas[0].AxisX.Minimum = bufferStart; _trendChart.ChartAreas[0].AxisX.Maximum = nowX; _trendChart.ChartAreas[0].AxisX.ScaleView.Size = viewSize;
            try { _trendChart.ChartAreas[0].AxisX.ScaleView.Scroll(Math.Max(bufferStart, nowX - viewSize)); } catch { }
        }

        private void RenderChartFromBuffer()
        {
            if (_trendChart == null || _trendChart.IsDisposed) return;
            double nowX = DateTime.Now.ToOADate(), viewSize = _currentViewMinutes / 1440.0, bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();
            _trendChart.SuspendLayout();
            if (_trendMode == TrendMode.Motion) { UpdateSeriesData("Roll", _motionBuffer, 1); UpdateSeriesData("Pitch", _motionBuffer, 2); UpdateSeriesData("Heave", _motionBuffer, 3); }
            else { UpdateSeriesData("WindSpeed", _windBuffer, 1); UpdateSeriesData("WindDir", _windBuffer, 2); }
            _trendChart.ChartAreas[0].AxisX.Minimum = bufferStart; _trendChart.ChartAreas[0].AxisX.Maximum = nowX;
            if (_isLiveMode) { _isProgrammaticScroll = true; _trendChart.ChartAreas[0].AxisX.ScaleView.Position = Math.Max(bufferStart, nowX - viewSize); _trendChart.ChartAreas[0].AxisX.ScaleView.Size = viewSize; _isProgrammaticScroll = false; }
            _trendChart.ResumeLayout(); _trendChart.Invalidate();
        }

        private void UpdateSeriesData(string seriesName, List<TrendPoint> buffer, int valueIndex)
        {
            var series = _trendChart.Series.FindByName(seriesName); if (series == null) return;
            TrendPoint[] dataSnapshot; lock (buffer) { dataSnapshot = buffer.ToArray(); }
            series.Points.SuspendUpdates(); series.Points.Clear();
            foreach (var pt in dataSnapshot) { if (pt.X > _trendChart.ChartAreas[0].AxisX.Minimum) series.Points.AddXY(pt.X, valueIndex == 1 ? pt.V1 : valueIndex == 2 ? pt.V2 : pt.V3); }
            series.Points.ResumeUpdates();
        }

        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) { }
    }
}