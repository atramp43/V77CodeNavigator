param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot "builds/$Version-portable/V77CodeNavigator"
$destination = Join-Path $PSScriptRoot 'Release/V77CodeNavigator'
$exe = Get-Item -LiteralPath (Join-Path $source 'V77CodeNavigator.exe')
if (($exe.VersionInfo.ProductVersion -split '\+', 2)[0] -ne $Version.Substring(1)) {
    throw "The executable version does not match $Version."
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
robocopy $source $destination /E /XJ /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) {
    throw "Could not copy the portable release (robocopy exit code $LASTEXITCODE)."
}
$global:LASTEXITCODE = 0

Write-Output (Join-Path $destination 'V77CodeNavigator.exe')
