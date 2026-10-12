param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$RevitInstallDir = 'C:\Program Files\Autodesk\Revit 2023',
    [switch]$RunSmokeTests
)
$ErrorActionPreference = 'Stop'
$buildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($buildCommand) { $buildExecutable = $buildCommand.Source }
else {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath)) { throw 'MSBuild not found. Install Visual Studio with .NET Framework 4.8 targeting pack.' }
    $buildExecutable = & $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $buildExecutable) { throw 'MSBuild not found in Visual Studio.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $RevitInstallDir 'RevitAPI.dll'))) { throw "RevitAPI.dll not found in $RevitInstallDir" }
& $buildExecutable (Join-Path $PSScriptRoot 'MEP_Export_BOM_From_View_ver1.csproj') /t:Build "/p:Configuration=$Configuration" "/p:RevitInstallDir=$RevitInstallDir" /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Add-in build failed: $LASTEXITCODE" }
if ($RunSmokeTests) {
    & $buildExecutable (Join-Path $PSScriptRoot 'Tests\SmokeTests.csproj') /t:Build /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Smoke test build failed: $LASTEXITCODE" }
    & (Join-Path $PSScriptRoot 'obj\SmokeTests\SmokeTests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed: $LASTEXITCODE" }
}
