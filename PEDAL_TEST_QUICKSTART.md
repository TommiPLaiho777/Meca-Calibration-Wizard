# MECA CUP1 — Pedal field test (v0.2.2)

**Expected time: 8–12 minutes.** Language: English. This test is READ-ONLY: it does not alter the USB controller, its firmware, Windows/DIView calibration, or Assetto Corsa Competizione settings.

## Get started

1. Open the latest **successful** `Validate and build Windows app` run in GitHub Actions on the **main** branch. In **Artifacts**, download `MecaCalibrationWizard-v0.2.2-win-x64`.
2. Extract the GitHub artifact ZIP. If it contains a second ZIP, extract that too to a **new folder**; do not overwrite earlier working versions.
3. Run `START_PEDAL_TEST.cmd` or `MecaCalibrationWizard.exe`. No .NET SDK or administrator access is required for the self-contained release.
4. Keep all normal USB connections, grounding and protective-earth connections **unchanged**. Do not change power wiring. Leave pedals mechanically adjusted as usual. Close ACC during this diagnostic session.

## Guided test

1. **Controller:** Select the controller whose values actually react to your pedals; it may appear as `Microsoft PC-joystick driver`. Click **Refresh** if needed. Leave **Clutch** unchecked on the two-pedal MECA rig.
2. **Brake:** Click **Begin**, then **Start** on the brake page. Keep your foot off the brake for the initial ~1.2-second rest reading. Follow **PRESS / RELEASE** prompts for **three** complete presses. Use a comfortable maximum force, not a forced mechanical stop.
3. **Throttle:** Repeat the three guided presses with the throttle only. Continue to **Live Test** (skip the absent clutch).
4. **Live bars:** Press each pedal separately. Confirm **Brake** responds only to the brake and **Throttle** only to the throttle. Both should settle near 0% after release. These are **WinMM values**, not direct load-cell readings.
5. **Brake sweep:** Click **Brake sweep**. Within the 8-second window: begin released, press smoothly, briefly hold, then release fully. Check that the red trace climbs and returns to rest. A status of **Sweep captured** is desirable; `Axis limit reached` is not a failure.
6. **Throttle sweep:** Repeat for **Throttle sweep**, verifying the green trace and status. If a sweep says **Incomplete sweep**, repeat more slowly with a clear release at the end.
7. **Signal test:** Remove your feet from the pedals. Click **Check Signal**, wait 3 seconds, and note whether the brake and throttle readings remain stable. If practical, compare again with the wheelbase operating under normal, safe conditions; do not move the rig's grounding wires to provoke noise.
8. **Export:** Click **Export preview report (JSON)**. Save the report, take a screenshot only if anything looks wrong, and share the JSON with the developer.

## Pass criteria

- Correct two-pedal identification: brake and throttle never swapped; no phantom clutch step.
- Bars respond independently and return to rest.
- Both recorded sweeps show meaningful rise and fall; the status text is reasonable.
- Export includes both `Sweeps.Brake.Assessment` and `Sweeps.Throttle.Assessment`.
- No crash, and prior Windows/DIView calibration still works exactly as before.

## If something fails

- **Wrong pedal moves:** restart setup; select the correct device and press only the prompted pedal during capture.
- **No values or missing device:** check USB, press **Refresh**, and reselect the correct controller.
- **Graph missing:** finish guided setup first, then repeat the sweep on the **Live Test** page.
- **Unexpected disconnect:** stop the test and restart after reconnecting the device. Do not change wiring while energized.
- **Error:** note the step and exact message. Keep the previous v0.2.1 release as fallback.

**Important limitation:** The program reads the Windows WinMM joystick API. Full-scale 65535 cannot establish the true sensor ADC maximum, actual jamming, or physical pedal force. The test cannot prove the rig is properly earthed, nor diagnose all possible EMI sources.