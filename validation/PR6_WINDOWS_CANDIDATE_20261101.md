# PR #6 Windows test candidate, 1 November 2026

## Result

A locally built, hash-bound, portable Windows candidate now exists and passed
bounded actual WPF/HID/export acceptance. Physical pedal and interactive sweep
acceptance remain unverified. This is a local private test package, not a beta
release, main merge, or replacement claim for a missing GitHub artifact.

## Exact source and GitHub evidence

Read-only connector review of
[PR #6](https://github.com/TommiPLaiho777/Meca-Calibration-Wizard/pull/6)
found an open draft, not merged. Its reviewed head was
`671e3f4d5e0051e8955db0582baef8ea6277e704`; its base was feature branch
`139cb3a96eae4326c1f6b30d8c773f8c545aa79a`, not main. Main remained
`ca68278e80e921b3f62e1aab5fc491c451e40637`. Repository PRIVATE/MIT.
Metadata listed 321 commits and 30 changed files. The older PR body and twelve
comments were not current-build acceptance; latest comment was 24 October,
with zero reviews and zero inline comments at inspection.

[Actions run 41336375347](https://github.com/TommiPLaiho777/Meca-Calibration-Wizard/actions/runs/41336375347)
completed successfully: verify and Windows build succeeded, artifact upload
was skipped. Its checkout was synthetic merge
`8a06338fae63a087b0a9727487f73f79f82cc4cc`, whose parents were the base and
head above. The local candidate uses precisely that checkout, not a guessed
equivalence to the PR head. The run-specific artifacts endpoint returned zero.
The repository-wide artifacts endpoint was refused by the connector (400,
endpoint not allowed); this audit cannot claim a global artifact inventory.
No workflow rerun, artifact deletion, billing change or remote write occurred.

## Local work performed

An isolated detached validation checkout preserved the earlier MECA delivery
checkout and its untracked global.json. No tracked product source was changed.
An untracked checkout-local global.json pins the already installed SDK 8.0.425.
Runtime I/O, startup, build file, WinMM/SetupAPI paths, export and focused checks
were inspected before execution. The read-only source heuristic passed for ten
C# files; this heuristic is not a complete security proof.

The existing guided-calibration checks ran locally and ended ALL CHECKS PASSED.
Their synthetic/recorded sweeps and WPF source contracts do not establish
physical or interactive GUI behavior. The self-contained win-x64 publish
succeeded with no errors. Existing CS8602 warnings remained in MainWindow at
line 272 and test Program at lines 189 and 336.

Actual tested executable SHA256:
`3248A1D7F0C985106F485F214D6EF26F4F16F00BC35373355BD56E96B2FD5D45`.
Owned PID 5716, creation UTC 18:37:31.4933642, was observed through normal UI
closure using a held process handle: exit code 0 at 18:43:52.7592932 UTC.

Actual GUI observations:

- WPF startup, optional-clutch ON/OFF and diagnostics navigation worked.
  No WinMM controller was selected; Begin remained disabled. This is not proof
  that physical hardware was absent or that every controller path works.
- Export without a controller or HID scan was explicitly refused.
- Two public read-only HID metadata scans returned 17 interfaces, zero without
  VID/PID and six groups. Rescan comparison reported no grouped count changes,
  while retaining the warning that individual devices could have changed.
- Actual SaveFileDialog export produced valid independently parsed JSON with
  Device null, empty Pedals and Sweeps, HID inventory and comparison, and
  WinmmAssociationVerified/UncalibratedSensorInputVerified both false.
- Refresh cleared the old scan/comparison and returned diagnostics to Not scanned.
- No pedal movement, guided capture, driver/firmware/Windows calibration or
  ACC setting change was performed. An unrelated user app was left untouched.

Automation limitations were preserved: a checkbox click overlapping another
app was refused and a visible unobscured coordinate was used; modal cached
indices sometimes failed. SetValue visibly changed the Save dialog field but
the application actually exported to its default Desktop filename, not the
requested workspace path. The success dialog alone was insufficient evidence:
the real file was located, independently parsed and copied byte-exact into
private workspace evidence. The original remains at
`<private-desktop>/meca-preview-20261101-203952.json`.
This routing discrepancy is not attributed to the product without more evidence.

## Package and remaining acceptance

Private workspace evidence is in
`tmp/meca-pr6-validation-20261101` under the shared chat workspace. It includes
checks.log, publish.log, source-sha256.json, owned-process.json, normal-exit.json,
GUI accessibility receipts, the actual report, packaging script and result JSON.

Package: `MECA-PR6-8a06338-Windows-test-20261101.zip`, 65,640,752 bytes.
SHA256: `3600EEB5B0A8757C2890098C54AE573E333B5C21D46CE0AFA6AEEC1477075957`.
ZIP integrity and every packaged file's hash were checked, including the exact
tested EXE. It contains source field-test guides, launcher, MIT license and a
scope-specific READ_ME_FIRST. The private HID report is excluded. Historical
version labels disagree (project/launcher v0.2.2, UI/export v0.2.3); this local
candidate does not rename the product or claim a beta version.

The next substantial safe step is a deliberate read-only physical MECA session
on this exact EXE: identify the intended WinMM controller, exercise guided
release/press/hold/release sweeps, inspect live bars and failure/retry behavior,
and export the resulting measurements. Actual sweep screens, interrupted input
and narrow-window/DPI acceptance remain open. WinMM-HID/physical association and
raw ADC provenance remain unverified; Windows-reported values are not a faithful
current-calibration readback or restorable backup. A beta decision must keep
these limits explicit. No new CI or Rhino test is needed for this package work.
