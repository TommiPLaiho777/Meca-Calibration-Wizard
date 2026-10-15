# Meca Calibration Wizard — v0.2.3

Visual, minimal-English **MECA CUP1 pedal setup assistant for Windows**. It is an independent project: **not related to a trail-braking coach, SuperContinuity or any other Rhino repository**.

## What is working / what is new

**Physical MECA tests performed by the owner:** v0.1.0 live bars and axis identification, v0.2.0 three-press wizard, v0.2.1 eight-second WinMM brake/throttle sweeps. The owner captured 172 samples per pedal from the v0.2.1 sweep on 2026-10-14.

**New in v0.2.2 (not yet hardware-tested):**

- Renders both recorded WinMM input sweeps as **in-app response graphs** on Live Test (brake red, throttle green).
- Analyses whether the sample order resembles release → substantial press → release, how much of the Windows-reported range was covered, and whether the axis maximum was reached. Provides a plain-English status instead of interpreting a full-scale reading as physically verified maximum force.
- Exports `Assessment` for each collected `Sweep` in the JSON report, preserving existing report fields.
- Runs regression checks on the **actual October 14 brake and throttle traces** (minimal source data: times and axis values only). Also checks rejected incomplete, idle and too-short traces.
- Adds CI workflows to test on Linux and Windows and publish a self-contained Windows build artifact. A weekly scheduled run revalidates the existing code; it does **not** autonomously write new features or drive real hardware.
- Adds a small heuristic source-code guard against accidentally introducing device/registry write calls in this read-only preview. It is not a safety certification.

## Important safety boundary

This prototype is **read-only with respect to the pedal controller, Windows calibration, registry and device driver**. It only writes an exported JSON diagnostic report to the user-selected local file.

- All inputs are reported by the legacy Windows WinMM (`joyGetPosEx`) API. Despite `RestRaw` and `FullRaw` compatibility keys, **these are not verified ADC-level signals**.
- A steady 65535 can represent a held pedal or a Windows-calibrated endpoint; it does not measure true load-cell force or show where clipping starts physically.
- No device calibration is saved and ACC will continue using its existing settings.
- Noise/EMI diagnosis from Windows joystick data alone is limited. Never disconnect protective earth or modify mains wiring in an attempt to reduce signal noise.
- The source has no virtual game controller, AI live-pedal intervention, or replacement Windows driver.

## Build on Windows 10/11

Prerequisite: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

Unzip the version into **its own folder**, leaving earlier v0.2.1 untouched. Double-click `build.bat`; it runs checks and publishes `release/MecaCalibrationWizard.exe`.

Alternatively:

```powershell
dotnet run --project tests/GuidedCalibrationChecks.csproj -c Release
dotnet publish src/MecaCalibrationWizard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release
```

**Current validation:** source XML and repository configuration checks have been done, but the v0.2.2 WPF app has not been compiled or hardware-tested in the developer environment. The CI build is configured and will run when source is committed to GitHub.

## Manual smoke test (when pedals are available)

1. Choose the MECA controller. Press and release the **brake** three times and then **throttle** three times; check that only their own bars move. The physically tested mappings were WinMM `X` (brake) and `R` (throttle), whereas DIView labels on this hardware are `Ry` brake, `Rz` throttle and `Rx` unconnected clutch. Do not assume both applications use identical axis labels.
2. Skip clutch unless one is installed. An unconnected clutch channel can hold a nonzero idle value.
3. In **Live Test**, run **Brake sweep**: release, press smoothly, hold briefly, and release within eight seconds. The visible graph should follow your foot.
4. Repeat **Throttle sweep**, inspect the graph and explanatory status, and export the JSON report.
5. Observe input stability at rest and, separately, under normal Simucube FFB load. Never alter electrical safety connections for the test.
6. If an error or unexpected behaviour occurs, close v0.2.2 and use the separately preserved v0.2.1 build.

## Automation / GitHub setup

Repository: `TommiPLaiho777/Meca-Calibration-Wizard` (private, MIT licensed).
GitHub Actions validates algorithms using simulated and previously recorded samples on Linux and Windows, and publishes a portable Windows executable for download. The weekly scheduled run checks the current version; it does not independently develop new functionality or control a physical pedal.

## Roadmap / not yet implemented

1. Verify input/report stability on real hardware in v0.2.2.
2. Investigate DIView raw-data and Windows calibration storage **without writing settings**.
3. Design a documented, reversible calibration write-back only if confirmed safe for the exact controller.
4. Finish the UI with a slotted/animated brake rotor, analog throttle gauge, optional clutch plates, reduced-motion support and optional sound effects.

No immediate pedal tests or owner interaction are needed for automated CI validation. Real USB/calibration tests must still be performed manually.


## WinMM capability identifiers (development branch)

The Advanced WinMM monitor and exported `Device` metadata include manufacturer and product IDs returned by Windows `joyGetDevCapsW`, when available. These identify the **WinMM driver-reported capabilities**, not necessarily the physical pedal USB VID/PID. They are **not** HID interface enumeration, device matching, raw ADC readings or calibration. Missing values remain null. This is a read-only preparation for issue #2's later HID/WinMM comparison.

## v0.2.3 signal health

Read-only WinMM diagnostics now calculate sample count, rate, median, standard deviation, largest sample step and range for each calibrated pedal. The JSON retains `NoiseCheckRawSpan` and adds `SignalHealth` for backward compatibility. At-rest motion can be reported as such, but no diagnosis of grounding, true sensor noise or elastomer wear is possible using WinMM alone. A zero spread can reflect Windows deadzones, not necessarily a quiet load cell.

## Optional HID interface inventory (development branch)

In **Advanced**, click **Scan HID identifiers** to enumerate *all present* Windows HID interfaces using read-only SetupAPI calls. The view shows grouped VID/PID identifiers and interface counts; it is not limited to MECA hardware and **does not associate any HID interface with the selected WinMM joystick**. No HID device is opened and no input report, calibration, registry or driver setting is read or written. Raw interface paths and serial/instance strings are discarded and never included in the JSON report. The additive `HidInventory` field is `null` until the user explicitly scans. This is preparatory metadata only, not raw ADC data. The synthetic-path tests cannot establish whether a particular MECA controller exposes an uncalibrated HID input path.

## Comparing two HID inventory scans (development branch)

In **Advanced**, click **Scan HID identifiers**, then **Rescan & compare** after any *user-controlled* USB connection change. The second scan compares grouped VID/PID interface **counts** with the immediately preceding complete scan; the optional JSON `HidInventoryComparison` field contains only those counts and differences. No interface paths, serials or HID reports are saved. A changed group may be a candidate for further investigation, **not** proof of MECA identity or association with WinMM axes. Unchanged counts cannot prove that individual devices stayed the same. Failed scans never produce a comparison. Refreshing the device list resets the comparison. On-hardware usability is not yet verified.

## Optional extended WinMM signal check (development branch)

In **Live Test → Signal Health**, the original **Check Signal · 3s** is preserved and an optional **Long check · 10s** is available. Keep both pedals completely released for the entire capture; the interface shows remaining time. The longer observation can reveal intermittent *Windows-reported* movement that a three-second window might miss, but it is still WinMM data and cannot establish raw ADC noise, an electrical grounding fault, or a HID-to-WinMM association. The JSON `SignalHealth` field records actual elapsed seconds, observed sample count and sampling rate; existing report keys remain unchanged. A controller disconnect cancels the in-progress check. The 10-second GUI flow requires physical acceptance testing before release.

## Signal-check sampling quality (development branch)

The 3-second and 10-second at-rest checks now flag a recording with fewer than eight WinMM readings, a duration under 0.5 seconds, or an **average observed WinMM polling rate below 5 Hz** as **Insufficient samples**, even when every reported value is zero. This conservative heuristic prevents a very sparse flat trace from being mistaken for a useful idle-stability check. The recorded sample count, rate, span and largest step are still exported for inspection. A 5 Hz average is a minimal quality threshold, **not** a measured native HID/ADC sampling rate or proof of absence of electrical noise. Repeat a sparse check without touching the pedals; no calibration settings are modified.


### Long WinMM polling gaps (development branch)

A high **average** polling rate can conceal a long interval with no samples. Timed Signal Health checks now export additive `SignalHealth.*.LargestSamplingGapSeconds` (measured from the start of the capture, including the delay before its first sample). If the largest gap exceeds **1.0 second**, the result is **Insufficient samples** even when the remaining values are flat and the average rate exceeds 5 Hz. The Live Test status displays the largest gap. A missing interval is a **sampling-quality limitation**, not evidence of an electrical problem. These are application-level WinMM polling times, not USB HID packet intervals or ADC measurements. Hardware GUI confirmation is pending.
