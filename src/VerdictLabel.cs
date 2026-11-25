using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class VerdictLabel : Label
{
    private bool? passed;

    internal void SetVerdict(string text, bool? result)
    {
        passed = result;
        Text = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = e.Graphics.DpiX / 96f;
        e.Graphics.ScaleTransform(scale, scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool highContrast = SystemInformation.HighContrast;
        Color fill = highContrast || !passed.HasValue ? SystemColors.WindowText
            : passed.Value ? Color.FromArgb(20, 108, 67) : Color.FromArgb(166, 27, 41);
        Color mark = highContrast || !passed.HasValue ? SystemColors.Window : Color.White;
        using (var brush = new SolidBrush(fill))
        using (var outline = new Pen(SystemColors.Window, 1))
        using (var pen = new Pen(mark, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            e.Graphics.FillEllipse(brush, 1, 2, 16, 16);
            e.Graphics.DrawEllipse(outline, 1, 2, 16, 16);
            if (!passed.HasValue)
                e.Graphics.DrawLine(pen, 6, 10, 12, 10);
            else if (passed.Value)
                e.Graphics.DrawLines(pen, new[] { new Point(5, 10), new Point(8, 13), new Point(13, 7) });
            else
            {
                e.Graphics.DrawLine(pen, 6, 7, 12, 13);
                e.Graphics.DrawLine(pen, 12, 7, 6, 13);
            }
        }
    }
}
