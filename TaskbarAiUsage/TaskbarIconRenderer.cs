using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarAiUsage;

public static class TaskbarIconRenderer
{
    public static BitmapSource Render(int? upper, int? lower, bool darkTheme = false, double scale = 1, int logicalSize = 32)
    {
        Validate(upper);
        Validate(lower);
        if (logicalSize is < 16 or > 256 || !double.IsFinite(scale) || scale < 0.5 || scale > 4096d / logicalSize)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var size = (int)Math.Round(logicalSize * scale);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var background = darkTheme ? Color.FromRgb(35, 35, 35) : Color.FromRgb(250, 250, 250);
            var foreground = darkTheme ? Colors.White : Colors.Black;
            context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, logicalSize, logicalSize));
            context.DrawLine(new Pen(new SolidColorBrush(darkTheme ? Colors.Gray : Colors.DarkGray), logicalSize / 32d), new Point(logicalSize / 16d, logicalSize / 2d), new Point(logicalSize - logicalSize / 16d, logicalSize / 2d));
            DrawNumber(context, upper, 0, logicalSize / 2d, foreground, logicalSize);
            DrawNumber(context, lower, logicalSize / 2d, logicalSize / 2d, foreground, logicalSize);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawNumber(DrawingContext context, int? value, double top, double height, Color color, int logicalSize)
    {
        var text = value?.ToString(CultureInfo.InvariantCulture) ?? "--";
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), logicalSize * (text.Length == 3 ? 0.30 : 0.38),
            new SolidColorBrush(color), 1);
        context.DrawText(formatted, new Point((logicalSize - formatted.Width) / 2d, top + (height - formatted.Height) / 2d - logicalSize / 32d));
    }

    private static void Validate(int? value)
    {
        if (value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Percentage must be from 0 to 100.");
    }
}
