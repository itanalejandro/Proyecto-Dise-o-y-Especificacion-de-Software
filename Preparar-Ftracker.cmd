@echo off
setlocal
cd /d "%~dp0"
set "FTRACKER_DOTNET=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "FTRACKER_DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
  ) else (
    echo Instala el SDK de .NET 10 para Windows y vuelve a ejecutar este archivo.
    pause
    exit /b 1
  )
)
echo Preparando Ftracker. La primera vez requiere internet para los paquetes.
"%FTRACKER_DOTNET%" restore Ftracker.sln --configfile NuGet.config
if errorlevel 1 goto :error
"%FTRACKER_DOTNET%" build Ftracker.sln --no-restore
if errorlevel 1 goto :error
start "" "src\Ftracker.Desktop\bin\Debug\net10.0-windows\Ftracker.exe"
exit /b 0
:error
echo No se pudo preparar Ftracker. Revisa el mensaje anterior.
pause
exit /b 1
