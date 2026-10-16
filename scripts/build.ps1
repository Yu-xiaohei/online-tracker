# 构建脚本：补齐当前会话缺失的 PROGRAMFILES*，避免 NuGet path1 崩溃
if (-not $env:PROGRAMFILES) { $env:PROGRAMFILES = "C:\Program Files" }
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = "C:\Program Files (x86)" }
if (-not $env:NUGET_COMMON_APPLICATION_DATA) { $env:NUGET_COMMON_APPLICATION_DATA = "C:\ProgramData" }

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet restore OnlineTracker.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build OnlineTracker.sln -c Debug -p:Platform=x64
exit $LASTEXITCODE
