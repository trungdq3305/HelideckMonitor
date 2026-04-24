using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using HelideckVer2.Models;

namespace HelideckVer2
{
    public partial class ConfigForm : Form
    {
        private NumericUpDown numWind, numRoll, numPitch, numHeave;
        private DataGridView dgvComConfig;
        private CheckBox chkSimulationMode;

        public static List<DeviceTask> Tasks = new List<DeviceTask>
        {
    
            new DeviceTask { TaskName = "GPS", PortName = "COM1", BaudRate = 9600 },
            new DeviceTask { TaskName = "WIND", PortName = "COM2", BaudRate = 4800 },
            new DeviceTask { TaskName = "R/P/H", PortName = "COM3", BaudRate = 9600 },
            new DeviceTask { TaskName = "HEADING", PortName = "COM4", BaudRate = 4800 },
            new DeviceTask { TaskName = "AUX", PortName = "COM5", BaudRate = 4800 }
        };

        public ConfigForm()
        {
            InitializeComponent();
            SetupUI();

            var cfg = HelideckVer2.Services.ConfigService.Load();
            HelideckVer2.Models.SystemConfig.Apply(cfg);

            if (cfg.Tasks != null && cfg.Tasks.Count > 0)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = Tasks.Find(x => x.TaskName == saved.TaskName);
                    if (t != null)
                    {
                        t.PortName = saved.PortName;
                        //t.BaudRate = saved.BaudRate; 
                    }
                }
            }

            LoadData();

            dgvComConfig.CellBeginEdit += (s, e) =>
            {
                var colName = dgvComConfig.Columns[e.ColumnIndex].Name;
                // Khóa không cho sửa bất kỳ cột nào
                if (colName == "Port" || colName == "Task" || colName == "Baud")
                    e.Cancel = true;
            };
        }

        private void SetupUI()
        {
            this.Text = "SYSTEM CONFIGURATION";

            // Dùng ClientSize thay vì Size để không gian bên trong luôn đảm bảo đúng 500x380, bất chấp viền cửa sổ dày/mỏng
            this.ClientSize = new Size(500, 380);
            this.FormBorderStyle = FormBorderStyle.FixedDialog; // Khóa resize để form không bị kéo giãn lung tung
            this.MaximizeBox = false;

            // 1. TẠO PANEL CHỨA NÚT BẤM VÀ CHECKBOX (Neo cố định ở đáy)
            Panel pnlBottom = new Panel()
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(10) // Cách lề 10px đều cả 4 phía cho đẹp
            };

            chkSimulationMode = new CheckBox()
            {
                Text = "Enable Simulation Mode (Fake Data)",
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Blue,
                Dock = DockStyle.Left, // Tự động bám chặt lề trái
                Padding = new Padding(10, 8, 0, 0) // Đẩy chữ xuống một chút cho cân bằng với nút
            };

            Button btnSave = new Button()
            {
                Text = "SAVE and EXIT",
                Width = 140,
                // Không cần set Height nữa vì Dock.Right sẽ tự ăn theo chiều cao của Panel trừ đi Padding
                BackColor = Color.LightGreen,
                Dock = DockStyle.Right // Tự động bám chặt lề phải (Chắc chắn không bao giờ bị mất)
            };
            btnSave.Click += BtnSave_Click;

            // Phải Add CheckBox trước, Button sau (hoặc ngược lại) để Dock hoạt động đúng
            pnlBottom.Controls.Add(chkSimulationMode);
            pnlBottom.Controls.Add(btnSave);

            // 2. TẠO TAB CONTROL (Tự động lấp đầy khoảng không gian còn lại ở trên)
            TabControl tabConfig = new TabControl
            {
                Dock = DockStyle.Fill
            };

            TabPage tabAlarm = new TabPage("Alarm Limit") { BackColor = Color.White };
            SetupAlarmTab(tabAlarm);
            tabConfig.TabPages.Add(tabAlarm);

            TabPage tabCom = new TabPage("COM configuration") { BackColor = Color.White };
            SetupComTab(tabCom);
            tabConfig.TabPages.Add(tabCom);

            // 3. THÊM CÁC THÀNH PHẦN VÀO FORM (Thứ tự thêm rất quan trọng)
            this.Controls.Add(tabConfig);
            this.Controls.Add(pnlBottom);

            // Đảm bảo Panel chứa nút bấm luôn nổi lên trên cùng, không bị Tab che mất
            pnlBottom.BringToFront();
        }
        private void SetupAlarmTab(TabPage tab)
        {
            int y = 20;
            numWind = AddRow(tab, "Wind Max (m/s):", ref y);
            numRoll = AddRow(tab, "Roll Max (deg):", ref y);
            numPitch = AddRow(tab, "Pitch Max (deg):", ref y);
            numHeave = AddRow(tab, "Heave Max (cm):", ref y);
        }

        private void SetupComTab(TabPage tab)
        {
            dgvComConfig = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White
            };

            var colTask = new DataGridViewTextBoxColumn { Name = "Task", HeaderText = "Task Name", ReadOnly = true };
            var colPort = new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "COM Port" };

            // THIẾT LẬP: Cột Baud Rate hiện ra nhưng KHÔNG cho sửa (ReadOnly = true)
            var colBaud = new DataGridViewTextBoxColumn
            {
                Name = "Baud",
                HeaderText = "Baud Rate",
                ReadOnly = true // KHÓA TẠI ĐÂY
            };

            dgvComConfig.Columns.AddRange(colTask, colPort, colBaud);

            // Thêm một lớp bảo mật nữa bằng cách chặn sự kiện Edit trực tiếp trên cột Baud
            dgvComConfig.CellBeginEdit += (s, e) => {
                if (dgvComConfig.Columns[e.ColumnIndex].Name == "Baud")
                {
                    e.Cancel = true;
                }
            };

            tab.Controls.Add(dgvComConfig);
        }

        private NumericUpDown AddRow(TabPage p, string text, ref int top)
        {
            Label lbl = new Label() { Text = text, Top = top, Left = 30, AutoSize = true };
            NumericUpDown num = new NumericUpDown() { Top = top - 3, Left = 200, Width = 100, DecimalPlaces = 1, Maximum = 1000 };
            p.Controls.Add(lbl);
            p.Controls.Add(num);
            top += 50;
            return num;
        }

        private void LoadData()
        {
            numWind.Value = (decimal)SystemConfig.WindMax;
            numRoll.Value = (decimal)SystemConfig.RMax;
            numPitch.Value = (decimal)SystemConfig.PMax;
            numHeave.Value = (decimal)SystemConfig.HMax;

            // --- ĐỌC GIÁ TRỊ LÊN CHECKBOX ---
            chkSimulationMode.Checked = SystemConfig.IsSimulationMode;

            dgvComConfig.Rows.Clear();
            foreach (var t in Tasks)
            {
                // Hiển thị BaudRate từ file cấu hình lên Grid
                dgvComConfig.Rows.Add(t.TaskName, t.PortName, t.BaudRate);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SystemConfig.WindMax = (double)numWind.Value;
            SystemConfig.RMax = (double)numRoll.Value;
            SystemConfig.PMax = (double)numPitch.Value;
            SystemConfig.HMax = (double)numHeave.Value;

            // --- LƯU TRẠNG THÁI CHECKBOX VÀO SYSTEM CONFIG ---
            SystemConfig.IsSimulationMode = chkSimulationMode.Checked;

            foreach (DataGridViewRow row in dgvComConfig.Rows)
            {

                string taskName = row.Cells["Task"].Value?.ToString();
                var task = Tasks.Find(t => t.TaskName == taskName);

                if (task != null)
                {

                    task.PortName = row.Cells["Port"].Value?.ToString();
                }
            }

            var saveCfg = HelideckVer2.Models.SystemConfig.Export();
            saveCfg.Tasks = Tasks;

            HelideckVer2.Services.ConfigService.Save(saveCfg);

            // Cảnh báo người dùng cần khởi động lại để đổi chế độ COM / Simulator
            MessageBox.Show("Configuration saved!\n\nPlease RESTART the application to apply Simulation Mode changes.", "Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);

            MessageBox.Show("Configuration saved!", "Notification");
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}