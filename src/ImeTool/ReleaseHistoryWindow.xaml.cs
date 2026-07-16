using System.Windows;
using ImeTool.Settings;
using ImeTool.Updates;
using WpfBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace ImeTool;

public partial class ReleaseHistoryWindow : Wpf.Ui.Controls.FluentWindow
{
    public ReleaseHistoryWindow(
        IReadOnlyList<ReleaseHistoryEntry> entries,
        SettingsWindowBackdrop backdrop)
    {
        InitializeComponent();
        HistoryItems.ItemsSource = entries;
        WindowBackdropType = backdrop == SettingsWindowBackdrop.Acrylic
            ? WpfBackdropType.Acrylic
            : WpfBackdropType.Mica;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
