@echo off
setlocal
cd /d "%~dp0"
where dotnet.exe >nul 2>&1
if errorlevel 1 (
  echo Se necesita el SDK de .NET 10 para generar la distribucion.
  pause
  exit /b 1
)
dotnet.exe run --project "%~dp0tools\Ftracker.Packaging" -- %*
if errorlevel 1 (
  echo.
  echo No se completo la distribucion. Revisa el error anterior.
  pause
  exit /b 1
)
echo.
pause
