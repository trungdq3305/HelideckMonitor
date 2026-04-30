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
using HelideckVer2.UI.Theme;

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
        private Panel _tabHistory;
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

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
        }

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
                        t.SentenceType = saved.SentenceType;
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
            this.Text            = "SYSTEM CONFIGURATION";
            this.ClientSize      = new Size(620, 480);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterParent;
            this.BackColor       = Palette.AppBg;

            var pnlBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 60,
                BackColor = Palette.PanelBg,
                Padding   = new Padding(10)
            };

            chkSimulationMode = new CheckBox
            {
                Text      = "Enable Simulation Mode (Fake Data)",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Palette.BtnSettingsFg,
                BackColor = Color.Transparent,
                Dock      = DockStyle.Left,
                Padding   = new Padding(10, 8, 0, 0)
            };

            var btnSave = new Button
            {
                Text      = "SAVE / EXIT",
                Width     = 140,
                BackColor = Palette.BtnPrimaryBg,
                ForeColor = Palette.BtnPrimaryFg,
                FlatStyle = FlatStyle.Flat,
                Dock      = DockStyle.Right
            };
            btnSave.FlatAppearance.BorderColor = Palette.BorderCard;
            btnSave.FlatAppearance.BorderSize  = 1;
            btnSave.Click += BtnSave_Click;

            pnlBottom.Controls.Add(chkSimulationMode);
            pnlBottom.Controls.Add(btnSave);

            // ── Tab header bar (replaces TabControl to eliminate white border) ──
            var tabBar = new FlowLayoutPanel
            {
                Dock         = DockStyle.Top,
                Height       = 36,
                BackColor    = Palette.PanelBg,
                Padding      = new Padding(4, 4, 0, 0),
                WrapContents = false
            };

            // ── Content area: one panel visible at a time ─────────────────────
            var contentArea = new Panel { Dock = DockStyle.Fill, BackColor = Palette.AppBg };

            var pnlAlarm = new Panel { Dock = DockStyle.Fill, BackColor = Palette.CardBg, Visible = true  };
            var pnlCom   = new Panel { Dock = DockStyle.Fill, BackColor = Palette.CardBg, Visible = false };
            var pnlImage = new Panel { Dock = DockStyle.Fill, BackColor = Palette.CardBg, Visible = false };
            _tabHistory  = new Panel { Dock = DockStyle.Fill, BackColor = Palette.CardBg, Visible = false };

            SetupAlarmTab(pnlAlarm);
            SetupComTab(pnlCom);
            SetupImageTab(pnlImage);
            SetupAlarmHistoryTab(_tabHistory);

            contentArea.Controls.Add(_tabHistory);
            contentArea.Controls.Add(pnlImage);
            contentArea.Controls.Add(pnlCom);
            contentArea.Controls.Add(pnlAlarm);

            // ── Tab buttons ───────────────────────────────────────────────────
            string[] names = { "Alarm Limits", "COM Configuration", "Vessel Image", "Alarm History" };
            Panel[]  pages = { pnlAlarm, pnlCom, pnlImage, _tabHistory };
            var btns = new Button[4];

            void SelectTab(int idx)
            {
                for (int i = 0; i < 4; i++)
                {
                    pages[i].Visible          = (i == idx);
                    btns[i].BackColor         = (i == idx) ? Palette.BtnActiveBg  : Palette.PanelBg;
                    btns[i].ForeColor         = (i == idx) ? Palette.BtnActiveFg  : Palette.TextLabel;
                    btns[i].FlatAppearance.BorderColor = (i == idx) ? Palette.BtnActiveFg : Palette.BorderCard;
                }
                pages[idx].BringToFront();
                if (idx == 3 && !_historyLoaded)
                {
                    _historyLoaded = true;
                    _ = LoadAlarmHistoryAsync();
                }
            }

            for (int i = 0; i < 4; i++)
            {
                int captured = i;
                btns[i] = new Button
                {
                    Text      = names[i],
                    BackColor = (i == 0) ? Palette.BtnActiveBg : Palette.PanelBg,
                    ForeColor = (i == 0) ? Palette.BtnActiveFg : Palette.TextLabel,
                    FlatStyle = FlatStyle.Flat,
                    Height    = 28,
                    AutoSize  = true,
                    Padding   = new Padding(10, 0, 10, 0),
                    Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                    Margin    = new Padding(0, 0, 2, 0)
                };
                btns[i].FlatAppearance.BorderColor = (i == 0) ? Palette.BtnActiveFg : Palette.BorderCard;
                btns[i].FlatAppearance.BorderSize  = 1;
                btns[i].Click += (s, e) => SelectTab(captured);
                tabBar.Controls.Add(btns[i]);
            }

            this.Controls.Add(contentArea);
            this.Controls.Add(tabBar);
            this.Controls.Add(pnlBottom);
        }

        // ── TAB: ALARM LIMITS ─────────────────────────────────────────────────

        private void SetupAlarmTab(Panel tab)
        {
            int y = 20;
            numWind = AddAlarmRow(tab, "Wind Max (m/s):", ref y);
            numRoll = AddAlarmRow(tab, "Roll Max (°):", ref y);
            numPitch = AddAlarmRow(tab, "Pitch Max (°):", ref y);
            numHeave = AddAlarmRow(tab, "Heave Max (cm):", ref y);
        }

        private NumericUpDown AddAlarmRow(Panel p, string text, ref int top)
        {
            var lbl = new Label
            {
                Text      = text, Top = top, Left = 30, AutoSize = true,
                Font      = new Font("Segoe UI", 10),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent
            };
            var num = new NumericUpDown
            {
                Top           = top - 3, Left = 220, Width = 110,
                DecimalPlaces = 1, Maximum = 9999,
                Font          = new Font("Segoe UI", 10),
                BackColor     = Palette.InputBg,
                ForeColor     = Palette.TextValue
            };
            p.Controls.Add(lbl);
            p.Controls.Add(num);
            top += 55;
            return num;
        }

        // ── TAB: COM CONFIGURATION ────────────────────────────────────────────

        private void SetupComTab(Panel tab)
        {
            var lbl = new Label
            {
                Text      = "Edit COM port assignments. Baud rate is set in config.json only.",
                Dock      = DockStyle.Top,
                Height    = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent,
                Padding   = new Padding(4, 0, 0, 0)
            };
            tab.Controls.Add(lbl);

            dgvComConfig = new DataGridView
            {
                Dock                    = DockStyle.Fill,
                AutoSizeColumnsMode     = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows      = false,
                AllowUserToDeleteRows   = false,
                RowHeadersVisible       = false,
                BackgroundColor         = Palette.CardBg,
                GridColor               = Palette.BorderCard,
                BorderStyle             = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                SelectionMode           = DataGridViewSelectionMode.FullRowSelect,
                EditMode                = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            dgvComConfig.ColumnHeadersDefaultCellStyle.BackColor = Palette.PanelBg;
            dgvComConfig.ColumnHeadersDefaultCellStyle.ForeColor = Palette.TextLabel;
            dgvComConfig.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            dgvComConfig.DefaultCellStyle.BackColor          = Palette.CardBg;
            dgvComConfig.DefaultCellStyle.ForeColor          = Palette.TextValue;
            dgvComConfig.DefaultCellStyle.SelectionBackColor = Palette.SurfaceHi;
            dgvComConfig.DefaultCellStyle.SelectionForeColor = Palette.TextValue;

            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Task", HeaderText = "Task",                ReadOnly = true, FillWeight = 80 });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "COM Port",             FillWeight = 90 });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn { Name = "Baud", HeaderText = "Baud Rate (config.json)", ReadOnly = true, FillWeight = 100 });

            tab.Controls.Add(dgvComConfig);
            lbl.BringToFront();
        }

        // ── TAB: VESSEL IMAGE ─────────────────────────────────────────────────

        private void SetupImageTab(Panel tab)
        {
            // Top strip: label trái + nút Browse phải – chỉ 1 Dock.Top, không tranh chấp với Fill
            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Palette.PanelBg };

            var lbl = new Label
            {
                Text      = "Vessel / Helideck Image",
                Left      = 8, Top = 6, AutoSize = false,
                Width     = 340, Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent
            };

            var lblPath = new Label
            {
                Text      = "Saved to: Images/picture1.png",
                Left      = 8, Top = 26, AutoSize = false,
                Width     = 340, Height = 16,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 7.5f),
                ForeColor = Palette.TextDim,
                BackColor = Color.Transparent
            };

            var btnBrowse = new Button
            {
                Text      = "📂  Browse Image...",
                Left      = 354, Top = 7, Height = 36, Width = 210,
                BackColor = Palette.BtnPrimaryBg,
                ForeColor = Palette.BtnPrimaryFg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnBrowse.FlatAppearance.BorderColor = Palette.BorderCard;
            btnBrowse.FlatAppearance.BorderSize  = 1;
            btnBrowse.Click += BtnBrowseImage_Click;

            pnlTop.Controls.Add(lbl);
            pnlTop.Controls.Add(lblPath);
            pnlTop.Controls.Add(btnBrowse);

            // PictureBox fill – chỉ 1 control Dock.Fill trong tab, không bao giờ bị chèn
            _imgPreview = new PictureBox
            {
                Dock        = DockStyle.Fill,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = Palette.ChartBg,
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

        private void SetupAlarmHistoryTab(Panel tab)
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
            split.Panel1.BackColor = Palette.PanelBg;
            split.Panel2.BackColor = Palette.CardBg;

            _cboTimeRange = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width     = 110, Left = 8, Top = 9,
                Font      = new Font("Segoe UI", 9),
                BackColor = Palette.InputBg,
                ForeColor = Palette.TextValue
            };
            _cboTimeRange.Items.AddRange(new object[] { "Last 1 hour", "Last 6 hours", "Last 24 hours", "Last 7 days" });
            _cboTimeRange.SelectedIndex = 2;

            var btnRefresh = MakeToolbarButton("⟳  Refresh", Palette.BtnPrimaryBg, Palette.BtnPrimaryFg, 130, 9, 100);
            btnRefresh.Click += (s, e) => _ = LoadAlarmHistoryAsync();

            var btnOpen = MakeToolbarButton("📂  Open Log Folder  (Ctrl+L)", Palette.PanelBg, Palette.TextLabel, 242, 9, 200);
            btnOpen.Click += (s, e) => OpenLogFolder();

            split.Panel1.Controls.AddRange(new Control[] { _cboTimeRange, btnRefresh, btnOpen });

            _dgvAlarmHistory = new DataGridView
            {
                Dock                      = DockStyle.Fill,
                ReadOnly                  = true,
                AllowUserToAddRows        = false,
                AllowUserToDeleteRows     = false,
                RowHeadersVisible         = false,
                BackgroundColor           = Palette.CardBg,
                GridColor                 = Palette.BorderCard,
                BorderStyle               = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                SelectionMode             = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode       = DataGridViewAutoSizeColumnsMode.Fill,
                Font                      = new Font("Segoe UI", 9)
            };
            _dgvAlarmHistory.ColumnHeadersDefaultCellStyle.BackColor = Palette.PanelBg;
            _dgvAlarmHistory.ColumnHeadersDefaultCellStyle.ForeColor = Palette.TextLabel;
            _dgvAlarmHistory.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _dgvAlarmHistory.DefaultCellStyle.BackColor          = Palette.CardBg;
            _dgvAlarmHistory.DefaultCellStyle.ForeColor          = Palette.TextValue;
            _dgvAlarmHistory.DefaultCellStyle.SelectionBackColor = Palette.SurfaceHi;
            _dgvAlarmHistory.DefaultCellStyle.SelectionForeColor = Palette.TextValue;
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTime",  HeaderText = "Time",     FillWeight = 100 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColAlarm", HeaderText = "Alarm ID", FillWeight =  70 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColEvent", HeaderText = "Event",    FillWeight =  60 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColState", HeaderText = "State",    FillWeight =  70 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColValue", HeaderText = "Value",    FillWeight =  50 });
            _dgvAlarmHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLimit", HeaderText = "Limit",    FillWeight =  50 });

            split.Panel2.Controls.Add(_dgvAlarmHistory);
            tab.Controls.Add(split);
        }

        private Button MakeToolbarButton(string text, Color bg, Color fg, int left, int top, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Left      = left,
                Top       = top,
                Width     = width,
                Height    = 26,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderColor = Palette.BorderCard;
            btn.FlatAppearance.BorderSize  = 1;
            return btn;
        }

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
                    "RAISED"  => Palette.AlarmActiveBg,
                    "CLEARED" => Palette.AlarmNormalBg,
                    "ACKED"   => Palette.AlarmAckBg,
                    _         => Palette.CardBg
                };
                _dgvAlarmHistory.Rows[idx].DefaultCellStyle.ForeColor = r.Evt switch
                {
                    "RAISED"  => Palette.AlarmActiveFg,
                    "CLEARED" => Palette.AlarmNormalFg,
                    "ACKED"   => Palette.AlarmAckFg,
                    _         => Palette.TextValue
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
