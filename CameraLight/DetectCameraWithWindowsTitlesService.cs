using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CameraLight;

public class DetectCameraWithWindowsTitlesService : ICameraDetectionService
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private readonly bool _includeInvisibleWindows;

    public DetectCameraWithWindowsTitlesService(IOptions<WindowDetectionOptions> options)
    {
        var regularExpressions = options.Value.RegularExpressions ?? throw new Exception("RegularExpressions is required");
        _includeInvisibleWindows = options.Value.IncludeInvisibleWindows ?? false;
        RegularExpressions = regularExpressions.Select(r => new Regex(r, RegexOptions.Compiled)).ToArray();
    }

    public Regex[] RegularExpressions { get; set; }

    public Task<bool> IsActive()
    {
        var isActive = false;
        EnumWindows(delegate (IntPtr wnd, IntPtr param)
        {
            if (!_includeInvisibleWindows && !IsWindowVisible(wnd))
            {
                return true;
            }
            var size = GetWindowTextLength(wnd);
            if (size++ <= 0) return true; // Increment size to account for terminating null character
            StringBuilder builder = new(size);
            var result = GetWindowText(wnd, builder, size);
            if (result == 0)
            {
                return true;
            }
            var windowTitle = builder.ToString();
            if (!RegularExpressions.Any(regularExpression => regularExpression.IsMatch(windowTitle)))
            {
                return true;
            }
            isActive = true;
            return false;
        }, IntPtr.Zero);
        return Task.FromResult(isActive);
    }
}