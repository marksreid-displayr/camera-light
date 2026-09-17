using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CameraLight;

internal interface IDisplayIdleBlocker
{
    void SetBlocked(bool blocked);
}

/// <summary>
/// Holds a Windows display power request while the camera is active. A display request prevents
/// idle screen saving, locking, and power-off without preventing the system itself from sleeping.
/// </summary>
internal sealed class DisplayIdleBlocker(ILogger<DisplayIdleBlocker> logger) : IDisplayIdleBlocker, IDisposable
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;
    private const string Reason = "CameraLight detected an active camera session";

    private readonly object _gate = new();

    private SafePowerRequestHandle? _request;
    private bool _blocked;
    private bool _disposed;

    public void SetBlocked(bool blocked)
    {
        lock (_gate)
        {
            if (_disposed || blocked == _blocked)
            {
                return;
            }

            try
            {
                if (blocked)
                {
                    Block();
                }
                else
                {
                    Unblock();
                }
            }
            catch (Exception ex)
            {
                // Power management must never prevent the detector from updating the lights. If
                // this pass fails, _blocked remains unchanged and the next poll retries it.
                logger.LogError(ex, "Could not {Action} display idle while the camera is active",
                    blocked ? "block" : "restore");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_blocked && _request is not null && !PowerClearRequest(_request, PowerRequestType.DisplayRequired))
            {
                LogWin32Error("Could not clear the display power request during shutdown");
            }

            _blocked = false;
            _request?.Dispose();
            _request = null;
            _disposed = true;
        }
    }

    private void Block()
    {
        if (!EnsureRequest())
        {
            return;
        }

        if (!PowerSetRequest(_request!, PowerRequestType.DisplayRequired))
        {
            LogWin32Error("Could not set the display power request");
            return;
        }

        _blocked = true;
        logger.LogInformation("Display idle blocked while the camera is active");
    }

    private void Unblock()
    {
        if (_request is null || !PowerClearRequest(_request, PowerRequestType.DisplayRequired))
        {
            LogWin32Error("Could not clear the display power request");
            return;
        }

        _blocked = false;
        logger.LogInformation("Display idle restored after camera use");
    }

    private bool EnsureRequest()
    {
        if (_request is { IsInvalid: false, IsClosed: false })
        {
            return true;
        }

        _request?.Dispose();

        var reason = Marshal.StringToHGlobalUni(Reason);
        try
        {
            var context = new ReasonContext
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextSimpleString,
                Reason = new ReasonContextUnion { SimpleReasonString = reason }
            };

            _request = PowerCreateRequest(ref context);
        }
        finally
        {
            Marshal.FreeHGlobal(reason);
        }

        if (!_request.IsInvalid)
        {
            return true;
        }

        LogWin32Error("Could not create a Windows power request");
        _request.Dispose();
        _request = null;
        return false;
    }

    private void LogWin32Error(string message)
    {
        var error = Marshal.GetLastWin32Error();
        logger.LogError(new Win32Exception(error), "{Message}", message);
    }

    private enum PowerRequestType
    {
        DisplayRequired = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public ReasonContextUnion Reason;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ReasonContextUnion
    {
        [FieldOffset(0)] public nint SimpleReasonString;
    }

    private sealed class SafePowerRequestHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePowerRequestHandle() : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafePowerRequestHandle PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafePowerRequestHandle powerRequest,
        PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(SafePowerRequestHandle powerRequest,
        PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
