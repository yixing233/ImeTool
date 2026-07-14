using System.Diagnostics;
using System.IO;

namespace ImeTool.Updates;

public static class SelfUpdateLauncher
{
    public static bool CanInstall => OperatingSystem.IsWindows();

    public static void Launch(string downloadedInstaller)
    {
        if (!File.Exists(downloadedInstaller))
        {
            throw new FileNotFoundException("下载的更新安装包不存在。", downloadedInstaller);
        }

        GitHubUpdateService.ValidateInstallerPayload(downloadedInstaller);
        if (Process.Start(CreateInstallerStartInfo(downloadedInstaller)) is null)
        {
            throw new InvalidOperationException("无法启动更新安装程序。");
        }
    }

    public static ProcessStartInfo CreateInstallerStartInfo(
        string downloadedInstaller,
        int? updateProcessId = null)
    {
        int processId = updateProcessId ?? Environment.ProcessId;
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updateProcessId),
                "更新进程 ID 必须大于 0。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = downloadedInstaller,
            WorkingDirectory = Path.GetDirectoryName(downloadedInstaller) ?? Path.GetTempPath(),
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("/VERYSILENT");
        startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
        startInfo.ArgumentList.Add("/UPDATE=1");
        startInfo.ArgumentList.Add($"/UPDATEPID={processId}");
        return startInfo;
    }
}
