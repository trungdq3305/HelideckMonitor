using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using HelideckVer2.Services;
using HelideckVer2.Models;

namespace HelideckVer2
{
    public partial class Form1 : Form
    {
        // =========================================================
        // DATA KẾT NỐI VỚI CỬA SỔ DATALIST (DÙNG CHUNG)
        // =========================================================
        public static string[] GridValues = new string[4] { "", "", "", "" };
        public static double[] GridAges = new double[4] { 999, 999, 999, 999 };
        public static bool[] GridStales = new bool[4] { true, true, true, true };
        public static string[] GridLimits = new string[4] { "", "", "", "" };
        public static string[] GridAlarms = new string[4] { "Normal", "Normal", "Normal", "Normal" };
        private DateTime?[] _lastUpdate = new DateTime?[4];

        // =========================================================
        private bool _isSimulationMode = true;
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
            public double X;   // OADate
            public double V1;  // series1
            public double V2;  // series2
            public double V3;  // series3 (motion)
        }

        private double _speedKnot, _headingDeg, _rollDeg, _pitchDeg, _heaveCm;
        private double _heavePeriodSec;
        private double _windSpeedMs, _windDirDeg;

        private System.Windows.Forms.Timer _healthTimer;
        private const double StaleSeconds = 2.0;

        private System.Windows.Forms.Timer _snapshotTimer;

        private double _rollOffset = 0.0;
        private double _pitchOffset = 0.0;
        private double _heaveArm = 10.0;

        private System.Windows.Forms.Timer _chartUpdateTimer;
        private bool _isProgrammaticScroll = false;

        private Label lblStatGPS, lblStatWind, lblStatMotion, lblStatHeading;

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
                    if (t != null) t.BaudRate = saved.BaudRate;
                }
            }

            SetupHMILabel(lblSpeed);
            SetupHMILabel(lblHeading);
            SetupHMILabel(lblRoll);
            SetupHMILabel(lblPitch);
            SetupHMILabel(lblHeave);
            SetupHMILabel(lblHeaveCycle);
            SetupHMILabel(lblWindSpeed);

            InitializeTasks();
            SetupMainLayout(); // <--- ĐÃ ĐƯỢC CHỈNH SỬA THÀNH BOTTOM PANEL
            EnsureAlarmBadge();
            SetupStatusBadges();
            SetupTrendButtonsNearChart();
            SetupIndustrialChart();

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
            RefreshDataListLimits();
            StartHealthTimer();

            _chartUpdateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _chartUpdateTimer.Tick += (s, e) => RenderChartFromBuffer();
            _chartUpdateTimer.Start();
        }

        private void StartSimulation()
        {
            _simTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            Random rnd = new Random();

            _simTimer.Tick += (s, e) =>
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                _simTimeCounter += 1.0;

                string lat = "1045." + rnd.Next(100, 999);
                string lon = "10640." + rnd.Next(100, 999);
                double speed = 5.0 + rnd.NextDouble() * 2;
                OnComDataReceived("COM1", $"$GPGGA,083226.00,{lat},N,{lon},E,1,08,1.0,10.0,M,0.0,M,,*XX");
                OnComDataReceived("COM1", $"$GPVTG,360.0,T,348.7,M,0.0,N,{speed.ToString("0.0", ci)},K*XX");

                double windSpeed = 15.0 + (rnd.NextDouble() * 10 - 5);
                double windDir = 120.0 + (rnd.NextDouble() * 20 - 10);
                OnComDataReceived("COM2", $"$WIMWV,{windDir.ToString("0.0", ci)},R,{windSpeed.ToString("0.0", ci)},M,A*XX");

                double roll = 2.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 8.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double pitch = 1.5 * Math.Cos(_simTimeCounter * 2 * Math.PI / 6.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double heave = 30.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 5.0) + (rnd.NextDouble() * 2 - 1);
                OnComDataReceived("COM3", $"$CNTB,{roll.ToString("0.00", ci)},{pitch.ToString("0.00", ci)},{heave.ToString("0.0", ci)}");

                double heading = 180.0 + (rnd.NextDouble() * 4 - 2);
                OnComDataReceived("COM4", $"$HEHDT,{heading.ToString("0.0", ci)},T*XX");
            };
            _simTimer.Start();
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
            tableLayoutPanel2.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel2.BackColor = Color.WhiteSmoke;

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

        // =========================================================
        // SỬA ĐỔI LAYOUT: DÙNG MENU BOTTOM GIỐNG ANCHOR
        // =========================================================
        private void SetupMainLayout()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.WhiteSmoke
            };
            this.Controls.Add(root);

            _topMenu = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.LightGray,
                Padding = new Padding(6, 6, 6, 6),
                WrapContents = false
            };

            Panel pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.WhiteSmoke };

            Button btnSetting = new Button { Text = "⚙ SETTINGS", Width = 150, Height = 40, Location = new Point(20, 10), BackColor = Color.Orange, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnSetting.Click += BtnSettings_Click;

            Button btnDataList = new Button { Text = "📋 VIEW DATA LIST", Width = 250, Height = 40, Location = new Point(190, 10), BackColor = Color.DodgerBlue, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            btnDataList.Click += (s, e) => { new DataListForm().ShowDialog(this); }; // Mở Pop-up

            pnlBottom.Controls.Add(btnSetting);
            pnlBottom.Controls.Add(btnDataList);

            // Gắn vào root (thứ tự quan trọng để không bị đè)
            root.Controls.Add(pnlBottom);
            root.Controls.Add(_topMenu);

            this.tableLayoutPanel1.Parent = root;
            this.tableLayoutPanel1.Dock = DockStyle.Fill;
            this.tableLayoutPanel1.Visible = true;
            this.tableLayoutPanel1.BringToFront();
            root.Controls.Add(this.tableLayoutPanel1);
        }

        private int FindRowIndex(string taskName)
        {
            return taskName switch
            {
                "GPS" => 0,
                "WIND" => 1,
                "R/P/H" => 2,
                "HEADING" => 3,
                _ => -1
            };
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
                        case "GPS": UpdateBadge(lblStatGPS, "GPS", isStale, age); break;
                        case "WIND": UpdateBadge(lblStatWind, "WIND", isStale, age); break;
                        case "R/P/H": UpdateBadge(lblStatMotion, "R/P/H", isStale, age); break;
                        case "HEADING": UpdateBadge(lblStatHeading, "HEADING", isStale, age); break;
                    }

                    if (_lastUpdate[i] == null) continue;

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
                }
            };
            _healthTimer.Start();
        }

        private void UpdateBadge(Label lbl, string name, bool isStale, double age)
        {
            if (age > 900)
            {
                lbl.Text = $"{name}: WAIT";
                lbl.BackColor = Color.DimGray;
            }
            else if (isStale)
            {
                lbl.Text = $"{name}: LOST";
                lbl.BackColor = Color.Red;
            }
            else
            {
                lbl.Text = $"{name}: OK";
                lbl.BackColor = Color.ForestGreen;
            }
        }

        private void UpdateGridByTask(string task, string value)
        {
            int row = FindRowIndex(task);
            if (row < 0) return;
            GridValues[row] = value;
            _lastUpdate[row] = DateTime.Now;
        }

        private void SetAlarmRow(string alarmId, string alarmStateText)
        {
            int row = alarmId switch
            {
                "AL_WIND" => 1,
                "AL_ROLL" => 2,
                "AL_PITCH" => 2,
                "AL_HEAVE" => 2,
                _ => -1
            };
            if (row < 0) return;
            GridAlarms[row] = $"{alarmId}:{alarmStateText}";
        }

        private void RefreshDataListLimits()
        {
            GridLimits[1] = $"{SystemConfig.WindMax:0.0}";
            GridLimits[2] = $"R:{SystemConfig.RMax:0.0} P:{SystemConfig.PMax:0.0} H:{SystemConfig.HMax:0.0}";
            GridLimits[0] = "N/A";
            GridLimits[3] = "N/A";
        }

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

        private void SetupTrendButtonsNearChart()
        {
            panelTrendButtons.Controls.Clear();

            Button btnTrend1 = CreateMenuButton("TREND R/P/H", Color.White);
            btnTrend1.Click += (s, e) => SetTrendMode(TrendMode.Motion);

            Button btnTrend2 = CreateMenuButton("TREND Wind", Color.White);
            btnTrend2.Click += (s, e) => SetTrendMode(TrendMode.Wind);

            Button btnZoom = CreateMenuButton("VIEW: 2 Min", Color.LightBlue);
            btnZoom.Click += (s, e) =>
            {
                if (_currentViewMinutes == 2.0)
                {
                    _currentViewMinutes = 20.0;
                    btnZoom.Text = "VIEW: 20 Min";
                    btnZoom.BackColor = Color.LightSalmon;
                }
                else
                {
                    _currentViewMinutes = 2.0;
                    btnZoom.Text = "VIEW: 2 Min";
                    btnZoom.BackColor = Color.LightBlue;
                }

                _isLiveMode = true;
                ApplyViewportToNow();
            };

            panelTrendButtons.Controls.Add(btnTrend1);
            panelTrendButtons.Controls.Add(btnTrend2);
            panelTrendButtons.Controls.Add(btnZoom);
        }

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
                if (starIndex > -1) data = data.Substring(0, starIndex);

                string[] p = data.Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var style = System.Globalization.NumberStyles.Any;

                if (p[0] == "$CNTB" && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double r) &&
                        double.TryParse(p[2], style, culture, out double pi) &&
                        double.TryParse(p[3], style, culture, out double h))
                    {
                        DisplayMotionData(r + _rollOffset, pi + _pitchOffset, h);
                    }
                    return;
                }

                if (p[0] == "$PRDID" && p.Length >= 3)
                {
                    if (double.TryParse(p[1], style, culture, out double rawPitch) &&
                        double.TryParse(p[2], style, culture, out double rawRoll))
                    {
                        double finalRoll = rawRoll + _rollOffset;
                        double finalPitch = rawPitch + _pitchOffset;

                        double pitchRad = (finalPitch * Math.PI) / 180.0;
                        double calcHeave = _heaveArm * Math.Sin(pitchRad);

                        DisplayMotionData(finalRoll, finalPitch, calcHeave);
                    }
                    return;
                }

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
                System.Diagnostics.Debug.WriteLine($"Parse Error: {ex.Message}");
            }
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            LoginForm login = new LoginForm();
            if (login.ShowDialog() == DialogResult.OK)
            {
                using (ConfigForm cfg = new ConfigForm())
                {
                    cfg.ShowDialog();
                }

                var newCfg = ConfigService.Load();
                SystemConfig.Apply(newCfg);

                RefreshDataListLimits();
            }
        }

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

        private void lblWindSpeed_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_WIND");
        private void lblRoll_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_ROLL");
        private void lblPitch_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_PITCH");
        private void lblHeave_Click(object sender, EventArgs e) => _alarmEngine.Ack("AL_HEAVE");

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

            SetTrendMode(TrendMode.Motion);
            _trendChart.AxisViewChanged += (s, e) =>
            {
                if (_isProgrammaticScroll) return;

                var area = _trendChart.ChartAreas[0];
                double currentEnd = area.AxisX.ScaleView.Position + area.AxisX.ScaleView.Size;
                double maxPos = area.AxisX.Maximum;

                double tolerance = 5.0 / (24 * 60 * 60);

                if (Math.Abs(maxPos - currentEnd) < tolerance)
                {
                    _isLiveMode = true;
                }
                else
                {
                    _isLiveMode = false;
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
            }
            else
            {
                AddSeries("WindSpeed");
                AddSeries("WindDir");
            }

            ApplyViewportToNow();
        }

        private void AddSeries(string name)
        {
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
                BorderWidth = 3,
                XValueType = ChartValueType.DateTime,
                IsVisibleInLegend = true,
                LegendText = legendText
            };
            _trendChart.Series.Add(s);
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

        private double TryGetNumberAfterToken(string[] parts, string token)
        {
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], token, StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(parts[i + 1],
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double v))
                    {
                        return v;
                    }
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
            _rollDeg = roll;
            _pitchDeg = pitch;
            _heaveCm = heave;

            lblRoll.Text = $"{roll:0.00}°";
            lblPitch.Text = $"{pitch:0.00}°";
            lblHeave.Text = $"{heave:0.0} cm";

            DateTime now = DateTime.Now;
            bool posCross = (_lastHeaveValue < 0 && heave >= 0);

            if (posCross)
            {
                if (_lastZeroCrossTime == null) _lastZeroCrossTime = now;
                else
                {
                    double periodSec = (now - _lastZeroCrossTime.Value).TotalSeconds;
                    if (periodSec > 2.0)
                    {
                        lblHeaveCycle.Text = $"{periodSec:0.0} s";
                        _heavePeriodSec = periodSec;
                        _lastZeroCrossTime = now;
                    }
                }
            }
            _lastHeaveValue = heave;

            UpdateGridByTask("R/P/H", $"R:{roll:0.00}  P:{pitch:0.00}  H:{heave:0.0}");

            lock (_motionBuffer)
            {
                double nowX = DateTime.Now.ToOADate();
                _motionBuffer.Add(new TrendPoint { X = nowX, V1 = roll, V2 = pitch, V3 = heave });

                double limitX = DateTime.Now.AddMinutes(-BufferMinutes - 1).ToOADate();
                if (_motionBuffer.Count > 0 && _motionBuffer[0].X < limitX)
                    _motionBuffer.RemoveAt(0);
            }

            _rollTag.Update(Math.Abs(roll));
            _pitchTag.Update(Math.Abs(pitch));
            _heaveTag.Update(Math.Abs(heave));

            _alarmEngine.Evaluate();
        }

        private void label9_Click(object sender, EventArgs e) { }
        private void lblHeading_Click(object sender, EventArgs e) { }
        private void lblHeaveCycle_Click(object sender, EventArgs e) { }
        private void tableLayoutPanel3_Paint(object sender, PaintEventArgs e) { }

        private void LoadImageFromFile(PictureBox picBox, string fileName)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "Images", fileName);

                if (File.Exists(path))
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        picBox.Image = Image.FromStream(stream);
                    }
                }
            }
            catch { }
        }

        private void UpdateLabelStatus(Label lbl, bool isAlive)
        {
            if (isAlive)
            {
                if (lbl.ForeColor == Color.Gray)
                    lbl.ForeColor = Color.Black;
            }
            else
            {
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
            area.AxisX.Minimum = bufferStart;
            area.AxisX.Maximum = nowX;

            if (_isLiveMode)
            {
                double viewStart = nowX - viewSize;
                if (viewStart < bufferStart) viewStart = bufferStart;

                _isProgrammaticScroll = true;
                area.AxisX.ScaleView.Position = viewStart;
                area.AxisX.ScaleView.Size = viewSize;
                _isProgrammaticScroll = false;
            }

            _trendChart.ResumeLayout();
            _trendChart.Invalidate();
        }

        private void UpdateSeriesData(string seriesName, List<TrendPoint> buffer, int valueIndex)
        {
            var series = _trendChart.Series.FindByName(seriesName);
            if (series == null) return;

            TrendPoint[] dataSnapshot;
            lock (buffer)
            {
                dataSnapshot = buffer.ToArray();
            }

            series.Points.SuspendUpdates();
            series.Points.Clear();

            foreach (var pt in dataSnapshot)
            {
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
            Label CreateBadge(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Size = new Size(140, 30),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    BackColor = Color.DimGray,
                    ForeColor = Color.White,
                    Margin = new Padding(5, 7, 0, 5),
                    BorderStyle = BorderStyle.FixedSingle
                };
            }

            lblStatGPS = CreateBadge("GPS: WAIT");
            lblStatWind = CreateBadge("WIND: WAIT");
            lblStatMotion = CreateBadge("R/P/H: WAIT");
            lblStatHeading = CreateBadge("HEADING: WAIT");

            _topMenu.Controls.Add(lblStatGPS);
            _topMenu.Controls.Add(lblStatWind);
            _topMenu.Controls.Add(lblStatMotion);
            _topMenu.Controls.Add(lblStatHeading);
        }
    }
}