using System;
using System.Drawing;
using System.Windows.Forms;
using HelideckVer2.UI.Theme;

namespace HelideckVer2
{
    public partial class DataListForm : Form
    {
        private DataGridView _dgvRaw;
        private System.Windows.Forms.Timer _updateTimer;

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
            "Meteorological: Temp / Humidity / Pressure"
        };

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
        }

        public DataListForm()
        {
            InitializeComponent();
            BuildUI();

            _updateTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _updateTimer.Tick += RefreshData;
            _updateTimer.Start();

            RefreshData(null, null);
        }

        private void BuildUI()
        {
            this.Text          = "RAW COM SCAN";
            this.Size          = new Size(1300, 380);
            this.MinimumSize   = new Size(960, 310);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor     = Palette.AppBg;

            // ── TITLE BAR ────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = Palette.SectionHdrBg
            };

            var lblTitle = new Label
            {
                Text      = "RAW DATA SCANNER",
                AutoSize  = false,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Palette.TextValue,
                BackColor = Color.Transparent,
                Padding   = new Padding(12, 0, 0, 10)
            };

            var lblSubtitle = new Label
            {
                Text      = "Live NMEA input monitor  ·  refreshes every 500 ms  ·  Age > 2 s = LOST",
                AutoSize  = false,
                Dock      = DockStyle.Bottom,
                Height    = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8f),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent,
                Padding   = new Padding(14, 0, 0, 0)
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);

            // ── LEGEND BAR (bottom) ───────────────────────────────────────────────
            var pnlLegend = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 28,
                BackColor = Palette.PanelBg
            };

            var flow = new FlowLayoutPanel
            {
                Dock         = DockStyle.Fill,
                BackColor    = Color.Transparent,
                WrapContents = false
            };
            flow.Controls.Add(new Label
            {
                Text      = "STATUS KEY:",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Palette.TextLabel,
                BackColor = Color.Transparent,
                Margin    = new Padding(14, 5, 8, 0)
            });
            flow.Controls.Add(MakeLegendDot("● OK — data received within 2 s",   Palette.OkFg));
            flow.Controls.Add(MakeLegendDot("   ● LOST — no data for > 2 s",     Palette.LostFg));
            flow.Controls.Add(MakeLegendDot("   ● WAIT — port never received any sentence", Palette.WaitFg));
            pnlLegend.Controls.Add(flow);

            // ── DATA GRID ─────────────────────────────────────────────────────────
            _dgvRaw = new DataGridView
            {
                Dock                      = DockStyle.Fill,
                AutoSizeColumnsMode       = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode          = DataGridViewAutoSizeRowsMode.None,
                BackgroundColor           = Palette.ChartBg,
                GridColor                 = Palette.BorderCard,
                BorderStyle               = BorderStyle.None,
                CellBorderStyle           = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                AllowUserToAddRows        = false,
                ReadOnly                  = true,
                RowHeadersVisible         = false,
                SelectionMode             = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight       = 34,
                RowTemplate               = { Height = 38 }
            };

            _dgvRaw.ColumnHeadersDefaultCellStyle.BackColor  = Palette.SectionHdrBg;
            _dgvRaw.ColumnHeadersDefaultCellStyle.ForeColor  = Palette.TextValue;
            _dgvRaw.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 9f, FontStyle.Bold);
            _dgvRaw.ColumnHeadersDefaultCellStyle.Alignment  = DataGridViewContentAlignment.MiddleCenter;
            _dgvRaw.ColumnHeadersDefaultCellStyle.Padding    = new Padding(4, 0, 4, 0);

            _dgvRaw.DefaultCellStyle.BackColor          = Palette.CardBg;
            _dgvRaw.DefaultCellStyle.ForeColor          = Palette.TextValue;
            _dgvRaw.DefaultCellStyle.Font               = new Font("Consolas", 9.5f);
            _dgvRaw.DefaultCellStyle.SelectionBackColor = Palette.SurfaceHi;
            _dgvRaw.DefaultCellStyle.SelectionForeColor = Palette.TextValue;
            _dgvRaw.DefaultCellStyle.WrapMode           = DataGridViewTriState.False;

            // ── COLUMNS ───────────────────────────────────────────────────────────
            AddCol("STT",     "#",                        38,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("COM",     "PORT",                     65,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Task",    "TASK",                     80,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Desc",    "DESCRIPTION",              200, DataGridViewContentAlignment.MiddleLeft);
            AddCol("Baud",    "BAUD",                     70,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Header",  "EXPECTED SENTENCE",        200, DataGridViewContentAlignment.MiddleCenter);
            AddCol("RawData", "RAW DATA  (last received)", 0,  DataGridViewContentAlignment.MiddleLeft);
            AddCol("Age",     "AGE (s)",                  80,  DataGridViewContentAlignment.MiddleCenter);
            AddCol("Status",  "STATUS",                   78,  DataGridViewContentAlignment.MiddleCenter);

            _dgvRaw.Columns["RawData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _dgvRaw.Columns["Desc"].DefaultCellStyle.Font    = new Font("Segoe UI", 9f);
            _dgvRaw.Columns["Desc"].DefaultCellStyle.ForeColor  = Palette.TextLabel;
            _dgvRaw.Columns["Desc"].DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            _dgvRaw.Columns["RawData"].DefaultCellStyle.Padding = new Padding(6, 0, 0, 0);

            // ── ROWS ──────────────────────────────────────────────────────────────
            var tasks = ConfigForm.Tasks;
            for (int i = 0; i < 5; i++)
            {
                string port = i < tasks.Count ? tasks[i].PortName : $"COM{i + 1}";
                string task = i < tasks.Count ? tasks[i].TaskName : "-";
                int    baud = i < tasks.Count ? tasks[i].BaudRate : 0;
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

        private static Label MakeLegendDot(string text, Color fg) => new Label
        {
            Text      = text,
            AutoSize  = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = fg,
            BackColor = Color.Transparent,
            Margin    = new Padding(16, 5, 0, 0)
        };

        private void AddCol(string name, string header, int width, DataGridViewContentAlignment align)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name       = name,
                HeaderText = header,
                ReadOnly   = true,
                SortMode   = DataGridViewColumnSortMode.NotSortable,
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
                string portName = i < tasks.Count ? tasks[i].PortName : $"COM{i + 1}";
                int    baud     = i < tasks.Count ? tasks[i].BaudRate : 0;

                _dgvRaw.Rows[i].Cells["COM"].Value  = portName;
                _dgvRaw.Rows[i].Cells["Baud"].Value = baud > 0 ? baud.ToString() : "-";

                var rowData = snap.TaskRows.Find(r => r.TaskName == taskName);
                if (rowData == null) continue;

                string raw = rowData.Value ?? "";
                _dgvRaw.Rows[i].Cells["RawData"].Value = raw.Length > 120 ? raw[..120] + "…" : raw;

                string ageStr = rowData.Age > 900 ? "" : rowData.Age.ToString("0.0");
                _dgvRaw.Rows[i].Cells["Age"].Value = ageStr;

                Color  rowFg;
                string status;

                if (rowData.Age > 900)
                {
                    status = "WAIT";
                    rowFg  = Palette.WaitFg;
                }
                else if (rowData.IsStale)
                {
                    status = "LOST";
                    rowFg  = Palette.LostFg;
                }
                else
                {
                    status = "OK";
                    rowFg  = Palette.OkFg;
                }

                _dgvRaw.Rows[i].Cells["Status"].Value = status;
                _dgvRaw.Rows[i].DefaultCellStyle.ForeColor = rowFg;

                var statusCell = _dgvRaw.Rows[i].Cells["Status"].Style;
                statusCell.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                switch (status)
                {
                    case "OK":
                        statusCell.BackColor = Palette.OkBg;
                        statusCell.ForeColor = Palette.OkFg;
                        break;
                    case "LOST":
                        statusCell.BackColor = Palette.LostBg;
                        statusCell.ForeColor = Palette.LostFg;
                        break;
                    default:
                        statusCell.BackColor = Palette.WaitBg;
                        statusCell.ForeColor = Palette.WaitFg;
                        break;
                }

                string alarm = rowData.AlarmString ?? "Normal";
                if (alarm != "Normal")
                {
                    _dgvRaw.Rows[i].DefaultCellStyle.BackColor = Palette.AlarmActiveBg;
                    _dgvRaw.Rows[i].DefaultCellStyle.ForeColor = Palette.AlarmActiveFg;
                }
                else if (status == "OK")
                {
                    _dgvRaw.Rows[i].DefaultCellStyle.BackColor = Palette.CardBg;
                }
            }
        }
    }
}
