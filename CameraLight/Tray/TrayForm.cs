namespace CameraLight.Tray;

/// <summary>
/// Shared behaviour for the tray's windows: closing hides them so their state and subscriptions
/// survive, and background threads have a safe way to touch them.
/// </summary>
public class TrayForm : Form
{
    protected TrayForm()
    {
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = SystemFonts.MessageBoxFont ?? Font;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Every service event arrives on a background
    /// thread, so nothing may touch a control directly.
    /// </summary>
    protected void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            // The window went away between the check and the post; nothing to update.
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    public void ShowOrActivate()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        BringToFront();
    }
}
