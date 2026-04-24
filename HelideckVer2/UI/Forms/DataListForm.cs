using System;
using System.Drawing;
using System.Windows.Forms;

namespace HelideckVer2
{
    /// <summary>
    /// Bảng quét thô – hiển thị live raw NMEA data từng cổng COM.
    /// Cột: STT | COM | TASK | BAUD | EXPECTED HEADER | RAW DATA (last) | AGE | STATUS
    /// </summary>
    public partial class DataListForm : Form
    {
        private DataGridView _dgvRaw;
        private System.Windows.Forms.Timer _updateTimer;

        // Cấu hình tĩnh từ ConfigForm
        private static readonly string[] _expectedHeaders = {
            "$GPGGA / $GPVTG",
            "$WIMWV",
            "$CNTB",
            "$HEHDT",
            "-"
        };
        private static readonly string[] _dataDesc = {
            "GPS Position & Speed Over Ground",
            "Wind Speed and Angle",
            "Roll / Pitch / Heave",
            "Heading, True",
            "Auxiliary data"
        };

        public DataListForm()
        {
            InitializeComponent();
            BuildUI();

            _updateTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _updateTimer.Tick += RefreshData;
            _updateTimer.Start();

            RefreshData(null, null); // Hiện ngay lập tức
        }

        private void BuildUI()
        {
            this.Text = "RAW COM SCAN";
            this.Size = new Size(1300, 380);
            this.MinimumSize = new Size(960, 310);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 30, 48);

            // ── TITLE BAR ────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(22, 38, 62)
            };

            var lblTitle = new Label
            {
                Text = "RAW DATA SCANNER",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 0, 0, 10)
            };

            var lblSubtitle = new Label
            {
                Text = "Live NMEA input monitor  ·  refreshes every 500 ms  ·  Age > 2 s = LOST",
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(120, 160, 200),
                BackColor = Color.Transparent,
                Padding = new Padding(14, 0, 0, 0)
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);

            // ── LEGEND BAR (bottom) ───────────────────────────────────────────────
            var pnlLegend = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(14, 20, 32)
            };

            Label MakeDot(string text, Color dot, Color fg) => new Label
            {
                Text = text,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = fg,
                BackColor = Color.Transparent,
                Margin = new Padding(16, 5, 0, 0)
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                WrapContents = false
            };
            flow.Controls.Add(new Label
            {
                Text = "STATUS KEY:",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 170, 210),
                BackColor = Color.Transparent,
                Margin = new Padding(14, 5, 8, 0)
            });
            flow.Controls.Add(MakeDot("● OK — data received within 2 s", Color.Green, Color.FromArgb(90, 210, 110)));
            flow.Controls.Add(MakeDot("   ● LOST — no data for > 2 s", Color.OrangeRed, Color.OrangeRed));
            flow.Controls.Add(MakeDot("   ● WAIT — port never received any sentence", Color.Gray, Color.FromArgb(130, 140, 160)));
            pnlLegend.Controls.Add(flow);

            // ── DATA GRID ─────────────────────────────────────────────────────────
            _dgvRaw = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                BackgroundColor = Color.FromArgb(18, 26, 40),
                GridColor = Color.FromArgb(50, 70, 100),
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 38 }
            };

            _dgvRaw.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 55, 95);
            _dgvRaw.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 220, 255);
            _dgvRaw.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _dgvRaw.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvRaw.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

            _dgvRaw.DefaultCellStyle.BackColor = Color.FromArgb(22, 34, 54);
            _dgvRaw.DefaultCellStyle.ForeColor = Color.FromArgb(200, 220, 255);
            _dgvRaw.DefaultCellStyle.Font = new Font("Consolas", 9.5f);
            _dgvRaw.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 80, 130);
            _dgvRaw.DefaultCellStyle.SelectionForeColor = Color.White;
            _dgvRaw.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            // ── COLUMNS ───────────────────────────────────────────────────────────
            AddCol("STT", "#", 38, DataGridViewContentAlignment.MiddleCenter);
            AddCol("COM", "PORT", 65, DataGridViewContentAlignment.MiddleCenter);
            AddCol("Task", "TASK", 80, DataGridViewContentAlignment.MiddleCenter);
            AddCol("Desc", "DESCRIPTION", 200, DataGridViewContentAlignment.MiddleLeft);
            AddCol("Baud", "BAUD", 70, DataGridViewContentAlignment.MiddleCenter);
            AddCol("Header", "EXPECTED SENTENCE", 200, DataGridViewContentAlignment.MiddleCenter);
            AddCol("RawData", "RAW DATA  (last received)", 0, DataGridViewContentAlignment.MiddleLeft);
            AddCol("Age", "AGE (s)", 80, DataGridViewContentAlignment.MiddleCenter);
            AddCol("Status", "STATUS", 78, DataGridViewContentAlignment.MiddleCenter);

            _dgvRaw.Columns["RawData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            // Style riêng cho cột Desc – font thường để dễ đọc
            _dgvRaw.Columns["Desc"].DefaultCellStyle.Font = new Font("Segoe UI", 9f);
            _dgvRaw.Columns["Desc"].DefaultCellStyle.ForeColor = Color.FromArgb(140, 175, 220);
            _dgvRaw.Columns["Desc"].DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            // Style riêng cho cột RawData – monospace đậm hơn
            _dgvRaw.Columns["RawData"].DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            // ── ROWS ──────────────────────────────────────────────────────────────
            var tasks = ConfigForm.Tasks;
            for (int i = 0; i < 5; i++)
            {
                string port = i < tasks.Count ? tasks[i].PortName : $"COM{i + 1}";
                string task = i < tasks.Count ? tasks[i].TaskName : "-";
                int baud = i < tasks.Count ? tasks[i].BaudRate : 0;
                string desc = i < _dataDesc.Length ? _dataDesc[i] : "";
                _dgvRaw.Rows.Add(
                    i + 1, port, task, desc,
                    baud > 0 ? baud.ToString() : "-",
                    _expectedHeaders[i],
                    "", "", "WAIT"
                );
            }

            this.Controls.Add(_dgvRaw);
            this.Controls.Add(pnlLegend);
            this.Controls.Add(pnlHeader);
        }

        private void AddCol(string name, string header, int width, DataGridViewContentAlignment align)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = align }
            };
            if (width > 0) col.Width = width;
            _dgvRaw.Columns.Add(col);
        }

        private void RefreshData(object sender, EventArgs e)
        {
            if (_dgvRaw == null || _dgvRaw.IsDisposed) return;

            var snap = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();
            var tasks = ConfigForm.Tasks;

            for (int i = 0; i < _dgvRaw.Rows.Count && i < 5; i++)
            {
                string taskName = i < tasks.Count ? tasks[i].TaskName : "-";
                string portName = i < tasks.Count ? tasks[i].PortName : $"COM{i + 1}";
                int baud = i < tasks.Count ? tasks[i].BaudRate : 0;

                // Cập nhật cổng và baud (có thể thay đổi sau khi cấu hình)
                _dgvRaw.Rows[i].Cells["COM"].Value = portName;
                _dgvRaw.Rows[i].Cells["Baud"].Value = baud > 0 ? baud.ToString() : "-";

                var rowData = snap.TaskRows.Find(r => r.TaskName == taskName);
                if (rowData == null) continue;

                // Raw data – cắt bớt nếu quá dài để hiển thị
                string raw = rowData.Value ?? "";
                _dgvRaw.Rows[i].Cells["RawData"].Value = raw.Length > 120 ? raw.Substring(0, 120) + "…" : raw;

                // Age
                string ageStr = rowData.Age > 900 ? "" : rowData.Age.ToString("0.0");
                _dgvRaw.Rows[i].Cells["Age"].Value = ageStr;

                // Status + màu
                Color rowFg;
                string status;
                if (rowData.Age > 900)
                {
                    status = "WAIT";
                    rowFg = Color.Gray;
                }
                else if (rowData.IsStale)
                {
                    status = "LOST";
                    rowFg = Color.OrangeRed;
                }
                else
                {
                    status = "OK";
                    rowFg = Color.FromArgb(100, 220, 120);
                }

                _dgvRaw.Rows[i].Cells["Status"].Value = status;
                _dgvRaw.Rows[i].DefaultCellStyle.ForeColor = rowFg;

                // Tô màu trực tiếp cell STATUS
                var statusCell = _dgvRaw.Rows[i].Cells["Status"].Style;
                statusCell.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                switch (status)
                {
                    case "OK":
                        statusCell.BackColor = Color.FromArgb(10, 70, 28);
                        statusCell.ForeColor = Color.FromArgb(80, 210, 110);
                        break;
                    case "LOST":
                        statusCell.BackColor = Color.FromArgb(80, 14, 14);
                        statusCell.ForeColor = Color.FromArgb(255, 90, 70);
                        break;
                    default: // WAIT
                        statusCell.BackColor = Color.FromArgb(35, 42, 58);
                        statusCell.ForeColor = Color.FromArgb(110, 120, 145);
                        break;
                }

                // Row highlight nếu có alarm
                string alarm = rowData.AlarmString ?? "Normal";
                if (alarm != "Normal")
                {
                    _dgvRaw.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(80, 20, 20);
                    _dgvRaw.Rows[i].DefaultCellStyle.ForeColor = Color.OrangeRed;
                }
                else if (status == "OK")
                {
                    _dgvRaw.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(22, 34, 54);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer?.Stop();
            base.OnFormClosing(e);
        }
    }
}
