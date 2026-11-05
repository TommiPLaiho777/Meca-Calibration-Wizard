# MECA 0.3.0-beta.2 — quickstart

Extract the complete Windows x64 ZIP into a new folder. Close older MECA copies
and run MecaCalibrationWizard.exe. No installation or extra driver is included.

1. Release all pedals. Select MECA CUP1; select optional clutch only if installed.
2. Start setup and identify all named pedals first.
3. Set pedal maximums: one preferred press/release for each named pedal.
4. Test. Brake Heavier/Lighter changes only brake, in fixed5% steps.
5. Close games, review and confirm Apply to Windows + save. Restart the game.

Use Back up Windows settings to save native calibration before changes.
Restore Windows backup requires that native backup JSON, the same controller,
and explicit confirmation. It makes an automatic backup first. After restore,
see the exact Windows-readback match and live calibrated-input test gauges.
No extra Apply or maximum selection is needed. Unknown roles use axis names.
A failed/unavailable verification clears the gauges and reports the problem.

Do not restore old preview/app-profile JSON as Windows calibration. Those files
use a different format/purpose. Restoring Windows archives the previous app
profile to avoid reloading its stale preview. Existing native backups remain.
Backups/archives: %LOCALAPPDATA%/MECA Pedal Setup/Windows backups.
They include device identifiers; keep them private.

Beta limitations: game raw-input paths may bypass Windows calibration. No
all-games guarantee, physical force measurement, firmware/driver/game-file
changes or telemetry. Optional clutch/disconnect and full automated live-device
acceptance are incomplete. Read README.md for the validation scope. If restore
reports incomplete rollback, preserve the backup and do not use that setup.
