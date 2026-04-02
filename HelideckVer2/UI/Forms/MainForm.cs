using HelideckVer2.Models;
using HelideckVer2.Services;
using HelideckVer2.Services.Parsing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection.Metadata;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static System.Net.Mime.MediaTypeNames;
using Application = System.Windows.Forms.Application;
using Font = System.Drawing.Font;
using Image = System.Drawing.Image;

namespace HelideckVer2
{
    public partial class MainForm : Form
    {
        private ComEngine _comEngine;
        private NmeaParserService _nmeaParser;
        private HelideckVer2.UI.Controls.RadarControl _radarControl;
        private HelideckVer2.UI.Controls.TrendChartControl.TrendMode _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion;
        private DataLogger _logger;
        
        private List<DeviceTask> _taskList;

        private double _lastHeaveValue = 0;
        private DateTime? _lastZeroCrossTime = null;

        private AlarmEngine _alarmEngine;
        private bool _isLiveMode = true;
        private bool _isSeparateTrend = false; // Cờ theo dõi Tách/Gộp Trend
        
        private bool _isChartZoomed = false;   // THÊM CỜ NÀY: Theo dõi trạng thái Click Zoom
        private Tag _windTag, _rollTag, _pitchTag, _heaveTag;
        private Label lblAlarmStatus;
        private FlowLayoutPanel _topMenu;
        private TabControl _mainTabControl;

        private const int BufferMinutes = 20;
        private double _currentViewMinutes = 2.0;

        

        private double _speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm, _heavePeriodSec, _windSpeedMs, _windDirDeg;

        private System.Windows.Forms.Timer _healthTimer, _snapshotTimer, _chartUpdateTimer;
        private const double StaleSeconds = 2.0;

        private double _rollOffset = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm = 10.0;
        private bool _isProgrammaticScroll = false;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;
        //private ToolTip _chartToolTip = new ToolTip();
        //private Point _lastMousePos = Point.Empty;

        //private string _lastTooltipText = "";

        private string _hoverText = "";
        private Point _hoverPoint = Point.Empty;
        

        // ===== BIẾN HÃM TỐC ĐỘ UI =====
        private DateTime _lastMotionUIUpdate = DateTime.MinValue;
        private DateTime _lastWindUIUpdate = DateTime.MinValue;
        private DateTime _lastGpsUIUpdate = DateTime.MinValue;

        private HelideckVer2.UI.Controls.TrendChartControl _trendControl;
        // ==========================================
        // 3. CONSTRUCTOR
        // ==========================================
        public MainForm()
        {
            InitializeComponent();
            ApplyLeftPanelStyles();

            LoadImageFromFile(pictureBox1, "picture1.png");
            // Không load plan_view nữa để nhường chỗ vẽ Radar nền trắng
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;

            // GẮN SỰ KIỆN VẼ RADAR LÊN PICTUREBOX2
            // --- THAY THẾ PICTUREBOX2 BẰNG RADAR ĐỘC LẬP ---
            _radarControl = new HelideckVer2.UI.Controls.RadarControl { Dock = DockStyle.Fill };

            // Chèn RadarControl vào cùng chỗ với pictureBox2
            pictureBox2.Parent.Controls.Add(_radarControl);
            _radarControl.BringToFront(); // Đưa lên lớp trên cùng
            pictureBox2.Visible = false;  // Giấu pictureBox2 cũ đi (không ảnh hưởng file Designer)

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

            SetupMainLayout();
            EnsureAlarmBadge();
            SetupStatusBadges();
            SetupTrendButtonsNearChart();
            

            _logger = new DataLogger();

            _snapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _snapshotTimer.Tick += (s, e) => _logger.LogSnapshot(_speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm, _heavePeriodSec, _windSpeedMs, _windDirDeg);
            _snapshotTimer.Start();

            _comEngine = new ComEngine();
            _comEngine.OnDataReceived += OnComDataReceived;

            // Dùng SystemConfig để quyết định chế độ chạy
            if (HelideckVer2.Models.SystemConfig.IsSimulationMode)
            {
                var simEngine = new SimulationEngine();
                // Bắt dữ liệu giả lập ném thẳng vào OnComDataReceived như thật
                simEngine.Start(OnComDataReceived);
                this.Text += " [SIMULATION MODE - READ FROM CONFIG]";
            }
            else
            {
                // Chạy COM Port thật
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
            
            StartHealthTimer();

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => _trendControl.Render(); ;
            _chartUpdateTimer.Start();
            // Khởi tạo và đăng ký lắng nghe sự kiện từ Parser
            _nmeaParser = new NmeaParserService();
            _nmeaParser.OnHeadingParsed += HandleHeading;
            _nmeaParser.OnWindParsed += HandleWind;
            _nmeaParser.OnMotionParsed += HandleMotion;
            _nmeaParser.OnPositionParsed += HandlePosition;
            _nmeaParser.OnSpeedParsed += HandleSpeed;
            // --- THAY THẾ CHART CŨ BẰNG CONTROL ĐỘC LẬP ---
            _trendControl = new HelideckVer2.UI.Controls.TrendChartControl { Dock = DockStyle.Fill };
            panelChartHost.Controls.Clear();
            panelChartHost.Controls.Add(_trendControl);
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

            _topMenu.Controls.Add(btnOver);

            _mainTabControl = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };

            var tabOverview = new TabPage("Overview") { BackColor = Color.WhiteSmoke };
            this.tableLayoutPanel1.Parent = tabOverview;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Visible = true;

            _mainTabControl.TabPages.Add(tabOverview);

            root.Controls.Add(_mainTabControl);
            root.Controls.Add(pnlBottom);
            root.Controls.Add(_topMenu);
        }
        // ==========================================
        // CÁC HÀM XỬ LÝ SỰ KIỆN TỪ NMEA PARSER
        // ==========================================
        private void HandleHeading(double heading)
        {
            this.Invoke(new Action(() => {
                _headingDeg = heading; 
                HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateSensorData("HEADING", $"{heading:0.0}", heading);

                DateTime now = DateTime.Now;
                if ((now - _lastGpsUIUpdate).TotalMilliseconds >= 250)
                {
                    lblHeading.Text = $"{heading:0.0}°";
                    _lastGpsUIUpdate = now;
                }
                _radarControl.UpdateRadar(_headingDeg, _windDirDeg);
            }));
        }

        private void HandleWind(double wSpeed, double wDir)
        {
            _trendControl.PushWindData(wSpeed, wDir);
            this.Invoke(new Action(() => {
                _windSpeedMs = wSpeed; _windDirDeg = wDir; 
                HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateSensorData("WIND", $"{wSpeed:0.0} / {wDir:0}", wSpeed, wDir);

                _windTag.Update(wSpeed);
                _alarmEngine.Evaluate();

                DateTime now = DateTime.Now;
                

                if ((now - _lastWindUIUpdate).TotalMilliseconds >= 250)
                {
                    lblWindSpeed.Text = $"{wSpeed:0.0} m/s";
                    lblWindRelated.Text = $"{wDir:0}°";
                    _lastWindUIUpdate = now;
                }
                _radarControl.UpdateRadar(_headingDeg, _windDirDeg);
            }));
        }

        private void HandleMotion(double r, double p, double h)
        {
            this.Invoke(new Action(() => {
                DisplayMotionData(r + _rollOffset, p + _pitchOffset, h);
            }));
        }

        private void HandlePosition(string fLat, string fLon)
        {
            this.Invoke(new Action(() => {
                HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateSensorData("GPS", $"{fLat} {fLon}");
                if ((DateTime.Now - _lastGpsUIUpdate).TotalMilliseconds >= 250)
                    lblPosition.Text = fLat == "NO FIX" ? "NO FIX" : $"{fLon}\r\n{fLat}";
            }));
        }

        private void HandleSpeed(double k)
        {
            this.Invoke(new Action(() => {
                _speedKnot = k;
                if ((DateTime.Now - _lastGpsUIUpdate).TotalMilliseconds >= 250)
                    lblSpeed.Text = $"{k:0.0} kn";
            }));
        }
        

        
        private void ApplyLeftPanelStyles()
        {
            if (tableLayoutPanel1 != null)
            {
                tableLayoutPanel1.ColumnStyles.Clear();
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
                tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            }

            // --- CẤU HÌNH NỀN GRID CHO CÁC THẺ ---
            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
            tableLayoutPanel2.BackColor = Color.FromArgb(230, 235, 240);
            tableLayoutPanel2.Padding = new Padding(5);
            tableLayoutPanel2.Controls.Clear();
            tableLayoutPanel2.ColumnStyles.Clear();
            tableLayoutPanel2.RowStyles.Clear();

            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.RowCount = 5;
            for (int i = 0; i < 5; i++) tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));

            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9 };
            string[] headers = { "POSITION", "SPEED", "HEADING", "ROLL", "PITCH", "HEAVE", "H.PERIOD", "WIND SPD", "WIND DIR" };
            Label[] values = { lblPosition, lblSpeed, lblHeading, lblRoll, lblPitch, lblHeave, lblHeaveCycle, lblWindSpeed, lblWindRelated };

            for (int i = 0; i < 9; i++)
            {
                if (titles[i] == null || values[i] == null) continue;

                // 1. Tiêu đề thẻ (Chữ)
                titles[i].Text = headers[i];
                titles[i].AutoSize = false;
                titles[i].Dock = DockStyle.Top;
                titles[i].TextAlign = ContentAlignment.MiddleCenter;
                titles[i].ForeColor = Color.DimGray;
                titles[i].BackColor = Color.White; // CHỐNG GIẬT: Bỏ Transparent, dùng White

                // 2. Giá trị thẻ (Số)
                values[i].Text = "---";
                values[i].AutoSize = false;
                values[i].Dock = DockStyle.Fill;
                values[i].TextAlign = ContentAlignment.MiddleCenter;
                values[i].BackColor = Color.White; // CHỐNG GIẬT: Bỏ Transparent, dùng White
                values[i].ForeColor = Color.Black;

                if (i == 0) values[i].ForeColor = Color.DarkBlue; // POSITION
                if (i == 5) values[i].ForeColor = Color.Red;      // HEAVE

                Panel card = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(6),
                    BackColor = Color.White
                };

                // CHỐNG GIẬT: Bật bộ đệm kép cho Card để viền không bị nháy khi vẽ lại
                EnableDoubleBuffer(card);

                int index = i;

                // --- ÉP FONT CO GIÃN THEO CẢ CHIỀU NGANG VÀ DỌC ---
                card.Resize += (s, e) =>
                {
                    if (card.Height <= 0 || card.Width <= 0) return;

                    titles[index].Height = (int)(card.Height * 0.25f);

                    float titleFontSize = Math.Min(card.Height * 0.10f, card.Width * 0.07f);
                    titleFontSize = Math.Max(9f, Math.Min(titleFontSize, 22f));

                    if (titles[index].Font == null || Math.Abs(titles[index].Font.Size - titleFontSize) > 0.5f)
                    {
                        Font oldFont = titles[index].Font;
                        titles[index].Font = new Font("Segoe UI", titleFontSize, FontStyle.Bold);
                        if (oldFont != null) oldFont.Dispose();
                    }

                    float valueFontSize;
                    if (index == 0)
                    {
                        valueFontSize = Math.Min(card.Height * 0.13f, card.Width * 0.045f);
                    }
                    else
                    {
                        valueFontSize = Math.Min(card.Height * 0.32f, card.Width * 0.14f);
                    }

                    valueFontSize = Math.Max(10f, Math.Min(valueFontSize, 50f));

                    if (values[index].Font == null || Math.Abs(values[index].Font.Size - valueFontSize) > 0.5f)
                    {
                        Font oldFont = values[index].Font;
                        values[index].Font = new Font("Segoe UI", valueFontSize, FontStyle.Bold);
                        if (oldFont != null) oldFont.Dispose();
                    }
                };

                // Vẽ bo góc thẻ và vạch kẻ ngang
                card.Paint += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    Rectangle rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                    using (GraphicsPath path = GetRoundedRect(rect, 12))
                    using (Pen pen = new Pen(Color.LightGray, 2))
                    {
                        g.DrawPath(pen, path);
                    }

                    int lineY = titles[index].Height;
                    using (Pen sepPen = new Pen(Color.FromArgb(220, 220, 220), 2))
                    {
                        g.DrawLine(sepPen, 15, lineY, card.Width - 15, lineY);
                    }
                };

                card.Controls.Add(values[i]);
                card.Controls.Add(titles[i]);

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
        // --- HÀM HỖ TRỢ VẼ BO GÓC ---
        private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (radius == 0) { path.AddRectangle(bounds); return path; }

            path.AddArc(arc, 180, 90); // Góc trên trái
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90); // Góc trên phải
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);   // Góc dưới phải
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);  // Góc dưới trái
            path.CloseFigure();

            return path;
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
                // 1. Lấy trạng thái mới nhất từ DataHub
                var snapshot = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();

                // 2. Duyệt qua từng Task để cập nhật UI Badges và màu chữ
                foreach (var row in snapshot.TaskRows)
                {
                    string taskName = row.TaskName;
                    bool isStale = row.IsStale;
                    double age = row.Age;

                    switch (taskName)
                    {
                        case "GPS":
                            UpdateBadge(lblStatGPS, "GPS", isStale, age);
                            UpdateLabelStatus(lblPosition, !isStale);
                            UpdateLabelStatus(lblSpeed, !isStale);
                            break;
                        case "WIND":
                            UpdateBadge(lblStatWind, "WIND", isStale, age);
                            UpdateLabelStatus(lblWindSpeed, !isStale);
                            UpdateLabelStatus(lblWindRelated, !isStale);
                            break;
                        case "R/P/H":
                            UpdateBadge(lblStatMotion, "R/P/H", isStale, age);
                            UpdateLabelStatus(lblRoll, !isStale);
                            UpdateLabelStatus(lblPitch, !isStale);
                            UpdateLabelStatus(lblHeave, !isStale);
                            UpdateLabelStatus(lblHeaveCycle, !isStale);
                            break;
                        case "HEADING":
                            UpdateBadge(lblStatHeading, "HEADING", isStale, age);
                            UpdateLabelStatus(lblHeading, !isStale);
                            break;
                    }
                }
            };
            _healthTimer.Start();
        }

        private void UpdateGridByTask(string task, string val)
        {
            // Ghi dữ liệu trực tiếp vào DataHub một cách an toàn
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateSensorData(task, val);
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

            string rphAlarm = activeRPH.Count > 0 ? string.Join(", ", activeRPH) : "Normal";
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateAlarmState("R/P/H", rphAlarm);

            string windAlarm = IsAlarmActive("AL_WIND") ? "WIND" : "Normal";
            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateAlarmState("WIND", windAlarm);
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
        
        private void OnComDataReceived(string portName, string rawData)
        {
            // Form1 không tự cắt chuỗi nữa, ném thẳng cho chuyên gia Parser xử lý
            _nmeaParser.Parse(portName, rawData);
        }



        private void DisplayMotionData(double r, double p, double h)
        {
            _trendControl.PushMotionData(r, p, h);
            _rollDeg = r; _pitchDeg = p; _heaveCm = h;
            DateTime now = DateTime.Now;

            // --- BÍ QUYẾT: HÃM UI NHƯNG KHÔNG HÃM CHART ---
            if ((now - _lastMotionUIUpdate).TotalMilliseconds >= 250)
            {
                lblRoll.Text = $"{r:0.0}°";   // Giảm xuống 1 số thập phân cho đỡ rối
                lblPitch.Text = $"{p:0.0}°";
                lblHeave.Text = $"{h:0.0} cm";
                _lastMotionUIUpdate = now;
            }

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

            HelideckVer2.Core.Data.HelideckDataHub.Instance.UpdateSensorData("R/P/H", $"R:{r:0.0} P:{p:0.0} H:{h:0.0}", Math.Abs(r), Math.Abs(p), Math.Abs(h));

            
            _rollTag.Update(Math.Abs(r)); _pitchTag.Update(Math.Abs(p)); _heaveTag.Update(Math.Abs(h)); _alarmEngine.Evaluate();
        }
       
        // ==========================================
        // 7. CÁC HÀM TIỆN ÍCH, VẼ GIAO DIỆN, CHART
        // ==========================================
        private void InitializeTasks() { _taskList = new List<DeviceTask>(); foreach (var t in ConfigForm.Tasks) _taskList.Add(new DeviceTask { TaskName = t.TaskName, PortName = t.PortName, BaudRate = t.BaudRate, Value1 = 0, HighLimit = t.HighLimit }); }
        private void UpdateBadge(Label lbl, string name, bool isStale, double age) { if (age > 900) { lbl.Text = $"{name}: WAIT"; lbl.BackColor = Color.DimGray; } else if (isStale) { lbl.Text = $"{name}: LOST"; lbl.BackColor = Color.Red; } else { lbl.Text = $"{name}: OK"; lbl.BackColor = Color.ForestGreen; } }
        private void UpdateLabelStatus(Label lbl, bool isAlive) { if (lbl != null) { if (isAlive && lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.Black; else if (!isAlive) lbl.ForeColor = Color.Gray; } }
        private void EnsureAlarmBadge() { if (lblAlarmStatus == null) { lblAlarmStatus = new Label { AutoSize = false, Width = 240, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.Black, ForeColor = Color.Lime, Text = "NORMAL", Margin = new Padding(20, 7, 5, 5) }; _topMenu.Controls.Add(lblAlarmStatus); } }
        private void SetupStatusBadges() { Label CreateBadge(string text) => new Label { Text = text, AutoSize = false, Size = new Size(140, 30), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.DimGray, ForeColor = Color.White, Margin = new Padding(5, 7, 0, 5), BorderStyle = BorderStyle.FixedSingle }; lblStatGPS = CreateBadge("GPS: WAIT"); lblStatWind = CreateBadge("WIND: WAIT"); lblStatMotion = CreateBadge("R/P/H: WAIT"); lblStatHeading = CreateBadge("HEADING: WAIT"); _topMenu.Controls.AddRange(new Control[] { lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading }); }
        private Button CreateMenuButton(string text, Color bg) => new Button
        {
            Text = text,
            BackColor = bg,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true, // Tự động giãn chiều ngang
            MinimumSize = new Size(0, 30), // Khóa cứng chiều cao tối thiểu 30px
            MaximumSize = new Size(0, 30), // Khóa cứng chiều cao tối đa 30px (Chống phình to chiều dọc)
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(5, 2, 5, 2)
        };

        private void LoadImageFromFile(PictureBox picBox, string fileName)
        {
            try
            {
                // 1. Lấy đường dẫn tới thư mục Images
                string folderPath = Path.Combine(Application.StartupPath, "Images");

                // 2. TỰ ĐỘNG TẠO THƯ MỤC NẾU CHƯA TỒN TẠI
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // 3. Ghép tên file và load ảnh
                string path = Path.Combine(folderPath, fileName);
                if (File.Exists(path))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        picBox.Image = Image.FromStream(stream);
                    }
                }
            }
            catch { /* Bỏ qua lỗi để không làm treo phần mềm nếu ảnh bị hỏng */ }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); this.DoubleBuffered = true; EnableDoubleBuffer(tableLayoutPanel1); EnableDoubleBuffer(tableLayoutPanel2); EnableDoubleBuffer(tableLayoutPanel3); EnableDoubleBuffer(tableLayoutPanelTrend); }
        private void EnableDoubleBuffer(Control c) { if (c != null) typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true, null); }
        private void BtnSettings_Click(object s, EventArgs e)
        {
            if (new LoginForm().ShowDialog() == DialogResult.OK)
            {
                new ConfigForm().ShowDialog();

                // 1. Nạp lại cấu hình mới vào biến tĩnh
                SystemConfig.Apply(ConfigService.Load());

            }
        }

        
        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();

            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.White);
            btnTrend1.Click += (s, e) => {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Motion;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            Button btnTrend2 = CreateMenuButton("TREND Wind", Color.White);
            btnTrend2.Click += (s, e) => {
                _currentTrendMode = HelideckVer2.UI.Controls.TrendChartControl.TrendMode.Wind;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend);
            };

            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btnZoom.Click += (s, e) => {
                if (_currentViewMinutes == 2.0) { _currentViewMinutes = 20.0; btnZoom.Text = "VIEW: 20 Min"; btnZoom.BackColor = Color.LightSalmon; }
                else { _currentViewMinutes = 2.0; btnZoom.Text = "VIEW: 2 Min"; btnZoom.BackColor = Color.LightBlue; }
                _trendControl.SetViewWindow(_currentViewMinutes); // Gọi thẳng Control
            };

            Button btnToggleSplit = CreateMenuButton("MODE: COMBINED", Color.LightGreen);
            btnToggleSplit.Width = 150;
            btnToggleSplit.Click += (s, e) => {
                _isSeparateTrend = !_isSeparateTrend;
                btnToggleSplit.Text = _isSeparateTrend ? "MODE: SPLIT" : "MODE: COMBINED";
                btnToggleSplit.BackColor = _isSeparateTrend ? Color.Yellow : Color.LightGreen;
                _trendControl.SetMode(_currentTrendMode, _isSeparateTrend); // Cập nhật lại Control
            };

            panelTrendButtons.Controls.AddRange(new Control[] { btnTrend1, btnTrend2, btnToggleSplit, btnZoom });
        }
        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) { }
    }
}