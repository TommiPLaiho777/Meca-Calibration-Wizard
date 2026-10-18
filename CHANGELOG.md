# Changelog

## v0.2.3 (development candidate, 2026-10-15)

- Allow an explicit successful HID inventory to be exported from Advanced diagnostics without a WinMM controller selection. HID-only JSON contains no device, pedal or sweep readings and declares both association and raw sensor verification false; add hardware-free eligibility, privacy and provenance regression. Windows GUI verification pending.

- Add an Advanced diagnostics JSON export button reusing the existing read-only preview export, without repeating pedal capture. Explain when no WinMM controller is selected. Add a hardware-free XAML UI regression; Windows GUI validation pending.

- Clarify that an unchanged total HID interface count can conceal VID/PID group changes; add synthetic swap, no-false-positive and JSON privacy regressions. No physical-device or WinMM association inferred. Windows GUI validation pending.

- Prioritize largest HID interface groups in the 12-row Advanced inventory preview; preserve all groups in count-only JSON, stable VID/PID tie-breaking and synthetic privacy regression. No WinMM association inferred; GUI/hardware validation pending.

- Prioritize the largest VID/PID interface-count differences in the Advanced 12-row preview, with deterministic VID/PID tie-breaking. The full count-only JSON remains unchanged; add synthetic >12-group and privacy regression tests. No device association inferred; Windows GUI/hardware check pending.

- Clarify mixed HID count changes: a VID/PID group count change cannot identify simultaneous changes among interfaces lacking VID/PID. Add synthetic regression; Windows GUI/hardware confirmation pending.

- Explicitly label HID inventory count comparisons as non-ADC metadata (`UncalibratedSensorInputVerified: false`); extend the synthetic JSON provenance regression. Windows GUI/hardware confirmation pending.

- Add explicit false provenance flags to optional HID inventory/comparison JSON to prevent machine readers from interpreting count metadata as a verified WinMM match or uncalibrated sensor input. Add synthetic serialization/privacy regression; physical GUI verification pending.

- Restrict read-only VID/PID parsing to the hardware-ID segment of a HID interface path; do not infer device IDs from instance/serial substrings. Add synthetic boundary regressions. CI and GUI verification pending.

- Clarify when only HID interfaces without VID/PID change in count: no device or WinMM association can be inferred. Add hardware-free guidance/privacy regression. CI and Windows GUI validation pending.
- Limit native SetupAPI HID path reads to the allocated buffer and aggregate VID/PID counts during enumeration rather than retaining interface paths. Ignore trailing data after native NUL terminators; add hardware-free regression checks. Read-only metadata only; Windows GUI/hardware verification pending.
- Stop read-only WinMM idle checks on backwards wall-clock timestamps and classify them as `Insufficient samples`, rather than recording a false long gap or a reassuring zero-variation result. Export additive `TimestampRegressionDetected`; add deterministic rollback tests. Hardware GUI confirmation pending.
- Flag >1.0s gaps between WinMM idle-check samples (including capture-start delay) as `Insufficient samples`, even if average rate is adequate; export additive `LargestSamplingGapSeconds`, show it in Live Test and cover normal, delayed and bursty polling in deterministic tests. Read-only timing quality only; physical GUI confirmation pending.
- Guard against false stationary-signal reassurance when WinMM polling is too sparse: below 5 Hz average, the read-only health result is `Insufficient samples` even if its span is zero. Keep all numeric metrics; add regression tests for sparse and threshold captures. This heuristic is not a HID or ADC sampling-rate measurement; GUI/hardware confirmation pending.
- Add a user-triggered 10-second WinMM at-rest signal check alongside the existing 3-second check. Show remaining time, cancel cleanly on controller loss, preserve existing JSON signal-health fields and read-only behavior. Deterministic duration and sampling-rate regression tests; GUI/hardware check pending.
- Compare two explicitly requested HID interface inventories by VID/PID count changes only. Show a conservative English diff in Advanced and optionally export `HidInventoryComparison`; no serials, raw paths, reports or WinMM association. Add synthetic add/remove, unchanged-count and failed-scan tests; hardware GUI verification pending.
- Add optional, explicit read-only SetupAPI HID interface inventory in Advanced: group present HID interface VID/PID counts without reading reports, exporting paths/serials, or claiming a WinMM-to-HID association. Hardware verification still pending; regressions use synthetic paths.
- Surface WinMM capability manufacturer/product IDs (when available) in Advanced monitor and additive JSON Device fields; explicitly not verified USB VID/PID or HID matching. Add regression for unknown and known identifiers.
- Real-hardware 2026-10-15 feedback: signal-health messages now tell users to repeat a contaminated at-rest check and distinguish movement from electrical noise. The report gains an additive `SignalHealth.*.Guidance` string; legacy fields stay unchanged.
- Read-only WinMM idle signal-health stats with sample rate, median, standard deviation and abrupt-change counts.
- Conservative interpretation of movement, zero span and possible Windows deadzones.
- New `SignalHealth` export field, retaining `NoiseCheckRawSpan`.
- Automated regression cases for stable, moving, slight variation and timed captures.
- When guided peaks reach the Windows axis limit, show physical-force repeatability as unknown rather than a misleading peak-spread percentage.
- Preserve numeric WinMM peak-spread export for compatibility; add explicit interpretation and saturation flag in JSON.


## v0.2.2 (source staging, 2026-10-15)

- Add in-app graph for WinMM brake and throttle sweep captures.
- Add conservative sweep-shape assessment: release/press/release completeness, distinct values and reported endpoint reached.
- Export per-pedal assessments in diagnostic JSON.
- Add hardware-free regression tests using the 2026-10-14 MECA CUP1 sweep records.
- Configure GitHub Actions checks and Windows artifact publishing for a future separate repository.
- Add read-only API guard, security notes and automation-ready project documentation.

This version is source-checked but has **not** been compiled with .NET or tested on the physical controller. Calibration write-back remains disabled.

## v0.2.1 (2026-10-14)

- Add eight-second captured WinMM axis traces.
- Hardware tested by owner: brake and throttle sweeps; 172 values per pedal.

## v0.2.0 (2026-10-14)

- Add three-press guided calibration previews and repeatability diagnostics.
- Hardware tested by owner.

## v0.1.0 (2026-10-14)

- Add initial axis detection, in-app live bars, noise-span report and JSON export.
- Hardware tested by owner.
