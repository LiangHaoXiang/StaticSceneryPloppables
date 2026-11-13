@echo off
rem StaticSceneryPloppables build: C# first (its deploy step clears the mod folder),
rem then the UI bundle written straight into the mod folder root beside the dlls (MoveIt layout).
rem Env vars are required, see README.md (ModPostProcessor needs .NET6 + proxy bypass).
set "DOTNET_ROOT=C:\Users\10135\.dotnet"
set "HTTP_PROXY=http://127.0.0.1:9"
set "NO_PROXY=localhost,127.0.0.1"
set "DOTNET_ROLL_FORWARD="
set "CSII_USERDATAPATH=C:\Users\10135\AppData\LocalLow\Colossal Order\Cities Skylines II"

rem ---- Safety gate: never build while the game is running. ----
rem Mod.targets deploy step DELETES Mods\StaticSceneryPloppables first, then copies.
rem If the game holds the dll, the copy fails (MSB3231) AFTER the folder was wiped,
rem leaving a broken install (missing main dll / images -> empty panel, white models).
tasklist /FI "IMAGENAME eq Cities2.exe" 2>nul | find /I "Cities2.exe" >nul
if not errorlevel 1 (
    echo.
    echo [ABORT] Cities2.exe is running.
    echo         Close the game completely before building, otherwise the deploy step
    echo         wipes Mods\StaticSceneryPloppables and fails to restore the dll.
    echo.
    exit /b 2
)

cd /d "%~dp0StaticSceneryPloppables"
dotnet build -c Release %*
if errorlevel 1 exit /b 1

cd /d "%~dp0ssp-ui"
call npm run build
