using HelideckVer2.Models;
using HelideckVer2.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static System.Net.Mime.MediaTypeNames;
using Font = System.Drawing.Font;
using Image = System.Drawing.Image;
using Application = System.Windows.Forms.Application;
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
        private bool _isSeparateTrend = false; // Cờ theo dõi Tách/Gộp Trend
        
        private bool _isChartZoomed = false;   // THÊM CỜ NÀY: Theo dõi trạng thái Click Zoom
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
        //private ToolTip _chartToolTip = new ToolTip();
        //private Point _lastMousePos = Point.Empty;

        //private string _lastTooltipText = "";

        private string _hoverText = "";
        private Point _hoverPoint = Point.Empty;
        // ===== BIẾN CHO HMI RADAR =====
        private double _drawHeading = 0, _drawWindDir = 0;
        private readonly PointF[] _shipPoly = { new PointF(0, -100), new PointF(30, -30), new PointF(30, 90), new PointF(-30, 90), new PointF(-30, -30) };

        // ===== BIẾN HÃM TỐC ĐỘ UI =====
        private DateTime _lastMotionUIUpdate = DateTime.MinValue;
        private DateTime _lastWindUIUpdate = DateTime.MinValue;
        private DateTime _lastGpsUIUpdate = DateTime.MinValue;

        // ==========================================
        // 3. CONSTRUCTOR
        // ==========================================
        public Form1()
        {
            InitializeComponent();
            ApplyLeftPanelStyles();

            LoadImageFromFile(pictureBox1, "elevation_view.png");
            // Không load plan_view nữa để nhường chỗ vẽ Radar nền trắng
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;

            // GẮN SỰ KIỆN VẼ RADAR LÊN PICTUREBOX2
            AttachHmiToPictureBox();

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

        private void AttachHmiToPictureBox()
        {
            if (pictureBox2 != null)
            {
                pictureBox2.BackColor = Color.FromArgb(240, 240, 240); // Màu nền nhạt
                pictureBox2.Paint += PictureBox2_Paint;
                pictureBox2.Resize += (s, e) => pictureBox2.Invalidate();
            }
        }

        private void PictureBox2_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = pictureBox2.Width, h = pictureBox2.Height;
            Point center = new Point(w / 2, h / 2);

            // Bán kính la bàn
            int radius = (int)(Math.Min(w, h) / 2 * 0.75f);
            if (radius < 20) return;

            using (SolidBrush bgBrush = new SolidBrush(Color.White))
                g.FillEllipse(bgBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (Pen pBorder = new Pen(Color.Gray, 2))
                g.DrawEllipse(pBorder, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            float radarFontSize = Math.Max(7f, radius * 0.08f);

            // Vẽ lưới la bàn và số độ
            using (Pen pGrid = new Pen(Color.FromArgb(180, 200, 220), 1) { DashStyle = DashStyle.Dot })
            using (Pen pMain = new Pen(Color.LightSlateGray, 2))
            using (Font font = new Font("Segoe UI", radarFontSize, FontStyle.Bold))
            using (Font fontN = new Font("Segoe UI", radarFontSize * 1.5f, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.DarkSlateGray))
            {
                g.DrawEllipse(pGrid, center.X - radius * 0.66f, center.Y - radius * 0.66f, radius * 1.33f, radius * 1.33f);
                g.DrawEllipse(pGrid, center.X - radius * 0.33f, center.Y - radius * 0.33f, radius * 0.66f, radius * 0.66f);
                g.DrawLine(pGrid, center.X - radius, center.Y, center.X + radius, center.Y);
                g.DrawLine(pGrid, center.X, center.Y - radius, center.X, center.Y + radius);

                for (int i = 0; i < 360; i += 30)
                {
                    double rad = (i - 90) * Math.PI / 180.0;
                    int len = (i % 90 == 0) ? 15 : 8;
                    float x1 = center.X + (float)(Math.Cos(rad) * radius);
                    float y1 = center.Y + (float)(Math.Sin(rad) * radius);
                    float x2 = center.X + (float)(Math.Cos(rad) * (radius - len));
                    float y2 = center.Y + (float)(Math.Sin(rad) * (radius - len));
                    g.DrawLine((i % 90 == 0) ? pMain : pGrid, x1, y1, x2, y2);

                    if (i != 0)
                    {
                        string txt = i.ToString("000");
                        float xT = center.X + (float)(Math.Cos(rad) * (radius + radarFontSize * 1.4f));
                        float yT = center.Y + (float)(Math.Sin(rad) * (radius + radarFontSize * 1.4f));

                        var stateText = g.Save();
                        g.TranslateTransform(xT, yT);
                        SizeF s = g.MeasureString(txt, font);
                        g.DrawString(txt, font, brush, -s.Width / 2, -s.Height / 2);
                        g.Restore(stateText);
                    }
                }

                SizeF sizeN = g.MeasureString("N", fontN);
                g.DrawString("N", fontN, Brushes.DarkBlue, center.X - (sizeN.Width / 2), center.Y - radius - (radarFontSize * 1.8f) - (sizeN.Height / 2));
            }

            // --- 1. VẼ TÀU (THU NHỎ THÀNH ICON TRUNG TÂM) ---
            var stateShip = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_drawHeading);

            // Vạch định hướng (Heading line) dài ra mép Radar
            using (Pen pHead = new Pen(Color.DarkGoldenrod, 2) { DashStyle = DashStyle.Dash })
                g.DrawLine(pHead, 0, 0, 0, -radius + 15);

            // [CHỈNH SỬA]: Chia cho 380f để tàu nhỏ đi gọn gàng ở chính giữa
            float shipScale = radius / 380f;
            g.ScaleTransform(shipScale, shipScale);

            // Tô màu xám nhạt cho tàu và vẽ viền đậm để nổi bật
            using (SolidBrush bShip = new SolidBrush(Color.LightGray)) g.FillPolygon(bShip, _shipPoly);
            using (Pen pShipBorder = new Pen(Color.DimGray, 2)) g.DrawPolygon(pShipBorder, _shipPoly);

            g.Restore(stateShip);

            // --- 2. VẼ MŨI TÊN GIÓ (THON DÀI, BẮT ĐẦU TỪ NGOÀI TÀU) ---
            var stateWind = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_drawWindDir + 180);

            // [CHỈNH SỬA]: Tỷ lệ mũi tên thanh thoát hơn, không đè lên tàu
            float arrowTipY = radius * 0.75f;   // Chóp mũi tên ở vị trí 75% bán kính (gần rìa)
            float arrowBaseY = radius * 0.35f;  // Đuôi mũi tên ở vị trí 35% bán kính (nằm ngoài con tàu)
            float headLen = radius * 0.15f;     // Chiều dài chóp
            float headWidth = radius * 0.05f;   // Độ béo của chóp

            // Vẽ thân mũi tên (Cán)
            float tailThickness = Math.Max(2f, radius * 0.02f);
            using (Pen pWindTail = new Pen(Color.RoyalBlue, tailThickness))
                g.DrawLine(pWindTail, 0, -arrowBaseY, 0, -arrowTipY + headLen);
            using (Pen pWindTailBorder = new Pen(Color.White, 1) { DashStyle = DashStyle.Dot })
                g.DrawLine(pWindTailBorder, 0, -arrowBaseY, 0, -arrowTipY + headLen);

            // Vẽ chóp mũi tên
            PointF[] arrowHead = {
                new PointF(0, -arrowTipY),
                new PointF(-headWidth, -arrowTipY + headLen),
                new PointF(headWidth, -arrowTipY + headLen)
            };

            using (SolidBrush bWind = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(bWind, arrowHead);
            using (Pen pBorderWind = new Pen(Color.White, 1)) g.DrawPolygon(pBorderWind, arrowHead);

            g.Restore(stateWind);

            // --- 3. BẢNG CHÚ GIẢI ---
            DrawLegend(g, radarFontSize);
        }

        // --- HÀM VẼ BẢNG CHÚ GIẢI (CẬP NHẬT LẠI MÀU TÀU) ---
        // --- HÀM VẼ BẢNG CHÚ GIẢI (NEO VÀO GÓC TRÁI TRÊN CÙNG ĐỂ CHỐNG LỖI ĐÈ) ---
        private void DrawLegend(Graphics g, float radarFontSize)
        {
            // Tự động tính toán font chú giải bé hơn font radar một chút
            using (Font legendFont = new Font("Segoe UI", Math.Max(8f, radarFontSize * 0.9f), FontStyle.Bold))
            {
                // Neo cứng tọa độ X, Y ở góc trên cùng bên trái của khung (cách mép 10px)
                float startX = 10f;
                float startY = 10f;
                float lineSpacing = legendFont.Height + 10f; // Khoảng cách giữa 2 dòng chú giải

                // 1. Vẽ Chú giải Tàu (Ship Heading)
                var stateLegendShip = g.Save();
                g.TranslateTransform(startX + 15, startY + (legendFont.Height / 2));
                g.ScaleTransform(0.07f, 0.07f); // Thu nhỏ polygon của tàu
                using (SolidBrush bLegShip = new SolidBrush(Color.LightGray)) g.FillPolygon(bLegShip, _shipPoly);
                using (Pen pLegShip = new Pen(Color.DimGray, 25)) g.DrawPolygon(pLegShip, _shipPoly);
                g.Restore(stateLegendShip);

                g.DrawString("Ship Heading", legendFont, Brushes.DimGray, startX + 35, startY);

                // 2. Vẽ Chú giải Gió (Wind Direction)
                float windY = startY + lineSpacing; // Tọa độ Y của dòng thứ 2
                var stateLegendWind = g.Save();
                g.TranslateTransform(startX + 15, windY + (legendFont.Height / 2));
                // Tự vẽ 1 cái mũi tên tam giác siêu nhỏ (đỡ phải dùng Scale phức tạp)
                PointF[] miniArrow = { new PointF(0, -7), new PointF(-6, 7), new PointF(6, 7) };
                using (SolidBrush bLegWind = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(bLegWind, miniArrow);
                g.Restore(stateLegendWind);

                g.DrawString("Wind Direction", legendFont, Brushes.RoyalBlue, startX + 35, windY);
            }
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
            // TĂNG TỐC ĐỘ: 100ms (10 lần/giây) thay vì 1000ms như cũ
            _simTimer = new System.Windows.Forms.Timer { Interval = 100 };
            Random rnd = new Random();

            _simTimer.Tick += (s, e) =>
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;

                // Vì Timer chạy nhanh gấp 10 lần, ta phải giảm bước thời gian xuống 10 lần (0.1 thay vì 1.0)
                // Điều này giúp chu kỳ sóng (5s, 6s, 8s) vẫn giữ nguyên độ chân thực, nhưng đường vẽ ra sẽ mịn màng hơn
                _simTimeCounter += 0.1;

                string lat = "1045." + rnd.Next(100, 999), lon = "10640." + rnd.Next(100, 999);
                double speed = 5.0 + rnd.NextDouble() * 2;
                double windSpeed = 15.0 + (rnd.NextDouble() * 10 - 5);
                double windDir = 120.0 + (rnd.NextDouble() * 20 - 10);

                double roll = 2.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 8.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double pitch = 1.5 * Math.Cos(_simTimeCounter * 2 * Math.PI / 6.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double heave = 30.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 5.0) + (rnd.NextDouble() * 0.2 - 0.1);
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
                DateTime now = DateTime.Now;

                if (p[0].EndsWith("HDT") && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                    {
                        _headingDeg = heading; _drawHeading = heading;
                        UpdateGridByTask("HEADING", $"{heading:0.0}");

                        // Cập nhật số liệu GPS UI mỗi 250ms
                        if ((now - _lastGpsUIUpdate).TotalMilliseconds >= 250)
                        {
                            lblHeading.Text = $"{heading:0.0}°";
                            _lastGpsUIUpdate = now;
                        }
                        pictureBox2?.Invalidate();
                    }
                    return;
                }
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) && double.TryParse(p[3], style, culture, out double wSpeed))
                    {
                        _windSpeedMs = wSpeed; _windDirDeg = wDir; _drawWindDir = wDir;
                        UpdateGridByTask("WIND", $"{wSpeed:0.0} / {wDir:0}");
                        _windTag.Update(wSpeed); _alarmEngine.Evaluate();
                        lock (_windBuffer) { _windBuffer.Add(new TrendPoint { X = now.ToOADate(), V1 = wSpeed, V2 = wDir }); if (_windBuffer.Count > 0 && _windBuffer[0].X < now.AddMinutes(-BufferMinutes - 1).ToOADate()) _windBuffer.RemoveAt(0); }

                        // Cập nhật số liệu Wind UI mỗi 250ms
                        if ((now - _lastWindUIUpdate).TotalMilliseconds >= 250)
                        {
                            lblWindSpeed.Text = $"{wSpeed:0.0} m/s";
                            lblWindRelated.Text = $"{wDir:0}°";
                            _lastWindUIUpdate = now;
                        }
                        pictureBox2?.Invalidate();
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
                        UpdateGridByTask("GPS", $"{fLat} {fLon}");
                        if ((now - _lastGpsUIUpdate).TotalMilliseconds >= 250) lblPosition.Text = $"{fLon}\r\n{fLat}";
                    }
                    else lblPosition.Text = "NO FIX";
                    return;
                }
                if (p[0] == "$GPVTG")
                {
                    double k = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(k))
                    {
                        _speedKnot = k;
                        if ((now - _lastGpsUIUpdate).TotalMilliseconds >= 250) lblSpeed.Text = $"{k:0.0} kn";
                    }
                }
            }
            catch { }
        }

        private void DisplayMotionData(double r, double p, double h)
        {
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

            UpdateGridByTask("R/P/H", $"R:{r:0.0} P:{p:0.0} H:{h:0.0}");

            // Dữ liệu Chart vẫn nạp cực nhanh (10Hz) để đồ thị không bị khựng
            lock (_motionBuffer)
            {
                _motionBuffer.Add(new TrendPoint { X = now.ToOADate(), V1 = r, V2 = p, V3 = h });
                if (_motionBuffer.Count > 0 && _motionBuffer[0].X < now.AddMinutes(-BufferMinutes).ToOADate()) _motionBuffer.RemoveAt(0);
            }
            _rollTag.Update(Math.Abs(r)); _pitchTag.Update(Math.Abs(p)); _heaveTag.Update(Math.Abs(h)); _alarmEngine.Evaluate();
        }
        // --- HÀM VẼ ĐƯỜNG GIỚI HẠN BÁO ĐỘNG (RED LIMIT LINES) ---
        private void DrawAlarmLines(ChartArea area, string type)
        {
            area.AxisY.StripLines.Clear();

            // Hàm cục bộ để tạo 1 vạch đỏ
            StripLine CreateLimit(double offset) => new StripLine
            {
                IntervalOffset = offset,
                BorderColor = Color.Red,
                BorderDashStyle = ChartDashStyle.Dash,
                BorderWidth = 2
            };

            if (type == "Roll")
            {
                area.AxisY.StripLines.Add(CreateLimit(SystemConfig.RMax));
                area.AxisY.StripLines.Add(CreateLimit(-SystemConfig.RMax)); // Vẽ thêm limit ở phần âm
            }
            else if (type == "Pitch")
            {
                area.AxisY.StripLines.Add(CreateLimit(SystemConfig.PMax));
                area.AxisY.StripLines.Add(CreateLimit(-SystemConfig.PMax));
            }
            else if (type == "Heave")
            {
                area.AxisY.StripLines.Add(CreateLimit(SystemConfig.HMax));
                area.AxisY.StripLines.Add(CreateLimit(-SystemConfig.HMax));
            }
            else if (type == "Wind")
            {
                area.AxisY.StripLines.Add(CreateLimit(SystemConfig.WindMax));
            }
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
        private double TryGetNumberAfterToken(string[] p, string t) { for (int i = 0; i < p.Length - 1; i++) if (string.Equals(p[i], t, StringComparison.OrdinalIgnoreCase) && double.TryParse(p[i + 1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v)) return v; return double.NaN; }
        private void LoadImageFromFile(PictureBox picBox, string fileName) { try { string path = Path.Combine(Application.StartupPath, "Images", fileName); if (File.Exists(path)) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) picBox.Image = Image.FromStream(stream); } catch { } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); this.DoubleBuffered = true; EnableDoubleBuffer(tableLayoutPanel1); EnableDoubleBuffer(tableLayoutPanel2); EnableDoubleBuffer(tableLayoutPanel3); EnableDoubleBuffer(tableLayoutPanelTrend); }
        private void EnableDoubleBuffer(Control c) { if (c != null) typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true, null); }
        private void BtnSettings_Click(object s, EventArgs e)
        {
            if (new LoginForm().ShowDialog() == DialogResult.OK)
            {
                new ConfigForm().ShowDialog();

                // 1. Nạp lại cấu hình mới vào biến tĩnh
                SystemConfig.Apply(ConfigService.Load());

                // 2. Cập nhật bảng Data List (nếu có)
                RefreshDataListLimits();

                // 3. QUAN TRỌNG: Vẽ lại các đường đỏ trên Chart với giá trị mới
                UpdateChartAlarmLines();
            }
        }

        // Hàm mới để duyệt và vẽ lại toàn bộ đường đỏ
        private void UpdateChartAlarmLines()
        {
            if (_trendChart == null || _trendChart.ChartAreas.Count == 0) return;

            foreach (var area in _trendChart.ChartAreas)
            {
                // Kiểm tra xem Area này đang chứa Series nào để vẽ đúng loại đường đỏ
                if (area.Name.Contains("Roll")) DrawAlarmLines(area, "Roll");
                else if (area.Name.Contains("Pitch")) DrawAlarmLines(area, "Pitch");
                else if (area.Name.Contains("Heave")) DrawAlarmLines(area, "Heave");
                else if (area.Name.Contains("Wind")) DrawAlarmLines(area, "Wind");
                else if (area.Name == "MainArea") // Chế độ Gộp (Combined)
                {
                    // Nếu bạn muốn vẽ đường đỏ ở chế độ gộp, hãy gọi ở đây
                    // Tuy nhiên thường chế độ gộp sẽ bị rối nên ta có thể bỏ qua
                }
            }
        }

        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();

            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.White);
            btnTrend1.Click += (s, e) => SetTrendMode(TrendMode.Motion);

            Button btnTrend2 = CreateMenuButton("TREND Wind", Color.White);
            btnTrend2.Click += (s, e) => SetTrendMode(TrendMode.Wind);

            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btnZoom.Click += (s, e) => {
                if (_currentViewMinutes == 2.0) { _currentViewMinutes = 20.0; btnZoom.Text = "VIEW: 20 Min"; btnZoom.BackColor = Color.LightSalmon; }
                else { _currentViewMinutes = 2.0; btnZoom.Text = "VIEW: 2 Min"; btnZoom.BackColor = Color.LightBlue; }
                _isLiveMode = true;
                ApplyViewportToNow();
            };

            Button btnToggleSplit = CreateMenuButton("MODE: COMBINED", Color.LightGreen);
            btnToggleSplit.Width = 150;
            btnToggleSplit.Click += (s, e) => {
                _isSeparateTrend = !_isSeparateTrend;
                // Đổi chữ theo trạng thái
                btnToggleSplit.Text = _isSeparateTrend ? "MODE: SPLIT" : "MODE: COMBINED";
                btnToggleSplit.BackColor = _isSeparateTrend ? Color.Yellow : Color.LightGreen;
                SetTrendMode(_trendMode);
            };

            // Đưa 4 nút cơ bản vào panel (Đã bỏ btnLive)
            panelTrendButtons.Controls.AddRange(new Control[] { btnTrend1, btnTrend2, btnToggleSplit, btnZoom });
        }

        private void SetupIndustrialChart()
        {
            if (panelChartHost == null) return;
            if (_trendChart != null) { panelChartHost.Controls.Remove(_trendChart); _trendChart.Dispose(); }

            _trendChart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            _trendChart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Center, IsTextAutoFit = false, Font = new Font("Segoe UI", 12, FontStyle.Bold) });

            SetTrendMode(TrendMode.Motion);

            _trendChart.AxisViewChanged += (s, e) => {
                if (!_isProgrammaticScroll && _trendChart.ChartAreas.Count > 0)
                    _isLiveMode = (Math.Abs(_trendChart.ChartAreas[0].AxisX.Maximum - (_trendChart.ChartAreas[0].AxisX.ScaleView.Position + _trendChart.ChartAreas[0].AxisX.ScaleView.Size)) < (5.0 / 86400.0));
            };

            // --- 1. CHỈ TÍNH TOÁN DỮ LIỆU KHI DI CHUỘT (KHÔNG DÙNG TOOLTIP NỮA) ---
            _trendChart.MouseMove += (s, e) =>
            {
                if (_trendChart.ChartAreas.Count == 0) return;
                try
                {
                    ChartArea area = _trendChart.ChartAreas[0];
                    double xVal = area.AxisX.PixelPositionToValue(e.X);
                    area.CursorX.Position = xVal; // Đường gióng bám theo chuột

                    bool hasData = false;
                    string tip = "";

                    if (_trendMode == TrendMode.Motion)
                    {
                        TrendPoint closest = GetClosestPoint(_motionBuffer, xVal);
                        if (closest.X != 0)
                        {
                            tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nRoll: {closest.V1:0.00}°\nPitch: {closest.V2:0.00}°\nHeave: {closest.V3:0.0} cm";
                            hasData = true;
                        }
                    }
                    else
                    {
                        TrendPoint closest = GetClosestPoint(_windBuffer, xVal);
                        if (closest.X != 0)
                        {
                            tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nWind: {closest.V1:0.0} m/s\nDir: {closest.V2:0}°";
                            hasData = true;
                        }
                    }

                    if (hasData)
                    {
                        _hoverText = tip;
                        _hoverPoint = e.Location;
                    }
                    else
                    {
                        _hoverText = "";
                    }
                }
                catch { _hoverText = ""; }
            };

            // --- 2. ẨN CROSSHAIR KHI CHUỘT RỜI ĐI ---
            _trendChart.MouseLeave += (s, e) =>
            {
                _hoverText = "";
                foreach (var area in _trendChart.ChartAreas) area.CursorX.Position = double.NaN;
            };

            // --- 3. VẼ BẢNG THÔNG SỐ TRỰC TIẾP LÊN CHART (SIÊU MƯỢT) ---
            _trendChart.PostPaint += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_hoverText))
                {
                    Graphics g = e.ChartGraphics.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) // Nền trắng hơi trong suốt
                    using (Pen border = new Pen(Color.Gray, 1))
                    using (SolidBrush fg = new SolidBrush(Color.Black))
                    {
                        SizeF size = g.MeasureString(_hoverText, f);
                        float x = _hoverPoint.X + 15;
                        float y = _hoverPoint.Y + 15;

                        // Giữ bảng thông số luôn nằm trong khung hình (Không bị tràn ra ngoài)
                        if (x + size.Width + 10 > _trendChart.Width) x = _trendChart.Width - size.Width - 15;
                        if (y + size.Height + 10 > _trendChart.Height) y = _trendChart.Height - size.Height - 15;

                        RectangleF rect = new RectangleF(x, y, size.Width + 10, size.Height + 10);

                        g.FillRectangle(bg, rect);
                        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                        g.DrawString(_hoverText, f, fg, rect.X + 5, rect.Y + 5);
                    }
                }
            };

            panelChartHost.Controls.Add(_trendChart);
        }
        // --- HÀM VẼ CHÚ GIẢI TÙY BIẾN (CUSTOM LEGEND) ---
        private void RenderCustomLegend()
        {
            if (_trendChart == null || panelChartHost == null) return;

            // 1. Dọn dẹp Custom Legend cũ nếu có
            foreach (Control c in panelChartHost.Controls)
            {
                if (c.Name == "pnlCustomLegend") { panelChartHost.Controls.Remove(c); c.Dispose(); break; }
            }

            // 2. Tạo Panel chứa Custom Legend (Đặt nó nằm trên Chart)
            FlowLayoutPanel pnlCustomLegend = new FlowLayoutPanel
            {
                Name = "pnlCustomLegend",
                Dock = DockStyle.Top, // Đặt nó nằm trên cùng của panelHost
                Height = 35,
                BackColor = Color.White, // Nền trắng tiệp màu với Chart
                Padding = new Padding(10, 5, 10, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            // Hàm hỗ trợ tạo một Badge (Huy hiệu) có nền màu cho chữ
            Label CreateBadge(string text, Color bgColor, Color fgColor) => new Label
            {
                Text = text,
                AutoSize = true,
                BackColor = bgColor,
                ForeColor = fgColor,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Padding = new Padding(8, 2, 8, 2),
                Margin = new Padding(0, 0, 5, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Hàm hỗ trợ tạo vạch màu tương ứng với màu dây Chart
            Panel CreateColorLine(string seriesName)
            {
                Series s = _trendChart.Series.FindByName(seriesName);
                if (s == null) return null;
                return new Panel
                {
                    Width = 30,
                    Height = 6,
                    BackColor = s.Color,
                    Margin = new Padding(15, 12, 3, 0)
                };
            }

            //// 3. Vẽ Chú giải dựa trên Mode
            //if (_trendMode == TrendMode.Motion)
            //{
            //    // Chế độ Motion: R, P, H
            //    pnlCustomLegend.Controls.Add(CreateColorLine("Roll"));
            //    pnlCustomLegend.Controls.Add(CreateBadge("Roll", Color.DarkBlue, Color.White));

            //    pnlCustomLegend.Controls.Add(CreateColorLine("Pitch"));
            //    pnlCustomLegend.Controls.Add(CreateBadge("Pitch", Color.DarkOrange, Color.Black));

            //    pnlCustomLegend.Controls.Add(CreateColorLine("Heave"));
            //    pnlCustomLegend.Controls.Add(CreateBadge("Heave", Color.Red, Color.White));
            //}
            //else
            //{
            //    // Chế độ Wind: Tốc độ, Hướng
            //    pnlCustomLegend.Controls.Add(CreateColorLine("WindSpeed"));
            //    pnlCustomLegend.Controls.Add(CreateBadge("Wind speed", Color.DarkBlue, Color.White));

            //    pnlCustomLegend.Controls.Add(CreateColorLine("WindDir"));
            //    pnlCustomLegend.Controls.Add(CreateBadge("Direction", Color.DarkOrange, Color.Black));
            //}

            // 4. Thêm Custom Legend vào Panel Host
            panelChartHost.Controls.Add(pnlCustomLegend);
            pnlCustomLegend.BringToFront();
        }
        private void SetTrendMode(TrendMode mode)
        {
            _trendMode = mode;
            if (_trendChart == null) return;
            _trendChart.Series.Clear();
            _trendChart.ChartAreas.Clear();

            if (_isSeparateTrend)
            {
                string masterAreaName = "";

                if (mode == TrendMode.Motion)
                {
                    masterAreaName = "AreaRoll";
                    AddConfiguredChartArea(masterAreaName);
                    AddConfiguredChartArea("AreaPitch");
                    AddConfiguredChartArea("AreaHeave");

                    AddSeries("Roll", masterAreaName);
                    AddSeries("Pitch", "AreaPitch");
                    AddSeries("Heave", "AreaHeave");

                    AlignChartArea("AreaPitch", masterAreaName);
                    AlignChartArea("AreaHeave", masterAreaName);

                    // [THÊM MỚI]: VẼ ĐƯỜNG ĐỎ CẢNH BÁO CHO CHẾ ĐỘ TÁCH (SPLIT)
                    DrawAlarmLines(_trendChart.ChartAreas["AreaRoll"], "Roll");
                    DrawAlarmLines(_trendChart.ChartAreas["AreaPitch"], "Pitch");
                    DrawAlarmLines(_trendChart.ChartAreas["AreaHeave"], "Heave");
                }
                else
                {
                    masterAreaName = "AreaWindSpeed";
                    AddConfiguredChartArea(masterAreaName);
                    AddConfiguredChartArea("AreaWindDir");

                    AddSeries("WindSpeed", masterAreaName);
                    AddSeries("WindDir", "AreaWindDir");

                    AlignChartArea("AreaWindDir", masterAreaName);

                    // [THÊM MỚI]: VẼ ĐƯỜNG ĐỎ CẢNH BÁO GIÓ
                    DrawAlarmLines(_trendChart.ChartAreas["AreaWindSpeed"], "Wind");
                }
            }
            else
            {
                AddConfiguredChartArea("MainArea");
                if (mode == TrendMode.Motion)
                {
                    AddSeries("Roll", "MainArea");
                    AddSeries("Pitch", "MainArea");
                    AddSeries("Heave", "MainArea");
                    // Ở chế độ gộp, trục Y xài chung nên không vẽ đường đỏ để tránh rối mắt
                }
                else
                {
                    AddSeries("WindSpeed", "MainArea");
                    AddSeries("WindDir", "MainArea");
                }
            }

            ApplyViewportToNow();
            RenderCustomLegend();
        }

        // --- HÀM HỖ TRỢ CĂN LỀ (THÊM VÀO DƯỚI SetTrendMode) ---
        private void AlignChartArea(string areaToAlignName, string masterAreaName)
        {
            ChartArea target = _trendChart.ChartAreas.FindByName(areaToAlignName);
            if (target != null)
            {
                target.AlignWithChartArea = masterAreaName; // Căn lề theo Area chuẩn
                target.AlignmentOrientation = AreaAlignmentOrientations.Vertical; // Căn theo chiều dọc
                target.AlignmentStyle = AreaAlignmentStyles.PlotPosition | AreaAlignmentStyles.AxesView; // Căn cả vị trí vẽ và trục
            }
        }

        private void AddConfiguredChartArea(string areaName)
        {
            var area = new ChartArea(areaName) { BackColor = Color.White };
            area.AxisX.LabelStyle.Format = "HH:mm:ss";
            area.AxisX.IntervalType = DateTimeIntervalType.Minutes;
            area.AxisX.Interval = 1;
            area.AxisX.MajorGrid.LineColor = Color.LightGray;
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisX.ScaleView.Zoomable = true;

            area.CursorX.IsUserEnabled = true;
            area.CursorX.Interval = 0;
            area.CursorY.IsUserEnabled = true;
            area.CursorY.Interval = 0;

            area.AxisX.ScrollBar.Enabled = true;
            area.AxisY.IsStartedFromZero = false;
            area.AxisY.MajorGrid.LineColor = Color.LightGray;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            _trendChart.ChartAreas.Add(area);
        }

        private void AddSeries(string name, string areaName)
        {
            string legendText = name switch
            {
                "Roll" => "Roll",
                "Pitch" => "Pitch",
                "Heave" => "Heave",
                "WindSpeed" => "Wind speed",
                "WindDir" => "Direction",
                _ => name
            }; _trendChart.Series.Add(new Series(name)
            {
                ChartType = SeriesChartType.FastLine,
                BorderWidth = 3,
                XValueType = ChartValueType.DateTime,
                IsVisibleInLegend = true,
                LegendText = legendText,
                ChartArea = areaName
            });
        }
        private TrendPoint GetClosestPoint(List<TrendPoint> buffer, double targetX)
        {
            TrendPoint best = new TrendPoint { X = 0 };
            double minDiff = double.MaxValue;
            lock (buffer)
            {
                foreach (var pt in buffer)
                {
                    double diff = Math.Abs(pt.X - targetX);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        best = pt;
                    }
                }
            }
            // Chỉ hiện thông số nếu chuột nằm gần điểm dữ liệu (sai số dưới 5 giây)
            if (minDiff < (5.0 / 86400.0)) return best;
            return new TrendPoint { X = 0 }; // Trả về X = 0 nếu không có data gần đó
        }
        private void ApplyViewportToNow()
        {
            if (_trendChart == null) return;
            double nowX = DateTime.Now.ToOADate(), viewSize = _currentViewMinutes / 1440.0, bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            foreach (var area in _trendChart.ChartAreas)
            {
                area.AxisX.Minimum = bufferStart;
                area.AxisX.Maximum = nowX;
                area.AxisX.ScaleView.Size = viewSize;
                try { area.AxisX.ScaleView.Scroll(Math.Max(bufferStart, nowX - viewSize)); } catch { }
            }
        }

        private void RenderChartFromBuffer()
        {
            if (_trendChart == null || _trendChart.IsDisposed) return;
            double nowX = DateTime.Now.ToOADate(), viewSize = _currentViewMinutes / 1440.0, bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _trendChart.SuspendLayout();
            if (_trendMode == TrendMode.Motion) { UpdateSeriesData("Roll", _motionBuffer, 1); UpdateSeriesData("Pitch", _motionBuffer, 2); UpdateSeriesData("Heave", _motionBuffer, 3); }
            else { UpdateSeriesData("WindSpeed", _windBuffer, 1); UpdateSeriesData("WindDir", _windBuffer, 2); }

            foreach (var area in _trendChart.ChartAreas)
            {
                area.AxisX.Minimum = bufferStart;
                area.AxisX.Maximum = nowX;
                if (_isLiveMode)
                {
                    _isProgrammaticScroll = true;
                    area.AxisX.ScaleView.Position = Math.Max(bufferStart, nowX - viewSize);
                    area.AxisX.ScaleView.Size = viewSize;
                    _isProgrammaticScroll = false;
                }
            }
            _trendChart.ResumeLayout(); _trendChart.Invalidate();
        }

        private void UpdateSeriesData(string seriesName, List<TrendPoint> buffer, int valueIndex)
        {
            var series = _trendChart.Series.FindByName(seriesName); if (series == null) return;
            var area = _trendChart.ChartAreas[series.ChartArea];

            TrendPoint[] dataSnapshot; lock (buffer) { dataSnapshot = buffer.ToArray(); }
            series.Points.SuspendUpdates(); series.Points.Clear();

            foreach (var pt in dataSnapshot)
            {
                if (pt.X > area.AxisX.Minimum)
                    series.Points.AddXY(pt.X, valueIndex == 1 ? pt.V1 : valueIndex == 2 ? pt.V2 : pt.V3);
            }
            series.Points.ResumeUpdates();
        }

        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) { }
    }
}