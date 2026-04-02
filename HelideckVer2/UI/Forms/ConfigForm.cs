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

        public static List<DeviceTask> Tasks = new List<DeviceTask>
        {
            new DeviceTask { TaskName = "GPS", PortName = "COM1" },
            new DeviceTask { TaskName = "WIND", PortName = "COM2" },
            new DeviceTask { TaskName = "R/P/H", PortName = "COM3" },
            new DeviceTask { TaskName = "HEADING", PortName = "COM4" }
        };

        public ConfigForm()
        {
            InitializeComponent();
            SetupUI();

            var cfg = HelideckVer2.Services.ConfigService.Load();
            HelideckVer2.Models.SystemConfig.Apply(cfg);

            // Bỏ qua giá trị lưu cũ, ép toàn bộ Tasks về 4800
            if (cfg.Tasks != null && cfg.Tasks.Count > 0)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = Tasks.Find(x => x.TaskName == saved.TaskName); // Tìm theo TaskName an toàn hơn
                    if (t != null) t.PortName = saved.PortName;
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
            this.Size = new Size(500, 400);

            TabControl tabConfig = new TabControl { Dock = DockStyle.Top, Height = 300 };

            TabPage tabAlarm = new TabPage("Alarm Limit") { BackColor = Color.White };
            SetupAlarmTab(tabAlarm);
            tabConfig.TabPages.Add(tabAlarm);

            TabPage tabCom = new TabPage("COM configuration") { BackColor = Color.White };
            SetupComTab(tabCom);
            tabConfig.TabPages.Add(tabCom);

            this.Controls.Add(tabConfig);

            Button btnSave = new Button() { Text = "SAVE and EXIT", Top = 310, Left = 170, Width = 150, Height = 40, BackColor = Color.LightGreen };
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
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
            var colPort = new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "COM Port" }; // Cho phép gõ để sửa

            // BỎ HẲN CỘT BAUD RATE VÌ ĐÃ FIX CỨNG 4800 TRONG COMENGINE
            dgvComConfig.Columns.AddRange(colTask, colPort);
            tab.Controls.Add(dgvComConfig);

            // Khóa cột Task, chỉ cho người dùng sửa cột Port
            dgvComConfig.CellBeginEdit += (s, e) =>
            {
                if (dgvComConfig.Columns[e.ColumnIndex].Name == "Task")
                    e.Cancel = true;
            };
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

            foreach (var t in Tasks)
            {
                dgvComConfig.Rows.Add(t.TaskName, t.PortName);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SystemConfig.WindMax = (double)numWind.Value;
            SystemConfig.RMax = (double)numRoll.Value;
            SystemConfig.PMax = (double)numPitch.Value;
            SystemConfig.HMax = (double)numHeave.Value;

            foreach (DataGridViewRow row in dgvComConfig.Rows)
            {
                string port = row.Cells["Port"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(port)) continue;

                var task = Tasks.Find(t => t.PortName == port);
                if (task != null) task.PortName = port; // Cố định 4800
            }

            var saveCfg = HelideckVer2.Models.SystemConfig.Export();
            saveCfg.Tasks = Tasks;

            HelideckVer2.Services.ConfigService.Save(saveCfg);

            MessageBox.Show("Configuration saved!", "Notification");
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}