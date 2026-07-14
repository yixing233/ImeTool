namespace ImeTool.Tests;

public sealed class StartupLaunchPolicyTests
{
    [Fact]
    public void Silent_Start_Stays_In_Tray()
    {
        Assert.False(StartupLaunchPolicy.ShouldShowSettings([], silentStart: true));
    }

    [Fact]
    public void Non_Silent_Start_Opens_Settings()
    {
        Assert.True(StartupLaunchPolicy.ShouldShowSettings([], silentStart: false));
    }

    [Fact]
    public void Explicit_Settings_Argument_Overrides_Silent_Start()
    {
        Assert.True(StartupLaunchPolicy.ShouldShowSettings(["--settings"], silentStart: true));
    }

    [Fact]
    public void Tray_Preview_Does_Not_Also_Open_Settings()
    {
        Assert.False(StartupLaunchPolicy.ShouldShowSettings(["--tray-menu"], silentStart: false));
    }

    [Fact]
    public void Explicit_Silent_Argument_Does_Not_Open_Settings()
    {
        Assert.False(StartupLaunchPolicy.ShouldShowSettings(["--silent"], silentStart: false));
    }

    [Fact]
    public void Reads_Update_Health_Check_Path()
    {
        string? path = StartupLaunchPolicy.GetArgumentValue(
            ["--update-health-check", @"C:\Temp\health.ok"],
            "--update-health-check");

        Assert.Equal(@"C:\Temp\health.ok", path);
    }
}
