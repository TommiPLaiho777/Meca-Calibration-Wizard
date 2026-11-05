# Controller selection failure and verified readability fix — 4 November Helsinki

## Human failure report takes precedence over the earlier candidate status

The human tried the 3 November private candidate and reported that it did not work or recognise the pedals. They confirmed that the controller dropdown looked completely empty. Their report `meca-preview-20261103-233124.json` contains `Device: null`, empty `Pedals` and `Sweeps`, and only the all-device HID metadata inventory (17 interfaces). It does not establish a working pedal session, physical association, or the reason for the empty-looking list. SHA-256 of the original report and the preserved private workspace copy is `B9D05DB26B48F1E6D7ADA87405D24A79FEBD3DEB87CE5CD2F6DC6E823B168560`. The original is unchanged; the private report is not committed.

The human explicitly prioritised the simple foundation demonstrated by DIView: find the controller and show its readings. Prior startup/checkbox tests did not validate that foundation. Do not represent the prior package as a successful physical-pedal beta or spend further work on decorative gauges while controller visibility and live input remain unresolved.

## Distinct read-only diagnosis

A small independent .NET 8 WinMM probe used only `joyGetNumDevs`, `joyGetPosEx`, and `joyGetDevCapsW` from System32. It checked the public structure sizes (JOYINFOEX 52 bytes, JOYCAPSW 728 bytes on this x64 process), all supported slots up to the documented 16 limit, and preserved native return codes. At `2026-11-03T22:11:27.797618Z`, slot 0 returned success for both read and capabilities, reported 3 axes and the generic name `Microsoft PC-joystick driver`. Slots 1–15 returned 165; requesting only X/Y instead of all fields did not change their result. The native executable host gave the same visibility at 22:11:57 UTC, so this bounded comparison did not demonstrate a dotnet-host versus apphost visibility difference.

A subsequent separately selected probe path linked the exact current product `WinmmJoystick.cs`, without reflection, and called its actual `FindDevices` and `TryRead`. At 22:12:30 UTC it also found one controller with a successful current read. WinMM manufacturer/product numbers were 7634/8914. Matching numbers in HID metadata remain candidate evidence only, not verified USB identity or proof of the physical pedal assignment. No pedal movement was requested or observed.

Then the exact unchanged previously delivered executable `292668A3C40EA87F5DAA78F9481DCE6C466363D81AE30959EC4D2436CE79D084` was run as owned PID 15764. The actual GUI now selected that generic controller, showed `Input streaming · read-only`, and enabled Begin. This differs from the earlier empty inventory observation. Its cause is still unknown: no device/settings/allowlist changes were performed, and no claim that the earlier failure was repaired follows from the later availability. The original report does not preserve the enumeration return codes at the failure time.

## Reproduced product defect and correction

The same actual GUI revealed a separate definite defect: the selected controller name and open dropdown row rendered in nearly white text on a light native ComboBox surface. The accessibility tree contained the named controller while its visible text looked blank. This was observed on the unchanged binary, not inferred from source or a simulation.

`App.xaml` applies a light Foreground to every TextBlock for the dark wizard. A native ComboBox uses a light surface. Its automatically generated text inherited the global TextBlock style, so setting the surrounding window colours did not make the controller name readable. The correction gives this specific ComboBox an item DataTemplate with an explicit dark TextBlock Foreground, and sets the ComboBox foreground itself. A local value on that TextBlock takes precedence over the global style. No global theme, input backend, device selection rule, calibration threshold or capture setting changed.

The selector now also shows `Readable WinMM controllers: N. Windows may use generic driver names.` directly below it. That count is taken from the same actual list assigned to the ComboBox on refresh. It distinguishes a zero-item scan from an unselected item or an unfamiliar generic name. It does not claim N physical pedals or verified MECA association.

This **verified readability bug is not proven to be the sole cause** of the human's failed session. In particular, their report had no selected Device; the later process selected one automatically. Preserve both observations rather than rewriting the earlier failure as a successful detection.

## Changed build and actual GUI validation

Self-contained win-x64 publish completed with exit 0 using existing cached dependencies and the existing .NET 8.0.425 SDK pin. The existing nullable warning moved to `MainWindow.xaml.cs:280`. No SDK/driver/runtime was installed. Detached published EXE SHA-256 is `8AB1B4B741A526CA35E90DCADD48BB3C8D962284DFD17799CB487C76EA78D217`.

Owned PID 14964, created `2026-11-03T22:15:34.9918696Z`, ran that exact binary. Actual screenshot and accessibility observations verified a readable dark selected name, a readable dark popup row, controller count 1, streaming status, and Begin enabled. Clicking Begin entered the real brake page. It correctly remained at no mapped pedal / awaiting movement / 0 presses with Continue disabled. No capture was started, no pedal moved, and no live percentage or physical mapping success is claimed. Closing the actual window produced normal held-handle exit 0 at `2026-11-03T22:16:32.2596890Z`.

The earlier owned unchanged process also closed normally, exit 0 at 22:14:22 UTC. Full read-only process inventories preceded both GUI launches; no user MECA or DIView instance was closed. A first Advanced navigation attempt on the unchanged GUI was refused by Computer Use with `coordinate input geometry is unavailable`; it did not verify Advanced values. Fresh observations and activation were used before further inputs. No saved screenshot artifact exists. No Rhino launch, controller setting write, HID device open, configuration reader retry, remote mutation, public publication, or old unchanged regression-suite rerun occurred.

Raw source/probe code, both host observations, product-linked enumerator receipt, unchanged and fixed owned-process/exit receipts, private feedback copy, and detached publish are in workspace `tmp/meca-enumeration-20261104`. Both native probe variants compiled without warnings/errors. `git diff --check` passed for the product patch. The existing full hardware-free suite passed before this change; it was not repeated as a substitute for actual controller-menu visual validation.

## Required next acceptance

Investigate and preserve native enumeration failures in the product so a truly empty list has an actionable reason. Establish the physical controller and a real moving-axis stream before claiming the user's primary requirement works. Generic labels and matching IDs alone cannot establish pedal association. Any additional supported public read-only input backend must be reviewed and validated, not guessed or marketed from an interface list. Calibration readback, backup and restore remain separate unimplemented acceptance work. No new package should be described as a fully working pedal beta merely because this selector rendering is fixed.

Public API references used for the probe: [joyGetPosEx](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetposex), [joyGetDevCapsW](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetdevcapsw), [joyGetNumDevs](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetnumdevs). Driver slot capacity is not the count of attached controllers.
