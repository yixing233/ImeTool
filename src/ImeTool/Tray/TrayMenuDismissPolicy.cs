namespace ImeTool.Tray;

public static class TrayMenuDismissPolicy
{
    public static bool IsButtonDownMessage(int message)
    {
        return message is 0x0201 or // WM_LBUTTONDOWN
               0x0204 or           // WM_RBUTTONDOWN
               0x0207 or           // WM_MBUTTONDOWN
               0x020B;             // WM_XBUTTONDOWN
    }
}
