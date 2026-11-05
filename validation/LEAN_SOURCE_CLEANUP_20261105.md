# Lean source cleanup — 5 November 2026

Owner requested separating obsolete code and simplifying the working implementation.

- Moved five diagnostic modules (1,037 lines) unchanged into `historical/diagnostics`, outside the active application source tree. Pre/post move SHA256 equality and Git source-content equality checked. Historical test links updated; removed the now-unnecessary application exclusion list.
- Removed an unused dynamic-peak display calculation. Kept the active fixed-Windows-range display and chosen-endpoint final display.
- One `HasSustainedPress` predicate now expresses the identical minimum sample-count/hold-time test in both existing release paths. No threshold, state transition or failure policy changed.
- Updated the current code map so maintainers can find the small active workflow without following obsolete diagnostic tags.

Existing actual C# `PedalSetupChecks` PASS after changes: two/three-pedal sequences, separate identification and adjustment rounds, locked axes, chosen maximum, normal/inverted brake fine adjustment with bounds, unchanged other pedals, interruption, malformed/out-of-range samples, duplicate timestamps, release dropout, gradual release and isolated-peak protections. No new duplicate test harness was added.

WPF Release build PASS: 0 errors, 0 warnings. Historical check project compiles with 0 errors and its 2 existing nullable warnings; obsolete GUI assertions were not executed or claimed PASS. Exact managed DLL hash and archive hashes are in parent workspace `tmp/meca-cleanup-20261105/result.json`.

No native GUI or physical-pedal retest performed for this cleanup. Owner's preliminary successful morning test applies to the earlier delivered candidate, not this new DLL. Existing morning ZIP preserved unchanged; no replacement ZIP/publication, device/settings write or running-user-app intervention.
