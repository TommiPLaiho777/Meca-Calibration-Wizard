# Changelog

## v0.2.3 (development candidate, 2026-10-15)

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
