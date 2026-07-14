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

    void Invalidate()
    {
    }
}

public sealed class CaretService : ICaretService, IDisposable
{
    private readonly UiAutomationCaretReader _uiAutomationReader;

    public CaretService()
    {
        _uiAutomationReader = new UiAutomationCaretReader(ReadCaretFromUiAutomation);
    }

    public string? LastFailureReason { get; private set; }

    public bool TryGetCaret(out CaretSnapshot snapshot)
    {
        bool foundNative = TryGetCaretFromGuiThreadInfo(out CaretSnapshot nativeSnapshot);

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        UiAutomationCaretReadResult automationResult = default;
        bool hasAutomationResult = foreground != IntPtr.Zero &&
                                   _uiAutomationReader.TryGetResult(
                                       foreground,
                                       out automationResult);
        if (foundNative &&
            hasAutomationResult &&
            automationResult.TrustNativeCaret &&
            IsSameTargetProcess(nativeSnapshot.FocusHwnd, automationResult.FocusHwnd))
        {
            snapshot = nativeSnapshot;
            LastFailureReason = null;
            return true;
        }

        if (hasAutomationResult && automationResult.Found)
        {
            snapshot = automationResult.Snapshot;
            LastFailureReason = null;
            return true;
        }

        snapshot = default;
        LastFailureReason = foreground == IntPtr.Zero
            ? "No foreground window is available."
            : hasAutomationResult
                ? automationResult.FailureReason ?? "UI Automation returned no exact caret."
                : "UI Automation caret lookup is pending.";
        return false;
    }

    public void Invalidate()
    {
        _uiAutomationReader.Invalidate();
    }

    public void Dispose()
    {
        _uiAutomationReader.Dispose();
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

        if (rect.Width <= 0 || rect.Height <= 0 || rect.Height > 200)
        {
            return false;
        }

        snapshot = new CaretSnapshot(info.hwndFocus, coordinateHwnd, rect, CaretSource.GuiThreadInfo);
        return true;
    }

    private static bool IsSameTargetProcess(IntPtr leftHwnd, IntPtr rightHwnd)
    {
        if (leftHwnd == IntPtr.Zero || rightHwnd == IntPtr.Zero)
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(leftHwnd, out uint leftProcessId);
        NativeMethods.GetWindowThreadProcessId(rightHwnd, out uint rightProcessId);
        return leftProcessId == 0 || rightProcessId == 0 || leftProcessId == rightProcessId;
    }

    private static UiAutomationCaretReadResult ReadCaretFromUiAutomation(IntPtr expectedForeground)
    {
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return UiAutomationCaretReadResult.Failure(
                    "UI Automation returned no focused element.");
            }

            List<AutomationElement> candidates = GetCandidateElements(focused);
            string? focusedFailure = null;
            bool trustNativeCaret = false;
            IntPtr trustedFocusHwnd = IntPtr.Zero;
            foreach (AutomationElement candidate in candidates)
            {
                try
                {
                    if (TryGetCaretFromElement(
                            candidate,
                            out CaretSnapshot snapshot,
                            out string? failureReason,
                            out bool candidateTrustsNativeCaret,
                            out IntPtr candidateFocusHwnd))
                    {
                        if (NativeMethods.GetForegroundWindow() != expectedForeground)
                        {
                            return UiAutomationCaretReadResult.Failure(
                                "Foreground window changed during UI Automation caret lookup.");
                        }

                        return UiAutomationCaretReadResult.Success(snapshot);
                    }

                    if (candidateTrustsNativeCaret)
                    {
                        trustNativeCaret = true;
                        trustedFocusHwnd = candidateFocusHwnd;
                    }

                    focusedFailure ??= failureReason;
                }
                catch (Exception exception)
                {
                    focusedFailure ??= $"UIA candidate became unavailable ({exception.GetType().Name}).";
                }
            }

            return UiAutomationCaretReadResult.Failure(
                focusedFailure ?? $"Focused UIA element was not editable: {DescribeElement(focused)}.",
                trustNativeCaret,
                trustedFocusHwnd);
        }
        catch (Exception exception)
        {
            return UiAutomationCaretReadResult.Failure(
                $"UI Automation failed with {exception.GetType().Name}: {exception.Message}",
                trustNativeCaret: true,
                focusHwnd: expectedForeground);
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
        out string? failureReason,
        out bool trustNativeCaret,
        out IntPtr focusHwnd)
    {
        snapshot = default;
        failureReason = null;
        trustNativeCaret = false;
        focusHwnd = IntPtr.Zero;

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
        trustNativeCaret = CaretTargetClassifier.ShouldTrustNativeCaret(
            element.Current.ControlType.ProgrammaticName,
            supportsTextPattern,
            supportsValuePattern,
            element.Current.IsPassword,
            element.Current.IsKeyboardFocusable,
            isReadOnly,
            element.Current.LocalizedControlType,
            element.Current.FrameworkId);
        focusHwnd = new IntPtr(element.Current.NativeWindowHandle);
        if (focusHwnd == IntPtr.Zero)
        {
            focusHwnd = NativeMethods.GetForegroundWindow();
        }

        if (!isLikelyTextInput)
        {
            failureReason = $"Focused UIA element was rejected: {DescribeElement(element)}, " +
                            $"TextPattern={supportsTextPattern}, ValuePattern={supportsValuePattern}, " +
                            $"ReadOnly={isReadOnly?.ToString() ?? "Unknown"}.";
            return false;
        }

        if (focusHwnd == IntPtr.Zero)
        {
            failureReason = "Editable UIA element had no native or foreground window.";
            return false;
        }

        if (TryGetTextPatternRect(
                textPatternObject,
                supportsTextPattern,
                out NativeMethods.RECT textRect))
        {
            snapshot = new CaretSnapshot(
                focusHwnd,
                focusHwnd,
                textRect,
                CaretSource.UiAutomationTextPattern);
            return true;
        }

        failureReason = $"Editable UIA element exposed no exact caret geometry: {DescribeElement(element)}.";
        return false;
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

        TextPatternRange collapsed = selections[0].Clone();
        collapsed.MoveEndpointByRange(
            TextPatternRangeEndpoint.Start,
            collapsed,
            TextPatternRangeEndpoint.End);
        return TryGetRangeRect(collapsed, out rect);
    }

    private static bool TryGetRangeRect(TextPatternRange range, out NativeMethods.RECT rect)
    {
        rect = default;
        System.Windows.Rect[] rectangles = range.GetBoundingRectangles();
        if (rectangles.Length == 0)
        {
            return false;
        }

        return CaretGeometry.TryCreateExactRect(rectangles[0], out rect);
    }
}
