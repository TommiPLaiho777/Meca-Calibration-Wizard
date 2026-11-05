# Actual Windows calibration integration — 5 November 2026

Owner requested multi-game Windows calibration, then explicitly approved the
bounded native test: save current calibration, change Z Rotation maximum18800
to18801, verify a fresh DirectInput connection, restore18800 and verify all axes.
An initial automatic approval review rejected execution because that precise
mutation was not yet explicitly approved. No action ran on rejection. After the
owner answered yes to that exact test, the reviewed execution was approved.

Native result: PASS_NATIVE_WRITE_FRESH_READ_RESTORE at01:27:14UTC. Exact observed
MECA CUP1 instance/HID path and current Z Rotation triple were required. Native
before-image saved; only the requested maximum changed. A fresh DirectInput
connection observed18801. Restoration returned18800; every original axis triple
matched in a fresh connection. Other axes unchanged. Exit0; no game, firmware,
driver or direct-registry manipulation. Raw report/backups/exact fixture and plan
are private workspace tmp/meca-windows-calibration-20261105.

Earlier distinct read probes established the actual DIPROP_CALIBRATION getter,
WinMM-ID/HID-path association and the actual product raw-reader format. Five
uncooked samples read successfully and calibration before/after was identical.
Local raw-mode selection is not a persistent calibration write. The raw backend
now verifies RAW mode per axis and refuses lost input/mode/identity.

Existing C# workflow/JSON checks PASS. New actual C# transaction checks PASS for
backup-before-write, current-before-image/controller refusal, changed-axis-only
writes, ignored setter/readback, setter/notification failure rollback, incomplete
rollback reporting and native-backup JSON round trip/corruption refusal. These
are fake-backend/disposable-file tests, distinct from the native result above.

Windows GUI candidate started with MECA CUP1, Windows calibration header, Apply
disabled before setup, and enabled backup/restore. The user interacted with the
window; a Windows-backup success dialog was observed through screenshot and
accessibility. Its output path/bytes were not independently located, so this is
bounded UI field evidence. User-used PID26540 is preserved; no automatic close
or normal exit is claimed while its watcher is pending. Final minor title and
invalid-session button guards were compiled after this observation.

Windows self-contained publish succeeded; NU1900 reports unavailable online
vulnerability metadata, not a completed vulnerability audit. No new physical
pedal-role/optional-clutch/disconnect/full UI Apply or actual game assertion PASS
is claimed. The supported scope is this MECA CUP1 interface family with readable
native calibration and nonnegative raw axes. Games bypassing Windows calibration
remain a separate compatibility limitation. Old packages are preserved.

Primary API references:
- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417908(v=vs.85)
- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417929(v=vs.85)
- https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nn-dinputd-idirectinputjoyconfig8
- https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nf-dinputd-idirectinputjoyconfig8-setcooperativelevel
- https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nf-dinputd-idirectinputjoyconfig8-unacquire
- https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/dinput.h
