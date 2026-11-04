<#
.SYNOPSIS
  Quita el servicio de Ftracker y su regla de firewall. NO borra los datos ni el certificado.
#>
[CmdletBinding()]
param([string]$ServiceName = 'Ftracker API', [switch]$RemoveCertificate)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta este script como administrador.' }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}
Get-NetFirewallRule -DisplayName $ServiceName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($RemoveCertificate) { Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq 'CN=ftracker-servidor' } | Remove-Item }
Write-Host 'Servicio y regla de firewall eliminados. Los datos en C:\ProgramData\Ftracker se conservan.'
