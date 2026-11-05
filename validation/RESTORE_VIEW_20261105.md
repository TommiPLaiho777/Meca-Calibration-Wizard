# Windows restore view correction — 5 November 2026

## Problem and behavior

Owner supplied native backup and screenshots show successful restore/readback.
The prior interpretation that restore did not start is superseded. RAW gauges
still used the previous PedalSetup mappings; those were not Windows readback.
The old Apply could overwrite restored values with that stale preview.

Successful restore now clears those mappings/instruments/brake adjustment and
Apply readiness, labels the completed Windows restore, and says no additional
Apply is required. Identification/setup is available for choosing new maximums.
Native backups have axes, not named roles, so roles are never inferred.
The old app settings are archived byte-for-byte without overwrite to a unique
Windows backups/MECA-app-before-restore file, preventing automatic startup offer.
Archive failure is explicitly reported after restore success and preserves the
old file. Cancel and native restore failure leave the existing session/profile.

## Verification

Actual production WPF App resources and MainWindow components instantiated on
an STA thread WITHOUT Run/Show/Loaded or a controller connection. Focused test
invokes actual reset with stale setup/gauges/Apply/brake controls populated:
all clear; later Poll and queued Apply cannot revive them; restored-state text
requires no additional Apply. Actual archive helper preserves exact bytes,
accepts absent profile, refuses destination collision without changing either
file. PASS tests/RestoreViewChecks. No geometry, device or Windows calibration
write. First harness attempt lacked application resources (BaseBrush); preserved
failure log, then loaded production App resources without startup and passed.

Windows self-contained publish succeeds with no warnings/errors in log.
Native setter/transaction unchanged; no repeated native mutation, game test or
full interactive GUI restore performed. The reset test checks actual WPF control
properties, not visual rendering or proof of physical role correctness.
Raw logs: ../tmp/meca-restore-view-20261105-checks.log (harness failure),
-checks-r2.log (PASS), -publish.log. Private package and hash receipt are in
../tmp/meca-restore-view-20261105. Previous packages/backups remain untouched.
