param(
    [string]$Version = "1.0.15",
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\src\ImeTool\bin\Release\net9.0-windows10.0.17763.0\win-x64\publish"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\installer"),
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$scriptPath = Join-Path $PSScriptRoot "ImeTool.iss"
$sourceExe = Join-Path $PublishDir "ImeTool.exe"

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Published executable not found: $sourceExe"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $PSScriptRoot "..\artifacts\tools\InnoSetup\ISCC.exe")
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw "Inno Setup compiler ISCC.exe was not found."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
& $IsccPath "/DMyAppVersion=$Version" "/DPublishDir=$PublishDir" "/DOutputDir=$OutputDir" $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $OutputDir "ImeTool_Windows_x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not created: $installerPath"
}

Get-Item -LiteralPath $installerPath
