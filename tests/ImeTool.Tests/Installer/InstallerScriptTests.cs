namespace ImeTool.Tests.Installer;

public sealed class InstallerScriptTests
{
    [Fact]
    public void UpdateShutdown_DoesNotTerminateInstallerChildProcessTree()
    {
        string script = File.ReadAllText(FindInstallerScript());

        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(
                script,
                "(?i)'[^'\\r\\n]*/T(?:\\s|')"),
            "The updater is launched by ImeTool, so taskkill /T would terminate the installer too.");
    }

    private static string FindInstallerScript()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "installer", "ImeTool.iss");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate installer/ImeTool.iss from the test output directory.");
    }
}
