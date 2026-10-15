# Safety and contribution boundaries

This is a Windows **read-only diagnostic preview**, not a Windows calibration or firmware editor.

- Read WinMM joystick data, display previews and write JSON only where the user selects a report file.
- Do not install new drivers, write registry calibration values, issue HID feature/output reports or modify device firmware.
- Do not attempt to defeat protective earth, bypass safety wiring, or prescribe improvised mains grounding.
- Never claim WinMM values represent uncalibrated sensor ADC values.
- Real MECA hardware testing must be performed separately by a human.
- Before enabling any future calibration writes, require specific device identification, documented storage protocol, backup and verified rollback.

If you discover a security problem, do not post exploit details to public issues.