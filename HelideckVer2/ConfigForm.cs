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
        private DataGridView dgvComConfig; // Grid để chỉnh COM

        // Giả lập danh sách Task (Trong thực tế nên lưu vào file JSON/DB)
        public static List<DeviceTask> Tasks = new List<DeviceTask>
        {
            new DeviceTask { TaskName = "GPS", PortName = "COM1", BaudRate = 9600 },
            new DeviceTask { TaskName = "WIND", PortName = "COM2", BaudRate = 4800 },
            new DeviceTask { TaskName = "R/P/H", PortName = "COM3", BaudRate = 9600 },
            new DeviceTask { TaskName = "HEADING", PortName = "COM4", BaudRate = 4800 }
        };

        public ConfigForm()
        {
            InitializeComponent();
            SetupUI();
            // LOAD CONFIG JSON
            var cfg = HelideckVer2.Services.ConfigService.Load();
            HelideckVer2.Models.SystemConfig.Apply(cfg);

            // merge baudrate vào Tasks theo PortName (fix cứng port)
            if (cfg.Tasks != null && cfg.Tasks.Count > 0)
            {
                foreach (var saved in cfg.Tasks)
                {
                    var t = Tasks.Find(x => x.PortName == saved.PortName);
                    if (t != null) t.BaudRate = saved.BaudRate;
                }
            }

            LoadData();

            dgvComConfig.CellBeginEdit += (s, e) =>
            {
                var colName = dgvComConfig.Columns[e.ColumnIndex].Name;
                if (colName == "Port" || colName == "Task")
                    e.Cancel = true;
            };
        }


        private void SetupUI()
        {
            this.Text = "SYSTEM CONFIGURATION";
            this.Size = new Size(500, 400); // To hơn xíu

            TabControl tabConfig = new TabControl { Dock = DockStyle.Top, Height = 300 };

            // --- TAB 1: ALARM LIMITS ---
            TabPage tabAlarm = new TabPage("Alarm Limit");
            tabAlarm.BackColor = Color.White;
            SetupAlarmTab(tabAlarm);
            tabConfig.TabPages.Add(tabAlarm);

            // --- TAB 2: COM CONFIG ---
            TabPage tabCom = new TabPage("COM configuration");
            tabCom.BackColor = Color.White;
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
            dgvComConfig = new DataGridView();
            dgvComConfig.Dock = DockStyle.Fill;
            dgvComConfig.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvComConfig.AllowUserToAddRows = false;
            dgvComConfig.AllowUserToDeleteRows = false;
            dgvComConfig.RowHeadersVisible = false;

            // Task
            var colTask = new DataGridViewTextBoxColumn
            {
                Name = "Task",
                HeaderText = "Task Name",
                ReadOnly = true
            };

            // Port (FIX CỨNG - KHÔNG CHO SỬA)
            var colPort = new DataGridViewTextBoxColumn
            {
                Name = "Port",
                HeaderText = "COM Port",
                ReadOnly = true
            };

            // Baudrate (CHO SỬA)
            var colBaud = new DataGridViewComboBoxColumn
            {
                Name = "Baud",
                HeaderText = "Baud Rate"
            };
            colBaud.Items.AddRange("4800", "9600", "19200", "38400", "115200");

            dgvComConfig.Columns.AddRange(colTask, colPort, colBaud);

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
            // Alarm
            numWind.Value = (decimal)SystemConfig.WindMax;
            numRoll.Value = (decimal)SystemConfig.RMax;
            numPitch.Value = (decimal)SystemConfig.PMax;
            numHeave.Value = (decimal)SystemConfig.HMax;

            // COM Config
            foreach (var t in Tasks)
            {
                dgvComConfig.Rows.Add(t.TaskName, t.PortName, t.BaudRate.ToString());
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Save Alarm
            SystemConfig.WindMax = (double)numWind.Value;
            SystemConfig.RMax = (double)numRoll.Value;
            SystemConfig.PMax = (double)numPitch.Value;
            SystemConfig.HMax = (double)numHeave.Value;

            // Save COM: CHỈ CẬP NHẬT BAUDRATE
            foreach (DataGridViewRow row in dgvComConfig.Rows)
            {
                string port = row.Cells["Port"].Value?.ToString();
                string baudStr = row.Cells["Baud"].Value?.ToString();

                if (string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(baudStr))
                    continue;

                var task = Tasks.Find(t => t.PortName == port);
                if (task != null)
                    task.BaudRate = int.Parse(baudStr);
            }
            // SAVE CONFIG JSON (SystemConfig + Tasks)
            var saveCfg = HelideckVer2.Models.SystemConfig.Export();
            saveCfg.Tasks = Tasks; // lưu PortName + BaudRate

            HelideckVer2.Services.ConfigService.Save(saveCfg);

            MessageBox.Show("Configuration saved!", "Notification");
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

    }
}