# ImeTool

轻量级 Windows 输入法状态提示工具，在文本光标旁显示中文、英文及大写锁定状态，并按窗口自动记忆和恢复输入法状态。

![ImeTool 图标](src/ImeTool/Assets/AppIconPreview.png)

## 功能

- 在文本光标附近显示中文、英文和大写锁定状态
- 按顶层窗口独立记忆并恢复中英文输入状态
- 支持小圆点、文字胶囊和自定义图片标记
- 可分别配置中文、英文和大写状态的颜色、文字与图片
- 支持始终显示、切换时显示、输入时显示等显示策略
- 支持标记跟随动画、淡入淡出、位置偏移和尺寸调整
- 支持应用排除规则，并可检测当前已打开的窗口
- 支持自定义全局快捷键
- 提供系统托盘菜单、开机启动和静默启动
- 通过 GitHub Releases 自动检查、下载并安装新版本
- 设置窗口支持 Acrylic 和 Mica 系统材质

## 系统要求

- Windows 10 / Windows 11
- x64 系统
- 轻量版需要安装 [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)
- 从源码构建需要 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## 下载版本

| 版本 | 体积 | 运行要求 |
| --- | ---: | --- |
| [`ImeTool-win-x64-lite.zip`](https://github.com/yixing233/ImeTool/releases/latest/download/ImeTool-win-x64-lite.zip) | 下载约 8.6 MB，解压约 30 MB | 需要 .NET 9 Desktop Runtime，推荐 |
| `ImeTool-win-x64.exe` | 约 192 MB | 自包含，无需额外运行库 |

轻量 ZIP 解压后只有一个 `ImeTool.exe`。两种构建会分别跟随对应的 GitHub Release 资源在线更新，不会在更新时互相切换。

## 从源码运行

```powershell
git clone https://github.com/yixing233/ImeTool.git
cd ImeTool
dotnet restore
dotnet run --project src\ImeTool\ImeTool.csproj -c Debug
```

开发时启用热重载：

```powershell
dotnet watch --project src\ImeTool\ImeTool.csproj run -c Debug
```

强制打开设置窗口：

```powershell
dotnet watch --project src\ImeTool\ImeTool.csproj run -c Debug -- --settings
```

> `--settings` 会强制打开设置窗口。测试静默启动时请不要添加该参数。

## 测试

```powershell
dotnet test ImeTool.sln -c Release
```

## 发布

轻量单文件版：

```powershell
dotnet publish src\ImeTool\ImeTool.csproj -c Release -r win-x64 --self-contained false
```

自包含单文件版：

```powershell
dotnet publish src\ImeTool\ImeTool.csproj -c Release -r win-x64 --self-contained true -p:UpdateAssetName=ImeTool-win-x64.exe
```

发布结果位于：

```text
src/ImeTool/bin/Release/net9.0-windows10.0.17763.0/win-x64/publish/
```

### 创建 GitHub Release

项目通过 GitHub Actions 自动构建 Release。发布新版本时：

1. 更新 `src/ImeTool/ImeTool.csproj` 中的 `Version`。
2. 提交代码并创建对应的版本标签。
3. 推送标签，工作流会自动测试、发布轻量版与自包含版，并分别生成 SHA-256 校验文件。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

应用通过以下接口检查最新版：

```text
https://api.github.com/repos/yixing233/ImeTool/releases/latest
```

自动更新要求 Release 同时包含：

```text
ImeTool-win-x64.exe
ImeTool-win-x64.exe.sha256
ImeTool-win-x64-lite.exe
ImeTool-win-x64-lite.exe.sha256
ImeTool-win-x64-lite.zip
ImeTool-win-x64-lite.zip.sha256
```

## 技术栈

- .NET 9
- WPF
- WPF-UI
- Win32 User32 / IMM32 / UI Automation
- xUnit

## 项目结构

```text
ImeTool
├─ src/ImeTool          应用程序
├─ tests/ImeTool.Tests  自动化测试
└─ ImeTool.sln          Visual Studio 解决方案
```
