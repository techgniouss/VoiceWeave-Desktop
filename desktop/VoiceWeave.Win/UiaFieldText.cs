using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>
/// Reads the text of a field through UI Automation so corrections to a dictation can be noticed. Password fields are never
/// read, and a field that does not expose its text (terminals, some games and remote desktops) simply cannot be learned from.
/// </summary>
public sealed class UiaFieldText : IFieldText
{
    private const int MaxChars = 200_000;
    private readonly AutomationElement _element;

    private UiaFieldText(AutomationElement element) => _element = element;

    /// <summary>The field that has the keyboard focus right now, or null when it cannot be read.</summary>
    public static IFieldText? CaptureFocused()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null || IsPassword(element)) return null;
            return new UiaFieldText(element);
        }
        catch (Exception e) when (IsUiaFailure(e)) { return null; }
    }

    /// <summary>The text selected in the field that has the keyboard focus, or null when there is none or it cannot be read.</summary>
    public static string? ReadFocusedSelection()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null || IsPassword(element)) return null;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
            var ranges = ((TextPattern)pattern).GetSelection();
            if (ranges.Length == 0) return null;
            var text = ranges[0].GetText(200).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception e) when (IsUiaFailure(e)) { return null; }
    }

    /// <summary>Wraps a known element (used by tests, which cannot take the keyboard focus).</summary>
    internal static IFieldText? FromElement(AutomationElement element)
    {
        try { return IsPassword(element) ? null : new UiaFieldText(element); }
        catch (Exception e) when (IsUiaFailure(e)) { return null; }
    }

    public string? Read()
    {
        try
        {
            if (IsPassword(_element)) return null;
            if (_element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
                return ((TextPattern)text).DocumentRange.GetText(MaxChars);
            if (_element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
                return ((ValuePattern)value).Current.Value;
            return null;
        }
        catch (Exception e) when (IsUiaFailure(e)) { return null; }
    }

    private static bool IsPassword(AutomationElement element) => (bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);

    private static bool IsUiaFailure(Exception e) =>
        e is ElementNotAvailableException or InvalidOperationException or COMException or Win32Exception or ArgumentException or NotSupportedException;
}
