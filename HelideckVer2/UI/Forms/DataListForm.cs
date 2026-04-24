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
        private DataGridView  _dgvRaw;
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
            this.Text            = "BẢNG QUÉT THÔ – RAW COM SCAN";
            this.Size            = new Size(1200, 340);
            this.MinimumSize     = new Size(900, 280);
            this.StartPosition   = FormStartPosition.CenterParent;
            this.BackColor       = Color.FromArgb(20, 30, 48);

            var titleLbl = new Label
            {
                Text      = "RAW DATA SCANNER  –  Live NMEA input per COM port",
                Dock      = DockStyle.Top,
                Height    = 34,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 50, 80),
                ForeColor = Color.White
            };

            _dgvRaw = new DataGridView
            {
                Dock                    = DockStyle.Fill,
                AutoSizeColumnsMode     = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode        = DataGridViewAutoSizeRowsMode.None,
                BackgroundColor         = Color.FromArgb(18, 26, 40),
                GridColor               = Color.FromArgb(50, 70, 100),
                BorderStyle             = BorderStyle.None,
                CellBorderStyle         = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                AllowUserToAddRows      = false,
                ReadOnly                = true,
                RowHeadersVisible       = false,
                SelectionMode           = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight     = 32,
                RowTemplate             = { Height = 38 }
            };

            // Header style
            _dgvRaw.ColumnHeadersDefaultCellStyle.BackColor  = Color.FromArgb(40, 70, 110);
            _dgvRaw.ColumnHeadersDefaultCellStyle.ForeColor  = Color.White;
            _dgvRaw.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _dgvRaw.ColumnHeadersDefaultCellStyle.Alignment  = DataGridViewContentAlignment.MiddleCenter;

            // Cell style
            _dgvRaw.DefaultCellStyle.BackColor         = Color.FromArgb(22, 34, 54);
            _dgvRaw.DefaultCellStyle.ForeColor         = Color.FromArgb(200, 220, 255);
            _dgvRaw.DefaultCellStyle.Font              = new Font("Consolas", 9.5f);
            _dgvRaw.DefaultCellStyle.SelectionBackColor= Color.FromArgb(50, 80, 130);
            _dgvRaw.DefaultCellStyle.SelectionForeColor= Color.White;
            _dgvRaw.DefaultCellStyle.WrapMode          = DataGridViewTriState.False;

            // Columns
            AddCol("STT",       "STT",             45,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("COM",       "COM",             65,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Task",      "TASK / IN-OUT",   100, DataGridViewContentAlignment.MiddleCenter);
            AddCol("Baud",      "BAUD RATE",       85,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Header",    "EXPECTED HEADER", 155, DataGridViewContentAlignment.MiddleCenter);
            AddCol("RawData",   "RAW DATA (last received)", 0, DataGridViewContentAlignment.MiddleLeft); // auto-fill
            AddCol("Age",       "AGE (s)",         70,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Status",    "STATUS",          80,  DataGridViewContentAlignment.MiddleCenter);

            // Cột RawData tự giãn
            _dgvRaw.Columns["RawData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            // Tạo 5 dòng
            var tasks  = ConfigForm.Tasks;
            for (int i = 0; i < 5; i++)
            {
                string port    = i < tasks.Count ? tasks[i].PortName  : $"COM{i+1}";
                string task    = i < tasks.Count ? tasks[i].TaskName  : "-";
                int    baud    = i < tasks.Count ? tasks[i].BaudRate  : 0;
                _dgvRaw.Rows.Add(
                    i + 1,
                    port,
                    task,
                    baud > 0 ? baud.ToString() : "-",
                    _expectedHeaders[i],
                    "",
                    "",
                    "WAIT"
                );
            }

            this.Controls.Add(_dgvRaw);
            this.Controls.Add(titleLbl);
            titleLbl.BringToFront();
        }

        private void AddCol(string name, string header, int width, DataGridViewContentAlignment align)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name            = name,
                HeaderText      = header,
                ReadOnly        = true,
                SortMode        = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = align }
            };
            if (width > 0) col.Width = width;
            _dgvRaw.Columns.Add(col);
        }

        private void RefreshData(object sender, EventArgs e)
        {
            if (_dgvRaw == null || _dgvRaw.IsDisposed) return;

            var snap  = HelideckVer2.Core.Data.HelideckDataHub.Instance.GetSnapshot();
            var tasks = ConfigForm.Tasks;

            for (int i = 0; i < _dgvRaw.Rows.Count && i < 5; i++)
            {
                string taskName = i < tasks.Count ? tasks[i].TaskName : "-";
                string portName = i < tasks.Count ? tasks[i].PortName : $"COM{i+1}";
                int    baud     = i < tasks.Count ? tasks[i].BaudRate : 0;

                // Cập nhật cổng và baud (có thể thay đổi sau khi cấu hình)
                _dgvRaw.Rows[i].Cells["COM"].Value  = portName;
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
                    rowFg  = Color.Gray;
                }
                else if (rowData.IsStale)
                {
                    status = "LOST";
                    rowFg  = Color.OrangeRed;
                }
                else
                {
                    status = "OK";
                    rowFg  = Color.FromArgb(100, 220, 120);
                }

                _dgvRaw.Rows[i].Cells["Status"].Value = status;
                _dgvRaw.Rows[i].DefaultCellStyle.ForeColor = rowFg;

                // Row background highlight nếu có alarm
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
