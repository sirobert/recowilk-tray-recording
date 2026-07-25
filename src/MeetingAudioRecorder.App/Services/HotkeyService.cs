using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App.Services;

/// <summary>
/// Globalny skrót przez WinAPI RegisterHotKey (bez keyboard hooka).
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xBEE1;
    private readonly ILogger<HotkeyService> _logger;
    private HwndSource? _source;
    private bool _registered;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? HotkeyPressed;
    public bool IsRegistered => _registered;
    public string? LastError { get; private set; }

    public void Attach(IntPtr hwnd)
    {
        if (_source is not null)
            return;

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    public bool Register(HotkeySettings settings)
    {
        if (_source is null)
        {
            LastError = "Brak uchwytu okna do rejestracji skrótu.";
            return false;
        }

        Unregister();

        var modifiers = 0;
        if (settings.Control) modifiers |= MOD_CONTROL;
        if (settings.Alt) modifiers |= MOD_ALT;
        if (settings.Shift) modifiers |= MOD_SHIFT;
        if (settings.Windows) modifiers |= MOD_WIN;

        if (!TryParseKey(settings.Key, out var vk))
        {
            LastError = $"Nieprawidłowy klawisz: {settings.Key}";
            _logger.LogWarning(LastError);
            return false;
        }

        var ok = RegisterHotKey(_source.Handle, HotkeyId, (uint)modifiers, vk);
        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            LastError = err == 1409
                ? $"Skrót {settings.DisplayText} jest już zajęty przez inną aplikację."
                : $"Nie udało się zarejestrować skrótu {settings.DisplayText} (kod {err}).";
            _logger.LogWarning("RegisterHotKey failed: {Error}", LastError);
            _registered = false;
            return false;
        }

        _registered = true;
        LastError = null;
        _logger.LogInformation("Zarejestrowano skrót: {Hotkey}", settings.DisplayText);
        return true;
    }

    public void Unregister()
    {
        if (_source is null || !_registered)
        {
            _registered = false;
            return;
        }

        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        key = key.Trim();

        // Litera A-Z lub cyfra 0-9
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        // Function keys F1-F24
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key[1..], out var fn)
            && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1); // VK_F1 = 0x70
            return true;
        }

        if (Enum.TryParse<Key>(key, ignoreCase: true, out var wpfKey))
        {
            var keyCode = KeyInterop.VirtualKeyFromKey(wpfKey);
            if (keyCode > 0)
            {
                vk = (uint)keyCode;
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
