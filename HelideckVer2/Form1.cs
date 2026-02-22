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
        // 1. KHAI BÁO BIẾN TOÀN CỤC
        // ==========================================
        private ComEngine _comEngine;
        private DataLogger _logger;
        private Chart _trendChart;

        private TabControl _mainTabControl;
        private DataGridView _dgvDataList;
        private List<DeviceTask> _taskList;

        private double _lastHeaveValue = 0;
        private DateTime? _lastZeroCrossTime = null;

        private AlarmEngine _alarmEngine;
        private bool _isLiveMode = true;

        private Tag _windTag;
        private Tag _rollTag;
        private Tag _pitchTag;
        private Tag _heaveTag;

        private Label lblAlarmStatus;
        private FlowLayoutPanel _topMenu;

        private const int BufferMinutes = 20;
        private double _currentViewMinutes = 2.0;

        private enum TrendMode { Motion, Wind }
        private TrendMode _trendMode = TrendMode.Motion;

        private readonly List<TrendPoint> _motionBuffer = new();
        private readonly List<TrendPoint> _windBuffer = new();

        private struct TrendPoint
        {
            public double X;
            public double V1;
            public double V2;
            public double V3;
        }

        private double _speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm;
        private double _heavePeriodSec;
        private double _windSpeedMs, _windDirDeg;

        private DateTime?[] _lastUpdate;
        private string[] _unitText;
        private string[] _limitText;
        private string[] _alarmText;
        private System.Windows.Forms.Timer _healthTimer;
        private const double StaleSeconds = 2.0;

        private System.Windows.Forms.Timer _snapshotTimer;
        private System.Windows.Forms.Timer _chartUpdateTimer;

        private double _rollOffset = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm = 10.0;
        private bool _isProgrammaticScroll = false;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;

        // ===== BIẾN CHO HMI TAB (RADAR VIEW) =====
        private Panel _panelHmiGraphic;
        // Các label HMI (sẽ dùng TableLayoutPanel để quản lý)
        private Label _lblHmiPosVal, _lblHmiSpeedVal, _lblHmiHeadingVal;
        private Label _lblHmiRollVal, _lblHmiPitchVal, _lblHmiHeaveVal, _lblHmiCycleVal;
        private Label _lblHmiWindSpdVal, _lblHmiWindDirVal;

        private double _drawHeading = 0;
        private double _drawWindDir = 0;

        private readonly PointF[] _shipPoly = {
            new PointF(0, -100), new PointF(30, -30),
            new PointF(30, 90), new PointF(-30, 90), new PointF(-30, -30)
        };

        // ==========================================
        // 2. CONSTRUCTOR
        // ==========================================
        public Form1()
        {
            InitializeComponent();
            ApplyLeftPanelStyles(); // Style cho trang Overview

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
                    if (t != null) t.BaudRate = saved.BaudRate;
                }
            }

            InitializeTasks();

            SetupNavigationSimple();
            EnsureAlarmBadge();
            SetupStatusBadges();
            SetupTrendButtonsNearChart();
            SetupIndustrialChart();

            _logger = new DataLogger();

            _snapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _snapshotTimer.Tick += (s, e) =>
            {
                _logger.LogSnapshot(_speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm, _heavePeriodSec, _windSpeedMs, _windDirDeg);
            };
            _snapshotTimer.Start();

            _comEngine = new ComEngine();
            _comEngine.OnDataReceived += OnComDataReceived;
            _comEngine.Initialize(_taskList);

            _alarmEngine = new AlarmEngine();
            _windTag = new Tag("WindSpeed");
            _rollTag = new Tag("Roll");
            _pitchTag = new Tag("Pitch");
            _heaveTag = new Tag("Heave");

            _alarmEngine.Register(new Alarm("AL_WIND", _windTag, () => SystemConfig.WindMax));
            _alarmEngine.Register(new Alarm("AL_ROLL", _rollTag, () => SystemConfig.RMax));
            _alarmEngine.Register(new Alarm("AL_PITCH", _pitchTag, () => SystemConfig.PMax));
            _alarmEngine.Register(new Alarm("AL_HEAVE", _heaveTag, () => SystemConfig.HMax));

            _alarmEngine.AlarmRaised += OnAlarmRaised;
            _alarmEngine.AlarmCleared += OnAlarmCleared;
            _alarmEngine.AlarmAcked += OnAlarmAcked;

            RefreshAlarmBanner();

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => RenderChartFromBuffer();
            _chartUpdateTimer.Start();
        }

        private void InitializeTasks()
        {
            _taskList = new List<DeviceTask>();
            foreach (var t in ConfigForm.Tasks)
            {
                _taskList.Add(new DeviceTask
                {
                    TaskName = t.TaskName,
                    PortName = t.PortName,
                    BaudRate = t.BaudRate,
                    Value1 = 0,
                    HighLimit = t.HighLimit
                });
            }
        }

        // ==========================================
        // 3. UI SETUP & NAVIGATION
        // ==========================================
        private void SetupNavigationSimple()
        {
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };
            this.Controls.Add(root);

            _topMenu = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.LightGray,
                Padding = new Padding(6, 6, 6, 6),
                WrapContents = false
            };

            Button btnOver = CreateMenuButton("OVERVIEW", Color.White);
            btnOver.Click += (s, e) => _mainTabControl.SelectTab(0);

            Button btnData = CreateMenuButton("DATA LIST", Color.White);
            btnData.Click += (s, e) => _mainTabControl.SelectTab(1);

            Button btnHMI = CreateMenuButton("HMI VIEW", Color.LightSkyBlue);
            btnHMI.Click += (s, e) => _mainTabControl.SelectTab(2);

            Button btnSet = CreateMenuButton("SETTINGS", Color.Orange);
            btnSet.Click += BtnSettings_Click;

            _topMenu.Controls.AddRange(new Control[] { btnOver, btnData, btnHMI, btnSet });

            _mainTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                ItemSize = new Size(0, 1),
                SizeMode = TabSizeMode.Fixed
            };

            var tabOverview = new TabPage("Overview") { BackColor = Color.WhiteSmoke };
            this.tableLayoutPanel1.Parent = tabOverview;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Visible = true;

            var tabDataList = new TabPage("DataList") { BackColor = Color.White };
            SetupDataGrid(tabDataList);

            var tabHmi = new TabPage("HMI") { BackColor = Color.White };
            SetupHmiTab(tabHmi);

            _mainTabControl.TabPages.AddRange(new TabPage[] { tabOverview, tabDataList, tabHmi });

            root.Controls.Add(_mainTabControl);
            root.Controls.Add(_topMenu);
        }

        // --- SETUP HMI TAB (GIAO DIỆN RADAR CHUYÊN NGHIỆP) ---
        private void SetupHmiTab(TabPage tab)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            tab.Controls.Add(layout);

            // --- CỘT 1: BẢNG SỐ LIỆU ---
            TableLayoutPanel tblData = new TableLayoutPanel();
            tblData.Dock = DockStyle.Fill;
            tblData.RowCount = 9;
            tblData.ColumnCount = 2;
            tblData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F)); // Khớp 35%
            tblData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F)); // Khớp 65%
            tblData.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tblData.BackColor = Color.White;

            // >>> QUAN TRỌNG: BẬT CHỐNG NHẤY CHO BẢNG SỐ LIỆU <<<
            EnableDoubleBuffer(tblData);

            void AddRow(int row, string title, ref Label valLabel, bool isGps = false)
            {
                tblData.RowStyles.Add(new RowStyle(SizeType.Percent, 11.1111f));

                Label lblTitle = new Label
                {
                    Text = title,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = Color.DimGray,
                    BackColor = Color.FromArgb(240, 240, 240),
                    Padding = new Padding(10, 0, 0, 0)
                };

                valLabel = new Label
                {
                    Text = "---",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Black,
                    BackColor = Color.White
                };

                if (isGps)
                {
                    valLabel.Font = new Font("Segoe UI", 13, FontStyle.Bold);
                    valLabel.ForeColor = Color.DarkBlue;
                }
                else
                {
                    valLabel.Font = new Font("Segoe UI", 28, FontStyle.Bold);
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

            // --- CỘT 2: PANEL RADAR ---
            _panelHmiGraphic = new Panel();
            _panelHmiGraphic.Dock = DockStyle.Fill;
            _panelHmiGraphic.BackColor = Color.FromArgb(230, 235, 240);
            EnableDoubleBuffer(_panelHmiGraphic); // Chống nháy cho Radar

            _panelHmiGraphic.Paint += HmiPanel_Paint;
            _panelHmiGraphic.Resize += (s, e) => _panelHmiGraphic.Invalidate();

            layout.Controls.Add(_panelHmiGraphic, 1, 0);
        }
        // Hàm chống nháy khi cập nhật Text
        private void SetLabelText(Label lbl, string newVal)
        {
            if (lbl != null && lbl.Text != newVal) // Chỉ gán khi khác nhau
            {
                lbl.Text = newVal;
            }
        }
        // ==========================================
        // 4. VẼ RADAR (HMI PAINT) - GIAO DIỆN SÁNG (LIGHT THEME)
        // ==========================================
        private void HmiPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _panelHmiGraphic.Width;
            int h = _panelHmiGraphic.Height;
            Point center = new Point(w / 2, h / 2);
            int radius = Math.Min(w, h) / 2 - 40;
            if (radius < 50) return;

            // ==========================================================
            // 0. VẼ NỀN TRÒN MÀU TRẮNG (TẠO ĐIỂM NHẤN)
            // ==========================================================
            // Vẽ hình tròn trắng lấp đầy để tách biệt với nền xám bên ngoài
            using (SolidBrush bgBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(bgBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }

            // Vẽ viền bao quanh cho sắc nét
            using (Pen pBorder = new Pen(Color.Gray, 2))
            {
                g.DrawEllipse(pBorder, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }

            // ==========================================================
            // 1. VẼ LƯỚI & VẠCH CHIA (GIỮ NGUYÊN STYLE SÁNG)
            // ==========================================================
            using (Pen pGrid = new Pen(Color.FromArgb(180, 200, 220), 1) { DashStyle = DashStyle.Dot })
            using (Pen pMain = new Pen(Color.LightSlateGray, 2))
            using (Font font = new Font("Segoe UI", 10, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.DarkSlateGray))
            {
                // Các vòng tròn nội bộ
                g.DrawEllipse(pGrid, center.X - radius * 0.66f, center.Y - radius * 0.66f, radius * 1.33f, radius * 1.33f);
                g.DrawEllipse(pGrid, center.X - radius * 0.33f, center.Y - radius * 0.33f, radius * 0.66f, radius * 0.66f);

                // Chữ thập tâm
                g.DrawLine(pGrid, center.X - radius, center.Y, center.X + radius, center.Y);
                g.DrawLine(pGrid, center.X, center.Y - radius, center.X, center.Y + radius);

                // Vạch chia độ
                for (int i = 0; i < 360; i += 10)
                {
                    double rad = (i - 90) * Math.PI / 180.0;
                    int len = (i % 90 == 0) ? 15 : 8;

                    float x1 = center.X + (float)(Math.Cos(rad) * radius);
                    float y1 = center.Y + (float)(Math.Sin(rad) * radius);
                    // Điểm bắt đầu vẽ vạch (nằm trên vòng tròn)

                    float x2 = center.X + (float)(Math.Cos(rad) * (radius - len));
                    float y2 = center.Y + (float)(Math.Sin(rad) * (radius - len));

                    g.DrawLine((i % 90 == 0) ? pMain : pGrid, x1, y1, x2, y2);

                    // Vẽ số
                    string txt = i.ToString();
                    // Đẩy số ra xa hơn chút để không dính vào viền
                    float xT = center.X + (float)(Math.Cos(rad) * (radius + 28));
                    float yT = center.Y + (float)(Math.Sin(rad) * (radius + 28));

                    var stateText = g.Save();
                    g.TranslateTransform(xT, yT);
                    g.RotateTransform(i);
                    SizeF s = g.MeasureString(txt, font);
                    g.DrawString(txt, font, brush, -s.Width / 2, -s.Height / 2);
                    g.Restore(stateText);
                }

                // Chữ N (Bắc)
                g.DrawString("N", new Font("Segoe UI", 16, FontStyle.Bold), Brushes.DarkBlue, center.X - 11, center.Y - radius - 50);
            }

            // ==========================================================
            // 2. VẼ TÀU
            // ==========================================================
            var stateShip = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_drawHeading);

            using (SolidBrush b = new SolidBrush(Color.DarkGray)) g.FillPolygon(b, _shipPoly);
            using (Pen p = new Pen(Color.DimGray, 2)) g.DrawPolygon(p, _shipPoly);

            // Heading Line (Vàng đất đậm)
            using (Pen pHead = new Pen(Color.DarkGoldenrod, 2) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(pHead, 0, -100, 0, -radius + 20);
            }
            g.Restore(stateShip);

            // ==========================================================
            // 3. VẼ GIÓ
            // ==========================================================
            var stateWind = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_drawWindDir + 180);

            int arrowPos = radius - 40;
            PointF[] arrow = { new PointF(0, -arrowPos - 30), new PointF(-10, -arrowPos), new PointF(10, -arrowPos) };

            // Mũi tên gió Xanh dương
            using (SolidBrush b = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(b, arrow);
            using (Pen p = new Pen(Color.RoyalBlue, 3)) g.DrawLine(p, 0, -arrowPos, 0, -arrowPos + 50);

            g.Restore(stateWind);
        }
        // ==========================================
        // 5. XỬ LÝ DỮ LIỆU
        // ==========================================
        private void OnComDataReceived(string portName, string rawData)
        {
            this.Invoke(new Action(() => ParseAndDisplay(portName, rawData)));
        }

        private void ParseAndDisplay(string portName, string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data) || !data.StartsWith("$")) return;
                int starIndex = data.IndexOf('*');
                string cleanData = (starIndex > -1) ? data.Substring(0, starIndex) : data;
                string[] p = cleanData.Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var style = System.Globalization.NumberStyles.Any;

                // HEADING
                if (p[0].EndsWith("HDT") && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                    {
                        _headingDeg = heading;
                        lblHeading.Text = $"{heading:0.0}°";
                        UpdateGridByTask("HEADING", $"{heading:0.0}");

                        // HMI
                        _drawHeading = heading;
                        if (_lblHmiHeadingVal != null) _lblHmiHeadingVal.Text = $"{heading:0.0}°";
                        if (_lblHmiHeadingVal != null) SetLabelText(_lblHmiHeadingVal, $"{heading:0.0}°");
                        _panelHmiGraphic?.Invalidate();
                    }
                    return;
                }

                // WIND
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) && double.TryParse(p[3], style, culture, out double wSpeed))
                    {
                        _windSpeedMs = wSpeed; _windDirDeg = wDir;
                        lblWindSpeed.Text = $"{wSpeed:0.0} m/s"; lblWindRelated.Text = $"{wDir:0}°";
                        UpdateGridByTask("WIND", $"{wSpeed:0.0} / {wDir:0}");
                        _windTag.Update(wSpeed); _alarmEngine.Evaluate();
                        lock (_windBuffer) { _windBuffer.Add(new TrendPoint { X = DateTime.Now.ToOADate(), V1 = wSpeed, V2 = wDir }); }
                        UpdateChartWind(wSpeed, wDir);

                        // HMI
                        _drawWindDir = wDir;
                        if (_lblHmiWindSpdVal != null)
                        {
                            SetLabelText(_lblHmiWindSpdVal, $"{wSpeed:0.0} m/s");
                            SetLabelText(_lblHmiWindDirVal, $"{wDir:0}°");
                        }
                        _panelHmiGraphic?.Invalidate();
                    }
                    return;
                }

                // MOTION
                if ((p[0].EndsWith("CNTB") && p.Length >= 4) || (p[0] == "$PRDID" && p.Length >= 3))
                {
                    double r = 0, pi = 0, h = 0;
                    bool ok = false;
                    if (p[0].EndsWith("CNTB"))
                    {
                        ok = double.TryParse(p[1], style, culture, out r) && double.TryParse(p[2], style, culture, out pi) && double.TryParse(p[3], style, culture, out h);
                    }
                    else
                    {
                        if (double.TryParse(p[1], style, culture, out double rawP) && double.TryParse(p[2], style, culture, out double rawR))
                        {
                            r = rawR; pi = rawP; h = _heaveArm * Math.Sin(rawP * Math.PI / 180.0); ok = true;
                        }
                    }
                    if (ok) DisplayMotionData(r + _rollOffset, pi + _pitchOffset, h);
                    return;
                }

                // GPS
                if (p[0].EndsWith("GGA") && p.Length >= 6)
                {
                    string lat = p[2], latD = p[3], lon = p[4], lonD = p[5];
                    if (lat.Length > 4 && lon.Length > 5)
                    {
                        string fLat = $"{lat.Substring(0, 2)}°{lat.Substring(2)}'{latD}";
                        string fLon = $"{lon.Substring(0, 3)}°{lon.Substring(3)}'{lonD}";
                        lblPosition.Text = $"{fLon}\r\n{fLat}";
                        UpdateGridByTask("GPS", $"{fLat} {fLon}");

                        // HMI
                        if (_lblHmiPosVal != null) _lblHmiPosVal.Text = $"{fLon}\r\n{fLat}";
                    }
                    else { lblPosition.Text = "NO FIX"; }
                    return;
                }

                // Speed
                if (p[0] == "$GPVTG")
                {
                    double k = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(k)) { _speedKnot = k; lblSpeed.Text = $"{k:0.0} kn"; if (_lblHmiSpeedVal != null) SetLabelText(_lblHmiSpeedVal, $"{k:0.0} kn"); }
                }
            }
            catch { }
        }

        private void DisplayMotionData(double r, double p, double h)
        {
            _rollDeg = r; _pitchDeg = p; _heaveCm = h;
            lblRoll.Text = $"{r:0.00}°"; lblPitch.Text = $"{p:0.00}°"; lblHeave.Text = $"{h:0.0} cm";

            // Calc Cycle
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
            _rollTag.Update(Math.Abs(r)); _pitchTag.Update(Math.Abs(p)); _heaveTag.Update(Math.Abs(h));
            _alarmEngine.Evaluate();

            // HMI Update
            if (_lblHmiRollVal != null)
            {
                SetLabelText(_lblHmiRollVal, $"{r:0.00}°");
                SetLabelText(_lblHmiPitchVal, $"{p:0.00}°");
                SetLabelText(_lblHmiHeaveVal, $"{h:0.0} cm");
                SetLabelText(_lblHmiCycleVal, $"{_heavePeriodSec:0.0} s");
            }
        }

        // ==========================================
        // 6. HELPER METHODS
        // ==========================================
        private void ApplyLeftPanelStyles()
        {
            // =================================================================
            // 1. SỬA ĐỘ RỘNG TRANG CHÍNH CHO BẰNG HMI
            // =================================================================
            // tableLayoutPanel1 là khung to nhất chứa (Bảng số liệu) và (Biểu đồ)
            if (tableLayoutPanel1 != null)
            {
                tableLayoutPanel1.ColumnStyles.Clear();
                // Cột trái (Số liệu): 35%
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
                // Cột phải (Biểu đồ): 65%
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            }

            // =================================================================
            // 2. CẤU HÌNH BẢNG SỐ LIỆU (BÊN TRÁI)
            // =================================================================
            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel2.BackColor = Color.White;

            // Chia tỷ lệ nội bộ bảng con: 35% Tiêu đề - 65% Giá trị
            tableLayoutPanel2.ColumnStyles.Clear();
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));

            // Chia đều 9 dòng
            tableLayoutPanel2.RowStyles.Clear();
            tableLayoutPanel2.RowCount = 9;
            for (int i = 0; i < 9; i++)
            {
                tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 11.1111f));
            }

            // =================================================================
            // 3. STYLE TIÊU ĐỀ
            // =================================================================
            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9 };
            string[] headers = { "POSITION", "SPEED", "HEADING", "ROLL", "PITCH", "HEAVE", "H.PERIOD", "WIND SPD", "WIND DIR" };

            for (int i = 0; i < 9; i++)
            {
                if (titles[i] == null) continue;
                titles[i].Text = headers[i];
                titles[i].AutoSize = false;
                titles[i].Dock = DockStyle.Fill;
                titles[i].TextAlign = ContentAlignment.MiddleLeft;
                titles[i].Padding = new Padding(10, 0, 0, 0);
                titles[i].Font = new Font("Segoe UI", 18, FontStyle.Bold);
                titles[i].ForeColor = Color.DimGray;
                titles[i].BackColor = Color.FromArgb(240, 240, 240);
                titles[i].Margin = new Padding(0);
            }

            // =================================================================
            // 4. STYLE GIÁ TRỊ (SỬA LỖI 0.00 THÀNH ---)
            // =================================================================
            Label[] values = { lblPosition, lblSpeed, lblHeading, lblRoll, lblPitch, lblHeave, lblHeaveCycle, lblWindSpeed, lblWindRelated };
            foreach (var v in values)
            {
                if (v == null) continue;

                // >>> QUAN TRỌNG: Gán mặc định là --- thay vì số 0
                v.Text = "---";

                v.AutoSize = false;
                v.Dock = DockStyle.Fill;
                v.TextAlign = ContentAlignment.MiddleCenter;
                v.BackColor = Color.White;
                v.ForeColor = Color.Black;
                v.Margin = new Padding(0);

                if (v == lblPosition)
                {
                    v.Font = new Font("Segoe UI", 18, FontStyle.Bold);
                    v.ForeColor = Color.DarkBlue;
                }
                else
                {
                    v.Font = new Font("Segoe UI", 28, FontStyle.Bold);
                }
            }
            lblHeave.ForeColor = Color.Red;
        }

        private void SetupDataGrid(TabPage parentTab)
        {
            // 1. Khởi tạo DataGridView với cấu hình giao diện chuẩn
            _dgvDataList = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                ReadOnly = true,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.None,

                // --- CẤU HÌNH HEADER (ĐỂ HIGHLIGHT) ---
                EnableHeadersVisualStyles = false, // Bắt buộc FALSE để chỉnh màu thủ công
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill // Tự động giãn cột lấp đầy
            };

            // Style cho Header (Dòng tiêu đề)
            _dgvDataList.ColumnHeadersDefaultCellStyle.BackColor = Color.Navy; // Nền Xanh Navy
            _dgvDataList.ColumnHeadersDefaultCellStyle.ForeColor = Color.White; // Chữ Trắng
            _dgvDataList.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11, FontStyle.Bold); // Chữ Đậm
            _dgvDataList.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvDataList.ColumnHeadersHeight = 45; // Tăng chiều cao header cho thoáng

            // Style cho các dòng dữ liệu (Rows)
            _dgvDataList.DefaultCellStyle.BackColor = Color.White;
            _dgvDataList.DefaultCellStyle.ForeColor = Color.Black;
            _dgvDataList.DefaultCellStyle.SelectionBackColor = Color.LightBlue; // Màu khi chọn dòng
            _dgvDataList.DefaultCellStyle.SelectionForeColor = Color.Black;
            _dgvDataList.DefaultCellStyle.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            _dgvDataList.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // 2. Thêm Cột
            _dgvDataList.Columns.Add("Task", "TASK NAME");
            _dgvDataList.Columns.Add("Port", "PORT");
            _dgvDataList.Columns.Add("Value", "VALUE");
            _dgvDataList.Columns.Add("Unit", "UNIT");
            _dgvDataList.Columns.Add("Age", "AGE(s)");
            _dgvDataList.Columns.Add("Status", "STATUS");
            _dgvDataList.Columns.Add("Limit", "LIMIT");
            _dgvDataList.Columns.Add("Alarm", "ALARM");

            // Tinh chỉnh tỷ lệ độ rộng cột (để hiển thị hết dữ liệu dài)
            _dgvDataList.Columns["Task"].FillWeight = 80;
            _dgvDataList.Columns["Port"].FillWeight = 60;
            _dgvDataList.Columns["Value"].FillWeight = 150; // Cột giá trị rộng nhất
            _dgvDataList.Columns["Unit"].FillWeight = 80;
            _dgvDataList.Columns["Age"].FillWeight = 60;
            _dgvDataList.Columns["Limit"].FillWeight = 100;

            // 3. Thêm Dòng Dữ Liệu (Điền sẵn Unit/Limit ngay tại đây để chắc chắn hiển thị)
            _dgvDataList.Rows.Add("GPS", "COM1", "", "kn", "", "WAIT", "", "Normal");

            // Dòng WIND: Điền sẵn limit từ config
            _dgvDataList.Rows.Add("WIND", "COM2", "", "m/s, °", "", "WAIT", $"{SystemConfig.WindMax:0.0}", "Normal");

            // Dòng R/P/H: Điền sẵn limit
            _dgvDataList.Rows.Add("R/P/H", "COM3", "", "°, °, cm", "", "WAIT", $"R:{SystemConfig.RMax} P:{SystemConfig.PMax} H:{SystemConfig.HMax}", "Normal");

            _dgvDataList.Rows.Add("HEADING", "COM4", "", "°", "", "WAIT", "", "Normal");

            // Khởi tạo mảng đệm
            int n = _dgvDataList.Rows.Count;
            _lastUpdate = new DateTime?[n];
            _unitText = new string[n];
            _limitText = new string[n];
            _alarmText = new string[n];

            // Đồng bộ lại vào mảng đệm (để logic update sau này không bị lỗi)
            SetMetaUnit("GPS", "kn");
            SetMetaUnit("WIND", "m/s, °");
            SetMetaUnit("R/P/H", "°, °, cm");
            SetMetaUnit("HEADING", "°");

            RefreshDataListLimits(); // Cập nhật lần cuối từ Config

            parentTab.Controls.Add(_dgvDataList);
            StartHealthTimer();
        }

        private void StartHealthTimer()
        {
            if (_healthTimer != null) return;
            _healthTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _healthTimer.Tick += (s, e) => {
                if (_dgvDataList == null) return;
                for (int i = 0; i < _dgvDataList.Rows.Count; i++)
                {
                    double age = 999.0;
                    if (_lastUpdate != null && _lastUpdate[i] != null) age = (DateTime.Now - _lastUpdate[i]!.Value).TotalSeconds;

                    _dgvDataList.Rows[i].Cells["Age"].Value = age.ToString("0.0");
                    string st = (age <= StaleSeconds) ? "OK" : "STALE";
                    _dgvDataList.Rows[i].Cells["Status"].Value = st;
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = (st == "OK") ? Color.Green : Color.Red;

                    // Update Badges
                    Label b = i switch { 0 => lblStatGPS, 1 => lblStatWind, 2 => lblStatMotion, 3 => lblStatHeading, _ => null };
                    if (b != null) UpdateBadge(b, b.Text.Split(':')[0], st != "OK", age);
                }
            };
            _healthTimer.Start();
        }

        private void UpdateBadge(Label lbl, string name, bool isStale, double age)
        {
            if (age > 900) { lbl.Text = $"{name}: WAIT"; lbl.BackColor = Color.DimGray; }
            else if (isStale) { lbl.Text = $"{name}: LOST"; lbl.BackColor = Color.Red; }
            else { lbl.Text = $"{name}: OK"; lbl.BackColor = Color.ForestGreen; }
        }

        private void UpdateGridByTask(string task, string val)
        {
            int r = -1;
            if (_dgvDataList == null) return; // Kiểm tra an toàn bảng

            for (int i = 0; i < _dgvDataList.Rows.Count; i++)
            {
                // --- SỬA LỖI TẠI ĐÂY ---
                // Lấy giá trị ô ra trước
                var cellVal = _dgvDataList.Rows[i].Cells[0].Value;

                // Kiểm tra khác null mới so sánh
                if (cellVal != null && cellVal.ToString() == task)
                {
                    r = i;
                    break; // Tìm thấy rồi thì thoát vòng lặp luôn cho nhẹ máy
                }
            }

            if (r >= 0)
            {
                _dgvDataList.Rows[r].Cells["Value"].Value = val;

                // Kiểm tra an toàn cho mảng thời gian
                if (_lastUpdate != null && r < _lastUpdate.Length)
                {
                    _lastUpdate[r] = DateTime.Now;
                }
            }
        }

        private void EnsureAlarmBadge()
        {
            if (lblAlarmStatus != null) return;
            lblAlarmStatus = new Label { AutoSize = false, Width = 240, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.Black, ForeColor = Color.Lime, Text = "NORMAL", Margin = new Padding(20, 7, 5, 5) };
            _topMenu.Controls.Add(lblAlarmStatus);
        }

        private void SetupStatusBadges()
        {
            Label CreateBadge(string t) => new Label { Text = t, AutoSize = false, Size = new Size(140, 30), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.DimGray, ForeColor = Color.White, Margin = new Padding(5, 7, 0, 5), BorderStyle = BorderStyle.FixedSingle };
            lblStatGPS = CreateBadge("GPS: WAIT"); lblStatWind = CreateBadge("WIND: WAIT"); lblStatMotion = CreateBadge("R/P/H: WAIT"); lblStatHeading = CreateBadge("HEADING: WAIT");
            _topMenu.Controls.AddRange(new Control[] { lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading });
        }

        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();
            Button btn1 = CreateMenuButton("TREND R/P/H", Color.White); btn1.Click += (s, e) => SetTrendMode(TrendMode.Motion);
            Button btn2 = CreateMenuButton("TREND Wind", Color.White); btn2.Click += (s, e) => SetTrendMode(TrendMode.Wind);
            Button btn3 = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btn3.Click += (s, e) => { if (_currentViewMinutes == 2) { _currentViewMinutes = 20; btn3.Text = "VIEW: 20 Min"; btn3.BackColor = Color.LightSalmon; } else { _currentViewMinutes = 2; btn3.Text = "VIEW: 2 Min"; btn3.BackColor = Color.LightBlue; } _isLiveMode = true; ApplyViewportToNow(); };
            panelTrendButtons.Controls.AddRange(new Control[] { btn1, btn2, btn3 });
        }

        private void SetupIndustrialChart()
        {
            if (panelChartHost == null) return;
            if (_trendChart != null) { panelChartHost.Controls.Remove(_trendChart); _trendChart.Dispose(); }
            _trendChart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            var area = new ChartArea("Main") { BackColor = Color.White };
            area.AxisX.LabelStyle.Format = "HH:mm:ss"; area.AxisX.MajorGrid.LineColor = Color.LightGray; area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.MajorGrid.LineColor = Color.LightGray; area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisX.ScrollBar.Enabled = true;
            _trendChart.ChartAreas.Add(area);
            _trendChart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Center });
            _trendChart.AxisViewChanged += (s, e) => { if (!_isProgrammaticScroll) _isLiveMode = (Math.Abs(_trendChart.ChartAreas[0].AxisX.Maximum - _trendChart.ChartAreas[0].AxisX.ScaleView.ViewMaximum) < 0.0001); };
            panelChartHost.Controls.Add(_trendChart);
            SetTrendMode(TrendMode.Motion);
        }

        private void SetTrendMode(TrendMode mode)
        {
            _trendMode = mode; _trendChart.Series.Clear();
            if (mode == TrendMode.Motion) { AddSeries("Roll"); AddSeries("Pitch"); AddSeries("Heave"); }
            else { AddSeries("WindSpeed"); AddSeries("WindDir"); }
            RenderChartFromBuffer();
        }

        private void AddSeries(string name) { _trendChart.Series.Add(new Series(name) { ChartType = SeriesChartType.FastLine, BorderWidth = 3, XValueType = ChartValueType.DateTime }); }

        private void RenderChartFromBuffer()
        {
            if (_trendChart == null || _trendChart.IsDisposed) return;
            double now = DateTime.Now.ToOADate();

            _trendChart.SuspendLayout();
            if (_trendMode == TrendMode.Motion) { UpdateSer("Roll", 1); UpdateSer("Pitch", 2); UpdateSer("Heave", 3); }
            else { UpdateSer("WindSpeed", 1); UpdateSer("WindDir", 2); }

            var a = _trendChart.ChartAreas[0];
            a.AxisX.Maximum = now; a.AxisX.Minimum = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();
            if (_isLiveMode) { _isProgrammaticScroll = true; a.AxisX.ScaleView.Size = _currentViewMinutes / 1440.0; a.AxisX.ScaleView.Scroll(now); _isProgrammaticScroll = false; }
            _trendChart.ResumeLayout(); _trendChart.Invalidate();
        }

        private void UpdateSer(string name, int idx)
        {
            var s = _trendChart.Series.FindByName(name); if (s == null) return;
            TrendPoint[] buf; lock ((_trendMode == TrendMode.Motion) ? _motionBuffer : _windBuffer) { buf = ((_trendMode == TrendMode.Motion) ? _motionBuffer : _windBuffer).ToArray(); }
            s.Points.Clear(); foreach (var p in buf) s.Points.AddXY(p.X, idx == 1 ? p.V1 : idx == 2 ? p.V2 : p.V3);
        }
        //// Hàm cập nhật giới hạn hiển thị lên bảng Data List
        //private void RefreshDataListLimits()
        //{
        //    if (_dgvDataList == null) return;

        //    // Cập nhật giới hạn Gió
        //    SetMetaLimit("WIND", $"{SystemConfig.WindMax:0.0}");

        //    // Cập nhật giới hạn R/P/H
        //    SetMetaLimit("R/P/H", $"R:{SystemConfig.RMax:0.0}  P:{SystemConfig.PMax:0.0}  H:{SystemConfig.HMax:0.0}");

        //    _dgvDataList.Invalidate();
        //}
        // Hàm hỗ trợ tìm dòng theo tên Task (cần thiết cho SetMetaLimit)
        private int FindRow(string taskName)
        {
            if (_dgvDataList == null) return -1;
            foreach (DataGridViewRow r in _dgvDataList.Rows)
            {
                if ((r.Cells["Task"]?.Value?.ToString() ?? "") == taskName)
                    return r.Index;
            }
            return -1;
        }

        private void RefreshDataListLimits()
        {
            if (_dgvDataList == null) return;

            // Cập nhật giới hạn Gió
            SetMetaLimit("WIND", $"{SystemConfig.WindMax:0.0}");

            // Cập nhật giới hạn R/P/H
            SetMetaLimit("R/P/H", $"R:{SystemConfig.RMax:0.0}  P:{SystemConfig.PMax:0.0}  H:{SystemConfig.HMax:0.0}");

            _dgvDataList.Invalidate();
        }

        // Hàm gán giới hạn (Limit) vào DataGrid
        private void SetMetaLimit(string task, string limit)
        {
            int row = FindRow(task);
            if (row >= 0)
            {
                // Cập nhật mảng đệm nếu có
                if (_limitText != null && row < _limitText.Length)
                    _limitText[row] = limit;

                // Cập nhật trực tiếp lên ô Grid
                if (_dgvDataList != null && row < _dgvDataList.Rows.Count)
                    _dgvDataList.Rows[row].Cells["Limit"].Value = limit;
            }
        }

        // Hàm gán Đơn vị (Unit) vào DataGrid
        private void SetMetaUnit(string task, string unit)
        {
            int row = FindRow(task);
            if (row >= 0)
            {
                if (_unitText != null && row < _unitText.Length)
                    _unitText[row] = unit;

                if (_dgvDataList != null && row < _dgvDataList.Rows.Count)
                    _dgvDataList.Rows[row].Cells["Unit"].Value = unit;
            }
        }
        // Hàm làm mới toàn bộ giới hạn (được gọi khi đổi cấu hình)

        private void ApplyViewportToNow() { _trendChart?.ChartAreas[0].AxisX.ScaleView.Scroll(DateTime.Now.ToOADate()); }
        private Button CreateMenuButton(string text, Color bg) => new Button { Text = text, BackColor = bg, FlatStyle = FlatStyle.Flat, Size = new Size(120, 30), Margin = new Padding(5, 2, 5, 2) };
        private double TryGetNumberAfterToken(string[] p, string t) { for (int i = 0; i < p.Length - 1; i++) if (p[i] == t && double.TryParse(p[i + 1], out double v)) return v; return double.NaN; }
        private void LoadImageFromFile(PictureBox p, string f) { try { using (var s = new FileStream(Path.Combine(Application.StartupPath, "Images", f), FileMode.Open, FileAccess.Read)) p.Image = Image.FromStream(s); } catch { } }
        private void UpdateChartWind(double s, double d) { if (_trendMode == TrendMode.Wind) _trendChart?.Invalidate(); }

        // Empty Handlers
        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) { }

        // Alarm
        private void OnAlarmRaised(Alarm a) { SetAlarmColor(a.Id, Color.Red); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Active"); _logger.LogAlarmEvent("RAISED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void OnAlarmCleared(Alarm a) { SetAlarmColor(a.Id, Color.Black); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Normal"); _logger.LogAlarmEvent("CLEARED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void OnAlarmAcked(Alarm a) { SetAlarmColor(a.Id, Color.Orange); RefreshAlarmBanner(); SetAlarmRow(a.Id, "Ack"); _logger.LogAlarmEvent("ACKED", a.Id, a.State.ToString(), a.Tag.Value, a.HighLimitProvider()); }
        private void SetAlarmColor(string id, Color c) { if (id == "AL_WIND") lblWindSpeed.ForeColor = c; else if (id == "AL_ROLL") lblRoll.ForeColor = c; else if (id == "AL_PITCH") lblPitch.ForeColor = c; else if (id == "AL_HEAVE") lblHeave.ForeColor = c; }
        private void lblWindSpeed_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_WIND"); private void lblRoll_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_ROLL"); private void lblPitch_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_PITCH"); private void lblHeave_Click(object s, EventArgs e) => _alarmEngine.Ack("AL_HEAVE");
        private void RefreshAlarmBanner() { if (lblAlarmStatus == null) return; var all = new List<Alarm>(_alarmEngine.GetAll()); var act = all.Find(a => a.IsActive && !a.IsAcked); if (act != null) { lblAlarmStatus.Text = "ALARM: " + act.Id; lblAlarmStatus.BackColor = Color.DarkRed; } else { lblAlarmStatus.Text = "NORMAL"; lblAlarmStatus.BackColor = Color.Black; } }
        // Hàm cập nhật trạng thái Alarm vào bảng (Phiên bản sửa lỗi logic)
        private void SetAlarmRow(string changedAlarmId, string state)
        {
            // 1. Xác định dòng (Row) nào bị ảnh hưởng
            int rowIndex = AlarmIdToRowIndex(changedAlarmId);
            if (rowIndex < 0 || _dgvDataList == null) return;

            // 2. Lấy tên Task của dòng đó (ví dụ: "R/P/H" hoặc "WIND")
            var taskCell = _dgvDataList.Rows[rowIndex].Cells["Task"];
            if (taskCell.Value == null) return;
            string taskName = taskCell.Value.ToString();

            // 3. Kiểm tra tổng hợp: Xem có bất kỳ Alarm nào thuộc dòng này đang Active không?
            List<string> activeAlerts = new List<string>();

            if (taskName == "R/P/H")
            {
                // Kiểm tra cả 3 thông số, cái nào bị thì thêm tên vào danh sách
                if (IsAlarmActive("AL_ROLL")) activeAlerts.Add("ROLL");
                if (IsAlarmActive("AL_PITCH")) activeAlerts.Add("PITCH");
                if (IsAlarmActive("AL_HEAVE")) activeAlerts.Add("HEAVE");
            }
            else if (taskName == "WIND")
            {
                if (IsAlarmActive("AL_WIND")) activeAlerts.Add("WIND");
            }

            // 4. Hiển thị kết quả cuối cùng
            var cellAlarm = _dgvDataList.Rows[rowIndex].Cells["Alarm"];

            if (activeAlerts.Count > 0)
            {
                // Nếu có ít nhất 1 cái bị Alarm -> Hiển thị tên cái đó (VD: "HEAVE" hoặc "ROLL, HEAVE")
                cellAlarm.Value = string.Join(", ", activeAlerts);
                cellAlarm.Style.ForeColor = Color.Red;
                cellAlarm.Style.Font = new Font("Segoe UI", 11, FontStyle.Bold);
                cellAlarm.Style.SelectionForeColor = Color.Red;
            }
            else
            {
                // Nếu không có cái nào bị -> Normal
                cellAlarm.Value = "Normal";
                cellAlarm.Style.ForeColor = Color.Green;
                cellAlarm.Style.Font = new Font("Segoe UI", 11, FontStyle.Regular);
                cellAlarm.Style.SelectionForeColor = Color.Green;
            }
        }// Hàm kiểm tra nhanh xem một Alarm ID có đang Active không
        private bool IsAlarmActive(string alarmId)
        {
            foreach (var alarm in _alarmEngine.GetAll())
            {
                if (alarm.Id == alarmId && alarm.IsActive) return true;/
            }
            return false;
        }
        private int AlarmIdToRowIndex(string alarmId)
        {
            return alarmId switch
            {
                "AL_WIND" => FindRow("WIND"),
                "AL_ROLL" => FindRow("R/P/H"),
                "AL_PITCH" => FindRow("R/P/H"),
                "AL_HEAVE" => FindRow("R/P/H"),
                _ => -1
            };
        }
        private void BtnSettings_Click(object s, EventArgs e) { LoginForm f = new LoginForm(); if (f.ShowDialog() == DialogResult.OK) { new ConfigForm().ShowDialog(); SystemConfig.Apply(ConfigService.Load()); RefreshDataListLimits(); } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); EnableDoubleBuffer(tableLayoutPanel1); EnableDoubleBuffer(tableLayoutPanel2); }
        private void EnableDoubleBuffer(Control c) { if (c != null) typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true, null); }
    }
}