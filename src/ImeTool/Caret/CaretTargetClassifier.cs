namespace ImeTool.Caret;

public static class CaretTargetClassifier
{
    private static readonly HashSet<string> NonTextInputControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ControlType.Button",
        "ControlType.CheckBox",
        "ControlType.RadioButton",
        "ControlType.Menu",
        "ControlType.MenuBar",
        "ControlType.MenuItem",
        "ControlType.List",
        "ControlType.ListItem",
        "ControlType.Tree",
        "ControlType.TreeItem",
        "ControlType.Tab",
        "ControlType.TabItem",
        "ControlType.Window",
        "ControlType.Pane",
        "ControlType.ToolBar",
        "ControlType.StatusBar",
        "ControlType.Slider",
        "ControlType.Spinner",
        "ControlType.ProgressBar",
        "ControlType.Hyperlink",
        "ControlType.Image"
    };

    public static bool IsLikelyTextInput(
        string? controlTypeProgrammaticName,
        bool supportsTextPattern,
        bool supportsValuePattern,
        bool isPassword,
        bool isKeyboardFocusable,
        bool? isReadOnly,
        string? localizedControlType,
        string? frameworkId = null)
    {
        if (!isKeyboardFocusable)
        {
            return false;
        }

        if (isPassword)
        {
            return true;
        }

        string controlType = controlTypeProgrammaticName ?? string.Empty;
        if (NonTextInputControlTypes.Contains(controlType))
        {
            return false;
        }

        if (isReadOnly == true)
        {
            return false;
        }

        if (string.Equals(controlType, "ControlType.Edit", StringComparison.OrdinalIgnoreCase))
        {
            // ControlType.Edit is an explicit provider assertion that the focused
            // element is editable. Custom-rendered controls often omit both UIA
            // patterns, so an unknown read-only state must not become a false
            // read-only result.
            return true;
        }

        if (string.Equals(controlType, "ControlType.Document", StringComparison.OrdinalIgnoreCase))
        {
            // Chromium/Firefox can report an ordinary page document as editable.
            // Their contenteditable hosts are exposed as focused Group/Edit
            // elements, so the whole browser document must remain excluded.
            return supportsTextPattern &&
                   isReadOnly == false &&
                   !IsBrowserFramework(frameworkId);
        }

        if (string.Equals(controlType, "ControlType.Group", StringComparison.OrdinalIgnoreCase))
        {
            // Chromium exposes contenteditable elements as focused Group controls
            // with a writable TextPattern, while ordinary focused page groups do
            // not expose that pattern.
            return supportsTextPattern && isReadOnly == false;
        }

        if (string.Equals(controlType, "ControlType.ComboBox", StringComparison.OrdinalIgnoreCase))
        {
            return supportsValuePattern;
        }

        if (string.Equals(controlType, "ControlType.Custom", StringComparison.OrdinalIgnoreCase))
        {
            return IsLocalizedTextInput(localizedControlType) &&
                   (supportsTextPattern || supportsValuePattern || isReadOnly is null);
        }

        return supportsValuePattern && IsLocalizedTextInput(localizedControlType);
    }

    public static bool ShouldTrustNativeCaret(
        string? controlTypeProgrammaticName,
        bool supportsTextPattern,
        bool supportsValuePattern,
        bool isPassword,
        bool isKeyboardFocusable,
        bool? isReadOnly,
        string? localizedControlType,
        string? frameworkId)
    {
        if (IsLikelyTextInput(
                controlTypeProgrammaticName,
                supportsTextPattern,
                supportsValuePattern,
                isPassword,
                isKeyboardFocusable,
                isReadOnly,
                localizedControlType,
                frameworkId))
        {
            return true;
        }

        string controlType = controlTypeProgrammaticName ?? string.Empty;
        if (NonTextInputControlTypes.Contains(controlType))
        {
            return false;
        }

        // Legacy Win32 providers are frequently incomplete, so a real native
        // caret remains authoritative there. Modern frameworks provide useful
        // UIA focus semantics; a rejected focused element means their native
        // caret handle is stale and must not keep the marker visible.
        return !IsModernFramework(frameworkId);
    }

    private static bool IsBrowserFramework(string? frameworkId)
    {
        if (string.IsNullOrWhiteSpace(frameworkId))
        {
            return false;
        }

        return frameworkId.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("Chromium", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("Gecko", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("Firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModernFramework(string? frameworkId)
    {
        if (string.IsNullOrWhiteSpace(frameworkId))
        {
            return false;
        }

        return IsBrowserFramework(frameworkId) ||
               frameworkId.Contains("Qt", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("XAML", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("WinUI", StringComparison.OrdinalIgnoreCase) ||
               frameworkId.Contains("WPF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalizedTextInput(string? localizedControlType)
    {
        if (string.IsNullOrWhiteSpace(localizedControlType))
        {
            return false;
        }

        string value = localizedControlType.Trim().ToLowerInvariant();
        return value.Contains("edit") ||
               value.Contains("text") ||
               value.Contains("document") ||
               value.Contains("输入") ||
               value.Contains("编辑") ||
               value.Contains("文本") ||
               value.Contains("文档");
    }
}
