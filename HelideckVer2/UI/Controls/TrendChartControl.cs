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

        private string _hoverText = "";
        private Point _hoverPoint = Point.Empty;

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

            if (_isSeparateTrend)
            {
                if (mode == TrendMode.Motion)
                {
                    AddArea("AreaRoll"); AddArea("AreaPitch"); AddArea("AreaHeave");
                    AddSeries("Roll",  "AreaRoll",  Palette.SeriesRoll);
                    AddSeries("Pitch", "AreaPitch", Palette.SeriesPitch);
                    AddSeries("Heave", "AreaHeave", Palette.SeriesHeave);
                    AlignAreas("AreaPitch", "AreaRoll"); AlignAreas("AreaHeave", "AreaRoll");
                }
                else
                {
                    AddArea("AreaWindSpeed"); AddArea("AreaWindDir");
                    AddSeries("WindSpeed", "AreaWindSpeed", Palette.SeriesWSpeed);
                    AddSeries("WindDir",   "AreaWindDir",   Palette.SeriesWDir);
                    AlignAreas("AreaWindDir", "AreaWindSpeed");
                }
            }
            else if (mode == TrendMode.Env)
            {
                AddArea("MainArea");
                AddSeries("Temp", "MainArea", Palette.SeriesRoll);
                var humSeries = AddSeries("Humidity", "MainArea", Palette.SeriesWSpeed);
                humSeries.YAxisType = AxisType.Secondary;

                var area = _chart.ChartAreas["MainArea"];
                area.AxisY.Title                    = "°C";
                area.AxisY.TitleForeColor           = Palette.SeriesRoll;
                area.AxisY2.Enabled                 = AxisEnabled.True;
                area.AxisY2.Title                   = "%";
                area.AxisY2.TitleForeColor          = Palette.SeriesWSpeed;
                area.AxisY2.LabelStyle.ForeColor    = Palette.SeriesWSpeed;
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

                    // Heave dùng trục Y phụ (bên phải) vì đơn vị cm khác Roll/Pitch (°)
                    var heaveSeries = AddSeries("Heave", "MainArea", Palette.SeriesHeave);
                    heaveSeries.YAxisType = AxisType.Secondary;

                    var area = _chart.ChartAreas["MainArea"];
                    area.AxisY.Title          = "°";
                    area.AxisY.TitleForeColor = Palette.TextLabel;
                    area.AxisY2.Enabled       = AxisEnabled.True;
                    area.AxisY2.Title         = "cm";
                    area.AxisY2.TitleForeColor          = Palette.SeriesHeave;
                    area.AxisY2.LabelStyle.ForeColor    = Palette.SeriesHeave;
                    area.AxisY2.LineColor               = Palette.SeriesHeave;
                    area.AxisY2.MajorTickMark.LineColor = Palette.SeriesHeave;
                    area.AxisY2.MajorGrid.LineColor     = Color.Transparent;
                }
                else
                {
                    AddSeries("WindSpeed", "MainArea", Palette.SeriesWSpeed);
                    AddSeries("WindDir",   "MainArea", Palette.SeriesWDir);
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

            area.AxisX.LabelStyle.Format   = "HH:mm:ss";
            area.AxisX.LabelStyle.ForeColor = Palette.TextLabel;
            area.AxisX.LineColor            = Palette.BorderPanel;
            area.AxisX.MajorTickMark.LineColor = Palette.BorderCard;
            area.AxisX.MajorGrid.LineColor  = Palette.GridLine;
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;

            area.AxisY.LabelStyle.ForeColor = Palette.TextLabel;
            area.AxisY.LineColor            = Palette.BorderPanel;
            area.AxisY.MajorTickMark.LineColor = Palette.BorderCard;
            area.AxisY.MajorGrid.LineColor  = Palette.GridLine;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;

            area.CursorX.IsUserEnabled = true;
            area.CursorX.Interval      = 0;
            area.CursorX.LineColor     = Palette.TextLabel;
            area.CursorY.IsUserEnabled = true;
            area.CursorY.Interval      = 0;

            area.AxisX.ScrollBar.Enabled = true;
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
            double nowX    = DateTime.Now.ToOADate();
            double viewSize = _viewMinutes / 1440.0;
            double minX    = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

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
                area.AxisX.Minimum = minX;
                area.AxisX.Maximum = nowX;
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

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            if (_chart.ChartAreas.Count == 0) return;
            try
            {
                ChartArea area = _chart.ChartAreas[0];
                double xVal = area.AxisX.PixelPositionToValue(e.X);
                area.CursorX.Position = xVal;

                bool hasData = false;
                string tip = "";

                if (_currentMode == TrendMode.Motion)
                {
                    TrendPoint closest = GetClosestPoint(_motionBuffer, xVal);
                    if (closest.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nRoll: {closest.V1:0.00}°\nPitch: {closest.V2:0.00}°\nHeave: {closest.V3:0.0} cm";
                        hasData = true;
                    }
                }
                else if (_currentMode == TrendMode.Env)
                {
                    TrendPoint closest = GetClosestPoint(_envBuffer, xVal);
                    if (closest.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nTemp: {closest.V1:0.0} °C\nHumidity: {closest.V2:0.0} %";
                        hasData = true;
                    }
                }
                else
                {
                    TrendPoint closest = GetClosestPoint(_windBuffer, xVal);
                    if (closest.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nWind: {closest.V1:0.0} m/s\nDir: {closest.V2:0}°";
                        hasData = true;
                    }
                }

                if (hasData) { _hoverText = tip; _hoverPoint = e.Location; }
                else _hoverText = "";
            }
            catch { _hoverText = ""; }
        }

        private void Chart_MouseLeave(object sender, EventArgs e)
        {
            _hoverText = "";
            foreach (var area in _chart.ChartAreas) area.CursorX.Position = double.NaN;
            _chart.Invalidate();
        }

        private void Chart_PostPaint(object sender, ChartPaintEventArgs e)
        {
            if (!string.IsNullOrEmpty(_hoverText))
            {
                Graphics g = e.ChartGraphics.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(220, Palette.CardBg.R, Palette.CardBg.G, Palette.CardBg.B)))
                using (Pen border = new Pen(Palette.BorderCard, 1))
                using (SolidBrush fg = new SolidBrush(Palette.TextValue))
                {
                    SizeF size = g.MeasureString(_hoverText, f);
                    float x = _hoverPoint.X + 15;
                    float y = _hoverPoint.Y + 15;

                    if (x + size.Width + 10 > _chart.Width)  x = _chart.Width  - size.Width  - 15;
                    if (y + size.Height + 10 > _chart.Height) y = _chart.Height - size.Height - 15;

                    RectangleF rect = new RectangleF(x, y, size.Width + 10, size.Height + 10);
                    g.FillRectangle(bg, rect);
                    g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                    g.DrawString(_hoverText, f, fg, rect.X + 5, rect.Y + 5);
                }
            }
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
