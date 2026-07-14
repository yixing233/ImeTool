using ImeTool.Tray;

namespace ImeTool.Tests.Tray;

public sealed class TrayMenuDismissPolicyTests
{
    [Theory]
    [InlineData(0x0200, false)] // WM_MOUSEMOVE
    [InlineData(0x0201, true)]  // WM_LBUTTONDOWN
    [InlineData(0x0204, true)]  // WM_RBUTTONDOWN
    [InlineData(0x0207, true)]  // WM_MBUTTONDOWN
    [InlineData(0x020B, true)]  // WM_XBUTTONDOWN
    [InlineData(0x0202, false)] // WM_LBUTTONUP
    public void Only_Physical_Button_Down_Messages_Request_Dismiss(int message, bool expected)
    {
        Assert.Equal(expected, TrayMenuDismissPolicy.IsButtonDownMessage(message));
    }
}
