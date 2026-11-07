$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.work\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
New-Item -ItemType Directory -Force -Path logs,builds | Out-Null
dotnet build V77CodeNavigator.sln -c Release --nologo *> logs\build-solution.txt
if ($LASTEXITCODE -ne 0) { Get-Content logs\build-solution.txt; throw 'Build failed' }
dotnet run --project tests\V77Navigator.Tests -c Release --no-build *> logs\tests.txt
if ($LASTEXITCODE -ne 0) { Get-Content logs\tests.txt; throw 'Tests failed' }
dotnet publish src\V77CodeNavigator\V77CodeNavigator.csproj -c Release -r win-x64 --self-contained false -o builds\diagnostic-win-x64 --nologo *> logs\build.txt
if ($LASTEXITCODE -ne 0) { Get-Content logs\build.txt; throw 'Publish failed' }
Get-FileHash -Algorithm SHA256 builds\diagnostic-win-x64\V77CodeNavigator.exe | Format-List | Out-File logs\exe-sha256.txt
Get-Content logs\tests.txt
Write-Output 'Ready: builds\diagnostic-win-x64\V77CodeNavigator.exe'
