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

        string targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("无法确定程序安装目录。");
        string token = Guid.NewGuid().ToString("N");
        string stagedPath = Path.Combine(targetDirectory, $".{Path.GetFileName(targetPath)}.update-{token}");
        string backupPath = Path.Combine(targetDirectory, $".{Path.GetFileName(targetPath)}.backup-{token}");
        string failedPath = Path.Combine(targetDirectory, $".{Path.GetFileName(targetPath)}.failed-{token}");
        string healthPath = Path.Combine(Path.GetTempPath(), $"ImeTool-Update-Health-{token}.ok");
        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImeTool-Updater-{token}.ps1");

        // Staging beside the target is both a write-permission preflight and a prerequisite
        // for File.Replace's atomic same-volume swap. The running app remains untouched here.
        File.Copy(downloadedExecutable, stagedPath, overwrite: false);
        File.WriteAllText(scriptPath, UpdaterScript);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        AddArgument(startInfo, "-NoProfile");
        AddArgument(startInfo, "-NonInteractive");
        AddArgument(startInfo, "-ExecutionPolicy", "Bypass");
        AddArgument(startInfo, "-WindowStyle", "Hidden");
        AddArgument(startInfo, "-File", scriptPath);
        AddArgument(startInfo, "-ProcessId", Environment.ProcessId.ToString());
        AddArgument(startInfo, "-SourcePath", downloadedExecutable);
        AddArgument(startInfo, "-StagedPath", stagedPath);
        AddArgument(startInfo, "-TargetPath", targetPath);
        AddArgument(startInfo, "-BackupPath", backupPath);
        AddArgument(startInfo, "-FailedPath", failedPath);
        AddArgument(startInfo, "-HealthPath", healthPath);

        try
        {
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("无法启动更新安装程序。");
            }
        }
        catch
        {
            File.Delete(stagedPath);
            File.Delete(scriptPath);
            throw;
        }
    }

    private static void AddArgument(ProcessStartInfo startInfo, params string[] values)
    {
        foreach (string value in values)
        {
            startInfo.ArgumentList.Add(value);
        }
    }

    private const string UpdaterScript = """
        param(
            [Parameter(Mandatory = $true)][int]$ProcessId,
            [Parameter(Mandatory = $true)][string]$SourcePath,
            [Parameter(Mandatory = $true)][string]$StagedPath,
            [Parameter(Mandatory = $true)][string]$TargetPath,
            [Parameter(Mandatory = $true)][string]$BackupPath,
            [Parameter(Mandatory = $true)][string]$FailedPath,
            [Parameter(Mandatory = $true)][string]$HealthPath
        )

        $ErrorActionPreference = 'Stop'
        try {
            try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
            if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { exit 2 }

            try {
                [System.IO.File]::Replace($StagedPath, $TargetPath, $BackupPath, $true)
            }
            catch {
                Start-Process -FilePath $TargetPath -ErrorAction SilentlyContinue
                exit 3
            }

            $newProcess = $null
            try {
                $quotedHealthPath = '"' + $HealthPath + '"'
                $newProcess = Start-Process -FilePath $TargetPath -ArgumentList @('--update-health-check', $quotedHealthPath) -PassThru
            }
            catch {}

            $healthy = $false
            for ($attempt = 0; $attempt -lt 60 -and -not $healthy; $attempt++) {
                if (Test-Path -LiteralPath $HealthPath) {
                    $healthy = $true
                    break
                }
                if ($null -ne $newProcess -and $newProcess.HasExited) { break }
                Start-Sleep -Milliseconds 500
            }

            if ($healthy) {
                for ($attempt = 0; $attempt -lt 10; $attempt++) {
                    if ($null -eq $newProcess -or $newProcess.HasExited) {
                        $healthy = $false
                        break
                    }
                    Start-Sleep -Milliseconds 500
                }
            }

            if ($healthy) {
                Remove-Item -LiteralPath $BackupPath -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $SourcePath -Force -ErrorAction SilentlyContinue
                exit 0
            }

            if ($null -ne $newProcess -and -not $newProcess.HasExited) {
                Stop-Process -Id $newProcess.Id -Force -ErrorAction SilentlyContinue
                try { Wait-Process -Id $newProcess.Id -Timeout 10 -ErrorAction SilentlyContinue } catch {}
            }

            try {
                [System.IO.File]::Replace($BackupPath, $TargetPath, $FailedPath, $true)
                Remove-Item -LiteralPath $FailedPath -Force -ErrorAction SilentlyContinue
                Start-Process -FilePath $TargetPath
                Remove-Item -LiteralPath $SourcePath -Force -ErrorAction SilentlyContinue
            }
            catch {
                # Keep BackupPath intact whenever rollback cannot complete.
                exit 4
            }
        }
        finally {
            Remove-Item -LiteralPath $HealthPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $StagedPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
        }
        """;
}
