<#
.SYNOPSIS
  Instala el servidor de Ftracker (API + SQLite) como servicio de Windows.
.DESCRIPTION
  Ejecutar como administrador, en el equipo que hara de servidor. Copia la API publicada, crea (si falta) un certificado
  propio, registra el servicio con inicio automatico y abre el puerto solo para la red indicada.
  NOTA: este script no se ha probado en una maquina real; ensayalo primero en una PC de prueba.
.EXAMPLE
  .\Instalar-Servidor.ps1 -PublishDir C:\publicado\api -DnsName ftracker-prefectura -AllowedRemoteAddress 192.168.10.0/24
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$DnsName,
    [string]$InstallDir = 'C:\Ftracker\Api',
    [string]$DataDirectory = 'C:\ProgramData\Ftracker',
    [int]$Port = 8443,
    [string]$AllowedRemoteAddress = 'LocalSubnet',
    [string]$ServiceName = 'Ftracker API'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta este script como administrador.' }
if (-not (Test-Path (Join-Path $PublishDir 'Ftracker.Api.exe'))) { throw "No se encontro Ftracker.Api.exe en $PublishDir. Publica primero la API (ver docs\servidor.md)." }

Write-Host '1/5 Copiando la API...'
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { Stop-Service -Name $ServiceName -Force; Start-Sleep -Seconds 2 }
New-Item -ItemType Directory -Force -Path $InstallDir, $DataDirectory | Out-Null
robocopy $PublishDir $InstallDir /MIR /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Fallo la copia de archivos (robocopy $LASTEXITCODE)." }

Write-Host '2/5 Certificado del servidor...'
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq 'CN=ftracker-servidor' -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Subject 'CN=ftracker-servidor' -DnsName $DnsName -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(3) -KeyExportPolicy NonExportable
}
$sha = [System.Security.Cryptography.SHA256]::Create()
$fingerprint = ($sha.ComputeHash($cert.RawData) | ForEach-Object { $_.ToString('X2') }) -join ''

Write-Host '3/5 Registrando el servicio de Windows...'
$exe = Join-Path $InstallDir 'Ftracker.Api.exe'
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName 'Ftracker API' -Description 'API y base de datos compartida de Ftracker' -StartupType Automatic | Out-Null
}
# Si el servicio falla, Windows lo reinicia solo.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
$urls = "https://0.0.0.0:$Port"
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -PropertyType MultiString -Force `
    -Value @("ASPNETCORE_ENVIRONMENT=Production", "Ftracker__DataDirectory=$DataDirectory", "Kestrel__Endpoints__Https__Url=$urls") | Out-Null

Write-Host '4/5 Abriendo el puerto solo para la red indicada...'
Get-NetFirewallRule -DisplayName $ServiceName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ServiceName -Direction Inbound -Protocol TCP -LocalPort $Port -RemoteAddress $AllowedRemoteAddress -Action Allow | Out-Null

Write-Host '5/5 Iniciando el servicio...'
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3
$state = (Get-Service -Name $ServiceName).Status

Write-Host ''
Write-Host "Servicio '$ServiceName': $state"
Write-Host "Direccion para la aplicacion:  https://${DnsName}:$Port"
Write-Host "Huella SHA-256 del certificado: $fingerprint"
Write-Host 'Escribe ambos en la pantalla de acceso de Ftracker (boton "Usar un servidor de prefectura...").'
Write-Host "Datos (base y evidencias locales): $DataDirectory"
if ($state -ne 'Running') { Write-Warning 'El servicio no quedo en ejecucion. Revisa el Visor de eventos de Windows (Aplicacion).' }
