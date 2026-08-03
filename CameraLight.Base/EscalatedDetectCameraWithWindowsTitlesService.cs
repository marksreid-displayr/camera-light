using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraLight.Base;

public class EscalatedDetectCameraWithWindowsTitlesService : ICameraDetectionService
{
    private readonly ILogger<EscalatedDetectCameraWithWindowsTitlesService> _logger;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    //[DllImport("wtsapi32.dll")]
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr Token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int TokenType,
        int ImpersonationLevel,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    private readonly bool _includeInvisibleWindows;
    public Regex[] RegularExpressions { get; set; }

    public EscalatedDetectCameraWithWindowsTitlesService(IOptions<WindowDetectionOptions> options, ILogger<EscalatedDetectCameraWithWindowsTitlesService> logger)
    {
        _logger = logger;
        var regularExpressions = options.Value.RegularExpressions ?? throw new Exception("RegularExpressions is required");
        _includeInvisibleWindows = options.Value.IncludeInvisibleWindows ?? false;
        RegularExpressions = regularExpressions.Select(r => new Regex(r, RegexOptions.Compiled)).ToArray();
    }

    public Task<bool> IsActive()
    {

        var userToken = GetActiveUserToken();
        if (userToken == IntPtr.Zero) return Task.FromResult(false);
        if (!ImpersonateLoggedOnUser(userToken))
        {
            _logger.LogError("Failed to impersonate user");
            return Task.FromResult(false);
        }
        var isActive = false;
        try
        {
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
                _logger.LogInformation("Window title {title}", builder);
                if (!RegularExpressions.Any(regularExpression => regularExpression.IsMatch(windowTitle)))
                {
                    return true;
                }
                isActive = true;
                return false;
            }, IntPtr.Zero);
        }
        finally
        {
            RevertToSelf(); // Stop impersonating
        }

        return Task.FromResult(isActive);
    }

    private IntPtr GetActiveUserToken()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        _logger.LogInformation("SessionId = {sessionId}",sessionId);
        if (sessionId == 0xFFFFFFFF)
        {
            _logger.LogInformation("No active session found.");
            return IntPtr.Zero;
        }

        if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            _logger.LogError("Failed to get user token.");
            return IntPtr.Zero;
        }

        if (DuplicateTokenEx(userToken, 0xF01FF, IntPtr.Zero, 2, 1, out var duplicatedToken)) return duplicatedToken;
        _logger.LogError("Failed to duplicate token.");
        return IntPtr.Zero;

    }
}
