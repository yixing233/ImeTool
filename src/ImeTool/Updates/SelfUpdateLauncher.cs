using System.Diagnostics;
using System.IO;

namespace ImeTool.Updates;

public static class SelfUpdateLauncher
{
    public static bool CanInstall =>
        !string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath);

    public static void Launch(string downloadedExecutable)
    {
        string targetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径。");
        if (!File.Exists(downloadedExecutable))
        {
            throw new FileNotFoundException("下载的更新文件不存在。", downloadedExecutable);
        }

        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"ImeTool-Updater-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, UpdaterScript);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-SourcePath");
        startInfo.ArgumentList.Add(Path.GetFullPath(downloadedExecutable));
        startInfo.ArgumentList.Add("-TargetPath");
        startInfo.ArgumentList.Add(Path.GetFullPath(targetPath));

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("无法启动更新安装程序。");
        }
    }

    private const string UpdaterScript = """
        param(
            [Parameter(Mandatory = $true)][int]$ProcessId,
            [Parameter(Mandatory = $true)][string]$SourcePath,
            [Parameter(Mandatory = $true)][string]$TargetPath
        )

        $ErrorActionPreference = 'Stop'
        $backupPath = "$TargetPath.old"
        try {
            try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch {}

            $installed = $false
            for ($attempt = 0; $attempt -lt 20 -and -not $installed; $attempt++) {
                try {
                    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
                    Move-Item -LiteralPath $TargetPath -Destination $backupPath -Force
                    Move-Item -LiteralPath $SourcePath -Destination $TargetPath -Force
                    $installed = $true
                }
                catch {
                    if (-not (Test-Path -LiteralPath $TargetPath) -and (Test-Path -LiteralPath $backupPath)) {
                        Move-Item -LiteralPath $backupPath -Destination $TargetPath -Force -ErrorAction SilentlyContinue
                    }
                    Start-Sleep -Milliseconds 500
                }
            }

            if (-not $installed) { exit 1 }
            Start-Process -FilePath $TargetPath
            Start-Sleep -Seconds 2
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        }
        finally {
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
        }
        """;
}
