using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using HelideckVer2.Services;
using HelideckVer2.Models;

namespace HelideckVer2
{
    public partial class Form1 : Form
    {
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

        private Label lblAlarmStatus;           // badge alarm nhỏ
        private FlowLayoutPanel _topMenu;       // top menu

        private const int BufferMinutes = 20; // Giữ nguyên bộ đệm 20p
        private double _currentViewMinutes = 2.0; // Biến thay đổi (Mặc định 2 phút)

        private enum TrendMode { Motion, Wind }
        private TrendMode _trendMode = TrendMode.Motion;

        private readonly List<TrendPoint> _motionBuffer = new();
        private readonly List<TrendPoint> _windBuffer = new();

        private struct TrendPoint
        {
            public double X;   // OADate
            public double V1;  // series1
            public double V2;  // series2
            public double V3;  // series3 (motion)
        }

        private double _speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm;
        private double _heavePeriodSec;
        private double _windSpeedMs, _windDirDeg;

        // ===== DataList meta/health =====
        private DateTime?[] _lastUpdate;    // theo từng row
        private string[] _unitText;         // UNIT theo row
        private string[] _limitText;        // LIMIT theo row (string để gộp R/P/H)
        private string[] _alarmText;        // ALARM theo row
        private System.Windows.Forms.Timer _healthTimer;

        private const double StaleSeconds = 2.0; // >2s là STALE

        private System.Windows.Forms.Timer _snapshotTimer;

        private double _rollOffset = 0.0;  // Tương đương Rollcalibvalue
        private double _pitchOffset = 0.0; // Tương đương Pitchcalibvalue
        private double _heaveArm = 10.0;   // Tương đương Heavecalibvalue (Khoảng cách mét/cm)
                                           // ...

        private System.Windows.Forms.Timer _chartUpdateTimer;
        // Biến cờ hiệu để tránh xung đột sự kiện cuộn
        private bool _isProgrammaticScroll = false;

        // Khai báo 4 nhãn trạng thái cho 4 hệ thống
        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;
        public Form1()
        {
            InitializeComponent();
            ApplyLeftPanelStyles();
            // --- CODE THÊM ẢNH VÀO ĐÂY ---
            // pictureBox1: Mặt chiếu đứng (Elevation View)
            LoadImageFromFile(pictureBox1, "elevation_view.png");

            // pictureBox2: Mặt chiếu ngang (Plan View)
            LoadImageFromFile(pictureBox2, "plan_view.png");

            // Chỉnh lại chế độ hiển thị cho đẹp (Zoom: vừa khít khung)
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            // ===== Load config =====
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

            // ===== UI base labels =====
            SetupHMILabel(lblSpeed);
            SetupHMILabel(lblHeading);
            SetupHMILabel(lblRoll);
            SetupHMILabel(lblPitch);
            SetupHMILabel(lblHeave);
            SetupHMILabel(lblHeaveCycle);
            SetupHMILabel(lblWindSpeed);

            InitializeTasks();

            // ===== Navigation + Tabs =====
            SetupNavigationSimple();

            // ===== Alarm badge nhỏ =====
            EnsureAlarmBadge();

            SetupStatusBadges();

            // ===== Chart + trend buttons =====
            SetupTrendButtonsNearChart();
            SetupIndustrialChart();

            // ===== Services =====
            _logger = new DataLogger();

            _snapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _snapshotTimer.Tick += (s, e) =>
            {
                _logger.LogSnapshot(
                    _speedKnot, _headingDeg,
                    _rollDeg, _pitchDeg, _heaveCm,
                    _heavePeriodSec,
                    _windSpeedMs, _windDirDeg
                );
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
            // Timer vẽ chart riêng biệt (10 lần/giây)
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

        private void ApplyLeftPanelStyles()
        {
            // ===== Table =====
            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel2.BackColor = Color.WhiteSmoke;

            // ===== Title labels (cột trái) =====
            Label[] titles = { label1, label2, label3, label4, label5, label6, label7, label8, label9 };
            foreach (var t in titles)
            {
                if (t == null) continue;
                t.AutoSize = false;
                t.Dock = DockStyle.Fill;
                t.TextAlign = ContentAlignment.MiddleLeft;
                t.Padding = new Padding(12, 0, 0, 0);
                t.Font = new Font("Segoe UI", 16, FontStyle.Bold);
                t.ForeColor = Color.Black;
                t.BackColor = Color.WhiteSmoke;
                t.Margin = new Padding(0);
            }

            // ===== Value labels (cột phải) =====
            Label[] values = { lblPosition, lblSpeed, lblHeading, lblRoll, lblPitch, lblHeave, lblHeaveCycle, lblWindSpeed, lblWindRelated };
            foreach (var v in values)
            {
                if (v == null) continue;
                v.AutoSize = false;
                v.Dock = DockStyle.Fill;
                v.TextAlign = ContentAlignment.MiddleCenter;
                v.Font = new Font("Segoe UI", 38, FontStyle.Bold);
                v.ForeColor = Color.Black;
                v.BackColor = Color.White;
                v.Margin = new Padding(0);
            }
        }

        // =========================
        //  NAVIGATION (SIMPLE)
        // =========================
        private void SetupNavigationSimple()
        {
            // Root container để đảm bảo layout không đè nhau
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.WhiteSmoke
            };
            this.Controls.Add(root);

            // Top menu
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

            Button btnSet = CreateMenuButton("SETTINGS", Color.Orange);
            btnSet.Click += BtnSettings_Click;

            _topMenu.Controls.Add(btnOver);
            _topMenu.Controls.Add(btnData);
            _topMenu.Controls.Add(btnSet);

            // TabControl
            _mainTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                ItemSize = new Size(0, 1),
                SizeMode = TabSizeMode.Fixed
            };

            var tabOverview = new TabPage("Overview") { BackColor = Color.WhiteSmoke };
            var tabDataList = new TabPage("DataList") { BackColor = Color.White };

            // Move overview layout into overview tab
            this.tableLayoutPanel1.Parent = tabOverview;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Visible = true;

            SetupDataGrid(tabDataList);

            _mainTabControl.TabPages.Add(tabOverview);
            _mainTabControl.TabPages.Add(tabDataList);

            // Add theo đúng thứ tự: Fill trước, Top sau (Top sẽ nằm trên)
            root.Controls.Add(_mainTabControl);
            root.Controls.Add(_topMenu);
        }

        // =========================
        //  DATA LIST GRID
        // =========================
        private void SetupDataGrid(TabPage parentTab)
        {
            _dgvDataList = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                EnableHeadersVisualStyles = false,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            _dgvDataList.ColumnHeadersDefaultCellStyle.BackColor = Color.Navy;
            _dgvDataList.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _dgvDataList.DefaultCellStyle.ForeColor = Color.Black;
            _dgvDataList.DefaultCellStyle.SelectionBackColor = Color.LightBlue;
            _dgvDataList.DefaultCellStyle.SelectionForeColor = Color.Black;
            _dgvDataList.DefaultCellStyle.Font = new Font("Arial", 11);

            _dgvDataList.Columns.Clear();
            _dgvDataList.Columns.Add("Task", "TASK NAME");
            _dgvDataList.Columns.Add("Port", "PORT");
            _dgvDataList.Columns.Add("Value", "VALUE");
            _dgvDataList.Columns.Add("Unit", "UNIT");
            _dgvDataList.Columns.Add("Age", "AGE(s)");
            _dgvDataList.Columns.Add("Status", "STATUS");
            _dgvDataList.Columns.Add("Limit", "LIMIT");
            _dgvDataList.Columns.Add("Alarm", "ALARM");

            // 4 dòng cố định theo hệ thống hiện giờ
            _dgvDataList.Rows.Clear();
            _dgvDataList.Rows.Add("GPS", "COM1", "", "", "", "WAIT", "", "Normal");
            _dgvDataList.Rows.Add("WIND", "COM2", "", "", "", "WAIT", "", "Normal");
            _dgvDataList.Rows.Add("R/P/H", "COM3", "", "", "", "WAIT", "", "Normal");
            _dgvDataList.Rows.Add("HEADING", "COM4", "", "", "", "WAIT", "", "Normal");

            int n = _dgvDataList.Rows.Count;
            _lastUpdate = new DateTime?[n];
            _unitText = new string[n];
            _limitText = new string[n];
            _alarmText = new string[n];

            // Unit basic
            SetMetaUnit("GPS", "kn");
            SetMetaUnit("WIND", "m/s,°");
            SetMetaUnit("R/P/H", "deg,deg,cm");
            SetMetaUnit("HEADING", "°");

            // Alarm default
            for (int i = 0; i < n; i++) _alarmText[i] = "Normal";

            // Limit theo config
            RefreshDataListLimits();

            parentTab.Controls.Add(_dgvDataList);

            StartHealthTimer();
        }

        private void StartHealthTimer()
        {
            if (_healthTimer != null) return;

            _healthTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _healthTimer.Tick += (s, e) =>
            {
                if (_dgvDataList == null) return;

                for (int i = 0; i < _dgvDataList.Rows.Count; i++)
                {
                    var row = _dgvDataList.Rows[i];
                    string taskName = row.Cells["Task"].Value?.ToString();

                    // Logic kiểm tra STALE (Mất tín hiệu)
                    bool isStale = true; // Mặc định là mất
                    double age = 999.0;

                    if (_lastUpdate != null && i < _lastUpdate.Length && _lastUpdate[i] != null)
                    {
                        age = (DateTime.Now - _lastUpdate[i]!.Value).TotalSeconds;
                        if (age <= StaleSeconds) isStale = false;
                    }

                    // --- CẬP NHẬT 4 THẺ TRẠNG THÁI TRÊN MENU ---
                    switch (taskName)
                    {
                        case "GPS": UpdateBadge(lblStatGPS, "GPS", isStale, age); break;
                        case "WIND": UpdateBadge(lblStatWind, "WIND", isStale, age); break;
                        case "R/P/H": UpdateBadge(lblStatMotion, "R/P/H", isStale, age); break; // Sửa tên hiển thị
                        case "HEADING": UpdateBadge(lblStatHeading, "HEADING", isStale, age); break; // Sửa tên hiển thị
                    }

                    // 1. Kiểm tra thời gian cập nhật cuối
                    if (_lastUpdate == null || i >= _lastUpdate.Length || _lastUpdate[i] == null) continue;

                    age = (DateTime.Now - _lastUpdate[i]!.Value).TotalSeconds;
                    isStale = age > StaleSeconds;

                    // 2. Cập nhật màu sắc cho các Label tương ứng trên giao diện Overview
                    // Logic: Nếu dữ liệu bị Stale (quá 2s) thì đổi chữ sang màu Xám
                    switch (taskName)
                    {
                        case "GPS":
                            UpdateLabelStatus(lblPosition, !isStale);
                            UpdateLabelStatus(lblSpeed, !isStale);
                            break;
                        case "WIND":
                            UpdateLabelStatus(lblWindSpeed, !isStale);
                            UpdateLabelStatus(lblWindRelated, !isStale);
                            break;
                        case "R/P/H":
                            UpdateLabelStatus(lblRoll, !isStale);
                            UpdateLabelStatus(lblPitch, !isStale);
                            UpdateLabelStatus(lblHeave, !isStale);
                            UpdateLabelStatus(lblHeaveCycle, !isStale);
                            break;
                        case "HEADING":
                            UpdateLabelStatus(lblHeading, !isStale);
                            break;
                    }

                    // 3. Cập nhật Status trên Grid (Giữ nguyên logic OK/STALE bạn đã viết)
                    row.Cells["Age"].Value = age.ToString("0.0");
                    if (isStale)
                    {
                        row.Cells["Status"].Value = "STALE";
                        row.Cells["Status"].Style.ForeColor = Color.OrangeRed;
                    }
                    else
                    {
                        row.Cells["Status"].Value = "OK";
                        row.Cells["Status"].Style.ForeColor = Color.Green;
                    }
                }
            };
            _healthTimer.Start();
        }
        private void UpdateBadge(Label lbl, string name, bool isStale, double age)
        {
            if (age > 900) // Chưa bao giờ nhận được dữ liệu (hoặc mới mở)
            {
                lbl.Text = $"{name}: WAIT";
                lbl.BackColor = Color.DimGray; // Màu xám tối
            }
            else if (isStale) // Đã từng có, nhưng giờ mất (> 2s)
            {
                lbl.Text = $"{name}: LOST";
                lbl.BackColor = Color.Red; // Báo động đỏ ngay
            }
            else // Tín hiệu tốt
            {
                lbl.Text = $"{name}: OK";
                lbl.BackColor = Color.ForestGreen; // Xanh tốt
            }
        }
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

        private void SetMetaUnit(string task, string unit)
        {
            int row = FindRow(task);
            if (row < 0) return;
            if (_unitText != null && row < _unitText.Length) _unitText[row] = unit;
        }

        private void SetMetaLimit(string task, string limit)
        {
            int row = FindRow(task);
            if (row < 0) return;
            if (_limitText != null && row < _limitText.Length) _limitText[row] = limit;
        }

        // Update VALUE + lastUpdate (không đụng Status/Limit/Alarm ở đây)
        private void UpdateGridByTask(string task, string value)
        {
            int row = FindRow(task);
            if (row < 0) return;

            _dgvDataList.Rows[row].Cells["Value"].Value = value;

            if (_lastUpdate != null && row < _lastUpdate.Length)
                _lastUpdate[row] = DateTime.Now;
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

        // Chỉ set Alarm text (Limit luôn lấy theo config)
        private void SetAlarmRow(string alarmId, string alarmStateText)
        {
            int row = AlarmIdToRowIndex(alarmId);
            if (row < 0) return;

            if (_alarmText != null && row < _alarmText.Length)
                _alarmText[row] = $"{alarmId}:{alarmStateText}";

            // refresh ngay
            if (_dgvDataList != null)
                _dgvDataList.Rows[row].Cells["Alarm"].Value = _alarmText[row];
        }

        private void RefreshDataListLimits()
        {
            if (_dgvDataList == null) return;

            // WIND
            SetMetaLimit("WIND", $"{SystemConfig.WindMax:0.0}");

            // R/P/H (gộp 3 limit)
            SetMetaLimit("R/P/H", $"R:{SystemConfig.RMax:0.0}  P:{SystemConfig.PMax:0.0}  H:{SystemConfig.HMax:0.0}");

            // render ngay
            _dgvDataList.Invalidate();
        }

        // =========================
        //  ALARM BADGE (COMPACT)
        // =========================
        private void EnsureAlarmBadge()
        {
            if (lblAlarmStatus != null) return;

            lblAlarmStatus = new Label
            {
                AutoSize = false,
                Width = 240,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Color.Black,
                ForeColor = Color.Lime,
                Text = "NORMAL",
                Margin = new Padding(20, 7, 5, 5)
            };

            _topMenu.Controls.Add(lblAlarmStatus);
        }

        private void RefreshAlarmBanner()
        {
            if (lblAlarmStatus == null) return;

            var all = new List<Alarm>(_alarmEngine.GetAll());

            var activeUnacked = all.Find(a => a.IsActive && !a.IsAcked);
            var activeAcked = all.Find(a => a.IsActive && a.IsAcked);

            if (activeUnacked != null)
            {
                lblAlarmStatus.Text = $"ALARM: {activeUnacked.Id}";
                lblAlarmStatus.BackColor = Color.DarkRed;
                lblAlarmStatus.ForeColor = Color.White;
                return;
            }

            if (activeAcked != null)
            {
                lblAlarmStatus.Text = $"ACK: {activeAcked.Id}";
                lblAlarmStatus.BackColor = Color.Orange;
                lblAlarmStatus.ForeColor = Color.Black;
                return;
            }

            lblAlarmStatus.Text = "NORMAL";
            lblAlarmStatus.BackColor = Color.Black;
            lblAlarmStatus.ForeColor = Color.Lime;
        }

        // =========================
        //  TREND BUTTONS NEAR CHART
        // =========================
        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();

            // Nút chọn Trend Motion
            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.White);
            btnTrend1.Click += (s, e) => SetTrendMode(TrendMode.Motion);

            // Nút chọn Trend Wind
            Button btnTrend2 = CreateMenuButton("TREND Wind", Color.White);
            btnTrend2.Click += (s, e) => SetTrendMode(TrendMode.Wind);

            // --- NÚT MỚI: CHUYỂN ĐỔI 2M / 20M ---
            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btnZoom.Click += (s, e) =>
            {
                if (_currentViewMinutes == 2.0)
                {
                    _currentViewMinutes = 20.0; // Chuyển sang 20 phút
                    btnZoom.Text = "VIEW: 20 Min";
                    btnZoom.BackColor = Color.LightSalmon; // Đổi màu để dễ nhận biết
                }
                else
                {
                    _currentViewMinutes = 2.0; // Quay về 2 phút
                    btnZoom.Text = "VIEW: 2 Min";
                    btnZoom.BackColor = Color.LightBlue;
                }

                // Cập nhật lại khung nhìn ngay lập tức
                _isLiveMode = true; // Kéo về Live luôn cho tiện
                ApplyViewportToNow();
            };

            panelTrendButtons.Controls.Add(btnTrend1);
            panelTrendButtons.Controls.Add(btnTrend2);
            // Thêm nút mới vào Panel
            panelTrendButtons.Controls.Add(btnZoom);
        }

        // =========================
        //  DATA FLOW
        // =========================
        private void OnComDataReceived(string portName, string rawData)
        {
            this.Invoke(new Action(() => ParseAndDisplay(portName, rawData)));
        }

        private void ParseAndDisplay(string portName, string data)
        {
            try
            {
                // 1. Lọc dữ liệu rác
                if (string.IsNullOrEmpty(data) || !data.StartsWith("$")) return;

                // 2. Cắt bỏ Checksum (*XX) để tránh lỗi parse số cuối
                int starIndex = data.IndexOf('*');
                if (starIndex > -1) data = data.Substring(0, starIndex);

                string[] p = data.Split(',');

                // Sử dụng CultureInfo.InvariantCulture để dấu chấm (.) là thập phân
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var style = System.Globalization.NumberStyles.Any;

                // ====================================================================
                // CASE A: CẢM BIẾN HIỆN ĐẠI ($CNTB) - Có sẵn Heave
                // Format: $CNTB,roll,pitch,heave
                // ====================================================================
                if (p[0] == "$CNTB" && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double r) &&
                        double.TryParse(p[2], style, culture, out double pi) &&
                        double.TryParse(p[3], style, culture, out double h))
                    {
                        // Áp dụng Offset người dùng nhập (nếu có)
                        DisplayMotionData(r + _rollOffset, pi + _pitchOffset, h);
                    }
                    return;
                }

                // ====================================================================
                // CASE B: CẢM BIẾN CŨ ($PRDID) - Không có Heave
                // Format: $PRDID,pitch,roll,heading (Theo code cũ của bạn: pitch=1, roll=2)
                // ====================================================================
                if (p[0] == "$PRDID" && p.Length >= 3)
                {
                    if (double.TryParse(p[1], style, culture, out double rawPitch) &&
                        double.TryParse(p[2], style, culture, out double rawRoll))
                    {
                        // 1. Áp dụng Offset
                        double finalRoll = rawRoll + _rollOffset;
                        double finalPitch = rawPitch + _pitchOffset;

                        // 2. Tự tính Heave (Công thức cánh tay đòn)
                        // Heave ≈ Cánh tay đòn * sin(Pitch)
                        // Lưu ý: Đổi độ sang radian trước khi dùng Math.Sin
                        double pitchRad = (finalPitch * Math.PI) / 180.0;

                        // Tính toán: Khoảng cách * Sin(Góc nghiêng)
                        // Ví dụ: Pitch = 2 độ, Arm = 10m (1000cm) => Heave ≈ 34.9cm
                        double calcHeave = _heaveArm * Math.Sin(pitchRad);

                        // Nếu bạn muốn hiển thị trị tuyệt đối giống code cũ thì thêm Math.Abs
                        // Nhưng chuẩn hàng hải nên để có âm dương (lên/xuống)
                        // calcHeave = Math.Abs(calcHeave); 

                        DisplayMotionData(finalRoll, finalPitch, calcHeave);
                    }
                    return;
                }

                // ====================================================================
                // CÁC SENSOR KHÁC (GIÓ, GPS, HEADING)
                // ====================================================================

                // 1) WIND ($MWV)
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) &&
                        double.TryParse(p[3], style, culture, out double wSpeed))
                    {
                        _windSpeedMs = wSpeed;
                        _windDirDeg = wDir;
                        lock (_windBuffer)
                        {
                            double nowX = DateTime.Now.ToOADate();
                            _windBuffer.Add(new TrendPoint { X = nowX, V1 = wSpeed, V2 = wDir, V3 = 0 });

                            double limitX = DateTime.Now.AddMinutes(-BufferMinutes - 1).ToOADate();
                            if (_windBuffer.Count > 0 && _windBuffer[0].X < limitX)
                                _windBuffer.RemoveAt(0);
                        }
                        lblWindSpeed.Text = $"{wSpeed:0.0} m/s";
                        lblWindRelated.Text = $"{wDir:0}°";
                        UpdateGridByTask("WIND", $"{wSpeed:0.0} / {wDir:0}");
                        _windTag.Update(wSpeed);
                        _alarmEngine.Evaluate();
                    }
                    return;
                }

                // 2) SPEED ($GPVTG)
                if (p[0] == "$GPVTG")
                {
                    double speedKnot = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(speedKnot))
                    {
                        _speedKnot = speedKnot;
                        lblSpeed.Text = $"{speedKnot:0.0} kn";
                    }
                    return;
                }

                // 3) HEADING ($HEHDT)
                if (p[0] == "$HEHDT" && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                    {
                        _headingDeg = heading;
                        lblHeading.Text = $"{heading:0.0}°";
                        UpdateGridByTask("HEADING", $"{heading:0.0}");
                    }
                    return;
                }

                // 4) GPS POSITION ($GPGGA)
                if (p[0] == "$GPGGA" && p.Length >= 6)
                {
                    string rawLat = p[2]; string latDir = p[3];
                    string rawLon = p[4]; string lonDir = p[5];
                    if (rawLat.Length > 4 && rawLon.Length > 5)
                    {
                        try
                        {
                            string fLat = $"{rawLat.Substring(0, 2)}°{rawLat.Substring(2)}'{latDir}";
                            string fLon = $"{rawLon.Substring(0, 3)}°{rawLon.Substring(3)}'{lonDir}";
                            lblPosition.Text = $"{fLat}\n{fLon}";
                            UpdateGridByTask("GPS", $"{fLat} {fLon}");
                        }
                        catch { }
                    }
                    else { lblPosition.Text = "NO FIX"; }
                    return;
                }
            }
            catch (Exception ex)
            {
                // Log lỗi nhẹ để debug
                System.Diagnostics.Debug.WriteLine($"Parse Error: {ex.Message}");
            }
        }

        // =========================
        //  SETTINGS
        // =========================
        private void BtnSettings_Click(object sender, EventArgs e)
        {
            LoginForm login = new LoginForm();
            if (login.ShowDialog() == DialogResult.OK)
            {
                using (ConfigForm cfg = new ConfigForm())
                {
                    cfg.ShowDialog();
                }

                // Load lại config mới + apply
                var newCfg = ConfigService.Load();
                SystemConfig.Apply(newCfg);

                // refresh limit hiển thị
                RefreshDataListLimits();
            }
        }

        // =========================
        //  ALARM EVENTS
        // =========================
        private void OnAlarmRaised(Alarm alarm)
        {
            switch (alarm.Id)
            {
                case "AL_WIND":
                    lblWindSpeed.ForeColor = Color.Red;
                    lblWindRelated.ForeColor = Color.Red;
                    break;
                case "AL_ROLL":
                    lblRoll.ForeColor = Color.Red; break;
                case "AL_PITCH":
                    lblPitch.ForeColor = Color.Red; break;
                case "AL_HEAVE":
                    lblHeave.ForeColor = Color.Red; break;
            }

            RefreshAlarmBanner();
            SetAlarmRow(alarm.Id, "Active");

            _logger?.LogAlarmEvent("RAISED", alarm.Id, alarm.State.ToString(),
                alarm.Tag.Value, alarm.HighLimitProvider());
        }

        private void OnAlarmCleared(Alarm alarm)
        {
            switch (alarm.Id)
            {
                case "AL_WIND":
                    lblWindSpeed.ForeColor = Color.Black;
                    lblWindRelated.ForeColor = Color.Black;
                    break;
                case "AL_ROLL":
                    lblRoll.ForeColor = Color.Black; break;
                case "AL_PITCH":
                    lblPitch.ForeColor = Color.Black; break;
                case "AL_HEAVE":
                    lblHeave.ForeColor = Color.Black; break;
            }

            RefreshAlarmBanner();
            SetAlarmRow(alarm.Id, "Normal");

            _logger?.LogAlarmEvent("CLEARED", alarm.Id, alarm.State.ToString(),
                alarm.Tag.Value, alarm.HighLimitProvider());
        }

        private void OnAlarmAcked(Alarm alarm)
        {
            switch (alarm.Id)
            {
                case "AL_WIND":
                    lblWindSpeed.ForeColor = Color.Orange;
                    lblWindRelated.ForeColor = Color.Orange;
                    break;
                case "AL_ROLL":
                    lblRoll.ForeColor = Color.Orange; break;
                case "AL_PITCH":
                    lblPitch.ForeColor = Color.Orange; break;
                case "AL_HEAVE":
                    lblHeave.ForeColor = Color.Orange; break;
            }

            RefreshAlarmBanner();
            SetAlarmRow(alarm.Id, "Ack");

            _logger?.LogAlarmEvent("ACKED", alarm.Id, alarm.State.ToString(),
                alarm.Tag.Value, alarm.HighLimitProvider());
        }

        // =========================
        //  ACK CLICK
        // =========================
        private void lblWindSpeed_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_WIND");
        private void lblRoll_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_ROLL");
        private void lblPitch_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_PITCH");
        private void lblHeave_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_HEAVE");

        // =========================
        //  UI HELPERS
        // =========================
        private Button CreateMenuButton(string text, Color bg)
        {
            return new Button
            {
                Text = text,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(120, 30),
                Margin = new Padding(5, 2, 5, 2)
            };
        }

        private void SetupHMILabel(Label lbl)
        {
            if (lbl == null) return;
            lbl.Font = new Font("Segoe UI", 42, FontStyle.Bold);
            lbl.ForeColor = Color.Black;
            lbl.BackColor = Color.White;
            lbl.TextAlign = ContentAlignment.MiddleCenter;
        }

        // =========================
        //  CHART
        // =========================
        private void SetupIndustrialChart()
        {
            if (panelChartHost == null) return;

            if (_trendChart != null)
            {
                panelChartHost.Controls.Remove(_trendChart);
                _trendChart.Dispose();
                _trendChart = null;
            }

            _trendChart = new Chart
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            var area = new ChartArea("Main")
            {
                BackColor = Color.White
            };

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

            var legend = new Legend
            {
                Docking = Docking.Top,
                Alignment = StringAlignment.Center,
                IsTextAutoFit = false,
                Font = new Font("Segoe UI", 14, FontStyle.Bold)
            };
            _trendChart.Legends.Add(legend);

            _trendChart.AntiAliasing = AntiAliasingStyles.None;
            _trendChart.TextAntiAliasingQuality = TextAntiAliasingQuality.Normal;

            //_trendChart.MouseDown += (s, e) => { _isLiveMode = true; };

            SetTrendMode(TrendMode.Motion);
            _trendChart.AxisViewChanged += (s, e) =>
            {
                // Nếu code đang tự cuộn thì bỏ qua, không xử lý
                if (_isProgrammaticScroll) return;

                // Nếu người dùng cuộn:
                var area = _trendChart.ChartAreas[0];
                double currentEnd = area.AxisX.ScaleView.Position + area.AxisX.ScaleView.Size;
                double maxPos = area.AxisX.Maximum;

                // Kiểm tra xem người dùng có đang xem ở sát mép phải (hiện tại) không?
                // Chênh lệch < 5 giây (trong đơn vị OADate) coi như là đang ở hiện tại
                double tolerance = 5.0 / (24 * 60 * 60);

                if (Math.Abs(maxPos - currentEnd) < tolerance)
                {
                    _isLiveMode = true;  // Kéo kịch phải -> Bật lại Live
                }
                else
                {
                    _isLiveMode = false; // Kéo về quá khứ -> Tạm dừng Live
                }
            };

            panelChartHost.Controls.Add(_trendChart);
            ApplyViewportToNow();

        }

        private void SetTrendMode(TrendMode mode)
        {
            _trendMode = mode;

            _trendChart?.Series.Clear();
            if (_trendChart == null) return;

            if (mode == TrendMode.Motion)
            {
                AddSeries("Roll");
                AddSeries("Pitch");
                AddSeries("Heave");
                RenderMotionFromBuffer();
            }
            else
            {
                AddSeries("WindSpeed");
                AddSeries("WindDir");
                RenderWindFromBuffer();
            }

            ApplyViewportToNow();
        }

        private void AddSeries(string name)
        {
            // Map hiển thị legend theo yêu cầu
            string legendText = name switch
            {
                "Roll" => "R",
                "Pitch" => "P",
                "Heave" => "H",
                "WindSpeed" => "Wind speed",
                "WindDir" => "Direction",
                _ => name
            };

            var s = new Series(name)
            {
                ChartType = SeriesChartType.FastLine,
                BorderWidth = 3, // đường + legend nhìn rõ hơn
                XValueType = ChartValueType.DateTime,
                IsVisibleInLegend = true,
                LegendText = legendText
            };
            _trendChart.Series.Add(s);
        }

        private void RenderMotionFromBuffer()
        {
            var sRoll = _trendChart.Series.FindByName("Roll");
            var sPitch = _trendChart.Series.FindByName("Pitch");
            var sHeave = _trendChart.Series.FindByName("Heave");
            if (sRoll == null || sPitch == null || sHeave == null) return;

            sRoll.Points.Clear();
            sPitch.Points.Clear();
            sHeave.Points.Clear();

            foreach (var pt in _motionBuffer)
            {
                sRoll.Points.AddXY(pt.X, pt.V1);
                sPitch.Points.AddXY(pt.X, pt.V2);
                sHeave.Points.AddXY(pt.X, pt.V3);
            }
        }

        private void RenderWindFromBuffer()
        {
            var sSpd = _trendChart.Series.FindByName("WindSpeed");
            var sDir = _trendChart.Series.FindByName("WindDir");
            if (sSpd == null || sDir == null) return;

            sSpd.Points.Clear();
            sDir.Points.Clear();

            foreach (var pt in _windBuffer)
            {
                sSpd.Points.AddXY(pt.X, pt.V1);
                sDir.Points.AddXY(pt.X, pt.V2);
            }
        }

        private void ApplyViewportToNow()
        {
            if (_trendChart == null) return;

            var area = _trendChart.ChartAreas[0];

            double nowX = DateTime.Now.ToOADate();
            double viewSize = _currentViewMinutes / (24.0 * 60.0);
            double bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            area.AxisX.Minimum = bufferStart;
            area.AxisX.Maximum = nowX;
            area.AxisX.ScaleView.Size = viewSize;

            double viewStart = nowX - viewSize;
            if (viewStart < bufferStart) viewStart = bufferStart;

            try { area.AxisX.ScaleView.Scroll(viewStart); } catch { }
        }

        private void EnsureTimeWindowAndLiveMode(double nowX)
        {
            if (_trendChart == null) return;

            var area = _trendChart.ChartAreas[0];
            double bufferStart = DateTime.FromOADate(nowX).AddMinutes(-BufferMinutes).ToOADate();
            double viewSize = _currentViewMinutes / (24.0 * 60.0);

            double viewMax = area.AxisX.ScaleView.ViewMaximum;
            if (!double.IsNaN(viewMax))
            {
                double tenSeconds = 10.0 / 86400.0;
                if (Math.Abs(viewMax - nowX) > tenSeconds)
                    _isLiveMode = false;
            }

            if (_isLiveMode)
            {
                area.AxisX.Minimum = bufferStart;
                area.AxisX.Maximum = nowX;

                area.AxisX.ScaleView.Size = viewSize;
                double viewStart = nowX - viewSize;
                if (viewStart < bufferStart) viewStart = bufferStart;

                try { area.AxisX.ScaleView.Scroll(viewStart); } catch { }
            }
        }

        private void UpdateChartMotion(double roll, double pitch, double heaveCm)
        {
            if (_trendChart == null) return;

            double nowX = DateTime.Now.ToOADate();
            double bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _motionBuffer.Add(new TrendPoint { X = nowX, V1 = roll, V2 = pitch, V3 = heaveCm });
            TrimBuffer(_motionBuffer, bufferStart);

            if (_trendMode == TrendMode.Motion)
            {
                _trendChart.Series["Roll"].Points.AddXY(nowX, roll);
                _trendChart.Series["Pitch"].Points.AddXY(nowX, pitch);
                _trendChart.Series["Heave"].Points.AddXY(nowX, heaveCm);

                TrimSeriesPoints(bufferStart);
                EnsureTimeWindowAndLiveMode(nowX);
                _trendChart.Invalidate();
            }
        }

        private void UpdateChartWind(double windSpeed, double windDirDeg)
        {
            if (_trendChart == null) return;

            double nowX = DateTime.Now.ToOADate();
            double bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _windBuffer.Add(new TrendPoint { X = nowX, V1 = windSpeed, V2 = windDirDeg });
            TrimBuffer(_windBuffer, bufferStart);

            if (_trendMode == TrendMode.Wind)
            {
                _trendChart.Series["WindSpeed"].Points.AddXY(nowX, windSpeed);
                _trendChart.Series["WindDir"].Points.AddXY(nowX, windDirDeg);

                TrimSeriesPoints(bufferStart);
                EnsureTimeWindowAndLiveMode(nowX);
                _trendChart.Invalidate();
            }
        }

        private void TrimBuffer(List<TrendPoint> buf, double bufferStartX)
        {
            while (buf.Count > 0 && buf[0].X < bufferStartX)
                buf.RemoveAt(0);
        }

        private void TrimSeriesPoints(double bufferStartX)
        {
            if (_trendChart == null) return;

            foreach (var s in _trendChart.Series)
            {
                while (s.Points.Count > 0 && s.Points[0].XValue < bufferStartX)
                    s.Points.RemoveAt(0);
            }
        }

        // Hàm này nằm gần cuối file Form1.cs
        private double TryGetNumberAfterToken(string[] parts, string token)
        {
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], token, StringComparison.OrdinalIgnoreCase))
                {
                    // --- ĐOẠN SỬA ---
                    // Bắt buộc dùng CultureInfo.InvariantCulture để hiểu dấu chấm (.) là thập phân
                    if (double.TryParse(parts[i + 1],
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double v))
                    {
                        return v;
                    }
                    // ----------------
                }
            }
            return double.NaN;
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
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(c, true, null);
        }
        private void DisplayMotionData(double roll, double pitch, double heave)
        {
            // Lưu giá trị vào biến toàn cục
            _rollDeg = roll;
            _pitchDeg = pitch;
            _heaveCm = heave;

            // Hiển thị lên Label
            lblRoll.Text = $"{roll:0.00}°";
            lblPitch.Text = $"{pitch:0.00}°";
            lblHeave.Text = $"{heave:0.0} cm";

            // --- LOGIC TÍNH CHU KỲ HEAVE (Zero Crossing) ---
            DateTime now = DateTime.Now;
            // Kiểm tra giao cắt điểm 0 (đi từ Âm sang Dương)
            bool posCross = (_lastHeaveValue < 0 && heave >= 0);

            if (posCross)
            {
                if (_lastZeroCrossTime == null) _lastZeroCrossTime = now;
                else
                {
                    double periodSec = (now - _lastZeroCrossTime.Value).TotalSeconds;
                    // Lọc nhiễu: Chỉ chấp nhận chu kỳ > 2 giây (sóng biển thường > 4s)
                    if (periodSec > 2.0)
                    {
                        lblHeaveCycle.Text = $"{periodSec:0.0} s";
                        _heavePeriodSec = periodSec;
                        _lastZeroCrossTime = now;
                    }
                }
            }
            _lastHeaveValue = heave;

            // Cập nhật Grid
            UpdateGridByTask("R/P/H", $"R:{roll:0.00}  P:{pitch:0.00}  H:{heave:0.0}");

            // Vẽ biểu đồ
            lock (_motionBuffer) // Khóa để an toàn
            {
                double nowX = DateTime.Now.ToOADate();
                _motionBuffer.Add(new TrendPoint { X = nowX, V1 = roll, V2 = pitch, V3 = heave });

                // Xóa bớt dữ liệu quá cũ trong buffer (quá 20 phút) để tiết kiệm RAM
                double limitX = DateTime.Now.AddMinutes(-BufferMinutes - 1).ToOADate();
                if (_motionBuffer.Count > 0 && _motionBuffer[0].X < limitX)
                    _motionBuffer.RemoveAt(0);
            }

            // Cập nhật Tag cho Alarm Engine
            _rollTag.Update(Math.Abs(roll));
            _pitchTag.Update(Math.Abs(pitch));
            _heaveTag.Update(Math.Abs(heave)); // Alarm thường tính biên độ tuyệt đối

            // Kiểm tra báo động
            _alarmEngine.Evaluate();
        }
        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }
        // Hàm này giúp load ảnh từ file bên ngoài mà không khóa file (để dễ xóa/sửa)
        private void LoadImageFromFile(PictureBox picBox, string fileName)
        {
            try
            {
                // Đường dẫn đến file ảnh: Thư mục chạy App\Images\Tên_File
                string path = Path.Combine(Application.StartupPath, "Images", fileName);

                if (File.Exists(path))
                {
                    // Dùng FileStream để đọc ảnh, giúp không bị lỗi "File đang được sử dụng"
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        picBox.Image = Image.FromStream(stream);
                    }
                }
                else
                {
                    // Nếu không thấy file thì thôi, hoặc có thể gán ảnh mặc định
                    // picBox.Image = null; 
                }
            }
            catch { /* Bỏ qua lỗi nếu ảnh hỏng */ }
        }
        private void UpdateLabelStatus(Label lbl, bool isAlive)
        {
            if (isAlive)
            {
                // Nếu có tín hiệu: Chữ xanh (hoặc đỏ nếu đang báo động)
                // Lưu ý: Chỉ đổi về xanh nếu nhãn đó không nằm trong danh sách đang báo động
                if (lbl.ForeColor == Color.Gray)
                    lbl.ForeColor = Color.Black;
            }
            else
            {
                // Nếu mất tín hiệu: Chữ màu xám
                lbl.ForeColor = Color.Gray;
            }
        }
        private void RenderChartFromBuffer()
        {
            if (_trendChart == null || _trendChart.IsDisposed) return;

            double nowX = DateTime.Now.ToOADate();
            double viewSize = _currentViewMinutes / (24.0 * 60.0);
            double bufferStart = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _trendChart.SuspendLayout();

            // 1. LUÔN CẬP NHẬT DỮ LIỆU (Để dù đang Pause, dữ liệu mới vẫn âm thầm nạp vào)
            if (_trendMode == TrendMode.Motion)
            {
                UpdateSeriesData("Roll", _motionBuffer, 1);
                UpdateSeriesData("Pitch", _motionBuffer, 2);
                UpdateSeriesData("Heave", _motionBuffer, 3);
            }
            else
            {
                UpdateSeriesData("WindSpeed", _windBuffer, 1);
                UpdateSeriesData("WindDir", _windBuffer, 2);
            }

            var area = _trendChart.ChartAreas[0];
            // Cập nhật giới hạn trục X (tổng buffer)
            area.AxisX.Minimum = bufferStart;
            area.AxisX.Maximum = nowX;

            // 2. CHỈ TỰ CUỘN KHI ĐANG LIVE MODE
            if (_isLiveMode)
            {
                double viewStart = nowX - viewSize;
                if (viewStart < bufferStart) viewStart = bufferStart;

                // Bật cờ hiệu: "Đây là code tự cuộn, không phải người dùng"
                _isProgrammaticScroll = true;
                area.AxisX.ScaleView.Position = viewStart;
                area.AxisX.ScaleView.Size = viewSize;
                _isProgrammaticScroll = false; // Tắt cờ hiệu
            }

            _trendChart.ResumeLayout();
            _trendChart.Invalidate();
        }

        // Hàm phụ trợ để đổ dữ liệu nhanh (Fast Data Binding)
        private void UpdateSeriesData(string seriesName, List<TrendPoint> buffer, int valueIndex)
        {
            var series = _trendChart.Series.FindByName(seriesName);
            if (series == null) return;

            // MẸO TỐI ƯU: Chỉ lấy những điểm mới chưa có trên chart thay vì Clear() rồi Add lại hết
            // Nhưng để đơn giản và mượt, ta dùng DataBind (rất nhanh với List)

            // Copy buffer ra mảng để tránh lỗi "Collection modified" khi đang vẽ
            TrendPoint[] dataSnapshot;
            lock (buffer) // Khóa buffer để an toàn đa luồng
            {
                dataSnapshot = buffer.ToArray();
            }

            // Xóa điểm cũ (chỉ giữ lại trong khoảng buffer)
            series.Points.SuspendUpdates();
            series.Points.Clear();

            foreach (var pt in dataSnapshot)
            {
                // Chỉ vẽ những điểm nằm trong khoảng thời gian cần hiển thị (cộng thêm chút lề)
                // Điều này giúp Chart không phải gánh hàng nghìn điểm thừa
                if (pt.X > _trendChart.ChartAreas[0].AxisX.Minimum)
                {
                    double val = (valueIndex == 1) ? pt.V1 : (valueIndex == 2 ? pt.V2 : pt.V3);
                    series.Points.AddXY(pt.X, val);
                }
            }
            series.Points.ResumeUpdates();
        }
        private void SetupStatusBadges()
        {
            // Hàm con tạo Label nhanh
            Label CreateBadge(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    // Tăng chiều rộng lên 140, chiều cao giữ nguyên hoặc tăng nhẹ
                    Size = new Size(140, 30),
                    TextAlign = ContentAlignment.MiddleCenter,
                    // Tăng cỡ chữ lên 11
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    BackColor = Color.DimGray,
                    ForeColor = Color.White,
                    Margin = new Padding(5, 7, 0, 5),
                    BorderStyle = BorderStyle.FixedSingle
                };
            }

            // Đổi tên cho khớp với Data List
            lblStatGPS = CreateBadge("GPS: WAIT");
            lblStatWind = CreateBadge("WIND: WAIT");
            lblStatMotion = CreateBadge("R/P/H: WAIT"); // Đổi MRU -> R/P/H
            lblStatHeading = CreateBadge("HEADING: WAIT"); // Đổi GYRO -> HEADING

            // Thêm vào thanh Menu
            _topMenu.Controls.Add(lblStatGPS);
            _topMenu.Controls.Add(lblStatWind);
            _topMenu.Controls.Add(lblStatMotion);
            _topMenu.Controls.Add(lblStatHeading);
        }
    }
}
