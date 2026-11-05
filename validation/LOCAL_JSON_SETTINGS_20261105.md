# Local JSON settings — 5 November 2026

Owner explicitly selected own JSON storage for Apply/save and backup.
Implemented two buttons and explicit startup reload in the existing window.
Backend uses versioned validated app-only JSON and same-directory atomic file
publication; it does not invoke any hardware/Windows writer.

Actual C# tests, generated controller samples and disposable workspace files:
- Full two/three-pedal workflow, original capture and adjusted endpoint round trip.
- Inverted brake endpoints round trip; fixed adjustment behavior retained.
- Exact-byte backup ignores unsaved preview changes and refuses the active path.
- Controller slot/range mismatch, unsupported schema, duplicate role/axis and
  out-of-range endpoint rejected without replacing saved settings.
- Locked destination failure preserves old bytes; retry/replacement succeeds;
  temporary files cleaned; corrupt backup source preserves an earlier backup.
- Existing sample timing, duplicate timestamp, bounds, release dropout, gradual
  release, isolated peak and session invalidation checks still PASS.

Initial test-helper exception filter omitted InvalidDataException and failed on
an expected controller mismatch refusal. Corrected the helper, not the product
guard; later runs passed. Final logs are in parent tmp/meca-json-20261105.

Windows self-contained publish succeeded. Owned native startup showed both
buttons disabled before a completed setup / first saved file; window closed
normally (see owned.json/exit.json). Screenshot captured Codex instead of target,
so only accessibility/control-state evidence is claimed. Actual GUI Save/Backup,
startup reload modal and physical pedals were not exercised; final guard edits
retain compilation and offline evidence only. No previous user settings/package
or device/Windows calibration was changed by these tests.
