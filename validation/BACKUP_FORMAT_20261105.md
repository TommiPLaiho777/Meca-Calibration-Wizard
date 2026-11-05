# Restore explains historical JSON formats — 5 November 2026

User field report: Restore Windows backup with an older JSON displayed
"The JSON property 'App' could not be mapped". The supplied header identifies
MECA Calibration Wizard v0.2.3 / READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS.
The correct behavior is refusal: an old cooked-input preview is not a native
DirectInput calibration backup. The defect was the unhelpful internal message.

The actual LoadBackup reader now checks the root format and reports old preview
or app-profile selection explicitly. It does not convert values or ignore unknown
native fields. Malformed/type/unknown-field errors receive a plain explanation.
Existing snapshot version, identity, range and duplicate-axis gates remain.
Restore still calls this reader before any Apply; refused files remain unchanged.
The Windows backup dialog title and Restore tooltip clarify the required format.

Focused actual C# reader tests PASS: historical App/Status report, app settings,
corrupt JSON, array root, unknown native field, wrong Scope type, unsupported
version and duplicate axes refuse; native backup round trip preserves exact
instance/interface/axis values. Every refused input file remains byte-identical.
The existing two/three-pedal, brake adjustment, JSON persistence and fake-backend
Windows transaction/rollback checks also PASS. Tests use disposable files and
synthetic data only; no DirectInput connection or native settings mutation.

Windows publish/package results are recorded in the private workspace
 tmp/meca-backup-format-20261105. Full updated native GUI and game behavior are
not newly tested. Earlier bounded native calibration read/write/restore evidence
remains historical for its exact build. No firmware/driver/game/registry writes,
user app closure or remote publication is part of this correction. Older ZIPs,
real Windows backups and original reports remain unchanged.
