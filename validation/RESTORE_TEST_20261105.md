# Immediate Windows restore test — 5 November 2026

## Delivered behavior

After native restore returns successfully, clear stale preview mappings and
open a fresh independent COOKED DirectInput connection. Mode and local reader
range 0..65535 are set/read back and checked during polling. The separate RAW
setup connection is retained. The calibrated reader has a separate backend
identity and refuses Apply. This adds local reader properties only, not any
new DIPROP_CALIBRATION setter, driver, firmware or game-file change.

Fresh readback exact-compares backup version/scope, controller instance/path,
name, sorted axis/object IDs and all calibration triples. Matching values show
Windows readback matches backup, plus actual Windows calibrated input and
percentage. Polling keeps checking; differing values/unavailable input clear
instruments and report failed/unavailable. No stale raw fallback. Existing
range, mode, identity, disconnect and timing guards remain.

Retain identified same-session role/axis/direction labels only, never old scale
endpoints. Without known role metadata show HID axis names. Known inversion
changes gauge orientation, with the actual calibrated numeric value still shown.
No new maximum selection or Apply is needed for testing. Apply and fine brake
adjustment remain disabled; explicit new setup is available separately.

## Evidence

Actual production WPF component harness PASS: synthetic exact native snapshot
and COOKED endpoint samples render 0/100%, unknown axes aren't guessed, known
inverted brake labels/direction are retained, changed calibration refused, failed
verification clears gauges/success state, Apply remains disabled. Additional
actual calibrated-reader Apply guard PASS before native calls. Existing reset/
profile archive preservation checks also PASS in the same focused executable.
No window Show/Run/Loaded or native connection in this harness.

Windows build/publish succeeded, 0 warnings/errors in build and no diagnostics
in publish. Raw logs ../tmp/meca-restore-test-20261105-checks.log and -checks-r2.log,
-publish.log. The second focused run includes the newly added read-only-Apply guard.

One distinct native read-only probe attempted the exact attached backup target
using current product source, an owned nonshown WPF handle and public DirectInput.
Open RAW failed Supported MECA CUP1 controller not found BEFORE COOKED mode/range
setup. Raw failure retained in ../tmp/meca-restore-test-20261105-native.log and
native-reader project. No retry, Apply, persistent calibration setter, SendNotify,
user-app closure or physical trial. This does NOT prove controller absent or
identify driver/access/root cause. Live COOKED reader, full interactive restore
and physical/game behavior remain UNVERIFIED. Package is a private test candidate.

Public SDK reference (reviewed for this change):
https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417929(v=vs.85)
COOKED requests actual calibrated data; RAW bypasses processing. DIPROP_RANGE
sets the reader output range, with verification/refusal for unsupported devices.
No claim is made that all games choose this mode or range.
