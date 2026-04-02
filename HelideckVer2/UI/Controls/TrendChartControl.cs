using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace HelideckVer2.UI.Controls
{
    public partial class TrendChartControl : UserControl
    {
        public struct TrendPoint { public double X, V1, V2, V3; }
        public enum TrendMode { Motion, Wind }

        private Chart _chart;
        private TrendMode _currentMode = TrendMode.Motion;
        private bool _isSeparateTrend = false;
        private double _viewMinutes = 2.0;
        private const int BufferMinutes = 20;
        private bool _isLiveMode = true;
        private bool _isProgrammaticScroll = false;
        private readonly List<TrendPoint> _motionBuffer = new List<TrendPoint>();
        private readonly List<TrendPoint> _windBuffer = new List<TrendPoint>();

        // --- CÁC BIẾN CHO TÍNH NĂNG HOVER CHUỘT ---
        private string _hoverText = "";
        private Point _hoverPoint = Point.Empty;

        public TrendChartControl()
        {
            InitializeComponent();
            SetupChart();
        }

        private void InitializeComponent()
        {
            _chart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            this.Controls.Add(_chart);
        }

        private void SetupChart()
        {
            _chart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Center, Font = new Font("Segoe UI", 10, FontStyle.Bold) });

            _chart.MouseMove += Chart_MouseMove;
            _chart.MouseLeave += Chart_MouseLeave;
            _chart.PostPaint += Chart_PostPaint;

            // BẮT SỰ KIỆN KHI NGƯỜI DÙNG KÉO THANH SCROLL
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
                    AddSeries("Roll", "AreaRoll", Color.Blue);
                    AddSeries("Pitch", "AreaPitch", Color.Orange);
                    AddSeries("Heave", "AreaHeave", Color.Red);
                    AlignAreas("AreaPitch", "AreaRoll"); AlignAreas("AreaHeave", "AreaRoll");
                }
                else
                {
                    AddArea("AreaWindSpeed"); AddArea("AreaWindDir");
                    AddSeries("WindSpeed", "AreaWindSpeed", Color.Blue);
                    AddSeries("WindDir", "AreaWindDir", Color.Orange);
                    AlignAreas("AreaWindDir", "AreaWindSpeed");
                }
            }
            else
            {
                AddArea("MainArea");
                if (mode == TrendMode.Motion)
                {
                    AddSeries("Roll", "MainArea", Color.Blue);
                    AddSeries("Pitch", "MainArea", Color.Orange);
                    AddSeries("Heave", "MainArea", Color.Red);
                }
                else
                {
                    AddSeries("WindSpeed", "MainArea", Color.Blue);
                    AddSeries("WindDir", "MainArea", Color.Orange);
                }
            }
        }

        private void AddArea(string name)
        {
            var area = new ChartArea(name) { BackColor = Color.White };
            area.AxisX.LabelStyle.Format = "HH:mm:ss";
            area.AxisX.MajorGrid.LineColor = Color.LightGray;
            area.AxisX.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.MajorGrid.LineColor = Color.LightGray;

            area.CursorX.IsUserEnabled = true;
            area.CursorX.Interval = 0;
            area.CursorY.IsUserEnabled = true;
            area.CursorY.Interval = 0;

            // BẬT THANH SCROLL VÀ ZOOM TẠI ĐÂY
            area.AxisX.ScrollBar.Enabled = true;
            area.AxisX.ScaleView.Zoomable = true;

            _chart.ChartAreas.Add(area);
        }

        private void AddSeries(string name, string areaName, Color color)
        {
            var s = new Series(name)
            {
                ChartType = SeriesChartType.FastLine,
                BorderWidth = 2,
                XValueType = ChartValueType.DateTime,
                ChartArea = areaName,
                Color = color
            };
            _chart.Series.Add(s);
        }

        private void AlignAreas(string target, string master)
        {
            _chart.ChartAreas[target].AlignWithChartArea = master;
            _chart.ChartAreas[target].AlignmentOrientation = AreaAlignmentOrientations.Vertical;
            _chart.ChartAreas[target].AlignmentStyle = AreaAlignmentStyles.PlotPosition | AreaAlignmentStyles.AxesView;
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
            double nowX = DateTime.Now.ToOADate();
            double viewSize = _viewMinutes / 1440.0;
            double minX = DateTime.Now.AddMinutes(-BufferMinutes).ToOADate();

            _chart.SuspendLayout();
            if (_currentMode == TrendMode.Motion)
            {
                UpdateSeries("Roll", _motionBuffer, 1);
                UpdateSeries("Pitch", _motionBuffer, 2);
                UpdateSeries("Heave", _motionBuffer, 3);
            }
            else
            {
                UpdateSeries("WindSpeed", _windBuffer, 1);
                UpdateSeries("WindDir", _windBuffer, 2);
            }

            foreach (var area in _chart.ChartAreas)
            {
                area.AxisX.Minimum = minX;
                area.AxisX.Maximum = nowX;
                area.AxisX.ScaleView.Size = viewSize;

                // Chỉ tự động cuộn (Auto-scroll) nếu đang ở Live Mode
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

        // ==========================================
        // CÁC HÀM XỬ LÝ SỰ KIỆN HOVER CHUỘT
        // ==========================================

        private TrendPoint GetClosestPoint(List<TrendPoint> buffer, double targetX)
        {
            TrendPoint best = new TrendPoint { X = 0 };
            double minDiff = double.MaxValue;
            lock (buffer)
            {
                foreach (var pt in buffer)
                {
                    double diff = Math.Abs(pt.X - targetX);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        best = pt;
                    }
                }
            }
            // Chỉ hiện thông số nếu chuột nằm gần điểm dữ liệu (sai số dưới 5 giây)
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
                area.CursorX.Position = xVal; // Đường gióng bám theo chuột

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
                else
                {
                    TrendPoint closest = GetClosestPoint(_windBuffer, xVal);
                    if (closest.X != 0)
                    {
                        tip = $"Time: {DateTime.FromOADate(closest.X):HH:mm:ss}\nWind: {closest.V1:0.0} m/s\nDir: {closest.V2:0}°";
                        hasData = true;
                    }
                }

                if (hasData)
                {
                    _hoverText = tip;
                    _hoverPoint = e.Location;
                }
                else
                {
                    _hoverText = "";
                }
            }
            catch { _hoverText = ""; }
        }

        private void Chart_MouseLeave(object sender, EventArgs e)
        {
            _hoverText = "";
            foreach (var area in _chart.ChartAreas) area.CursorX.Position = double.NaN;
            _chart.Invalidate(); // Xóa sạch text và crosshair khi chuột ra ngoài
        }

        private void Chart_PostPaint(object sender, ChartPaintEventArgs e)
        {
            if (!string.IsNullOrEmpty(_hoverText))
            {
                Graphics g = e.ChartGraphics.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                using (Pen border = new Pen(Color.Gray, 1))
                using (SolidBrush fg = new SolidBrush(Color.Black))
                {
                    SizeF size = g.MeasureString(_hoverText, f);
                    float x = _hoverPoint.X + 15;
                    float y = _hoverPoint.Y + 15;

                    // Giữ bảng thông số luôn nằm trong khung hình (Không bị tràn ra ngoài)
                    if (x + size.Width + 10 > _chart.Width) x = _chart.Width - size.Width - 15;
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
            // Kiểm tra xem người dùng đang tự kéo hay hệ thống kéo
            if (!_isProgrammaticScroll && _chart.ChartAreas.Count > 0)
            {
                ChartArea area = _chart.ChartAreas[0];
                double max = area.AxisX.Maximum;
                double viewEnd = area.AxisX.ScaleView.Position + area.AxisX.ScaleView.Size;

                // Nếu người dùng kéo sát về mép phải (cách hiện tại < 5 giây), bật lại tự động cuộn
                _isLiveMode = Math.Abs(max - viewEnd) < (5.0 / 86400.0);
            }
        }
    }
}