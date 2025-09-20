param(
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($Restore) {
        dotnet restore | Write-Host
    }
    dotnet build -c Release | Write-Host
    Write-Host "Build complete: $(Join-Path $PSScriptRoot 'DireWolfMod.dll')"
}
finally {
    Pop-Location
}

