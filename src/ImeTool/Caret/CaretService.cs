using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using ImeTool.Native;

namespace ImeTool.Caret;

public enum CaretSource
{
    GuiThreadInfo = 0,
    UiAutomationTextPattern = 1,
    UiAutomationElementBounds = 2
}

public readonly record struct CaretSnapshot(
    IntPtr FocusHwnd,
    IntPtr CaretHwnd,
    NativeMethods.RECT ScreenRect,
    CaretSource Source);

public interface ICaretService
{
    string? LastFailureReason { get; }

    bool TryGetCaret(out CaretSnapshot snapshot);
}

public sealed class CaretService : ICaretService
{
    private const long NativeValidationCacheMilliseconds = 250;
    private IntPtr _lastValidatedNativeFocus;
    private long _lastNativeValidationTimestamp;
    private bool _lastNativeValidationResult;

    public string? LastFailureReason { get; private set; }

    public bool TryGetCaret(out CaretSnapshot snapshot)
    {
        if (TryGetCaretFromGuiThreadInfo(out snapshot))
        {
            LastFailureReason = null;
            return true;
        }

        bool found = TryGetCaretFromUiAutomation(out snapshot);
        if (found)
        {
            LastFailureReason = null;
        }

        return found;
    }

    private bool TryGetCaretFromGuiThreadInfo(out CaretSnapshot snapshot)
    {
        snapshot = default;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        if (threadId == 0)
        {
            return false;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        if (info.hwndFocus == IntPtr.Zero || info.hwndCaret == IntPtr.Zero || info.rcCaret.IsEmpty)
        {
            return false;
        }

        IntPtr coordinateHwnd = info.hwndCaret;

        var topLeft = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        var bottomRight = new NativeMethods.POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
        if (!NativeMethods.ClientToScreen(coordinateHwnd, ref topLeft) || !NativeMethods.ClientToScreen(coordinateHwnd, ref bottomRight))
        {
            return false;
        }

        var rect = new NativeMethods.RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y
        };

        if (rect.Width <= 0 || rect.Height <= 0 || rect.Height > 200 || !IsNativeCaretTargetAllowed(info.hwndFocus))
        {
            return false;
        }

        snapshot = new CaretSnapshot(info.hwndFocus, coordinateHwnd, rect, CaretSource.GuiThreadInfo);
        return true;
    }

    private bool IsNativeCaretTargetAllowed(IntPtr focusHwnd)
    {
        long now = Environment.TickCount64;
        if (focusHwnd == _lastValidatedNativeFocus &&
            now - _lastNativeValidationTimestamp < NativeValidationCacheMilliseconds)
        {
            return _lastNativeValidationResult;
        }

        bool result = ValidateNativeCaretTarget(focusHwnd);
        _lastValidatedNativeFocus = focusHwnd;
        _lastNativeValidationTimestamp = now;
        _lastNativeValidationResult = result;
        return result;
    }

    private static bool ValidateNativeCaretTarget(IntPtr focusHwnd)
    {
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return true;
            }

            IntPtr automationHwnd = new(focused.Current.NativeWindowHandle);
            if (automationHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(focusHwnd, out uint nativeProcessId);
                NativeMethods.GetWindowThreadProcessId(automationHwnd, out uint automationProcessId);
                if (nativeProcessId != 0 && automationProcessId != 0 && nativeProcessId != automationProcessId)
                {
                    return false;
                }
            }

            bool supportsTextPattern = focused.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObject);
            bool supportsValuePattern = focused.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObject);
            bool? isReadOnly = GetIsReadOnly(
                textPatternObject,
                supportsTextPattern,
                valuePatternObject,
                supportsValuePattern);

            return CaretTargetClassifier.ShouldTrustNativeCaret(
                focused.Current.ControlType.ProgrammaticName,
                supportsTextPattern,
                supportsValuePattern,
                focused.Current.IsPassword,
                focused.Current.IsKeyboardFocusable,
                isReadOnly,
                focused.Current.LocalizedControlType,
                focused.Current.FrameworkId);
        }
        catch
        {
            // An unavailable UIA provider must not break native Win32 editors.
            return true;
        }
    }

    private bool TryGetCaretFromUiAutomation(out CaretSnapshot snapshot)
    {
        snapshot = default;

        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                LastFailureReason = "UI Automation returned no focused element.";
                return false;
            }

            List<AutomationElement> candidates = GetCandidateElements(focused);
            string? focusedFailure = null;
            foreach (AutomationElement candidate in candidates)
            {
                try
                {
                    if (TryGetCaretFromElement(candidate, out snapshot, out string? failureReason))
                    {
                        return true;
                    }

                    focusedFailure ??= failureReason;
                }
                catch (Exception exception)
                {
                    focusedFailure ??= $"UIA candidate became unavailable ({exception.GetType().Name}).";
                }
            }

            LastFailureReason = focusedFailure ?? $"Focused UIA element was not editable: {DescribeElement(focused)}.";
            return false;
        }
        catch (Exception exception)
        {
            LastFailureReason = $"UI Automation failed with {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static List<AutomationElement> GetCandidateElements(AutomationElement focused)
    {
        var candidates = new List<AutomationElement> { focused };

        try
        {
            string focusedType = focused.Current.ControlType.ProgrammaticName;
            if (string.Equals(focusedType, "ControlType.ComboBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(focusedType, "ControlType.Custom", StringComparison.OrdinalIgnoreCase))
            {
                var editCondition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Edit);
                AutomationElement editableChild = focused.FindFirst(TreeScope.Descendants, editCondition);
                if (editableChild is not null)
                {
                    candidates.Add(editableChild);
                }
            }
        }
        catch
        {
            // Some providers do not support descendant navigation. Parent
            // traversal below can still locate the editable host.
        }

        try
        {
            AutomationElement current = focused;
            for (int depth = 0; depth < 5; depth++)
            {
                AutomationElement parent = TreeWalker.ControlViewWalker.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                if (IsPotentialTextHost(parent.Current.ControlType.ProgrammaticName))
                {
                    candidates.Add(parent);
                }
                current = parent;
                if (parent.Current.ControlType == ControlType.Window)
                {
                    break;
                }
            }
        }
        catch
        {
            // Cross-process UIA trees can disappear while focus changes.
        }

        return candidates;
    }

    private static bool IsPotentialTextHost(string? controlType)
    {
        return string.Equals(controlType, "ControlType.Edit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(controlType, "ControlType.Document", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(controlType, "ControlType.Group", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(controlType, "ControlType.Custom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(controlType, "ControlType.ComboBox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCaretFromElement(
        AutomationElement element,
        out CaretSnapshot snapshot,
        out string? failureReason)
    {
        snapshot = default;
        failureReason = null;

        bool supportsTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObject);
        bool supportsValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObject);
        bool? isReadOnly = GetIsReadOnly(
            textPatternObject,
            supportsTextPattern,
            valuePatternObject,
            supportsValuePattern);
        bool isLikelyTextInput = CaretTargetClassifier.IsLikelyTextInput(
            element.Current.ControlType.ProgrammaticName,
            supportsTextPattern,
            supportsValuePattern,
            element.Current.IsPassword,
            element.Current.IsKeyboardFocusable,
            isReadOnly,
            element.Current.LocalizedControlType,
            element.Current.FrameworkId);

        if (!isLikelyTextInput)
        {
            failureReason = $"Focused UIA element was rejected: {DescribeElement(element)}, " +
                            $"TextPattern={supportsTextPattern}, ValuePattern={supportsValuePattern}, " +
                            $"ReadOnly={isReadOnly?.ToString() ?? "Unknown"}.";
            return false;
        }

        IntPtr focusHwnd = new(element.Current.NativeWindowHandle);
        if (focusHwnd == IntPtr.Zero)
        {
            focusHwnd = NativeMethods.GetForegroundWindow();
        }

        if (focusHwnd == IntPtr.Zero)
        {
            failureReason = "Editable UIA element had no native or foreground window.";
            return false;
        }

        if (TryGetTextPatternRect(textPatternObject, supportsTextPattern, out NativeMethods.RECT textRect))
        {
            snapshot = new CaretSnapshot(
                focusHwnd,
                focusHwnd,
                textRect,
                CaretSource.UiAutomationTextPattern);
            return true;
        }

        System.Windows.Rect bounding = element.Current.BoundingRectangle;
        if (bounding.IsEmpty ||
            bounding.Width <= 0 ||
            bounding.Height <= 0 ||
            double.IsInfinity(bounding.Left) ||
            double.IsNaN(bounding.Left))
        {
            failureReason = $"Editable UIA element had no usable bounds: {DescribeElement(element)}.";
            return false;
        }

        int left = (int)Math.Round(bounding.Left + 4);
        int top = (int)Math.Round(bounding.Top + Math.Max(4, Math.Min(bounding.Height - 4, 18)));
        snapshot = new CaretSnapshot(
            focusHwnd,
            focusHwnd,
            new NativeMethods.RECT
            {
                Left = left,
                Top = top,
                Right = left + 1,
                Bottom = top + 18
            },
            CaretSource.UiAutomationElementBounds);
        return true;
    }

    private static bool? GetIsReadOnly(
        object textPatternObject,
        bool supportsTextPattern,
        object valuePatternObject,
        bool supportsValuePattern)
    {
        if (supportsValuePattern && valuePatternObject is ValuePattern valuePattern)
        {
            return valuePattern.Current.IsReadOnly;
        }

        if (supportsTextPattern && textPatternObject is TextPattern textPattern)
        {
            try
            {
                object value = textPattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                return value is bool isReadOnly ? isReadOnly : null;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string DescribeElement(AutomationElement element)
    {
        try
        {
            return $"Type={element.Current.ControlType.ProgrammaticName}, " +
                   $"Class={element.Current.ClassName}, Framework={element.Current.FrameworkId}, " +
                   $"LocalizedType={element.Current.LocalizedControlType}, " +
                   $"KeyboardFocusable={element.Current.IsKeyboardFocusable}";
        }
        catch
        {
            return "element details unavailable";
        }
    }

    private static bool TryGetTextPatternRect(object textPatternObject, bool supportsTextPattern, out NativeMethods.RECT rect)
    {
        rect = default;

        if (!supportsTextPattern || textPatternObject is not TextPattern textPattern)
        {
            return false;
        }

        TextPatternRange[] selections = textPattern.GetSelection();
        if (selections.Length == 0)
        {
            return false;
        }

        System.Windows.Rect[] rectangles = selections[0].GetBoundingRectangles();
        if (rectangles.Length == 0 || rectangles[0].IsEmpty)
        {
            return false;
        }

        System.Windows.Rect first = rectangles[0];
        rect = new NativeMethods.RECT
        {
            Left = (int)Math.Round(first.Left),
            Top = (int)Math.Round(first.Top),
            Right = (int)Math.Round(first.Right <= first.Left ? first.Left + 1 : first.Right),
            Bottom = (int)Math.Round(first.Bottom <= first.Top ? first.Top + 18 : first.Bottom)
        };
        return !rect.IsEmpty;
    }
}
