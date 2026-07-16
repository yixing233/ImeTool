using System.Globalization;
using System.Windows;
using ImeTool.Settings;
using WpfBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace ImeTool;

public partial class ApplicationRuleEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    public ApplicationRuleEditorWindow(
        ApplicationRule? rule,
        SettingsWindowBackdrop backdrop)
    {
        InitializeComponent();
        WindowBackdropType = backdrop == SettingsWindowBackdrop.Acrylic
            ? WpfBackdropType.Acrylic
            : WpfBackdropType.Mica;

        if (rule is null)
        {
            return;
        }

        ProcessNameBox.Text = rule.ProcessName;
        WindowTitleBox.Text = rule.WindowTitleContains;
        WindowClassBox.Text = rule.WindowClass;
        ControlClassBox.Text = rule.ControlClass;
        HideMarkerBox.IsChecked = rule.HideMarker;
        DisableMemoryBox.IsChecked = rule.DisableWindowMemory;
        DisableRestoreBox.IsChecked = rule.DisableStateRestore;
        OffsetXBox.Text = rule.OffsetX?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        OffsetYBox.Text = rule.OffsetY?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        DeleteButton.Visibility = Visibility.Visible;
    }

    public ApplicationRule? Rule { get; private set; }
    public bool DeleteRequested { get; private set; }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        string processName = ApplicationRuleNormalizer.NormalizeProcessName(ProcessNameBox.Text);
        if (string.IsNullOrEmpty(processName))
        {
            ValidationText.Text = "请填写进程名。";
            ProcessNameBox.Focus();
            return;
        }

        if (!TryParseOptionalOffset(OffsetXBox.Text, out int? offsetX) ||
            !TryParseOptionalOffset(OffsetYBox.Text, out int? offsetY))
        {
            ValidationText.Text = "偏移量需要填写 -500 到 500 之间的整数，或保持为空。";
            return;
        }

        bool hasAction = HideMarkerBox.IsChecked == true ||
                         DisableMemoryBox.IsChecked == true ||
                         DisableRestoreBox.IsChecked == true ||
                         offsetX is not null || offsetY is not null;
        if (!hasAction)
        {
            ValidationText.Text = "请至少选择一种行为或填写一个额外偏移。";
            return;
        }

        Rule = new ApplicationRule
        {
            ProcessName = processName,
            WindowTitleContains = WindowTitleBox.Text,
            WindowClass = WindowClassBox.Text,
            ControlClass = ControlClassBox.Text,
            HideMarker = HideMarkerBox.IsChecked == true,
            DisableWindowMemory = DisableMemoryBox.IsChecked == true,
            DisableStateRestore = DisableRestoreBox.IsChecked == true,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
        DialogResult = true;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private static bool TryParseOptionalOffset(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed is < -500 or > 500)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
