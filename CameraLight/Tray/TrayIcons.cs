using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CameraLight.Base;

namespace CameraLight.Tray;

/// <summary>
/// The four tray icons, drawn at startup rather than shipped as .ico files so there is nothing to
/// keep in step with the code.
/// </summary>
public sealed class TrayIcons : IDisposable
{
    private static readonly Color OffColour = Color.FromArgb(120, 124, 130);
    private static readonly Color OnColour = Color.FromArgb(214, 44, 44);
    private static readonly Color PausedColour = Color.FromArgb(90, 94, 100);
    private static readonly Color ErrorColour = Color.FromArgb(232, 160, 30);

    private readonly List<IntPtr> _handles = [];

    public TrayIcons()
    {
        Off = Create(OffColour, slash: false);
        On = Create(OnColour, slash: false);
        Paused = Create(PausedColour, slash: true);
        Error = Create(ErrorColour, slash: false);
    }

    public Icon Off { get; }
    public Icon On { get; }
    public Icon Paused { get; }
    public Icon Error { get; }

    /// <summary>Amber beats everything: an unreachable light is the state worth noticing.</summary>
    public Icon For(LightStatus status) => status switch
    {
        { IsFailing: true } => Error,
        { ForcedOff: true } => Paused,
        { Applied: State.On } => On,
        _ => Off
    };

    private Icon Create(Color fill, bool slash)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var circle = new Rectangle(3, 3, size - 7, size - 7);
            using var brush = new SolidBrush(fill);
            graphics.FillEllipse(brush, circle);
            using var outline = new Pen(Color.FromArgb(180, 20, 20, 20), 2f);
            graphics.DrawEllipse(outline, circle);

            if (slash)
            {
                using var pen = new Pen(Color.FromArgb(235, 235, 235), 4f);
                graphics.DrawLine(pen, circle.Left + 4, circle.Bottom - 4, circle.Right - 4, circle.Top + 4);
            }
        }

        var handle = bitmap.GetHicon();
        _handles.Add(handle);
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        Off.Dispose();
        On.Dispose();
        Paused.Dispose();
        Error.Dispose();

        // Icon.FromHandle does not take ownership, so the icon handles have to go back by hand.
        foreach (var handle in _handles)
        {
            DestroyIcon(handle);
        }
        _handles.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
