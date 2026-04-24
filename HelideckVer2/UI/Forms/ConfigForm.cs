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

        private void SetupUI()
        {
            this.Text = "SYSTEM CONFIGURATION";
            this.ClientSize = new Size(520, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            Panel pnlBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 60,
                BackColor = Color.WhiteSmoke,
                Padding   = new Padding(10)
            };

            chkSimulationMode = new CheckBox
            {
                Text      = "Enable Simulation Mode (Fake Data)",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Blue,
                Dock      = DockStyle.Left,
                Padding   = new Padding(10, 8, 0, 0)
            };

            Button btnSave = new Button
            {
                Text      = "SAVE & EXIT",
                Width     = 140,
                BackColor = Color.LightGreen,
                Dock      = DockStyle.Right
            };
            btnSave.Click += BtnSave_Click;

            pnlBottom.Controls.Add(chkSimulationMode);
            pnlBottom.Controls.Add(btnSave);

            TabControl tabConfig = new TabControl { Dock = DockStyle.Fill };

            TabPage tabAlarm = new TabPage("Alarm Limits") { BackColor = Color.White };
            SetupAlarmTab(tabAlarm);
            tabConfig.TabPages.Add(tabAlarm);

            TabPage tabCom = new TabPage("COM Configuration") { BackColor = Color.White };
            SetupComTab(tabCom);
            tabConfig.TabPages.Add(tabCom);

            this.Controls.Add(tabConfig);
            this.Controls.Add(pnlBottom);
            pnlBottom.BringToFront();
        }

        private void SetupAlarmTab(TabPage tab)
        {
            int y = 20;
            numWind  = AddAlarmRow(tab, "Wind Max (m/s):",  ref y);
            numRoll  = AddAlarmRow(tab, "Roll Max (°):",    ref y);
            numPitch = AddAlarmRow(tab, "Pitch Max (°):",   ref y);
            numHeave = AddAlarmRow(tab, "Heave Max (cm):",  ref y);
        }

        private void SetupComTab(TabPage tab)
        {
            var lbl = new Label
            {
                Text      = "Chỉnh sửa cổng COM. BaudRate cố định theo phần cứng.",
                Dock      = DockStyle.Top,
                Height    = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.DimGray,
                Padding   = new Padding(4, 0, 0, 0)
            };
            tab.Controls.Add(lbl);

            dgvComConfig = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible     = false,
                BackgroundColor       = Color.White,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                EditMode              = DataGridViewEditMode.EditOnKeystrokeOrF2
            };

            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Task", HeaderText = "Task", ReadOnly = true, FillWeight = 80
            });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Port", HeaderText = "COM Port", FillWeight = 90
            });
            dgvComConfig.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Baud", HeaderText = "Baud Rate (fixed)", ReadOnly = true, FillWeight = 100
            });

            tab.Controls.Add(dgvComConfig);
            lbl.BringToFront();
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

        private void LoadData()
        {
            numWind.Value  = (decimal)SystemConfig.WindMax;
            numRoll.Value  = (decimal)SystemConfig.RMax;
            numPitch.Value = (decimal)SystemConfig.PMax;
            numHeave.Value = (decimal)SystemConfig.HMax;
            chkSimulationMode.Checked = SystemConfig.IsSimulationMode;

            dgvComConfig.Rows.Clear();
            foreach (var t in Tasks)
                dgvComConfig.Rows.Add(t.TaskName, t.PortName, t.BaudRate);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SystemConfig.WindMax           = (double)numWind.Value;
            SystemConfig.RMax              = (double)numRoll.Value;
            SystemConfig.PMax              = (double)numPitch.Value;
            SystemConfig.HMax              = (double)numHeave.Value;
            SystemConfig.IsSimulationMode  = chkSimulationMode.Checked;

            foreach (DataGridViewRow row in dgvComConfig.Rows)
            {
                string taskName = row.Cells["Task"].Value?.ToString();
                var task = Tasks.Find(t => t.TaskName == taskName);
                if (task == null) continue;

                task.PortName = row.Cells["Port"].Value?.ToString() ?? task.PortName;
                // BaudRate không lưu – cố định theo phần cứng
            }

            var saveCfg  = HelideckVer2.Models.SystemConfig.Export();
            saveCfg.Tasks = Tasks;
            HelideckVer2.Services.ConfigService.Save(saveCfg);

            MessageBox.Show(
                "Cấu hình đã lưu!\n\nVui lòng khởi động lại ứng dụng để áp dụng thay đổi COM / BaudRate.",
                "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
