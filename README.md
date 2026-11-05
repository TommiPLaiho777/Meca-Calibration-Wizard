# MECA Calibration Wizard — 0.3.0-beta.1

A compact Windows pedal-calibration application for **MECA CUP1**: identify your
pedals, choose comfortable maximums, test them, then explicitly apply Windows
calibration. Brake, throttle and optional clutch are supported. Independent
project, MIT licensed; no affiliation with MECA or Leo Bodnar is implied.

## Download and run

Download the Windows x64 ZIP from [Releases](https://github.com/TommiPLaiho777/Meca-Calibration-Wizard/releases),
extract **all files into a new folder**, close older MECA copies, and run
`MecaCalibrationWizard.exe`. The .NET runtime is included. Keep older packages
and Windows calibration backups. See [the quickstart](docs/BETA_QUICKSTART.md).
The normal Windows HID game-controller driver/DirectInput is used; this app
installs no driver, firmware or virtual controller.

## Pedal setup

1. Select MECA CUP1 and enable clutch only if installed. Release every pedal.
2. Start setup. Identify **all** enabled pedals with each named press/release.
3. Select **Set pedal maximums**. Press each named pedal once to its preferred
   maximum and release. The app locks the axis identified for that role.
4. Test the named pedals. Optional brake-only **Heavier / Lighter** changes the
   chosen brake span in fixed 5% steps within validated bounds.
5. Close games. Select **Apply to Windows + save**, review the endpoints and
   confirm. Current Windows values are backed up before any write; the app
   notifies Windows and requires exact readback. Failure attempts restoration;
   incomplete restoration is reported explicitly. Restart the game to test.

Setup gauges before Apply are a preview. Local RAW DirectInput input does not
measure physical force or establish ADC-level sensor readings.

## Back up, restore and verify

**Back up Windows settings** saves the current native calibration. **Restore
Windows backup** accepts that native JSON for the same controller/axis identity,
backs up current values, asks for confirmation, restores and reads back.

After Restore, dismiss the result message to see immediate verification:
Windows values must match the selected backup and live gauges read Windows
**calibrated** DirectInput input. No extra Apply or maximum selection is needed.
Known same-session pedal labels/direction are retained; unknown physical roles
show HID-axis names. An unavailable reader or mismatch clears gauges and shows
an explicit failure/unavailable state, never an old preview as proof of success.
The displayed percentage rounds to whole numbers; the numeric Windows input
is also shown. Apply and brake adjustment are disabled during verification.

Native backups do not contain named pedal roles. Historical `meca-preview`
reports and app `settings.json` are **not** Windows backups and cannot be used
as native calibration. Old app settings are preserved in a separate archive
after restore, preventing stale startup reload; archive failure is explicit.

Automatic native before-change backups and archived app settings are under:
`%LOCALAPPDATA%/MECA Pedal Setup/Windows backups`.
Keep backups private: they include the controller instance and interface path.

## Beta validation and limitations

- Actual C# setup/range/timing/JSON/rollback and WPF control-state checks pass;
  Windows self-contained publish and ZIP contents are verified.
- A separately approved native test verified backup, one-axis write, notification,
  fresh-interface readback and exact restoration. It is a bounded backend test.
- The owner reports the main workflow works and the restore gauge reaches 100%.
  This is user field evidence, not a claim of complete acceptance on all devices.
- The new COOKED reader's automated native probe did not find the supported
  target. Its cause remains unknown. Synthetic WPF tests do not replace live
  device verification. Optional physical clutch/disconnect acceptance remains
  unverified. See [validation notes](validation/RESTORE_TEST_20261105.md).
- Games can use raw input or their own processing and bypass Windows calibration.
  There is **no all-games guarantee** or independently completed ACC acceptance.
- No firmware, guessed registry, driver installation, game-file modification,
  force measurement, RPM telemetry or active driving intervention.

This release is a **beta prerelease**, not a stable or certified product.

## Build and checks

Requires the .NET 8 SDK. On Windows, `build.bat` runs the current hardware-free
setup checks and publishes the app. For explicit commands:

```powershell
$env:MECA_TEST_SCRATCH = Join-Path $PWD 'release-checks'
dotnet run --project tests/PedalSetupChecks.csproj -c Release
dotnet run --project tests/RestoreViewChecks/RestoreViewChecks.csproj -c Release
dotnet publish src/MecaCalibrationWizard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o release
```

The first test project also runs on Linux without a controller; WPF checks
require Windows. CI uses GitHub-hosted runners and simulated data only. Uploads
remain manual-dispatch-only. Historical diagnostics/tests under `historical/`
and older validation notes describe past versions, not current beta features.
