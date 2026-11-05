# Safety and contribution boundaries

This beta intentionally supports explicit **Windows DirectInput calibration**
for the identified MECA CUP1 interface family. It is not a firmware editor or
an unrestricted device configuration tool.

- Persistent native calibration writes belong only in the reviewed Apply/Restore
  transaction: exact identity, current before-image, durable backup, explicit
  user confirmation, notification, readback and attempted verified rollback.
- The separate COOKED verification reader cannot Apply. RAW preview JSON must
  never be reinterpreted as native calibration or a restorable Windows backup.
- Do not install drivers, issue HID feature/output reports, guess/write registry
  calibration branches, modify firmware, game files or other devices.
- Preserve input/session/range/timing/identity protections; fail closed on stale
  or ambiguous context. Never conceal an incomplete rollback.
- Never claim DirectInput/WinMM signals prove ADC-level readings or pedal force.
- Do not bypass electrical protective earth or prescribe mains-wiring changes.
- Do not post native backup JSON publicly; it contains device instance/path.
- Physical device/game tests must be recorded separately from synthetic checks.

The source-call heuristic is a regression alarm, not a security certification
or a statement that this Windows-calibration beta is entirely read-only.
If you discover a security problem, do not post exploit details to public issues.
