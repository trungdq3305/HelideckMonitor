using System;
using System.Drawing;
using System.Windows.Forms;

namespace HelideckVer2
{
    public partial class DataListForm : Form
    {
        private DataGridView _dgvDataList;
        private System.Windows.Forms.Timer _updateTimer;

        // Cấu hình cố định cho 4 dòng
        private string[] _tasks = { "GPS", "WIND", "R/P/H", "HEADING" };
        private string[] _ports = { "COM1", "COM2", "COM3", "COM4" };
        private string[] _units = { "kn", "m/s,°", "°, °, cm", "°" };

        public DataListForm()
        {
            InitializeComponent();
            SetupUI();

            // Timer quét dữ liệu từ Form1 mỗi giây
            _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _updateTimer.Tick += UpdateData;
            _updateTimer.Start();
        }

        private void SetupUI()
        {
            this.Text = "LIVE DATA LIST";
            // Giữ kích thước cửa sổ rộng để dữ liệu không bị che
            this.Size = new Size(1100, 350);
            this.StartPosition = FormStartPosition.CenterParent;

            _dgvDataList = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                // 1. VÔ HIỆU HÓA tự động chỉnh chiều cao dòng (Cố định chiều cao dòng)
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
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

            // 2. VÔ HIỆU HÓA tự động xuống hàng (Dữ liệu sẽ luôn nằm trên 1 dòng)
            _dgvDataList.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            _dgvDataList.Columns.Add("Task", "TASK NAME");
            _dgvDataList.Columns.Add("Port", "PORT");
            _dgvDataList.Columns.Add("Value", "VALUE");
            _dgvDataList.Columns.Add("Unit", "UNIT");
            _dgvDataList.Columns.Add("Age", "AGE(s)");
            _dgvDataList.Columns.Add("Status", "STATUS");
            _dgvDataList.Columns.Add("Limit", "LIMIT");
            _dgvDataList.Columns.Add("Alarm", "ALARM");

            // Tỷ lệ cột mặc định (Để đảm bảo hiển thị đủ Value/Limit dài)
            _dgvDataList.Columns["Task"].FillWeight = 80;
            _dgvDataList.Columns["Port"].FillWeight = 60;
            _dgvDataList.Columns["Value"].FillWeight = 200;
            _dgvDataList.Columns["Unit"].FillWeight = 70;
            _dgvDataList.Columns["Age"].FillWeight = 60;
            _dgvDataList.Columns["Status"].FillWeight = 70;
            _dgvDataList.Columns["Limit"].FillWeight = 160;
            _dgvDataList.Columns["Alarm"].FillWeight = 120;

            // Khởi tạo 4 dòng trống
            for (int i = 0; i < 4; i++)
            {
                _dgvDataList.Rows.Add(_tasks[i], _ports[i], "", _units[i], "", "WAIT", "", "Normal");
            }

            this.Controls.Add(_dgvDataList);
        }

        private void UpdateData(object sender, EventArgs e)
        {
            if (_dgvDataList == null || _dgvDataList.IsDisposed) return;

            for (int i = 0; i < 4; i++)
            {
                // Kéo dữ liệu tĩnh từ Form1 sang
                _dgvDataList.Rows[i].Cells["Value"].Value = Form1.GridValues[i];
                _dgvDataList.Rows[i].Cells["Limit"].Value = Form1.GridLimits[i];

                // --- RÚT GỌN CHUỖI ALARM ĐỂ TRÁNH GIẬT GIAO DIỆN ---
                string rawAlarm = Form1.GridAlarms[i] ?? ""; // Tránh null
                string shortAlarm = rawAlarm.Replace("ROLL", "R")
                                            .Replace("PITCH", "P")
                                            .Replace("HEAVE", "H");

                // Gán chuỗi đã rút gọn vào DataGridView
                _dgvDataList.Rows[i].Cells["Alarm"].Value = shortAlarm;

                double age = Form1.GridAges[i];
                bool isStale = Form1.GridStales[i];

                _dgvDataList.Rows[i].Cells["Age"].Value = age > 900 ? "" : age.ToString("0.0");

                // --- LOGIC MÀU SẮC STATUS GỐC (Dựa trên Age/Stale) ---
                if (age > 900)
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "WAIT";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.Gray;
                }
                else if (isStale)
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "STALE";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.OrangeRed;
                }
                else
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "OK";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.Green;
                }

                // --- LOGIC MÀU SẮC ALARM VÀ STATUS KHI CÓ LỖI ---
                if (shortAlarm != "Normal")
                {
                    // Tô đỏ Alarm khi không Normal
                    _dgvDataList.Rows[i].Cells["Alarm"].Style.ForeColor = Color.Red;

                    // Tô đỏ luôn Status cho đồng bộ cảnh báo (tuỳ chọn)
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.Red;
                }
                else
                {
                    // Trạng thái bình thường
                    _dgvDataList.Rows[i].Cells["Alarm"].Style.ForeColor = Color.Green;
                }
            }

            // 4. CHỐNG CHE DỮ LIỆU DÀI: Tự động mở rộng chiều rộng cột
            // Chúng ta chỉ cần AutoResize cột Value và Limit (hoặc DisplayedCells là đủ)
            _dgvDataList.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}