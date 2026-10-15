# MECA Calibration Wizard v0.2.2 — Test Plan

## Automated / hardware-free tests

- [ ] `python3 tools/check_read_only.py` passes (heuristic only).
- [ ] `dotnet run --project tests/GuidedCalibrationChecks.csproj -c Release` passes.
- [ ] Real v0.2.1 brake fixture: 172 samples, 30 distinct values, press/release recognized, 65535 limit reached.
- [ ] Real v0.2.1 throttle fixture: 172 samples, 22 distinct values, press/release recognized, 65535 limit reached.
- [ ] Synthetic no-movement, truncated and missing-release traces fail the full-sweep assessment.
- [ ] Windows CI builds executable and publishes downloadable ZIP (requires new GitHub repo).
- [ ] User-interface XAML compiles on Windows after repository build.

## On owner's MECA CUP1 rig — only after return

- [ ] Brake maps to physical brake, throttle to physical throttle; unused clutch does not auto-activate.
- [ ] Run three-press measurements on both pedals.
- [ ] Run 8-second brake and throttle sweeps with visible lines.
- [ ] Status message is sensible for each input sweep.
- [ ] Exported JSON includes new `Sweeps.Brake.Assessment` and `Sweeps.Throttle.Assessment`.
- [ ] Original Windows/DIView/ACC calibration remains unchanged.
- [ ] Device disconnect/reconnect does not crash the application.

## Deliberately out of scope

- [ ] No Windows registry calibration writes.
- [ ] No firmware/HID-feature reports.
- [ ] No automatic interaction with physical pedals.
- [ ] No trail-braking scoring or feedback.
