using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BlueBattery;

/// <summary>
/// Zeichnet das Tray-Icon: entweder ein farbiges Feld mit dem Ladezustand als Zahl
/// oder – wenn die Prozentangabe ausgeschaltet ist – ein Batteriesymbol mit Füllstand.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly Color NoDeviceColor = Color.FromArgb(110, 110, 110);

    public static Icon Create(int? level, AppSettings settings)
    {
        // Etwas größer zeichnen als nötig; Windows skaliert passend zur DPI-Einstellung.
        int size = Math.Max(32, SystemInformation.SmallIconSize.Width * 2);
        using var bitmap = CreateBitmap(level, settings, size);

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public static Bitmap CreateBitmap(int? level, AppSettings settings, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        if (settings.ShowPercentOnIcon)
            DrawNumberBadge(g, level, settings, size);
        else
            DrawBatteryGlyph(g, level, settings, size);

        return bitmap;
    }

    public static Color ColorFor(int? level, AppSettings settings)
    {
        if (level is null)
            return NoDeviceColor;
        if (level <= settings.CriticalThreshold)
            return settings.CriticalColorValue;
        if (level <= settings.LowThreshold)
            return settings.LowColorValue;
        return settings.NormalColorValue;
    }

    /// <summary>Schwarz oder Weiß – je nachdem, was auf dem Hintergrund besser lesbar ist.</summary>
    public static Color ContrastTextColor(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.6 ? Color.Black : Color.White;
    }

    private static void DrawNumberBadge(Graphics g, int? level, AppSettings settings, int size)
    {
        var background = ColorFor(level, settings);
        var rect = new RectangleF(0, 0, size - 1, size - 1);
        using (var path = RoundedRect(rect, size * 0.22f))
        using (var brush = new SolidBrush(background))
        {
            g.FillPath(brush, path);
        }

        string text = level?.ToString() ?? "–";
        DrawFittedText(g, text, new RectangleF(0, 0, size, size), ContrastTextColor(background));
    }

    private static void DrawBatteryGlyph(Graphics g, int? level, AppSettings settings, int size)
    {
        Color outline = IsTaskbarLight() ? Color.FromArgb(30, 30, 30) : Color.White;
        float stroke = Math.Max(1.5f, size / 14f);

        // Liegende Batterie mit Pol rechts.
        float bodyW = size * 0.82f;
        float bodyH = size * 0.56f;
        float x = stroke / 2;
        float y = (size - bodyH) / 2;
        var body = new RectangleF(x, y, bodyW - stroke, bodyH);

        using (var pen = new Pen(outline, stroke))
        using (var path = RoundedRect(body, size * 0.08f))
        {
            g.DrawPath(pen, path);
        }

        float nubW = size * 0.1f;
        float nubH = bodyH * 0.45f;
        using (var brush = new SolidBrush(outline))
        {
            g.FillRectangle(brush, body.Right + stroke / 2, (size - nubH) / 2, nubW, nubH);
        }

        if (level is int l && l > 0)
        {
            float pad = stroke * 1.4f;
            var inner = RectangleF.FromLTRB(body.Left + pad, body.Top + pad, body.Right - pad, body.Bottom - pad);
            float w = Math.Max(stroke, inner.Width * l / 100f);
            using var fill = new SolidBrush(ColorFor(level, settings));
            g.FillRectangle(fill, inner.X, inner.Y, w, inner.Height);
        }
    }

    private static void DrawFittedText(Graphics g, string text, RectangleF bounds, Color color)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        float maxWidth = bounds.Width * 0.96f;
        float maxHeight = bounds.Height * 0.9f;
        float fontSize = bounds.Height * 0.8f;

        Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        while (fontSize > 4)
        {
            var measured = g.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width <= maxWidth && measured.Height <= maxHeight)
                break;
            fontSize -= 1;
            font.Dispose();
            font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        using (font)
        using (var brush = new SolidBrush(color))
        {
            g.DrawString(text, font, brush, bounds, format);
        }
    }

    private static bool IsTaskbarLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
