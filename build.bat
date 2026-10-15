@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 8 SDK is required. Download it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
echo Checking read-only source constraints...
where py >nul 2>&1
if not errorlevel 1 (
    py -3 .\tools\check_read_only.py
    if errorlevel 1 exit /b 1
)
echo Checking calibration algorithm...
dotnet run --project .\tests\GuidedCalibrationChecks.csproj -c Release
if errorlevel 1 (
    echo ALGORITHM CHECKS FAILED. Please copy the error messages.
    pause
    exit /b 1
)
echo.
echo Building MECA Calibration Wizard v0.2.2...
dotnet publish .\src\MecaCalibrationWizard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\release
if errorlevel 1 (
    echo BUILD FAILED. Please copy the error messages.
    pause
    exit /b 1
)
echo.
echo Build complete: release\MecaCalibrationWizard.exe
pause
