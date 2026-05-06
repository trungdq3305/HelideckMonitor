using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using HelideckVer2.UI.Theme;

namespace HelideckVer2.UI.Controls
{
    public partial class TrendChartControl : UserControl
    {
        public struct TrendPoint { public double X, V1, V2, V3; }
        public enum TrendMode { Motion, Wind, Env }

        private Chart _chart;
        private TrendMode _currentMode = TrendMode.Motion;
        private bool _isSeparateTrend = false;
        private double _viewMinutes = 2.0;
        private const int BufferMinutes = 20;
        private bool _isLiveMode = true;
        private bool _isProgrammaticScroll = false;
        private readonly List<TrendPoint> _motionBuffer = new List<TrendPoint>();
        private readonly List<TrendPoint> _windBuffer   = new List<TrendPoint>();
        private readonly List<TrendPoint> _envBuffer    = new List<TrendPoint>();

        private string _hoverText  = "";
        private Point  _hoverPoint = Point.Empty;
        private double _hoverXValue = double.NaN; // giá trị X tại vị trí chuột — dùng để vẽ cursor trong PostPaint

        public TrendChartControl()
        {
            InitializeComponent();
            SetupChart();
        }

        private void InitializeComponent()
        {
            _chart = new Chart
            {
                Dock      = DockStyle.Fill,
                BackColor = Palette.ChartBg
            };
            this.Controls.Add(_chart);
        }

        private void SetupChart()
        {
            var legend = new Legend
            {
                Docking   = Docking.Top,
                Alignment = StringAlignment.Center,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Palette.ChartBg,
                ForeColor = Palette.TextLabel
            };
            _chart.Legends.Add(legend);

            _chart.MouseMove  += Chart_MouseMove;
            _chart.MouseLeave += Chart_MouseLeave;
            _chart.PostPaint  += Chart_PostPaint;
            _chart.AxisViewChanged += Chart_AxisViewChanged;

            SetMode(TrendMode.Motion, false);
        }

        public void SetMode(TrendMode mode, bool isSeparate)
        {
            _currentMode = mode;
            _isSeparateTrend = isSeparate;
            _chart.Series.Clear();
            _chart.ChartAreas.Clear();

            _chart.BackColor = Palette.ChartBg;
            if (_chart.Legends.Count > 0)
            {
                _chart.Legends[0].BackColor = Palette.ChartBg;
                _chart.Legends[0].ForeColor = Palette.TextLabel;
            }

            if (_isSeparateTrend)
            {
                if (mode == TrendMode.Motion)
                {
                    AddArea("AreaRoll"); AddArea("AreaPitch"); AddArea("AreaHeave");
                    AddSeries("Roll",  "AreaRoll",  Palette.SeriesRoll);
                    AddSeries("Pitch", "AreaPitch", Palette.SeriesPitch);
                    AddSeries("Heave", "AreaHeave", Palette.SeriesHeave);
                    AlignAreas("AreaPitch", "AreaRoll"); AlignAreas("AreaHeave", "AreaRoll");
                    SetAreaYTitle("AreaRoll",  "Roll (°)",    Palette.SeriesRoll);
                    SetAreaYTitle("AreaPitch", "Pitch (°)",   Palette.SeriesPitch);
                    SetAreaYTitle("AreaHeave", "Heave (cm)",  Palette.SeriesHeave);
                }
                else if (mode == TrendMode.Env)
                {
                    AddArea("AreaTemp"); AddArea("AreaHumidity");
                    AddSeries("Temp",     "AreaTemp",     Palette.SeriesRoll);
                    AddSeries("Humidity", "AreaHumidity", Palette.SeriesWSpeed);
                    AlignAreas("AreaHumidity", "AreaTemp");
                    SetAreaYTitle("AreaTemp",     "Temp (°C)",     Palette.SeriesRoll);
                    SetAreaYTitle("AreaHumidity", "Humidity (%)",  Palette.SeriesWSpeed, min: 0, max: 100);
                }
                else
                {
                    AddArea("AreaWindSpeed"); AddArea("AreaWindDir");
                    AddSeries("WindSpeed", "AreaWindSpeed", Palette.SeriesWSpeed);
                    AddSeries("WindDir",   "AreaWindDir",   Palette.SeriesWDir);
                    AlignAreas("AreaWindDir", "AreaWindSpeed");
                    SetAreaYTitle("AreaWindSpeed", "Wind Speed (m/s)", Palette.SeriesWSpeed);
                    SetAreaYTitle("AreaWindDir",   "Direction (°)",    Palette.SeriesWDir, min: 0, max: 360, interval: 90);
                }
            }
            else if (mode == TrendMode.Env)
            {
                AddArea("MainArea");
                AddSeries("Temp", "MainArea", Palette.SeriesRoll);
                var humSeries = AddSeries("Humidity", "MainArea", Palette.SeriesWSpeed);
                humSeries.YAxisType = AxisType.Secondary;

                var area = _chart.ChartAreas["MainArea"];
                area.AxisY.Title                    = "Temperature (°C)";
                area.AxisY.TitleForeColor           = Palette.SeriesRoll;
                area.AxisY.TitleFont                = new Font("Segoe UI", 11f, FontStyle.Bold);
                area.AxisY2.Enabled                 = AxisEnabled.True;
                area.AxisY2.Title                   = "Humidity (%)";
                area.AxisY2.TitleForeColor          = Palette.SeriesWSpeed;
                area.AxisY2.TitleFont               = new Font("Segoe UI", 11f, FontStyle.Bold);
                area.AxisY2.LabelStyle.ForeColor    = Palette.SeriesWSpeed;
                area.AxisY2.LabelStyle.Font         = new Font("Segoe UI", 9f, FontStyle.Bold);
                area.AxisY2.Minimum                 = 0;
                area.AxisY2.Maximum                 = 100;
                area.AxisY2.LineColor               = Palette.SeriesWSpeed;
                area.AxisY2.MajorTickMark.LineColor = Palette.SeriesWSpeed;
                area.AxisY2.MajorGrid.LineColor     = Color.Transparent;
            }
            else
            {
                AddArea("MainArea");
                if (mode == TrendMode.Motion)
                {
                    AddSeries("Roll",  "MainArea", Palette.SeriesRoll);
                    AddSeries("Pitch", "MainArea", Palette.SeriesPitch);

                    var heaveSeries = AddSeries("Heave", "MainArea", Palette.SeriesHeave);
                    heaveSeries.YAxisType = AxisType.Secondary;

                    var area = _chart.ChartAreas["MainArea"];
                    area.AxisY.Title          = "Roll / Pitch (°)";
                    area.AxisY.TitleForeColor = Palette.TextLabel;
                    area.AxisY.TitleFont      = new Font("Segoe UI", 11f, FontStyle.Bold);
                    area.AxisY2.Enabled                 = AxisEnabled.True;
                    area.AxisY2.Title                   = "Heave (cm)";
                    area.AxisY2.TitleForeColor          = Palette.SeriesHeave;
                    area.AxisY2.TitleFont               = new Font("Segoe UI", 11f, FontStyle.Bold);
                    area.AxisY2.LabelStyle.ForeColor    = Palette.SeriesHeave;
                    area.AxisY2.LabelStyle.Font         = new Font("Segoe UI", 9f, FontStyle.Bold);
                    area.AxisY2.LineColor               = Palette.SeriesHeave;
                    area.AxisY2.MajorTickMark.LineColor = Palette.SeriesHeave;
                    area.AxisY2.MajorGrid.LineColor     = Color.Transparent;
                }
                else
                {
                    AddSeries("WindSpeed", "MainArea", Palette.SeriesWSpeed);
                    var dirSeries = AddSeries("WindDir", "MainArea", Palette.SeriesWDir);
                    dirSeries.YAxisType = AxisType.Secondary;

                    var area = _chart.ChartAreas["MainArea"];
                    area.AxisY.Title          = "Wind Speed (m/s)";
                    area.AxisY.TitleForeColor = Palette.SeriesWSpeed;
                    area.AxisY.TitleFont      = new Font("Segoe UI", 11f, FontStyle.Bold);
                    area.AxisY2.Enabled                 = AxisEnabled.True;
                    area.AxisY2.Title                   = "Direction (°)";
                    area.AxisY2.TitleForeColor          = Palette.SeriesWDir;
                    area.AxisY2.TitleFont               = new Font("Segoe UI", 11f, FontStyle.Bold);
                    area.AxisY2.LabelStyle.ForeColor    = Palette.SeriesWDir;
                    area.AxisY2.LabelStyle.Font         = new Font("Segoe UI", 9f, FontStyle.Bold);
                    area.AxisY2.Minimum                 = 0;
                    area.AxisY2.Maximum                 = 360;
                    area.AxisY2.Interval                = 90;
                    area.AxisY2.LineColor               = Palette.SeriesWDir;
                    area.AxisY2.MajorTickMark.LineColor = Palette.SeriesWDir;
                    area.AxisY2.MajorGrid.LineColor     = Color.Transparent;
                }
            }
        }

        private void AddArea(string name)
        {
            var area = new ChartArea(name)
            {
                BackColor        = Palette.ChartBg,
                BorderColor      = Palette.BorderPanel,
                BorderDashStyle  = ChartDashStyle.Solid,
                BorderWidth      = 1
            };

            area.AxisX.LabelStyle.Format      = "HH:mm:ss";
            area.AxisX.LabelStyle.ForeColor   = Palette.TextLabel;
            area.AxisX.LabelStyle.Font        = new Font("Segoe UI", 9f, FontStyle.Bold);
            area.AxisX.LineColor              = Palette.BorderPanel;
            area.AxisX.MajorTickMark.LineColor = Palette.BorderCard;
            area.AxisX.MajorGrid.LineColor    = Palette.GridLine;
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;

            area.AxisY.LabelStyle.ForeColor   = Palette.TextLabel;
            area.AxisY.LabelStyle.Font        = new Font("Segoe UI", 9f, FontStyle.Bold);
            area.AxisY.TitleFont              = new Font("Segoe UI", 12f, FontStyle.Bold);
            area.AxisY.LineColor              = Palette.BorderPanel;
            area.AxisY.MajorTickMark.LineColor = Palette.BorderCard;
            area.AxisY.MajorGrid.LineColor    = Palette.GridLine;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;

            // Tắt built-in cursor — tự vẽ trong PostPaint để tránh double-repaint gây giật
            area.CursorX.IsUserEnabled = false;
            area.CursorY.IsUserEnabled = false;

            area.AxisX.ScrollBar.Enabled      = true;
            area.AxisX.ScrollBar.BackColor    = Palette.PanelBg;
            area.AxisX.ScrollBar.ButtonColor  = Palette.SurfaceHi;
            area.AxisX.ScrollBar.LineColor    = Palette.BorderCard;
            area.AxisX.ScaleView.Zoomable     = true;

            _chart.ChartAreas.Add(area);
        }

        private Series AddSeries(string name, string areaName, Color color)
        {
            var s = new Series(name)
            {
                ChartType    = SeriesChartType.FastLine,
                BorderWidth  = 2,
                XValueType   = ChartValueType.DateTime,
                ChartArea    = areaName,
                Color        = color
            };
            _chart.Series.Add(s);
            return s;
        }

        private void AlignAreas(string target, string master)
        {
            _chart.ChartAreas[target].AlignWithChartArea     = master;
            _chart.ChartAreas[target].AlignmentOrientation   = AreaAlignmentOrientations.Vertical;
            _chart.ChartAreas[target].AlignmentStyle         = AreaAlignmentStyles.PlotPosition | AreaAlignmentStyles.AxesView;
        }

        public void PushMotionData(double r, double p, double h)
        {
            lock (_motionBuffer)
            {
                double now = DateTime.Now.ToOADate();
                _motionBuffer.Add(new TrendPoint { X = now, V1 = r, V2 = p, V3 = h });
                if (_motionBuffer.Count > 0 && _motionBuffer[0].X < DateTime.Now.AddMinutes(-BufferMinutes).ToOADate())
                    _motionBuffer.RemoveAt(0);
            }
        }

        public void PushEnvData(double temp, double humidity)
        {
            lock (_envBuffer)
            {
                double now = DateTime.Now.ToOADate();
                _envBuffer.Add(new TrendPoint { X = now, V1 = temp, V2 = humidity });
                if (_envBuffer.Count > 0 && _envBuffer[0].X < DateTime.Now.AddMinutes(-BufferMinutes).ToOADate())
                    _envBuffer.RemoveAt(0);
            }
        }

        public void PushWindData(double speed, double dir)
        {
            lock (_windBuffer)
            {
                double now = DateTime.Now.ToOADate();
                _windBuffer.Add(new TrendPoint { X = now, V1 = speed, V2 = dir });
                if (_windBuffer.Count > 0 && _windBuffer[0].X < DateTime.Now.AddMinutes(-BufferMinutes).ToOADate())
                    _windBuffer.RemoveAt(0);
            }
        }

        public void SetViewWindow(double minutes) { _viewMinutes = minutes; }

        public void Render()
        {
            if (_chart.IsDisposed) return;
            double nowX     = DateTime.Now.ToOADate();
            double viewSize = _viewMinutes / 1440.0;
            double minX     = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _chart.SuspendLayout();
            if (_currentMode == TrendMode.Motion)
            {
                UpdateSeries("Roll",  _motionBuffer, 1);
                UpdateSeries("Pitch", _motionBuffer, 2);
                UpdateSeries("Heave", _motionBuffer, 3);
            }
            else if (_currentMode == TrendMode.Env)
            {
                UpdateSeries("Temp",     _envBuffer, 1);
                UpdateSeries("Humidity", _envBuffer, 2);
            }
            else
            {
                UpdateSeries("WindSpeed", _windBuffer, 1);
                UpdateSeries("WindDir",   _windBuffer, 2);
            }

            foreach (var area in _chart.ChartAreas)
            {
                area.AxisX.Minimum        = minX;
                area.AxisX.Maximum        = nowX;
                area.AxisX.ScaleView.Size = viewSize;

                if (_isLiveMode)
                {
                    _isProgrammaticScroll = true;
                    area.AxisX.ScaleView.Position = Math.Max(minX, nowX - viewSize);
                    _isProgrammaticScroll = false;
                }
            }
            _chart.ResumeLayout();
        }

        private void UpdateSeries(string name, List<TrendPoint> buffer, int valIdx)
        {
            var s = _chart.Series.FindByName(name);
            if (s == null) return;
            TrendPoint[] data; lock (buffer) { data = buffer.ToArray(); }
            s.Points.Clear();
            foreach (var p in data) s.Points.AddXY(p.X, valIdx == 1 ? p.V1 : valIdx == 2 ? p.V2 : p.V3);
        }

        private TrendPoint GetClosestPoint(List<TrendPoint> buffer, double targetX)
        {
            TrendPoint best = new TrendPoint { X = 0 };
            double minDiff = double.MaxValue;
            lock (buffer)
            {
                foreach (var pt in buffer)
                {
                    double diff = Math.Abs(pt.X - targetX);
                    if (diff < minDiff) { minDiff = diff; best = pt; }
                }
            }
            if (minDiff < (5.0 / 86400.0)) return best;
            return new TrendPoint { X = 0 };
        }

        // MouseMove chỉ lưu trạng thái, không gọi Invalidate — Render() 100ms sẽ kích repaint
        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            if (_chart.ChartAreas.Count == 0) return;
            try
            {
                ChartArea area = _chart.ChartAreas[0];
                double xVal = area.AxisX.PixelPositionToValue(e.X);
                _hoverXValue = xVal;

                string tip = "";
                bool hasData = false;

                if (_currentMode == TrendMode.Motion)
                {
                    TrendPoint pt = GetClosestPoint(_motionBuffer, xVal);
                    if (pt.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(pt.X):HH:mm:ss}\nRoll: {pt.V1:0.00}°\nPitch: {pt.V2:0.00}°\nHeave: {pt.V3:0.0} cm";
                        hasData = true;
                    }
                }
                else if (_currentMode == TrendMode.Env)
                {
                    TrendPoint pt = GetClosestPoint(_envBuffer, xVal);
                    if (pt.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(pt.X):HH:mm:ss}\nTemp: {pt.V1:0.0} °C\nHumidity: {pt.V2:0.0} %";
                        hasData = true;
                    }
                }
                else
                {
                    TrendPoint pt = GetClosestPoint(_windBuffer, xVal);
                    if (pt.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(pt.X):HH:mm:ss}\nWind: {pt.V1:0.0} m/s\nDir: {pt.V2:0}°";
                        hasData = true;
                    }
                }

                _hoverText  = hasData ? tip : "";
                _hoverPoint = e.Location;
            }
            catch { _hoverText = ""; }
        }

        private void Chart_MouseLeave(object sender, EventArgs e)
        {
            _hoverText   = "";
            _hoverXValue = double.NaN;
        }

        // PostPaint vẽ cả cursor line lẫn tooltip — một đường render duy nhất, không chồng lấn
        private void Chart_PostPaint(object sender, ChartPaintEventArgs e)
        {
            Graphics g = e.ChartGraphics.Graphics;

            // Vẽ đường cursor dọc trên tất cả các area
            if (!double.IsNaN(_hoverXValue))
            {
                using var linePen = new Pen(Color.FromArgb(160, Palette.TextLabel), 1)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
                };
                foreach (var area in _chart.ChartAreas)
                {
                    try
                    {
                        float px = (float)area.AxisX.ValueToPixelPosition(_hoverXValue);
                        if (px >= 0 && px <= _chart.Width)
                            g.DrawLine(linePen, px, 0, px, _chart.Height);
                    }
                    catch { }
                }
            }

            // Vẽ tooltip
            if (!string.IsNullOrEmpty(_hoverText))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var f      = new Font("Segoe UI", 10, FontStyle.Bold);
                using var bgBrush = new SolidBrush(Color.FromArgb(220, Palette.CardBg.R, Palette.CardBg.G, Palette.CardBg.B));
                using var border  = new Pen(Palette.BorderCard, 1);
                using var fgBrush = new SolidBrush(Palette.TextValue);

                SizeF size = g.MeasureString(_hoverText, f);
                float x = _hoverPoint.X + 15;
                float y = _hoverPoint.Y + 15;
                if (x + size.Width  + 10 > _chart.Width)  x = _chart.Width  - size.Width  - 15;
                if (y + size.Height + 10 > _chart.Height) y = _chart.Height - size.Height - 15;

                var rect = new RectangleF(x, y, size.Width + 10, size.Height + 10);
                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(_hoverText, f, fgBrush, rect.X + 5, rect.Y + 5);
            }
        }

        private void SetAreaYTitle(string areaName, string title, Color color,
                                    double min = double.NaN, double max = double.NaN, double interval = double.NaN)
        {
            var a = _chart.ChartAreas[areaName];
            a.AxisY.Title          = title;
            a.AxisY.TitleForeColor = color;
            a.AxisY.TitleFont      = new Font("Segoe UI", 11f, FontStyle.Bold);
            a.AxisY.LabelStyle.ForeColor = color;
            a.AxisY.LabelStyle.Font      = new Font("Segoe UI", 9f, FontStyle.Bold);
            a.AxisY.LineColor            = color;
            a.AxisY.MajorTickMark.LineColor = color;
            if (!double.IsNaN(min))      a.AxisY.Minimum  = min;
            if (!double.IsNaN(max))      a.AxisY.Maximum  = max;
            if (!double.IsNaN(interval)) a.AxisY.Interval = interval;
        }

        private void Chart_AxisViewChanged(object sender, ViewEventArgs e)
        {
            if (!_isProgrammaticScroll && _chart.ChartAreas.Count > 0)
            {
                ChartArea area = _chart.ChartAreas[0];
                double max     = area.AxisX.Maximum;
                double viewEnd = area.AxisX.ScaleView.Position + area.AxisX.ScaleView.Size;
                _isLiveMode    = Math.Abs(max - viewEnd) < (5.0 / 86400.0);
            }
        }
    }
}
