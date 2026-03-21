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
            this.Size = new Size(900, 300);
            this.StartPosition = FormStartPosition.CenterParent;

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

            _dgvDataList.Columns.Add("Task", "TASK NAME");
            _dgvDataList.Columns.Add("Port", "PORT");
            _dgvDataList.Columns.Add("Value", "VALUE");
            _dgvDataList.Columns.Add("Unit", "UNIT");
            _dgvDataList.Columns.Add("Age", "AGE(s)");
            _dgvDataList.Columns.Add("Status", "STATUS");
            _dgvDataList.Columns.Add("Limit", "LIMIT");
            _dgvDataList.Columns.Add("Alarm", "ALARM");

            // Khởi tạo 4 dòng trống
            for (int i = 0; i < 4; i++)
            {
                _dgvDataList.Rows.Add(_tasks[i], _ports[i], "", _units[i], "", "WAIT", "", "Normal");
            }

            this.Controls.Add(_dgvDataList);
        }

        private void UpdateData(object sender, EventArgs e)
        {
            for (int i = 0; i < 4; i++)
            {
                // Kéo dữ liệu tĩnh từ Form1 sang
                _dgvDataList.Rows[i].Cells["Value"].Value = Form1.GridValues[i];
                _dgvDataList.Rows[i].Cells["Limit"].Value = Form1.GridLimits[i];
                _dgvDataList.Rows[i].Cells["Alarm"].Value = Form1.GridAlarms[i];

                double age = Form1.GridAges[i];
                bool isStale = Form1.GridStales[i];

                _dgvDataList.Rows[i].Cells["Age"].Value = age > 900 ? "" : age.ToString("0.0");

                if (isStale && age <= 900)
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "STALE";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.OrangeRed;
                }
                else if (age > 900)
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "WAIT";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.Gray;
                }
                else
                {
                    _dgvDataList.Rows[i].Cells["Status"].Value = "OK";
                    _dgvDataList.Rows[i].Cells["Status"].Style.ForeColor = Color.Green;
                }

                string alarmState = Form1.GridAlarms[i];
                if (alarmState.Contains("Active"))
                    _dgvDataList.Rows[i].Cells["Alarm"].Style.ForeColor = Color.Red;
                else if (alarmState.Contains("Ack"))
                    _dgvDataList.Rows[i].Cells["Alarm"].Style.ForeColor = Color.Orange;
                else
                    _dgvDataList.Rows[i].Cells["Alarm"].Style.ForeColor = Color.Green;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}