using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Graphics;

namespace FiloMediaPlayer.Components
{
    public class ColorWheelDrawable : IDrawable
    {
        public double Hue { get; set; }
        public Color PreviewColor { get; set; } = Colors.Red;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float centerX = dirtyRect.Center.X;
            float centerY = dirtyRect.Center.Y;

            float radius = Math.Min(
                dirtyRect.Width,
                dirtyRect.Height) / 2f - 25f;

            // Draw the rainbow ring
            canvas.StrokeSize = 23;
            canvas.StrokeLineCap = LineCap.Round;

            for (int degree = 0; degree < 360; degree++)
            {
                double angle = degree * Math.PI / 180;

                float x1 = centerX +
                    (float)(Math.Sin(angle) * (radius - 11));

                float y1 = centerY -
                    (float)(Math.Cos(angle) * (radius - 11));

                float x2 = centerX +
                    (float)(Math.Sin(angle) * (radius + 11));

                float y2 = centerY -
                    (float)(Math.Cos(angle) * (radius + 11));

                canvas.StrokeColor = Color.FromHsv(
                    degree / 360f, 1f, 1f);

                canvas.DrawLine(x1, y1, x2, y2);
            }

            // Preview in the center
            canvas.FillColor = PreviewColor;
            canvas.FillCircle(centerX, centerY, radius * 0.62f);

            // Indicate the selected hue
            double selectedAngle = Hue * Math.PI / 180;

            float markerX = centerX +
                (float)(Math.Sin(selectedAngle) * radius);

            float markerY = centerY -
                (float)(Math.Cos(selectedAngle) * radius);

            canvas.FillColor = Colors.White;
            canvas.FillCircle(markerX, markerY, 10);

            canvas.FillColor = Color.FromHsv(
                (float)(Hue / 360), 1f, 1f);

            canvas.FillCircle(markerX, markerY, 7);
        }
    }
}
