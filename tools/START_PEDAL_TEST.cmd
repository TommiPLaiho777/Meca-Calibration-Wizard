@echo off
setlocal
cd /d "%~dp0"
if not exist "MecaCalibrationWizard.exe" (
  echo Could not find MecaCalibrationWizard.exe in this folder.
  echo Extract the entire Windows release ZIP to a new folder and retry.
  pause
  exit /b 1
)
echo MECA Calibration Wizard v0.2.2 - READ ONLY diagnostic preview
echo No pedal or Windows calibration changes will be made.
start "" "%~dp0MecaCalibrationWizard.exe"
if errorlevel 1 (
  echo Could not launch the program.
  pause
  exit /b 1
)
endlocal
