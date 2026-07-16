using ImeTool.Ime;

namespace ImeTool.Tests.Ime;

public sealed class ImeDiagnosticsStateTests
{
    [Fact]
    public void Update_Publishes_Only_Changed_Snapshots()
    {
        var state = new ImeDiagnosticsState();
        var snapshot = new ImeDiagnosticSnapshot
        {
            FocusHwnd = new IntPtr(1),
            KeyboardLayout = "0x0000000008040804",
            FinalMode = TextInputMode.Chinese
        };
        int notifications = 0;
        state.SnapshotChanged += _ => notifications++;

        state.Update(snapshot);
        state.Update(snapshot);
        state.Update(snapshot with { FinalMode = TextInputMode.English });

        Assert.Equal(2, notifications);
        Assert.Equal(TextInputMode.English, state.CurrentSnapshot?.FinalMode);
    }
}
