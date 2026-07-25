using System.Windows;
using System.Windows.Interop;

namespace MeetingAudioRecorder.App;

/// <summary>
/// Niewidoczne okno hostujące pętlę komunikatów Win32 (RegisterHotKey).
/// </summary>
public sealed class HostWindow : Window
{
    public HostWindow()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Opacity = 0;
        ResizeMode = ResizeMode.NoResize;
    }

    public IntPtr Handle
    {
        get
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
                helper.EnsureHandle();
            return helper.Handle;
        }
    }
}
