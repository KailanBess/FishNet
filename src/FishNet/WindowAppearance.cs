using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FishNet;

public sealed partial class MainWindow
{
    private const int DwmBorderColor = 34;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, uint size);

    private void ApplyAppearance()
    {
        var accent = new SolidColorBrush(appearance.Accent);
        Wordmark.Foreground = accent;
        MediaSurface.Background = appearance.SurfaceBrush;
        Root.Background = appearance.PageBrush;

        var border = appearance.BorderColor;
        uint color = border is { } rgb
            ? (uint)(rgb.R | (rgb.G << 8) | (rgb.B << 16))
            : appearance.HideBorder ? 0xFFFFFFFEu : 0xFFFFFFFFu;
        var applied = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this),
            DwmBorderColor, ref color, sizeof(uint)) == 0;
        Root.BorderBrush = border is { } fallback ? new SolidColorBrush(fallback) : null;
        Root.BorderThickness = new Thickness(!applied && border.HasValue ? 2 : 0);
    }
}
