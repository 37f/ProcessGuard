param([string]$Dotnet = 'dotnet', [string]$Output = (Join-Path $PSScriptRoot 'publish'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
# Fail immediately on a failed native build/test command in both PowerShell 5.1 and 7.
function Invoke-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed (exit $LASTEXITCODE): $Arguments" }
}
Push-Location $PSScriptRoot
try {
    $sdkVersion = & $Dotnet --version
    if ($LASTEXITCODE -ne 0 -or [int]($sdkVersion.Split('.')[0]) -lt 8) { throw 'Install .NET SDK 8 or later.' }
    Invoke-Dotnet @('run', '--project', 'tests/ProcessGuard.Tests', '-c', 'Release')
    Invoke-Dotnet @('run', '--project', 'tests/ProcessGuard.Integration', '-c', 'Release')
    Invoke-Dotnet @('run', '--project', 'tests/ProcessGuard.UiTests', '-c', 'Release')
    Invoke-Dotnet @('publish', 'src/ProcessGuard.App', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-o', $Output)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.zh-CN.md') -Destination $Output -Force
    Get-FileHash -LiteralPath (Join-Path $Output 'ProcessGuard.exe') -Algorithm SHA256
} finally { Pop-Location }
