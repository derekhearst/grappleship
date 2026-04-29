<#
.SYNOPSIS
    Launches the s&box editor on the GrappleShip project.

.DESCRIPTION
    Resolves grappleship/grappleship.sbproj and launches it via sbox-dev.exe.
    Searches common Steam library paths first to work around the known broken
    Steam "s&box editor" shortcut (Facepunch/sbox-public#10086).
    Falls back to Windows file association if sbox-dev.exe can't be located.

.EXAMPLE
    pwsh ./scripts/start-editor.ps1

.EXAMPLE
    pwsh ./scripts/start-editor.ps1 -Verbose
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SbProj      = Join-Path $ProjectRoot "grappleship\grappleship.sbproj"

if ( -not (Test-Path $SbProj) ) {
    Write-Error "Could not find $SbProj. Has the s&box editor scaffolded the project yet?"
    exit 1
}

# Try to discover Steam install path from the registry, then check its libraryfolders.vdf
function Get-SteamLibraries {
    $libs = @()
    try {
        $steamPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction Stop).SteamPath
        if ( $steamPath ) { $libs += $steamPath }

        $vdf = Join-Path $steamPath "config\libraryfolders.vdf"
        if ( Test-Path $vdf ) {
            $content = Get-Content $vdf -Raw
            $matches = [regex]::Matches($content, '"path"\s+"([^"]+)"')
            foreach ( $m in $matches ) {
                $p = $m.Groups[1].Value -replace '\\\\','\'
                if ( $libs -notcontains $p ) { $libs += $p }
            }
        }
    } catch {
        Write-Verbose "Steam registry lookup failed: $_"
    }
    return $libs
}

# Hard-coded fallbacks in case registry lookup misses
$Fallbacks = @(
    "C:\Program Files (x86)\Steam",
    "C:\Program Files\Steam",
    "D:\SteamLibrary",
    "E:\SteamLibrary",
    "F:\SteamLibrary"
)

$Libraries = (Get-SteamLibraries) + $Fallbacks | Select-Object -Unique

$SboxDev = $null
foreach ( $lib in $Libraries ) {
    $candidate = Join-Path $lib "steamapps\common\sbox\bin\win64\sbox-dev.exe"
    if ( Test-Path $candidate ) {
        $SboxDev = $candidate
        break
    }
}

if ( $SboxDev ) {
    Write-Host "Launching: $SboxDev" -ForegroundColor Green
    Write-Host "Project:   $SbProj"
    Start-Process -FilePath $SboxDev -ArgumentList "`"$SbProj`""
} else {
    Write-Host "sbox-dev.exe not found in any known Steam library." -ForegroundColor Yellow
    Write-Host "Falling back to Windows file association: $SbProj"
    Start-Process -FilePath $SbProj
}
