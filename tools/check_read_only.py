#!/usr/bin/env python3
"""Conservative source-level regression alarm, NOT a security proof.

Windows DIPROP_CALIBRATION is an explicitly reviewed beta feature. This guard
rejects the separate prohibited driver/firmware/registry/system call families;
it does not assert that the program is read-only.
Never interpret a passing guard as proving a binary safe.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1] / "src"
patterns = (
    r"\bRegistry\.(?:SetValue|DeleteKey|DeleteValue)\b",
    r"\bHidD_SetFeature\b",
    r"\bDeviceIoControl\b",
    r"\bRegSetValue(?:Ex)?\b",
    r"\bSetupDiCallClassInstaller\b",
    r"\bCreateService\b",
    r"\bjoySetCapture\b",
    r"\bSendInput\s*\(",
)
errors = []
files = list(root.rglob('*.cs'))
for file in files:
    contents = file.read_text(encoding='utf-8')
    for pattern in patterns:
        if re.search(pattern, contents):
            errors.append(f'{file.relative_to(root)}: forbidden call matching {pattern}')
if errors:
    raise SystemExit("Potential device/system write found:\n" + "\n".join(errors))
print(f"PASS: prohibited device/system-call guard scanned {len(files)} C# files (heuristic only)")
