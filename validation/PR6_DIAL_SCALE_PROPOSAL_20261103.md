# PR6 analog dial correction proposal — 3 November 2026

This local proposal targets the exact newer PR6 head `49e24a04098a4658ab3bc117f79f981761434eee`. It is **not applied to this older validation checkout**, whose product source remains the previously tested synthetic merge `8a06338`. It is not published or sent to GitHub.

## User-visible change

The existing needle starts near the lower-left zero label. Multiplying the input percentage by 1.1 rotates it through only 110 degrees: at 50 percent it points left, and at 100 percent it points upper-left. The printed scale instead places 50 at the top and 100 at lower-right.

The accompanying `PR6_DIAL_SCALE_20261103.patch` changes the three live needle mappings to `-5 + percent * 2.7`. A full input sweep then rotates through 270 degrees. At 0 percent the existing start position is retained; at 50 percent the tip is nearly above the centre; at 100 percent it reaches the lower-right scale region. The existing -5-degree unavailable/stale reset positions are untouched. The existing brake source-contract test literal is updated to match the intended mapping.

Only the drawing angle changes. The patch does not change input normalization, range interpretation, device association, capture, calibration, export, or device settings.

Follow-up source review found a second exact-angle assertion in tests/WinmmStaleGaugeChecks.cs. The initial proposal updated only the brake assertion in tests/Program.cs. The revised patch also updates the clutch assertion so that it does not require the old incorrect 1.1 mapping. The complete newer-head suite still has not been executed. Revision artifacts are preserved separately in tmp/meca-dial-fix-20261103-r2; the initial artifacts are unchanged. SHA-256 comparison confirms the revised candidate MainWindow.xaml.cs is byte-identical to the candidate that passed the nine WPF region checks, so those unchanged drawing checks were not rerun.

## Focused verification

Exact-SHA XAML and C# text were fetched with the approved read-only connector. Their UTF-8 text hashes are recorded in the artifact folder. The source was inspected as text; no newer PR executable or project code was compiled or executed.

A new PowerShell check reads the actual three needle coordinates and rotation centres from the XAML, and extracts the three actual live mapping expressions from C#. It applies the installed public WPF RotateTransform to the needle tip for inputs 0, 50 and 100. It does not instantiate a window or access hardware.

The original head fails this check at 50 and 100 percent for each pedal. The proposed candidate passes all nine region checks. For every pedal, the proposed tip coordinates are:

| Percent | X | Y | Scale region |
| --- | --- | --- | --- |
| 0 | 81.11550732975056 | 122.35684944502569 | lower left |
| 50 | 117.66599332141632 | 32.50344354589852 | above centre |
| 100 | 155.35684944502566 | 121.88449267024943 | lower right |

The centre is (118,85), and Canvas Y increases downward. The midpoint check requires horizontal distance from the centre below one pixel. The endpoint checks require the appropriate lower quadrant. These are drawing checks, not calibration or signal tolerances. Because this artwork has text labels without precise tick lines, this verifies the intended regions rather than exact text-centre alignment or rendered text clearance.

## Artifacts and remaining work

Artifacts are in workspace `tmp/meca-dial-fix-20261103`: `sources.json`, `source-hashes.json`, `prepare.py`, `candidate/`, `dial-scale.patch`, `check.ps1`, and the preserved `baseline-check.log`. The candidate PASS was observed directly in the tool output; it was not rerun just to create a log. Reproduction, when a changed version requires it: run prepare.py with Python 3, then check.ps1 without Candidate for the expected original failure, or with -Candidate for the proposal.

The full newer PR head is still not validated. Before integrating this proposal, review the current head and all affected tests, apply the patch only if its context still matches, and check the complete project's build and focused contracts. A rendered Windows check must separately confirm the dial direction, text clearance, unavailable resets and actual live update wiring. No physical pedal or calibration success is implied.

This local commit contains only this explanation and the correction patch. The original delivery checkout, this checkout's tracked product source, untracked global.json, existing tested executable and private ZIP remain unchanged.

