namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Konfiguracja globalnego skrótu klawiszowego.
/// </summary>
public sealed class HotkeySettings
{
    public string Key { get; set; } = "R";
    public bool Control { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Windows { get; set; }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");
            parts.Add(Key.ToUpperInvariant());
            return string.Join(" + ", parts);
        }
    }

    public HotkeySettings Clone() => new()
    {
        Key = Key,
        Control = Control,
        Alt = Alt,
        Shift = Shift,
        Windows = Windows
    };

    public bool EqualsHotkey(HotkeySettings? other)
    {
        if (other is null) return false;
        return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase)
               && Control == other.Control
               && Alt == other.Alt
               && Shift == other.Shift
               && Windows == other.Windows;
    }
}
