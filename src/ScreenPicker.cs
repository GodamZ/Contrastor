using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

internal sealed class ScreenPicker : IDisposable
{
    private const int PixelRadius = 5;
    private const int PixelSize = 11;
    internal const int LensRadius = 60;
    private readonly List<PickerOverlay> overlays = new List<PickerOverlay>();
    private readonly Timer timer = new Timer { Interval = 16 };
    private readonly Action<Color?> completed;
    private Point? position;
    private bool cursorHidden;
    private bool disposed;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPhysicalCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    private static void CaptureDisplay(Bitmap image, Rectangle bounds)
    {
        const uint sourceCopy = 0x00CC0020;
        const uint captureLayeredWindows = 0x40000000;
        IntPtr source = GetDC(IntPtr.Zero);
        if (source == IntPtr.Zero)
            throw new InvalidOperationException("Le bureau Windows est inaccessible.");
        try
        {
            using (Graphics graphics = Graphics.FromImage(image))
            {
                IntPtr destination = graphics.GetHdc();
                try
                {
                    if (!BitBlt(destination, 0, 0, bounds.Width, bounds.Height, source,
                        bounds.X, bounds.Y, sourceCopy | captureLayeredWindows))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    graphics.ReleaseHdc(destination);
                }
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, source);
        }
    }

    internal ScreenPicker(Action<Color?> completed)
    {
        this.completed = completed;
        timer.Tick += delegate { TrackCursor(); };
    }

    internal void Show()
    {
        // Capture all displays before showing any overlay.
        foreach (Screen screen in Screen.AllScreens)
        {
            Rectangle bounds = screen.Bounds;
            var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            try
            {
                CaptureDisplay(image, bounds);
                overlays.Add(new PickerOverlay(this, bounds, image));
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
        if (overlays.Count == 0)
            throw new InvalidOperationException("Aucun écran disponible.");

        TrackCursor();
        PickerOverlay initial = position.HasValue ? FindOverlay(position.Value) : overlays[0];
        foreach (PickerOverlay overlay in overlays)
            if (overlay != initial)
                overlay.Show();
        initial.Show();
        initial.Activate();

        Cursor.Hide();
        cursorHidden = true;
        timer.Start();
    }

    private PickerOverlay FindOverlay(Point point)
    {
        foreach (PickerOverlay overlay in overlays)
            if (overlay.ScreenBounds.Contains(point))
                return overlay;
        return null;
    }

    internal Color? Sample(Point point)
    {
        PickerOverlay overlay = FindOverlay(point);
        if (overlay == null)
            return null;
        return overlay.Image.GetPixel(point.X - overlay.ScreenBounds.X, point.Y - overlay.ScreenBounds.Y);
    }

    private void TrackCursor()
    {
        Point current;
        Point? previous = position;
        position = GetPhysicalCursorPos(out current) && FindOverlay(current) != null ? current : (Point?)null;
        if (previous == position)
            return;
        foreach (PickerOverlay overlay in overlays)
        {
            if (previous.HasValue)
                overlay.InvalidateLens(previous.Value);
            if (position.HasValue)
                overlay.InvalidateLens(position.Value);
            overlay.PositionInstructions(position);
        }
    }

    internal void DrawLens(Graphics graphics, Rectangle bounds)
    {
        if (!position.HasValue)
            return;

        Point pixel = position.Value;
        int x = pixel.X - bounds.X;
        int y = pixel.Y - bounds.Y;
        var circle = new Rectangle(x - LensRadius, y - LensRadius, LensRadius * 2 + 1, LensRadius * 2 + 1);

        GraphicsState state = graphics.Save();
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(Color.Black))
        {
            path.AddEllipse(circle);
            graphics.SetClip(path, CombineMode.Intersect);
            graphics.FillRectangle(SystemBrushes.ControlDarkDark, circle);
            for (int dy = -PixelRadius; dy <= PixelRadius; dy++)
            {
                for (int dx = -PixelRadius; dx <= PixelRadius; dx++)
                {
                    Color? color = Sample(new Point(pixel.X + dx, pixel.Y + dy));
                    if (!color.HasValue)
                        continue;
                    brush.Color = color.Value;
                    graphics.FillRectangle(brush, x + dx * PixelSize - PixelSize / 2,
                        y + dy * PixelSize - PixelSize / 2, PixelSize, PixelSize);
                }
            }
        }
        graphics.Restore(state);

        state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var rim = new Pen(Color.Black, 4))
            graphics.DrawEllipse(rim, circle);
        graphics.DrawEllipse(Pens.White, circle);
        graphics.Restore(state);
        graphics.DrawRectangle(Pens.Black, x - 6, y - 6, 12, 12);
        graphics.DrawRectangle(Pens.White, x - 5, y - 5, 10, 10);
    }

    internal bool HandleKey(Keys key, PickerOverlay activeOverlay)
    {
        if (key == Keys.Escape)
        {
            Finish(null);
            return true;
        }
        Point current;
        if (key == Keys.Space)
        {
            Color? color = GetPhysicalCursorPos(out current) ? Sample(current) : null;
            activeOverlay.AnnouncePixel(color.HasValue ? string.Format("Couleur {0}, X {1}, Y {2}.",
                Contrast.Hex(color.Value), current.X, current.Y) : "Pixel indisponible.");
            return true;
        }
        if (key == Keys.Enter)
        {
            if (GetPhysicalCursorPos(out current))
                Choose(current);
            return true;
        }

        int dx = 0;
        int dy = 0;
        switch (key)
        {
            case Keys.Left: dx = -1; break;
            case Keys.Right: dx = 1; break;
            case Keys.Up: dy = -1; break;
            case Keys.Down: dy = 1; break;
            default: return false;
        }

        if (GetPhysicalCursorPos(out current))
        {
            var next = new Point(current.X + dx, current.Y + dy);
            if (FindOverlay(next) != null)
                SetPhysicalCursorPos(next.X, next.Y);
            TrackCursor();
        }
        return true;
    }

    internal void Choose(Point point)
    {
        Color? color = Sample(point);
        if (color.HasValue)
            Finish(color);
    }

    internal void Finish(Color? color)
    {
        if (disposed)
            return;
        Dispose();
        completed(color);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Dispose();
        if (cursorHidden)
            Cursor.Show();
        foreach (PickerOverlay overlay in overlays)
            overlay.Dispose();
        overlays.Clear();
    }
}

internal sealed class PickerOverlay : Form
{
    private readonly ScreenPicker picker;
    internal Rectangle ScreenBounds { get; private set; }
    internal readonly Bitmap Image;
    private readonly Label instructions = new Label
    {
        AutoSize = true, BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText,
        Padding = new Padding(8), LiveSetting = AutomationLiveSetting.Polite,
        Text = "Flèches pour déplacer · Espace pour lire · Entrée pour choisir · Échap pour annuler"
    };

    internal PickerOverlay(ScreenPicker picker, Rectangle bounds, Bitmap image)
    {
        this.picker = picker;
        this.ScreenBounds = bounds;
        this.Image = image;
        Text = "Pipette Contrastor. Flèches pour déplacer, Espace pour lire, Entrée pour choisir, Échap pour annuler.";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Bounds = bounds;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Opaque, true);
        Controls.Add(instructions);
        instructions.MouseUp += delegate(object sender, MouseEventArgs e)
        {
            HandlePointer(e.Button, instructions.PointToScreen(e.Location));
        };
        instructions.SizeChanged += delegate { PositionInstructions(lastPosition); };
        PositionInstructions(null);
    }

    private Point? lastPosition;

    internal void PositionInstructions(Point? center)
    {
        lastPosition = center;
        int offset = center.HasValue ? center.Value.Y - ScreenBounds.Y : 0;
        bool nearTop = center.HasValue && ScreenBounds.Contains(center.Value) &&
            offset < instructions.Height + ScreenPicker.LensRadius + 12;
        instructions.Location = new Point(12, nearTop ? Math.Max(12, ClientSize.Height - instructions.Height - 12) : 12);
    }

    internal void AnnouncePixel(string message)
    {
        if (instructions.Text == message)
            ((Control.ControlAccessibleObject)instructions.AccessibilityObject).RaiseLiveRegionChanged();
        else
            instructions.Text = message;
    }

    internal void InvalidateLens(Point center)
    {
        int margin = ScreenPicker.LensRadius + 4;
        var area = new Rectangle(center.X - ScreenBounds.X - margin,
            center.Y - ScreenBounds.Y - margin, margin * 2 + 1, margin * 2 + 1);
        area.Intersect(ClientRectangle);
        if (!area.IsEmpty)
            Invalidate(area);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(Image, Point.Empty);
        picker.DrawLens(e.Graphics, ScreenBounds);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        HandlePointer(e.Button, PointToScreen(e.Location));
    }

    private void HandlePointer(MouseButtons button, Point point)
    {
        if (button == MouseButtons.Left)
            picker.Choose(point);
        else if (button == MouseButtons.Right)
            picker.Finish(null);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        return picker.HandleKey(key, this) || base.ProcessCmdKey(ref message, key);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            BeginInvoke((Action)delegate { picker.Finish(null); });
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Image.Dispose();
        base.Dispose(disposing);
    }
}
