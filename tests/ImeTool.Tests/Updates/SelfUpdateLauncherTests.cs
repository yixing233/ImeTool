using ImeTool.Updates;

namespace ImeTool.Tests.Updates;

public sealed class SelfUpdateLauncherTests
{
    [Fact]
    public void CreateInstallerStartInfo_UsesSilentInnoUpdateArguments()
    {
        string installerPath = Path.Combine(Path.GetTempPath(), "ImeTool_Windows_x64.exe");

        System.Diagnostics.ProcessStartInfo startInfo =
            SelfUpdateLauncher.CreateInstallerStartInfo(installerPath, 12345);

        Assert.Equal(installerPath, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Contains("/VERYSILENT", startInfo.ArgumentList);
        Assert.Contains("/SUPPRESSMSGBOXES", startInfo.ArgumentList);
        Assert.Contains("/NORESTART", startInfo.ArgumentList);
        Assert.Contains("/CLOSEAPPLICATIONS", startInfo.ArgumentList);
        Assert.Contains("/UPDATE=1", startInfo.ArgumentList);
        Assert.Contains("/UPDATEPID=12345", startInfo.ArgumentList);
    }
}
