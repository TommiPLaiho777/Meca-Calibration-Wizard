# Development decisions — 4 November 2026

## #PRIORITY — reliable input first

The owner wants a simple, dependable read-only backend, followed by a straightforward calibration interface. DIView is the reliability reference. Reuse the working Windows input code and finish a small, usable workflow before visual polish. Keep the projects independent. Avoid extra frameworks, speculative layers, broad rewrites and repeated tests of unchanged behavior.

## #PEDALS — brake, throttle and optional clutch

All three pedals remain supported in the backend and GUI. Clutch is optional for a two-pedal setup; disabling its display does not mean removing clutch support. Axis roles are identified through actual movement. Preserve captured mappings, session invalidation and the distinction between Windows input values and physical force.

## #UI_REFERENCE — finish styling after the core works

The owner supplied `MECA-pedaalien kalibrointinäkymä.png` and `MECA-pedaalien kalibrointipaneeli.png` as future visual references. They show a dark metal/graphite/white design, clear device status, pedal values and white-faced gauges. Keep the originals unchanged. Work on the appearance after the functional core is dependable. Device, interface and signal status labels must reflect actual evidence. The RPM example is outside the pedal calibration workflow. Clutch support remains required even where a reference image shows only brake and throttle.

## #MAINTENANCE — English, searchable code

Code comments, change records and interface text are in English. Use meaningful searchable comment tags at functional boundaries. Explain responsibilities, units and important state transitions so a human maintainer can update the code. Keep tags stable and avoid tagging every statement.

## #IMPLEMENTED — first simplification

Refresh, controller selection changes and unavailable-input handling previously repeated the same display-reset statements. They now call one `ResetLiveValues()` method in the existing window class. It resets the brake, throttle **and clutch** instruments. Each caller retains its own capture cancellation, session validity, explanatory messages and navigation rules. This removes duplicated reset behavior without changing measurement or selection policy or adding a module/dependency.

The existing source-contract checks now require each lifecycle path to call the shared reset and still check the same reset statements, including clutch. Compile and regression results are recorded in the local validation context. The previous user-tested package remains unchanged; it is the rollback/reference build until a replacement is validated. This extraction does not establish new physical-pedal or GUI runtime evidence.

## #CODE_MAP — current lean application

| File | Responsibility |
| --- | --- |
| `src/WinmmJoystick.cs` | Read-only Windows controller discovery, samples and whole-sample bounds |
| `src/GuidedCalibration.cs` | Rest, sustained press and stable release; sample timing/range/spike safeguards |
| `src/CalibrationModels.cs` | Identify all enabled roles, explicit maximum-selection round, final mappings and brake-only fine adjustment |
| `src/MainWindow.xaml.cs` | Controller/session lifecycle and rendering of the current setup |
| `src/MainWindow.xaml`, `src/App.xaml` | Dark interface and shared white analog gauge |
| `src/App.xaml.cs` | App-owned single-instance lifetime |
| `historical/diagnostics/` | Removed HID/report/sweep/noise modules; not application inputs |

`tests/PedalSetupChecks.csproj` is the current workflow regression entry point.
The older `tests/GuidedCalibrationChecks.csproj` explicitly links archived modules;
its obsolete GUI contracts are historical, not current acceptance checks.
Earlier sections below record development history and may describe removed features.

## #NEXT_DELIVERABLE — morning progress

The owner expects concrete progress by the next morning. Focus that work on a smaller, maintainable working input/calibration core for all three pedal roles and a reviewable runnable result. Keep GUI styling, additional research and optional features deferred. This is a delivery priority, not a claim of continuous execution or a guarantee that remaining acceptance checks have already passed.

## #VALIDATION — this cleanup

The full existing C# checks passed after adapting the existing source contracts to follow the shared reset. They still require all three callers and every original brake/throttle/clutch reset. Initial preparation refused a whole-file reset-count assumption before mutation; later checks caught Windows line-ending sensitivity and one remaining inline-reset assumption. Those refusals/failures were corrected, with final output retained in `tmp/meca-mvp-20261104/simplified-checks-r3.log`. No measurement tolerance or safety assertion was dropped. WPF compilation succeeded with zero errors and the existing nullable warning; output is isolated at `tmp/meca-simplified-20261104/build`. No new native GUI or physical-pedal validation is claimed for this refactor. The previously delivered user-tested package remains unchanged.

### Bounded Windows runtime check after reboot

On 4 November at 01:11–01:12 UTC, the existing build from source `81c00a6` ran in owned process 13540. Its managed DLL SHA256 was `025E54397C6AE9E9069E988AF64E13298D29F39099E419A2B8158967F89294A0`. Actual Windows accessibility observations showed one selected controller and all six streaming axis rows. Clicking the optional clutch checkbox added the clutch mapping row. Clicking Refresh exercised the shared reset route without an exception; the refreshed-session instruction appeared, all three roles remained uncaptured, and current axis values resumed. The process then closed normally with exit code 0. Held-process and exit receipts are in `tmp/meca-simplified-runtime-20261104` in the parent workspace.

This is a bounded runtime routing check. No nonzero mapped pedal instrument, physical clutch, disconnect, invalid-sample path or completed-capture clearing was exercised. Screen captures returned a white client area even though accessibility and input responses were available; therefore visual rendering is **not verified** in this run, and the cause of that capture result is unknown. No replacement package, device/settings write or repeated physical acceptance procedure was performed. The earlier user-tested package stays the reference build.


## #REPORT_FILE — preserve earlier diagnostic exports on failed writes

ReadOnlyHidReport.SaveJson now stages UTF-8 report text in a unique sibling file, then replaces/moves it into the selected path. MainWindow retains all session/current-sample/consent checks and calls this helper only at the final write. A failed staging write or locked replacement must preserve the previous report. This is local-file reliability, not calibration backup/restore or a device write. Actual disposable-filesystem checks and full C# suite passed; WPF build0errors/existing nullable warning. New GUI save-dialog behavior remains unverified and the delivered62b801c package is unchanged. See validation/REPORT_FILE_PROTECTION_20261104.md.

## #PEDAL_WORKFLOW — owner correction, 4 November 2026

The owner requires this simple ordered workflow: identify brake with a physical press, calibrate that identified brake, identify throttle with a physical press, calibrate that throttle, then optionally identify and calibrate clutch. Identification and calibration are distinct phases. The user must not repair reversed roles with a swap button or choose WinMM axis names. Keep one explicit current role and its identified axis through calibration and into display/export. Invalid/disconnected/ambiguous input must still refuse safely. Advanced diagnostics and export must not complicate the primary path.

Owner field correction: pressing brake moved Throttle and accelerator moved Brake; wrong headings were already observed during identification. Earlier JSON traces establish captured values, not correct physical role assignment. Exact root cause remains unverified. Current source and delivered62b use the same role for phase heading/capture/test mapping; this source inspection does not refute the owner's observation or prove the actual executed binary. A proposed swap-button change was compiled and synthetically checked, then rejected by the owner as the wrong workflow; all its tracked changes and added test/doc files were removed. Its isolated tmp/meca-role-correction-20261104 build is NOT a deliverable and must not be packaged or used as the solution. Existing accepted source b383782 and known packages remain preserved. Implement the six-phase workflow using existing input/capture code; avoid a new framework or broad rewrite. Do not describe the reported reversal as fixed until the real flow is verified.


## #LEAN_SCOPE — newest owner instruction implemented

The owner explicitly removed all extra application features: keep pedal identification/calibration and the graphical interface only. This supersedes earlier signal/sweep/HID/export requirements for the active MECA app. Controller selection and session/range/disconnect safeguards remain. The six-phase core and shared dark/white gauge GUI are implemented; see validation/LEAN_PEDAL_SETUP_20261104.md for exact verification and limits. Diagnostics source/tests stay historical and are excluded from the app build. Do not restore them or add profile/export/research scope without a new owner request. The new lean test package supersedes the earlier candidate for this workflow, while preserving the old package unchanged. Physical role correctness is still pending confirmation.

## #TWO_ROUND_SETUP — latest owner clarification

Identify ALL enabled pedals first; do not alternate each identification with calibration. After brake/throttle/optionalclutch identities are known, explicitly begin the maximum-selection round. Ask how much the user wants to press the named pedal; one sustained press to the desired endpoint and release selects its maximum. Show the needle and Windows numeric value during that round. The final test uses the selected endpoint as100%. This supersedes earlier alternating six-phase and three-press UI directions. See validation/TWO_ROUND_SETUP_20261104.md for implementation and evidence limits. The hardware path remains read-only.

## #BRAKE_FINE_ADJUST and #SINGLE_INSTANCE — owner-requested morning test

At final Pedal test only, +Heavier/-Lighter change the brake preview scale in fixed5% steps of the selected brake span, retaining the original capture and other pedal roles. Clamp at Windows endpoints and the existing minimum movement span; refuse invalid sessions. The user requested this feature explicitly after the scope reduction. One app-owned local-session mutex prevents duplicate new copies. Old versions without it must be closed first. Clear numbered identification/maximum-selection/test headings are required. Do not represent the old two-copy observation as a proven sole cause. See validation/TWO_ROUND_SETUP_20261104.md for exact checks and remaining physical verification.

## #LEAN_CLEANUP — owner-authorized source cleanup, 5 November

The owner preliminarily reports that the morning candidate works, including its
brake adjustment endpoint limit. This is useful field feedback, not independent
attestation of physical clutch or disconnect behavior.

Five already-excluded diagnostic modules moved byte-for-byte from `src` to
`historical/diagnostics`. The application now builds only its active source tree
without an exclusion list. Historical checks keep explicit links to the moved
files. This reduces clutter; it does not claim a smaller shipped application,
since those modules were already excluded.

Removed the unused `GuidedCalibration.PreviewPercent` calculation, which scaled
against a growing peak. The active maximum-selection display already uses the
fixed Windows range through `WindowsRangePercent`; final display still uses the
chosen pedal endpoint. Consolidated the identical sustained-press predicate used
when crossing the gradual-release band and when accepting a stable release. It
still requires at least three high samples spanning at least 100 milliseconds;
release timing, isolated-peak rejection and all ranges are unchanged.

No new framework or feature was added. The two-round workflow, optional clutch,
axis locking, brake-only +/- steps, session invalidation and read-only hardware
policy are retained. The morning ZIP remains the field-test reference. Compilation
and offline workflow evidence for this cleanup are separate from native GUI and
physical-pedal validation; see `validation/LEAN_SOURCE_CLEANUP_20261105.md`.

## #REFERENCE_STYLE — owner-requested visual finish, 5 November

Apply the two supplied MECA reference PNGs to the current lean layout: graphite
panels, subtle metallic gradients, white analog faces with clear tick marks, blue
needles and a blue primary action. Keep identification, maximum selection and
final test in their existing order. Do not copy the reference's diagnostics,
reports, navigation, RPM or unverified device/connection claims into the app.

The appearance is implemented in the existing App.xaml resources and shared
PedalGauge template. No bitmap assets, new libraries or appearance framework are
needed. Each gauge renders the same numeric percentage, input reading and needle
for its named role, including optional clutch. The needle now points upward at
50% and sweeps 270 degrees from the lower-left 0 to lower-right 100. Input and
calibration calculations are unchanged. Scroll remains available at small sizes.

## #LOCAL_JSON — owner-authorized settings persistence and backup

The owner explicitly requested Apply/save and backup, then clarified the target
as the application's own JSON file. This supersedes the earlier no-profile scope
only for this small persistence feature. No Windows or controller writes.

`PedalSettingsFile` stores version/scope, the Windows capability context, all
original pedal captures and applied endpoints. The brake's original selected
maximum survives fine adjustment. The fixed file is
`%LOCALAPPDATA%/MECA Pedal Setup/settings.json`. Save requires a complete current
setup, fresh bounded input and no polling/session interruption. A unique sibling
file is flushed before replace/move, preserving the old JSON on write failure.

Backup copies the last saved valid file byte-for-byte to the user-selected JSON;
it does not capture unsaved preview changes and refuses the active file as its
own backup. With no saved file the backup button is disabled. Press backup BEFORE
saving over a version that should be preserved.

On startup (or the first explicit controller selection), a compatible file may
be offered for loading. Exact Windows slot/name/IDs/ranges/fallback provenance
must match, and the user must explicitly confirm the same physical controller.
These Windows fields are not verified physical USB identity. No silent apply
occurs. Invalid schema, duplicate roles/axes, unsupported ranges or incompatible
controller refuse loading. Refresh cancels the offer and keeps the established
fresh-session workflow. Loading keeps the original captures and the brake's
adjusted maximum separately, so later +/- uses the original fixed step.

Backups are ordinary copies of the same JSON format. To restore a backup, close
the app, preserve the current settings.json separately, then copy the chosen
backup to that path and name; the next launch still validates and asks before
loading. There is no hardware calibration restore or game-setting integration.

## #GAME_EFFECTIVE_WINDOWS — owner supersedes preview-only scope

On 5 November the owner explicitly requests actual game-effective calibration
and chooses Windows-wide calibration rather than per-game integration. This
supersedes the read-only-Windows policy ONLY for reviewed, explicitly confirmed
MECA CUP1 DirectInput axis calibration. Firmware, driver installation, guessed
registry paths and unrelated device/settings changes remain outside scope.

Receiving modules: WindowsCalibration and existing MainWindow/PedalSetup. The
product blocker was missing actual calibration readback and uncooked input.
Acceptance: exact controller/axis identity; current native calibration backup;
raw capture without applying old calibration twice; explicit Apply; documented
Windows configuration lock and SendNotify; readback and rollback/restore.

Read-only native probes now bind WinMM joystick0 to the observed MECA CUP1
DirectInput instance and HID interface via DIPROP_JOYSTICKID/GUIDANDPATH. Per-axis
DIPROP_CALIBRATION succeeds for the three observed HID rotation axes; this is a
DIFFERENT public getter from the earlier unavailable JoyConfig8.GetConfig scans.
Those older unavailable results remain true for their exact path and date.

The supported controller is opened nonexclusively with a local RAW input format.
HID usage identifies each input slot, not guessed WinMM X/R names. Persisted
backend identity separates these raw captures from older WinMM-scaled profiles;
old preview mappings cannot be applied as raw Windows calibration. Fresh physical
identification/maximum selection is required for the new backend. Optional clutch
remains supported; its physical acceptance is separate.

Apply shows named pedal raw endpoints, checks known running games and requires
user confirmation that games are closed. Only mapped axes change; midpoint is
halfway between the selected raw endpoints. Greater brake span requires more raw
input for full output. Before any setter, the program acquires configuration mode,
rechecks identity/current values, flushes a native before-image JSON, then sets
DIPROP_CALIBRATION and calls SendNotify. Exact readback is mandatory. Failure
attempts verified restoration under the same lock; failed restoration is explicit.
A separate restore button validates the same controller/axis identity, backs up
the current calibration first and restores the selected native backup.

App JSON and Windows backup JSON have distinct scopes. App profile saving after
Windows apply can fail separately; the message explicitly reports that Windows
was applied if only the subsequent app-file write fails. No all-games guarantee:
DirectInput calibrated readers use Windows calibration by default, but raw-input
games/drivers may bypass it. No game pedal test has been performed by the agent.

References: Microsoft IDirectInputDevice8 GetProperty/SetProperty,
IDirectInputJoyConfig8 SetCooperativeLevel/Acquire/SendNotify/Unacquire and the
Microsoft WinSDK dinput.h projection. See validation/WINDOWS_CALIBRATION_20261105.md
for exact native evidence and the separately approved reversible write test.


## #BACKUP_FORMAT — explain refusal of historical preview reports

The owner selected an older JSON through Restore Windows backup and received
an internal .NET unmapped-property error for App. The supplied App/Status header
identifies the historical READ_ONLY_PREVIEW_NOT_APPLIED_TO_WINDOWS report. That
file contains cooked input observations, not a native Windows calibration backup.
It must never be converted into raw calibration endpoints or silently accepted.

LoadBackup now inspects the JSON root before strict snapshot deserialization.
A historical MECA App report or MECA_APP_PREVIEW_ONLY app settings file receives
a specific explanation directing the owner to Back up Windows settings. Malformed
JSON, wrong property types and unknown native fields remain refused with a plain
message; schema, controller and axis validation remain mandatory. No native
connection/write is needed for this reader. Restore already loads the file before
reading the current snapshot, asking for confirmation or invoking Apply, so this
refusal applies no Windows change. The dialog title and tooltip identify the
required Windows backup. Real backup files and historical reports are preserved.

## #RESTORE_VIEW — clear app preview after verified Windows restore

Owner screenshots show both the restore confirmation and a successful native
readback message. This corrects the earlier report of no visible progress;
it is field evidence, not an independently witnessed Windows/game test.
The remaining product defect was stale RAW-input app gauges/Apply state.

Only after Apply returns successfully, ClearRestoredPreview clears the setup,
gauges, brake adjustment and Apply readiness. It prevents the old saved-profile
offer for the rest of the session, and labels Windows restore complete with no
additional Apply required. The raw Windows backup has no named pedal roles;
we do not invent them or reinterpret native endpoints as role mappings.
A new setup remains available if the owner wants new maximums.

The previous app settings.json, if present, is moved without overwrite to a
unique MECA-app-before-restore JSON under Windows backups. This preserves its
bytes while preventing stale startup reload. Archive failure leaves the source
intact and is explicitly reported as a separate problem after successful Windows
restore. Cancel/refusal/failed Windows restore do not clear the preview or move
the saved app profile. Existing native identity/backup/write/readback/rollback
and user confirmation remain unchanged. See validation/RESTORE_VIEW_20261105.md.

## #RESTORE_TEST — immediate verification with live Windows-calibrated input

Owner requires an immediate pedal test after Restore, superseding the previous
clear-only post-restore view. After successful restore, open a fresh independent
DirectInput reader in COOKED mode. It uses a local verified 0..65535 range; RAW
capture remains on its separate connection. No software reconstruction from old
preview endpoints is used. The COOKED reader refuses Apply before native calls.
Microsoft documents COOKED as applying calibration, RAW as ignoring calibration
and range/dead-zone/saturation: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417929(v=vs.85)

A fresh native snapshot must exactly match the selected backup, including
instance/path/axis identities and Min/Center/Max. The main window shows a match
and live calibrated values/percentages; checks continue during polling. A
mismatch or unavailable reader removes gauges and shows unavailable/failed,
never a fallback raw preview or invented success. Existing disconnection/range/
timing guards apply. No Apply or brake fine adjustment is enabled in test mode.

Same-session identified role/axis/direction metadata can label the test gauges;
old endpoint values are discarded. Unknown roles display honest HID-axis names,
including a connected optional clutch axis, without guessed physical labels or
new maximum-selection requirements. A known inverted role reverses display
orientation only; numeric COOKED value remains visible. Refresh, controller or
pedal-set change, new setup and close dispose the extra reader. Previous app
profile archive behavior remains. Game compatibility is not established by a
Windows test; RAW-reading games may bypass calibration.

See validation/RESTORE_TEST_20261105.md. Native read-only attempt could not find
the supported target before reader-mode setup; no successful native COOKED
result, driver fault diagnosis, new restore mutation or game test is claimed.

## #BANNER_ART — owner-requested final graphical finish

Header only: generated monochrome brushed-steel gear macro, shallow depth of
field and soft left/bottom fade based on supplied references. Crisp live MECA /
Calibration Wizard typography, compact112px header, original palette preserved.
Embedded versioned asset ships inside the executable resources; no loose path
or generated-images folder dependency. Gauges, role workflow, backend/readback,
restore verification and all control/event bindings unchanged. No reference
mockup diagnostics/navigation/RPM features restored. Built-in image_gen used;
asset provenance/final prompt and offscreen WPF visual evidence are in
validation/BANNER_FINISH_20261105.md. Illustrative artwork, not exact hardware
photograph/functional evidence. Existing compact-window body scrolling retained.
