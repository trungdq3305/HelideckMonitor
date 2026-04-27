using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HelideckVer2.Models;

namespace HelideckVer2
{
    public partial class ConfigForm : Form
    {
        // ── Alarm Limits tab ─────────────────────────────────────────────────
        private NumericUpDown numWind, numRoll, numPitch, numHeave;

        // ── COM Config tab ───────────────────────────────────────────────────
        private DataGridView dgvComConfig;
        private CheckBox chkSimulationMode;

        // ── Vessel Image tab ─────────────────────────────────────────────────
        private PictureBox _imgPreview;
        private string _pendingImagePath = null;

        // ── Alarm History tab ────────────────────────────────────────────────
        private DataGridView _dgvAlarmHistory;
        private ComboBox _cboTimeRange;
        private TabPage _tabHistory;
        private bool _historyLoaded;
        private CancellationTokenSource _historyCts;

        // ── Shared state ─────────────────────────────────────────────────────
        public static List<DeviceTask> Tasks = new List<DeviceTask>
        {
            new DeviceTask { TaskName = "GPS",     PortName = "COM1", BaudRate = 9600 },
            new DeviceTask { TaskName = "WIND",    PortName = "COM2", BaudRate = 4800 },
            new DeviceTask { TaskName = "R/P/H",   PortName = "COM3", BaudRate = 9600 },
            new DeviceTask { TaskName = "HEADING", PortName = "COM4", BaudRate = 4800 },
            new DeviceTask { TaskName = "AUX",     PortName = "COM5", BaudRate = 4800 }
        };

        public ConfigForm()
        {
            InitializeComponent();
            SetupUI();

            var cfg = HelideckVer2.Services.ConfigService.Load();
            HelideckVer2.Models.SystemConfig.Apply(cfg);

            if (cfg.Tasks != null)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = Tasks.Find(x => x.TaskName == saved.TaskName);
                    if (t != null)
                    {
                        t.PortName = saved.PortName;
                        if (saved.BaudRate > 0) t.BaudRate = saved.BaudRate;
                    }
                }
            }

            LoadData();
        }

        // ── KEYBOARD SHORTCUTS ────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.L))
            {
                OpenLogFolder();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── UI SETUP ──────────────────────────────────────────────────────────

        private void SetupUI()
        {
            this.Text = "SYSTEM CONFIGURATION";
            this.ClientSize = new Size(620, 480);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            Panel pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(10)
            };

            chkSimulationMode = new CheckBox
            {
                Text = "Enable Simulation Mode (Fake Data)",
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Blue,
                Dock = DockStyle.Left,
                Padding = new Padding(10, 8, 0, 0)
            };

            Button btnSave = new Button
            {
                Text = "SAVE / EXIT",
                Width = 140,
                BackColor = Color.LightGreen,
                Dock = DockStyle.Right
            };
            btnSave.Click += BtnSave_Click;

            pnlBottom.Controls.Add(chkSimulationMode);
            pnlBottom.Controls.Add(btnSave);

            TabControl tabConfig = new TabControl { Dock = DockStyle.Fill };

            var tabAlarm = new TabPage("Alarm Limits") { BackColor = Color.White };
            SetupAlarmTab(tabAlarm);
            tabConfig.TabPages.Add(tabAlarm);

            var tabCom = new TabPage("COM Configuration") { BackColor = Color.White };
            SetupComTab(tabCom);
            tabConfig.TabPages.Add(tabCom);

            var tabImage = new TabPage("Vessel Image") { BackColor = Color.White };
            SetupImageTab(tabImage);
            tabConfig.TabPages.Add(tabImage);

            _tabHistory = new TabPage("Alarm History") { BackColor = Color.White };
            SetupAlarmHistoryTab(_tabHistory);
            tabConfig.TabPages.Add(_tabHistory);

            tabConfig.Selected += (s, e) =>
            {
                if (e.TabPage == _tabHistory && !_historyLoaded)
                {
                    _historyLoaded = true;
                    _ = LoadAlarmHistoryAsync();
                }
            };

            this.Controls.Add(pnlBottom);
            this.Controls.Add(tabConfig);
            pnlBottom.BringToFront();
        }

        // ── TAB: ALARM LIMITS ─────────────────────────────────────────────────

        private void SetupAlarmTab(TabPage tab)
        {
            int y = 20;
            numWind = AddAlarmRow(tab, "Wind Max (m/s):", ref y);
            numRoll = AddAlarmRow(tab, "Roll Max (°):", ref y);
            numPitch = AddAlarmRow(tab, "Pitch Max (°):", ref y);
            numHeave = AddAlarmRow(tab, "Heave Max (cm):", ref y);
        }

        private NumericUpDown AddAlarmRow(TabPage p, string text, ref int top)
        {
            var lbl = new Label { Text = text, Top = top, Left = 30, AutoSize = true, Font = new Font("Segoe UI", 10) };
            var num = new NumericUpDown { Top = top - 3, Left = 220, Width = 110, DecimalPlaces = 1, Maximum = 9999, Font = new Font("Segoe UI", 10) };
            p.Controls.Add(lbl);
            p.Controls.Add(num);
            top += 55;
            return num;
        }

        // ── TAB: COM CONFIGURATION ────────────────────────────────────────────

        private void SetupComTab(TabPage tab)
        {
            var lbl = new Label
            {
                Text = "Edit COM port assignments. Baud rate is set in config.json only.",
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.DimGray,
                Padding = new Padding(4, 0, 0, 0)
            };
            tab.Controls.Add(lbl);

            dgvComConfig = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };

            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Task", HeaderText = "Task",                ReadOnly = true, FillWeight = 80 });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "COM Port",             FillWeight = 90 });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Baud", HeaderText = "Baud Rate (config.json)", ReadOnly = true, FillWeight = 100 });

            tab.Controls.Add(dgvComConfig);
            lbl.BringToFront();
        }

        // ── TAB: VESSEL IMAGE ─────────────────────────────────────────────────

        private void SetupImageTab(TabPage tab)
        {
            // Top strip: label trái + nút Browse phải – chỉ 1 Dock.Top, không tranh chấp với Fill
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.WhiteSmoke };

            var lbl = new Label
            {
                Text      = "Vessel / Helideck Image  (saved to Images/picture1.png)",
                Left      = 8, Top = 16, AutoSize = false,
                Width     = 340, Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.DimGray
            };

            var btnBrowse = new Button
            {
                Text      = "📂  Browse Image...",
                Left      = 354, Top = 7, Height = 36, Width = 210,
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += BtnBrowseImage_Click;

            pnlTop.Controls.Add(lbl);
            pnlTop.Controls.Add(btnBrowse);

            // PictureBox fill – chỉ 1 control Dock.Fill trong tab, không bao giờ bị chèn
            _imgPreview = new PictureBox
            {
                Dock        = DockStyle.Fill,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = Color.FromArgb(220, 230, 242),
                BorderStyle = BorderStyle.FixedSingle
            };
            LoadPreviewFromFile(Path.Combine(Application.StartupPath, "Images", "picture1.png"));

            tab.Controls.Add(pnlTop);       // Dock.Top  – index 0
            tab.Controls.Add(_imgPreview);  // Dock.Fill – index 1
        }

        private void LoadPreviewFromFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                // Read all bytes so the file is not locked after this call
                byte[] bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                _imgPreview.Image?.Dispose();
                _imgPreview.Image = bmp;
            }
            catch { }
        }

        private void BtnBrowseImage_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select Vessel Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            _pendingImagePath = dlg.FileName;
            LoadPreviewFromFile(_pendingImagePath);
        }

        // ── TAB: ALARM HISTORY ────────────────────────────────────────────────

        private void SetupAlarmHistoryTab(TabPage tab)
        {
            // SplitContainer chia tab thành 2 phần cứng: toolbar trên, bảng dưới
            // Không phụ thuộc vào thứ tự Dock — không bao giờ bị lấn vào nhau
            var split = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Horizontal,
                SplitterDistance = 44,
                FixedPanel       = FixedPanel.Panel1,
                IsSplitterFixed  = true,
                Panel1MinSize    = 44,
                Panel2MinSize    = 20,
                SplitterWidth    = 1
            };
            split.Panel1.BackColor = Color.WhiteSmoke;
            split.Panel2.BackColor = Color.White;

            _cboTimeRange = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 110, Left = 8, Top = 9,
                Font  = new Font("Segoe UI", 9)
            };
            _cboTimeRange.Items.AddRange(new object[] { "Last 1 hour", "Last 6 hours", "Last 24 hours", "Last 7 days" });
            _cboTimeRange.SelectedIndex = 2;

            var btnRefresh = MakeToolbarButton("⟳  Refresh", Color.SteelBlue, 130, 9, 100);
            btnRefresh.Click += (s, e) => _ = LoadAlarmHistoryAsync();

            var btnOpen = MakeToolbarButton("📂  Open Log Folder  (Ctrl+L)", Color.DimGray, 242, 9, 200);
            btnOpen.Click += (s, e) => OpenLogFolder();

            split.Panel1.Controls.AddRange(new Control[] { _cboTimeRange, btnRefresh, btnOpen });

            _dgvAlarmHistory = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                ReadOnly              = true,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible     = false,
                BackgroundColor       = Color.White,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
                Font                  = new Font("Segoe UI", 9)
            };
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTime",  HeaderText = "Time",     FillWeight = 100 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColAlarm", HeaderText = "Alarm ID", FillWeight =  70 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColEvent", HeaderText = "Event",    FillWeight =  60 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColState", HeaderText = "State",    FillWeight =  70 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColValue", HeaderText = "Value",    FillWeight =  50 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLimit", HeaderText = "Limit",    FillWeight =  50 });

            split.Panel2.Controls.Add(_dgvAlarmHistory);
            tab.Controls.Add(split);
        }

        private Button MakeToolbarButton(string text, Color bg, int left, int top, int width) => new Button
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = 26,
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };

        private async Task LoadAlarmHistoryAsync()
        {
            _historyCts?.Cancel();
            _historyCts = new CancellationTokenSource();
            var token = _historyCts.Token;

            double hoursBack = _cboTimeRange.SelectedIndex switch
            {
                0 => 1.0,
                1 => 6.0,
                2 => 24.0,
                3 => 168.0,
                _ => 24.0
            };
            DateTime cutoff = DateTime.Now.AddHours(-hoursBack);
            string baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            _dgvAlarmHistory.Rows.Clear();
            _dgvAlarmHistory.Rows.Add("Loading…", "", "", "", "", "");

            List<(DateTime Time, string Id, string Evt, string State, string Val, string Lim)> rows;
            try
            {
                rows = await Task.Run(() => ReadAlarmRows(cutoff, baseFolder, token), token);
            }
            catch (OperationCanceledException) { return; }

            if (token.IsCancellationRequested || IsDisposed) return;

            _dgvAlarmHistory.SuspendLayout();
            _dgvAlarmHistory.Rows.Clear();
            foreach (var r in rows)
            {
                int idx = _dgvAlarmHistory.Rows.Add(
                    r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.Id, r.Evt, r.State, r.Val, r.Lim);

                _dgvAlarmHistory.Rows[idx].DefaultCellStyle.BackColor = r.Evt switch
                {
                    "RAISED"  => Color.FromArgb(255, 215, 215),
                    "CLEARED" => Color.FromArgb(210, 245, 215),
                    "ACKED"   => Color.FromArgb(255, 240, 195),
                    _         => Color.White
                };
            }
            _dgvAlarmHistory.ResumeLayout();
        }

        private static List<(DateTime Time, string Id, string Evt, string State, string Val, string Lim)>
            ReadAlarmRows(DateTime cutoff, string baseFolder, CancellationToken token)
        {
            var rows = new List<(DateTime Time, string Id, string Evt, string State, string Val, string Lim)>();
            if (!Directory.Exists(baseFolder)) return rows;

            foreach (var dir in Directory.GetDirectories(baseFolder))
            {
                token.ThrowIfCancellationRequested();

                string dirName = Path.GetFileName(dir);
                if (!DateTime.TryParseExact(dirName, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime dirDate)) continue;
                if (dirDate.Date < cutoff.Date) continue;

                foreach (var file in Directory.GetFiles(dir, "*.csv"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (var line in File.ReadLines(file))
                        {
                            var p = line.Split(',');
                            if (p.Length < 15 || p[1] != "ALARM") continue;
                            if (!TimeSpan.TryParse(p[0], out TimeSpan ts)) continue;

                            DateTime fullTime = dirDate.Date.Add(ts);
                            if (fullTime < cutoff) continue;

                            string evtName = (p.Length > 14 ? p[14] : "").Split('|')[0];
                            rows.Add((fullTime, p[10], evtName, p[11], p[12], p[13]));
                        }
                    }
                    catch { }
                }
            }

            rows.Sort((a, b) => b.Time.CompareTo(a.Time));
            return rows;
        }

        private static void OpenLogFolder()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        // ── LOAD / SAVE ───────────────────────────────────────────────────────

        private void LoadData()
        {
            numWind.Value = (decimal)SystemConfig.WindMax;
            numRoll.Value = (decimal)SystemConfig.RMax;
            numPitch.Value = (decimal)SystemConfig.PMax;
            numHeave.Value = (decimal)SystemConfig.HMax;
            chkSimulationMode.Checked = SystemConfig.IsSimulationMode;

            dgvComConfig.Rows.Clear();
            foreach (var t in Tasks)
                dgvComConfig.Rows.Add(t.TaskName, t.PortName, t.BaudRate);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SystemConfig.WindMax = (double)numWind.Value;
            SystemConfig.RMax = (double)numRoll.Value;
            SystemConfig.PMax = (double)numPitch.Value;
            SystemConfig.HMax = (double)numHeave.Value;
            SystemConfig.IsSimulationMode = chkSimulationMode.Checked;

            foreach (DataGridViewRow row in dgvComConfig.Rows)
            {
                string taskName = row.Cells["Task"].Value?.ToString();
                var task = Tasks.Find(t => t.TaskName == taskName);
                if (task == null) continue;
                task.PortName = row.Cells["Port"].Value?.ToString() ?? task.PortName;
            }

            // Save selected vessel image → Images/picture1.png
            if (_pendingImagePath != null)
            {
                try
                {
                    string folder = Path.Combine(Application.StartupPath, "Images");
                    Directory.CreateDirectory(folder);
                    string dest = Path.Combine(folder, "picture1.png");
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Copy(_pendingImagePath, dest);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Image save failed: {ex.Message}", "Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            var saveCfg = HelideckVer2.Models.SystemConfig.Export();
            saveCfg.Tasks = Tasks;
            HelideckVer2.Services.ConfigService.Save(saveCfg);

            MessageBox.Show(
                "Configuration saved!\n\nPlease restart the application to apply COM port / Baud rate changes.",
                "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
