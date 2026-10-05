using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClassIsland.Helpers;

internal static class DpiHelper
{
    public static void GetCurrentDpi(out double dpiX, out double dpiY, Visual? visual = null)
    {
        dpiX = 1.0;
        dpiY = 1.0;
        try
        {
            if (visual != null)
            {
                var source = PresentationSource.FromVisual(visual);
                if (source?.CompositionTarget != null)
                {
                    dpiX = 1.0 * source.CompositionTarget.TransformToDevice.M11;
                    dpiY = 1.0 * source.CompositionTarget.TransformToDevice.M22;
                    return;
                }
            }

            var dpi = GetDpiForSystem();
            dpiX = dpi / 96.0;
            dpiY = dpi / 96.0;
        }
        catch
        {
            dpiX = 1.0;
            dpiY = 1.0;
        }
    }
}
