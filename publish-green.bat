@echo off
setlocal
cd /d "%~dp0"
set "PAUSE_AT_END=1"
if /I "%~1"=="--no-pause" set "PAUSE_AT_END=0"

echo [1/3] Check dotnet ...
where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet not found. Please install .NET 8 SDK first.
  if "%PAUSE_AT_END%"=="1" pause
  exit /b 1
)

echo [2/3] Publish green package ...
dotnet publish ".\RemoteRDPTool.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:InvariantGlobalization=true -o ".\dist\green\win-x64"
if errorlevel 1 (
  echo Publish failed.
  if "%PAUSE_AT_END%"=="1" pause
  exit /b 1
)

set "OUTPUT_DIR=%cd%\dist\green\win-x64"
set "DEV_CONFIG=%cd%\rdp-config.json"

if not exist "%DEV_CONFIG%" set "DEV_CONFIG=%cd%\bin\Debug\net8.0\rdp-config.json"

if exist "%DEV_CONFIG%" copy /Y "%DEV_CONFIG%" "%OUTPUT_DIR%\rdp-config.json" >nul

if not exist "%OUTPUT_DIR%\rdp-config.json" (
  (
    echo {
    echo   "groups": [
    echo     {
    echo       "name": "\u9ed8\u8ba4",
    echo       "connections": []
    echo     }
    echo   ],
    echo   "settings": {
    echo     "autoReducePingFrequency": true,
    echo     "pingIntervalSeconds": 5,
    echo     "reducedPingIntervalSeconds": 8
    echo   }
    echo }
  )>"%OUTPUT_DIR%\rdp-config.json"
)

if exist "%OUTPUT_DIR%\rdp-connections.json" del /f /q "%OUTPUT_DIR%\rdp-connections.json" >nul 2>nul
if exist "%OUTPUT_DIR%\rdp-settings.json" del /f /q "%OUTPUT_DIR%\rdp-settings.json" >nul 2>nul

echo [3/3] Done
echo Output: %cd%\dist\green\win-x64
echo You can distribute this folder directly.
if "%PAUSE_AT_END%"=="1" pause
exit /b 0
