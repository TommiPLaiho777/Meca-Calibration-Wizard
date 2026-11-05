# Historical diagnostics — excluded from the application

These five modules belonged to the earlier HID inventory, report export, sweep and signal-health workflow. The owner removed those features from the current pedal setup application. They are retained here unchanged for historical evidence and rollback, outside `src`, so the application project cannot include them by default.

`tests/GuidedCalibrationChecks.csproj` explicitly links these files for historical diagnostic checks. Some checks in that older suite describe the removed GUI; they are not current GUI acceptance tests. The active workflow regression entry point is `tests/PedalSetupChecks.csproj`.

Do not restore these features without an owner request. Current Windows input access remains read-only in `src/WinmmJoystick.cs`.
