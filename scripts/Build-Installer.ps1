# Optional compatibility wrapper. Generar-Instalador.cmd does not use PowerShell.
[CmdletBinding()]
param(
    [string]$Version = '3.0.0-beta.1',
    [switch]$PortableOnly,
    [switch]$SkipTests,
    [string]$IsccPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$command = @('run', '--project', (Join-Path $root 'tools\Ftracker.Packaging'), '--', '--version', $Version)
if ($PortableOnly) { $command += '--portable-only' }
if ($SkipTests) { $command += '--skip-tests' }
if ($IsccPath) { $command += @('--iscc', $IsccPath) }
& dotnet.exe @command
exit $LASTEXITCODE
