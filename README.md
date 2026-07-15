# ImeTool

Windows 输入法状态提示工具，在文本光标旁显示中文、英文及大写锁定状态，并按窗口自动记忆和恢复输入法状态。

![ImeTool 图标](src/ImeTool/Assets/AppIconPreview.png)

## 功能

- 在文本光标附近显示中文、英文和大写锁定状态
- 按顶层窗口独立记忆并恢复中英文输入状态
- 自动恢复会读回验证实际中英模式，并为现代 TSF 输入法提供 Shift 兼容回退
- 提供窗口记忆总开关、运行期窗口列表和单窗口独立开关
- 窗口记忆可选 JSON 持久化，并支持自定义存储路径
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
- 安装程序会在需要时自动下载并安装 .NET 9 Desktop Runtime x64；此时系统可能显示 UAC 并要求管理员权限
- 从源码构建需要 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## 下载版本

| 下载 | 体积 | 运行要求 |
| --- | ---: | --- |
| [`ImeTool_Windows_x64.exe`](https://github.com/yixing233/ImeTool/releases/latest/download/ImeTool_Windows_x64.exe) | 约 7.2 MB | Windows 10/11 x64 |

ImeTool 本体默认按当前用户安装到 `%LocalAppData%\Programs\ImeTool`，不需要管理员权限，并提供开始菜单快捷方式和标准卸载入口。若系统缺少 .NET 9 Desktop Runtime，安装该系统运行库时可能需要管理员权限。用户设置仍保存在 `%AppData%\ImeTool`，覆盖安装或升级不会删除设置。

> 从 v1.0.5 或更早的 ZIP 版本迁移时，需要手动运行一次安装程序。安装完成后的版本将使用安装包自动更新。

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

```powershell
dotnet publish src\ImeTool\ImeTool.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o artifacts\installer-publish

.\installer\build-installer.ps1 `
  -Version 1.0.15 `
  -PublishDir artifacts\installer-publish `
  -OutputDir artifacts\installer
```

构建安装包需要 Inno Setup 6，结果位于：

```text
artifacts/installer/ImeTool_Windows_x64.exe
```

### 创建 GitHub Release

项目通过 GitHub Actions 自动构建 Release。发布新版本时：

1. 更新 `src/ImeTool/ImeTool.csproj` 中的 `Version`。
2. 提交代码并创建对应的版本标签。
3. 推送标签，工作流会自动测试并发布 `ImeTool_Windows_x64.exe`。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

应用优先通过以下不受 GitHub REST API 配额影响的页面检查最新版：

```text
https://github.com/yixing233/ImeTool/releases/latest
```

Release 只包含：

```text
ImeTool_Windows_x64.exe
```

客户端使用 GitHub 提供的 Release Asset SHA-256 digest 校验安装包，然后以静默升级模式启动 Inno Setup。

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
├─ installer            Inno Setup 安装与构建脚本
└─ ImeTool.sln          Visual Studio 解决方案
```
