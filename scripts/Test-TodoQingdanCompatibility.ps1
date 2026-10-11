[CmdletBinding()]
param(
    [string]$InstallationPath,
    [string]$DataRoot,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$probe = Join-Path $PSScriptRoot "todo-qingdan-compatibility-probe.js"

function Find-TodoQingdanExecutable {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidate = if (Test-Path -LiteralPath $ExplicitPath -PathType Container) {
            Get-ChildItem -LiteralPath $ExplicitPath -Filter "Todo*.exe" -File |
                Where-Object { $_.Name -notlike "Uninstall*" } |
                Sort-Object Name |
                Select-Object -First 1 -ExpandProperty FullName
        }
        else {
            $ExplicitPath
        }
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
        return $null
    }

    $running = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "Todo*.exe" -and
            $_.ExecutablePath -and
            (Test-Path -LiteralPath $_.ExecutablePath -PathType Leaf)
        } |
        Select-Object -First 1 -ExpandProperty ExecutablePath
    if ($running) { return $running }

    $registryRoots = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($entry in Get-ItemProperty $registryRoots -ErrorAction SilentlyContinue) {
        if ($entry.DisplayName -notlike "*Todo*") { continue }
        $candidates = @($entry.DisplayIcon, $entry.InstallLocation) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object {
                $value = ([string]$_).Trim('"') -replace ',\d+$', ''
                if (Test-Path -LiteralPath $value -PathType Container) {
                    Get-ChildItem -LiteralPath $value -Filter "Todo*.exe" -File |
                        Where-Object { $_.Name -notlike "Uninstall*" } |
                        Select-Object -First 1 -ExpandProperty FullName
                }
                else { $value }
            }
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $programsRoot = Join-Path $env:LOCALAPPDATA "Programs"
    if (Test-Path -LiteralPath $programsRoot -PathType Container) {
        return Get-ChildItem -LiteralPath $programsRoot -Filter "Todo*.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike "Uninstall*" } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    return $null
}

$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    throw "Node.js 22.5 or later is required for the read-only node:sqlite probe."
}

$executable = Find-TodoQingdanExecutable $InstallationPath
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = Join-Path $env:APPDATA "todo-list"
}
$resolvedDataRoot = if (Test-Path -LiteralPath $DataRoot -PathType Container) {
    (Resolve-Path -LiteralPath $DataRoot).Path
}
else { $null }

$version = $null
if ($executable) {
    $version = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
}
$applicationRunning = [bool](Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "Todo*.exe" } |
    Select-Object -First 1)

$arguments = @($probe)
if ($executable) { $arguments += @("--executable", $executable) }
if ($resolvedDataRoot) { $arguments += @("--data-root", $resolvedDataRoot) }
if ($version) { $arguments += @("--version", $version) }
$arguments += @("--application-running", $applicationRunning.ToString().ToLowerInvariant())
$previousNodeOptions = [Environment]::GetEnvironmentVariable("NODE_OPTIONS", "Process")
try {
    [Environment]::SetEnvironmentVariable("NODE_OPTIONS", "--no-warnings", "Process")
    $json = & $node.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Todo清单 compatibility probe exited with code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable("NODE_OPTIONS", $previousNodeOptions, "Process")
}
$report = $json | ConvertFrom-Json

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $fullOutputPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [System.IO.File]::WriteAllText(
        $fullOutputPath,
        (($report | ConvertTo-Json -Depth 20) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}

$report | ConvertTo-Json -Depth 20
