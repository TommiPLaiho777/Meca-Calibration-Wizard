# Local JSON report-file protection — 4 November 2026

## User-visible behavior

The existing export path serialized the report, then wrote directly to the selected destination. Once opened, an interrupted disk write could leave a truncated replacement report. Export now stages the complete UTF-8 text in a unique sibling file and publishes with File.Replace for an existing report or File.Move for a new report. If writing or replacement fails, the earlier destination stays intact; the existing GUI error message is retained. A valid retry after unlocking works. The normal saved JSON content, UTF-8 without BOM, report schema and file-selection UI stay the same.

Implementation is one SaveJson method in the existing export-policy class ReadOnlyHidReport, called at the existing final export-write point in MainWindow. No new framework, shared project dependency, device access, measurements, protocol or GUI styling. Existing post-dialog/current-read/session/provenance/consent gates remain before serialization and publication. Brake/throttle/optional clutch support is unchanged. Search #JSON_EXPORT/#REPORT_FILE for the explanation beside the code.

## Verification

The full existing C# check executable passed, including new real disposable-filesystem checks for UTF-8/JSON round trip, replacement, interrupted staging (strict UTF-8 failure after8192characters), preservation of existing bytes, absence of a failed new destination, locked replacement refusal, no implicit parent-directory creation, retry and temporary-file cleanup. The two existing GUI source-order checks now recognize the SaveJson call and retain the same export/session gate assertions. These checks do not operate WPF dialogs or a controller.

WPF compilation succeeded:0errors,1existing CS8602 warning atMainWindow264. New managed DLL SHA256: 5807F3DAD20ADE6899129C7E3C24CC2696D2361E44750C3254EE0F9215143CE4. Build: tmp/meca-report-file-save-20261104/build. Full check log: tmp/meca-report-file-checks-20261104.log; build log: tmp/meca-report-file-build-20261104.log. No repeated physical test, new native GUI/save-dialog observation or replacement user package is claimed. Existing62b801c user-tested ZIP remains untouched.

## Native GUI follow-up — 18:37 UTC

The existing d5c5bb9 build, managed DLL SHA256 5807F3DAD20ADE6899129C7E3C24CC2696D2361E44750C3254EE0F9215143CE4, ran in owned process19732. The actual GUI displayed the readable controller selector and six live axis rows. Advanced diagnostics -> Export opened the explicit controller-only confirmation, then the native SaveFileDialog. Alt+D selected the address visually; typing the workspace folder and Enter navigated there while retaining the generated filename. The saved file was independently parsed:1815bytes, UTF-8 without BOM, controller0/six ranges, empty Pedals/Sweeps, null HID inventory, read-only status and both verification flags false. No staging files remained. SHA256:9988FC6E0DE1F6E453484B025BD34CF1B336D207FADCFEDFBC320AB23E2C1E6E. The GUI displayed Export complete; the same held process exited normally with code0 at18:37:46.5179026UTC.

This verifies the new-report GUI path through the staged writer. Existing-file replacement/failure protection remains separately established by the filesystem checks above, not by this GUI run. No physical pedal capture, HID scan, disconnect, calibration backup/restore or settings write occurred. The user-tested62b ZIP remains unchanged. Raw owned-process/exit/report/parser receipts: workspace tmp/meca-report-gui-20261104. No build or prior suite was repeated.

Automation limitations retained: one clipped Advanced button click was refused before maximizing the window; cached modal Yes/Save element clicks were unavailable. Fresh screenshot-based clicks succeeded. Accessibility continued reporting SearchBox focus even while the screenshot visibly showed the selected address and typing changed that address; navigation and the actual output path were independently verified. This does not establish the cause of earlier failed filename routing or white captures.

## Limits

Same-directory filesystem rename/replace is the publication boundary; no fallback truncating write is allowed. This does not guarantee power-loss durability, support every network filesystem, provide report history, or create a restorable calibration backup. A process crash or cleanup access failure can leave the uniquely named staging file; ordinary tested failure paths clean it. No Windows/controller settings are written.

## Coordination

Fresh approved read-only audit: PRIVATE/mainca68278; known PR3->4->5->6 stack retains exact49e24a head. New open dependency-update PR1/7/8 targetmain and were not integrated or executed. Issue2read; latest exactPR6 run41500489213 verify/windows-buildSUCCESS/artifacts0. No push/merge/dispatch/upload/host/device settings action. Original delivery0065839 and untracked global.json in both checkouts preserved.
