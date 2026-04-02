using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HelideckVer2.UI.Controls
{
    /// <summary>
    /// Control Radar độc lập. Tự quản lý việc vẽ đồ họa 2D.
    /// </summary>
    public class RadarControl : UserControl
    {
        private double _heading = 0;
        private double _windDir = 0;
        private readonly PointF[] _shipPoly = { new PointF(0, -100), new PointF(30, -30), new PointF(30, 90), new PointF(-30, 90), new PointF(-30, -30) };

        public RadarControl()
        {
            this.DoubleBuffered = true; // Bật chống giật hình (Flickering)
            this.BackColor = Color.FromArgb(240, 240, 240);
            this.Resize += (s, e) => this.Invalidate(); // Tự vẽ lại khi thay đổi kích thước
        }

        // Cổng giao tiếp duy nhất để Form1 ném dữ liệu vào
        public void UpdateRadar(double heading, double windDir)
        {
            _heading = heading;
            _windDir = windDir;
            this.Invalidate(); // Ra lệnh vẽ lại với dữ liệu mới
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = this.Width, h = this.Height;
            Point center = new Point(w / 2, h / 2);

            int radius = (int)(Math.Min(w, h) / 2 * 0.75f);
            if (radius < 20) return;

            using (SolidBrush bgBrush = new SolidBrush(Color.White))
                g.FillEllipse(bgBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (Pen pBorder = new Pen(Color.Gray, 2))
                g.DrawEllipse(pBorder, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            float radarFontSize = Math.Max(7f, radius * 0.08f);

            // --- VẼ LƯỚI VÀ SỐ ĐỘ ---
            using (Pen pGrid = new Pen(Color.FromArgb(180, 200, 220), 1) { DashStyle = DashStyle.Dot })
            using (Pen pMain = new Pen(Color.LightSlateGray, 2))
            using (Font font = new Font("Segoe UI", radarFontSize, FontStyle.Bold))
            using (Font fontN = new Font("Segoe UI", radarFontSize * 1.5f, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.DarkSlateGray))
            {
                g.DrawEllipse(pGrid, center.X - radius * 0.66f, center.Y - radius * 0.66f, radius * 1.33f, radius * 1.33f);
                g.DrawEllipse(pGrid, center.X - radius * 0.33f, center.Y - radius * 0.33f, radius * 0.66f, radius * 0.66f);
                g.DrawLine(pGrid, center.X - radius, center.Y, center.X + radius, center.Y);
                g.DrawLine(pGrid, center.X, center.Y - radius, center.X, center.Y + radius);

                for (int i = 0; i < 360; i += 30)
                {
                    double rad = (i - 90) * Math.PI / 180.0;
                    int len = (i % 90 == 0) ? 15 : 8;
                    float x1 = center.X + (float)(Math.Cos(rad) * radius);
                    float y1 = center.Y + (float)(Math.Sin(rad) * radius);
                    float x2 = center.X + (float)(Math.Cos(rad) * (radius - len));
                    float y2 = center.Y + (float)(Math.Sin(rad) * (radius - len));
                    g.DrawLine((i % 90 == 0) ? pMain : pGrid, x1, y1, x2, y2);

                    if (i != 0)
                    {
                        string txt = i.ToString("000");
                        float xT = center.X + (float)(Math.Cos(rad) * (radius + radarFontSize * 1.4f));
                        float yT = center.Y + (float)(Math.Sin(rad) * (radius + radarFontSize * 1.4f));

                        var stateText = g.Save();
                        g.TranslateTransform(xT, yT);
                        SizeF s = g.MeasureString(txt, font);
                        g.DrawString(txt, font, brush, -s.Width / 2, -s.Height / 2);
                        g.Restore(stateText);
                    }
                }
                SizeF sizeN = g.MeasureString("N", fontN);
                g.DrawString("N", fontN, Brushes.DarkBlue, center.X - (sizeN.Width / 2), center.Y - radius - (radarFontSize * 1.8f) - (sizeN.Height / 2));
            }

            // --- 1. VẼ TÀU ---
            var stateShip = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_heading);

            using (Pen pHead = new Pen(Color.DarkGoldenrod, 2) { DashStyle = DashStyle.Dash })
                g.DrawLine(pHead, 0, 0, 0, -radius + 15);

            float shipScale = radius / 380f;
            g.ScaleTransform(shipScale, shipScale);

            using (SolidBrush bShip = new SolidBrush(Color.LightGray)) g.FillPolygon(bShip, _shipPoly);
            using (Pen pShipBorder = new Pen(Color.DimGray, 2)) g.DrawPolygon(pShipBorder, _shipPoly);
            g.Restore(stateShip);

            // --- 2. VẼ MŨI TÊN GIÓ ---
            var stateWind = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform((float)_windDir + 180);

            float arrowTipY = radius * 0.75f;
            float arrowBaseY = radius * 0.35f;
            float headLen = radius * 0.15f;
            float headWidth = radius * 0.05f;

            float tailThickness = Math.Max(2f, radius * 0.02f);
            using (Pen pWindTail = new Pen(Color.RoyalBlue, tailThickness))
                g.DrawLine(pWindTail, 0, -arrowBaseY, 0, -arrowTipY + headLen);
            using (Pen pWindTailBorder = new Pen(Color.White, 1) { DashStyle = DashStyle.Dot })
                g.DrawLine(pWindTailBorder, 0, -arrowBaseY, 0, -arrowTipY + headLen);

            PointF[] arrowHead = {
                new PointF(0, -arrowTipY),
                new PointF(-headWidth, -arrowTipY + headLen),
                new PointF(headWidth, -arrowTipY + headLen)
            };

            using (SolidBrush bWind = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(bWind, arrowHead);
            using (Pen pBorderWind = new Pen(Color.White, 1)) g.DrawPolygon(pBorderWind, arrowHead);
            g.Restore(stateWind);

            // --- 3. BẢNG CHÚ GIẢI ---
            DrawLegend(g, radarFontSize);
        }

        private void DrawLegend(Graphics g, float radarFontSize)
        {
            using (Font legendFont = new Font("Segoe UI", Math.Max(8f, radarFontSize * 0.9f), FontStyle.Bold))
            {
                float startX = 10f;
                float startY = 10f;
                float lineSpacing = legendFont.Height + 10f;

                var stateLegendShip = g.Save();
                g.TranslateTransform(startX + 15, startY + (legendFont.Height / 2));
                g.ScaleTransform(0.07f, 0.07f);
                using (SolidBrush bLegShip = new SolidBrush(Color.LightGray)) g.FillPolygon(bLegShip, _shipPoly);
                using (Pen pLegShip = new Pen(Color.DimGray, 25)) g.DrawPolygon(pLegShip, _shipPoly);
                g.Restore(stateLegendShip);

                g.DrawString("Ship Heading", legendFont, Brushes.DimGray, startX + 35, startY);

                float windY = startY + lineSpacing;
                var stateLegendWind = g.Save();
                g.TranslateTransform(startX + 15, windY + (legendFont.Height / 2));
                PointF[] miniArrow = { new PointF(0, -7), new PointF(-6, 7), new PointF(6, 7) };
                using (SolidBrush bLegWind = new SolidBrush(Color.RoyalBlue)) g.FillPolygon(bLegWind, miniArrow);
                g.Restore(stateLegendWind);

                g.DrawString("Wind Direction", legendFont, Brushes.RoyalBlue, startX + 35, windY);
            }
        }
    }
}