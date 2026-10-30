using System.Runtime.CompilerServices;

namespace MecaCalibrationWizard;

// Hardware-free WPF source contract: a disconnected or invalid WinMM stream
// must not leave a plausible-looking pedal gauge frozen at its last value.
// The Windows CI build separately compiles the actual WPF view.
internal static class WinmmStaleGaugeChecks
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml.cs"));
        int start = source.IndexOf("private void MarkLiveReadingsUnavailable()", StringComparison.Ordinal);
        int end = source.IndexOf("private void UpdateDeviceInfo(", start, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new Exception("FAILED: WinMM stale-reading UI handler not found");

        string body = source[start..end];
        foreach (string expected in new[]
        {
            "LiveValue.Text = \"—\";",
            "LiveRaw.Text = \"WinMM input interrupted",
            "CaptureProgress.Value = 0;",
            "BrakeProgress.Value = 0;",
            "ThrottleProgress.Value = 0;",
            "ClutchProgress.Value = 0;",
            "NeedleRotation.Angle = -5;",
            "ClutchDiscShift.X = 0;",
            "ClutchHubShift.X = 0;",
            "BrakeCaliper.Opacity = 1.0;"
        })
            if (!body.Contains(expected, StringComparison.Ordinal))
                throw new Exception($"FAILED: stale WinMM gauge not reset: {expected}");

        // A deliberate device selection change, including selecting no device,
        // must clear the last controller's visible gauges immediately. Poll
        // returns without updating the UI when _device is null. This is a
        // WPF source contract, not a runtime or physical pedal test.
        int deviceSelectionStart = source.IndexOf(
            "private void DeviceCombo_SelectionChanged(", StringComparison.Ordinal);
        int deviceSelectionEnd = source.IndexOf(
            "private void ClutchCheck_Changed(", deviceSelectionStart, StringComparison.Ordinal);
        if (deviceSelectionStart < 0 || deviceSelectionEnd <= deviceSelectionStart)
            throw new Exception("FAILED: WinMM controller selection handler not found");
        string deviceSelectionBody = source[deviceSelectionStart..deviceSelectionEnd];
        foreach (string expected in new[]
        {
            "_raw = Array.Empty<uint>();",
            "LiveValue.Text = \"—\";",
            "LiveRaw.Text = \"Controller selection changed",
            "CaptureProgress.Value = 0;",
            "BrakeProgress.Value = 0;",
            "ThrottleProgress.Value = 0;",
            "ClutchProgress.Value = 0;",
            "NeedleRotation.Angle = -5;",
            "ClutchDiscShift.X = 0;",
            "ClutchHubShift.X = 0;",
            "BrakeCaliper.Opacity = 1.0;",
            "BrakeValue.Text = \"No live input\";",
            "ThrottleValue.Text = \"No live input\";",
            "ClutchValue.Text = \"No live input\";",
            "SweepProgress.Value = 0;",
            "AdvancedAxesText.Text = \"No current WinMM reading"
        })
            if (!deviceSelectionBody.Contains(expected, StringComparison.Ordinal))
                throw new Exception($"FAILED: controller selection left stale WinMM UI: {expected}");
        Console.WriteLine("PASS: controller selection clears stale WinMM gauges (WPF source contract)");

        // A completed WinMM idle/noise check is historical after disconnect.
        // The user can still review its metrics, but must see a stale label
        // immediately in the live test view without navigating away and back.
        if (!body.Contains("SignalText.Text = MarkStaleForReview(SignalText.Text);",
                StringComparison.Ordinal))
            throw new Exception("FAILED: completed WinMM idle check must be marked stale on disconnect");
        Console.WriteLine("PASS: completed WinMM idle check marked stale after disconnect (WPF source contract)");

        // A completed sweep's status line must also become stale immediately;
        // marking only the separate analysis text leaves a success label visible.
        if (!body.Contains("SweepStatusText.Text = MarkStaleForReview(SweepStatusText.Text);",
                StringComparison.Ordinal))
            throw new Exception("FAILED: completed WinMM sweep status must be marked stale on disconnect");
        Console.WriteLine("PASS: completed WinMM sweep status marked stale after disconnect (WPF source contract)");

        // Source-level regression: after MarkLiveReadingsUnavailable labels completed
        // measurements STALE, Poll must not overwrite their status with "cancelled".
        // Cancellation text applies only to a measurement that was still active.
        // This does not execute WPF or a real WinMM device.
        int pollStart = source.IndexOf("private void Poll(", StringComparison.Ordinal);
        int readFailureStart = source.IndexOf(
            "if (_device is null || !WinmmJoystick.TryRead(_device.Id, out var sample))",
            pollStart, StringComparison.Ordinal);
        int outOfRangeStart = source.IndexOf(
            "if (!WinmmJoystick.IsSampleWithinAxisBounds(_device, sample))",
            readFailureStart, StringComparison.Ordinal);
        int streamingStart = source.IndexOf(
            "StatusText.Text = \"Input streaming · read-only\";",
            outOfRangeStart, StringComparison.Ordinal);
        if (pollStart < 0 || readFailureStart <= pollStart ||
            outOfRangeStart <= readFailureStart || streamingStart <= outOfRangeStart)
            throw new Exception("FAILED: WinMM Poll interruption branches not found");
        foreach (var (section, reason) in new[]
        {
            (source[readFailureStart..outOfRangeStart], "read failure"),
            (source[outOfRangeStart..streamingStart], "out-of-range reading")
        })
        {
            int stale = section.IndexOf("MarkLiveReadingsUnavailable();", StringComparison.Ordinal);
            int signalGuard = section.IndexOf("if (signalWasActive)", StringComparison.Ordinal);
            int signalCancel = section.IndexOf("SignalText.Text = \"Signal check cancelled", StringComparison.Ordinal);
            int sweepGuard = section.IndexOf("if (sweepWasActive)", StringComparison.Ordinal);
            int sweepCancel = section.IndexOf("SweepStatusText.Text = \"Sweep cancelled", StringComparison.Ordinal);
            if (stale < 0 ||
                !section.Contains("bool signalWasActive = _noise is not null;", StringComparison.Ordinal) ||
                !section.Contains("bool sweepWasActive = _sweep is not null;", StringComparison.Ordinal) ||
                signalGuard <= stale || signalCancel <= signalGuard ||
                sweepGuard <= stale || sweepCancel <= sweepGuard)
                throw new Exception($"FAILED: completed WinMM diagnostics overwritten on {reason}");
        }
        Console.WriteLine("PASS: Poll retains completed STALE signal/sweep status (WPF source contract)");

        // Regression for the out-of-range branch: a previously completed
        // guided capture must keep its STALE footer. Only a capture that was
        // actually running may receive a cancellation message. This checks
        // source structure, not WPF runtime behavior or physical pedals.
        string invalidAxisPoll = source[outOfRangeStart..streamingStart];
        int captureActiveFlag = invalidAxisPoll.IndexOf(
            "bool captureWasActive = _capture is not null;", StringComparison.Ordinal);
        int staleCaptureMark = invalidAxisPoll.IndexOf(
            "MarkLiveReadingsUnavailable();", StringComparison.Ordinal);
        int captureCancelGuard = invalidAxisPoll.IndexOf(
            "if (captureWasActive && _step is >= 1 and <= 3)", StringComparison.Ordinal);
        int captureCancelMessage = invalidAxisPoll.IndexOf(
            "Active capture cancelled; refresh and capture again.",
            StringComparison.Ordinal);
        if (captureActiveFlag < 0 || staleCaptureMark <= captureActiveFlag ||
            captureCancelGuard <= staleCaptureMark || captureCancelMessage <= captureCancelGuard)
            throw new Exception("FAILED: out-of-range WinMM poll must retain completed capture STALE footer");
        Console.WriteLine("PASS: invalid WinMM axis preserves completed guided-capture STALE footer (WPF source contract)");



        // An interrupted guided-capture view must not keep its earlier
        // "Preview captured" footer or an unmarked quality result. Navigating
        // away and back must preserve the stale warning. Source-only contract:
        // actual WPF navigation and physical pedals are not exercised here.
        if (!body.Contains("if (_step is >= 1 and <= 3)", StringComparison.Ordinal) ||
            !body.Contains("CaptureFooter.Text = ", StringComparison.Ordinal) ||
            !body.Contains("STALE — WinMM session interrupted.", StringComparison.Ordinal) ||
            !body.Contains("CaptureQualityText.Text = MarkStaleForReview(CaptureQualityText.Text);",
                StringComparison.Ordinal))
            throw new Exception("FAILED: interrupted guided capture must mark visible footer and quality stale");
        int stepStart = source.IndexOf("private void ChangeStep(", StringComparison.Ordinal);
        int stepEnd = source.IndexOf("private PedalKind StepKind(", stepStart, StringComparison.Ordinal);
        if (stepStart < 0 || stepEnd <= stepStart)
            throw new Exception("FAILED: guided navigation source not found");
        string stepBody = source[stepStart..stepEnd];
        if (!stepBody.Contains("MarkStaleForReview(current.PeakSpreadSummary);",
                StringComparison.Ordinal) ||
            !stepBody.Contains("CaptureFooter.Text = _winmmSessionInvalid",
                StringComparison.Ordinal))
            throw new Exception("FAILED: guided navigation must keep prior capture results visibly stale");
        Console.WriteLine("PASS: interrupted guided capture and re-entry keep quality/footer stale (WPF source contract)");

        Console.WriteLine("PASS: stale WinMM gauges reset after input interruption (WPF source contract)");

        // After a failed WinMM poll, a refresh must not silently reselect a
        // reused joystick ID. This checks the WPF source contract only; no
        // controller, WPF window, or physical pedal is exercised here.
        int refreshStart = source.IndexOf(
            "private void RefreshDevices(bool requireExplicitSelection = false)",
            StringComparison.Ordinal);
        int refreshClick = source.IndexOf("private void Refresh_Click(",
            StringComparison.Ordinal);
        int selectionChanged = source.IndexOf("private void DeviceCombo_SelectionChanged(",
            StringComparison.Ordinal);
        if (refreshStart < 0 || refreshClick <= refreshStart ||
            selectionChanged <= refreshClick)
            throw new Exception("FAILED: explicit WinMM refresh handlers not found");
        string refreshBody = source[refreshStart..refreshClick];
        string refreshClickBody = source[refreshClick..selectionChanged];
        if (!refreshBody.Contains(
                "requireExplicitSelection\n            ? null : WinmmJoystick.SelectSuggestedDevice(devices, previousId);",
                StringComparison.Ordinal) ||
            !refreshClickBody.Contains(
                "bool requireExplicitSelection = _winmmSessionInvalid;",
                StringComparison.Ordinal) ||
            !refreshClickBody.Contains(
                "RefreshDevices(requireExplicitSelection);",
                StringComparison.Ordinal) ||
            !refreshClickBody.Contains(
                "select controller explicitly, then capture pedals again",
                StringComparison.Ordinal))
            throw new Exception("FAILED: interrupted WinMM session must require explicit controller reselection");
        Console.WriteLine("PASS: interrupted WinMM session requires manual controller reselection (WPF source contract)");

        // Selecting another controller from the old combo-box list does not
        // prove a new scan took place. The interruption flag must survive that
        // event until Refresh_Click starts a new read-only session.
        int selectionEnd = source.IndexOf("private void ClutchCheck_Changed(", selectionChanged,
            StringComparison.Ordinal);
        if (selectionEnd <= selectionChanged ||
            source[selectionChanged..selectionEnd].Contains(
                "_winmmSessionInvalid = false;", StringComparison.Ordinal) ||
            !refreshClickBody.Contains("_winmmSessionInvalid = false;", StringComparison.Ordinal))
            throw new Exception("FAILED: WinMM selection must not bypass required refresh");
        Console.WriteLine("PASS: stale combo-box selection cannot bypass WinMM refresh (WPF source contract)");

        // WPF SaveFileDialog is modal but may pump dispatcher messages. A failed
        // WinMM poll while it is open must block the report, even though the
        // initial export gate was satisfied. No WPF dialog or pedal is opened here.
        int exportStart = source.IndexOf("private void Export_Click(", StringComparison.Ordinal);
        int dialogIndex = source.IndexOf("if (save.ShowDialog(this) != true) return;",
            exportStart, StringComparison.Ordinal);
        int secondGate = source.IndexOf("if (!ReadOnlyHidReport.CanExport(",
            dialogIndex, StringComparison.Ordinal);
        int fileWrite = source.IndexOf("File.WriteAllText(save.FileName",
            dialogIndex, StringComparison.Ordinal);
        if (exportStart < 0 || dialogIndex < 0 || secondGate <= dialogIndex ||
            fileWrite <= secondGate)
            throw new Exception("FAILED: missing post-dialog WinMM export gate");
        string postDialog = source[secondGate..fileWrite];
        foreach (string expectedGate in new[]
        {
            "_winmmSessionInvalid",
            "_capture is not null || _noise is not null || _sweep is not null",
            "No report saved."
        })
            if (!postDialog.Contains(expectedGate, StringComparison.Ordinal))
                throw new Exception($"FAILED: post-dialog WinMM export gate missing: {expectedGate}");
        Console.WriteLine("PASS: report export rechecks WinMM session after save dialog (WPF source contract)");

        // A modal SaveFileDialog can also change a report from measured to
        // controller-only (e.g. Refresh clears the captured mappings). Consent
        // must therefore be requested AFTER choosing the file path, not based
        // only on the pre-dialog snapshot. This is a source-order contract.
        int earlyConsent = source.IndexOf(
            "if (ReadOnlyHidReport.IsControllerOnlyExport(", exportStart, StringComparison.Ordinal);
        int controllerOnlyConsent = source.IndexOf(
            "if (!controllerOnlyConsentGiven && ReadOnlyHidReport.IsControllerOnlyExport(",
            dialogIndex, StringComparison.Ordinal);
        int finalDirectRead = source.IndexOf(
            "WinmmJoystick.TryRead(exportDevice.Id", dialogIndex, StringComparison.Ordinal);
        int postConsentGate = source.IndexOf(
            "if (!ReadOnlyHidReport.CanExport(", controllerOnlyConsent, StringComparison.Ordinal);
        if (earlyConsent <= exportStart || earlyConsent >= dialogIndex ||
            controllerOnlyConsent <= dialogIndex ||
            finalDirectRead <= controllerOnlyConsent ||
            postConsentGate <= controllerOnlyConsent || postConsentGate >= finalDirectRead ||
            !source[controllerOnlyConsent..postConsentGate].Contains(
                "Controller-only report", StringComparison.Ordinal) ||
            !source[postConsentGate..finalDirectRead].Contains(
                "Input changed during confirmation. No report saved.", StringComparison.Ordinal))
            throw new Exception("FAILED: controller-only consent must be fresh after SaveFileDialog and session rechecked");
        Console.WriteLine("PASS: early controller-only consent and post-dialog recheck (WPF source contract)");

        // The timer might not have polled after the save dialog closed. A
        // final direct read must precede serialization and invalidate the
        // session if WinMM fails or any reported/assumed axis is out of range.
        // This is a source contract, not a physical WinMM or WPF dialog test.
        int freshRead = postDialog.IndexOf("WinmmJoystick.TryRead(exportDevice.Id", StringComparison.Ordinal);
        int freshBounds = postDialog.IndexOf("WinmmJoystick.IsSampleWithinAxisBounds(exportDevice, exportSample)", StringComparison.Ordinal);
        int invalidate = postDialog.IndexOf("_winmmSessionInvalid = true;", StringComparison.Ordinal);
        int staleGauges = postDialog.IndexOf("MarkLiveReadingsUnavailable();", StringComparison.Ordinal);
        if (!postDialog.Contains("if (_device is { } exportDevice &&", StringComparison.Ordinal) ||
            freshRead < 0 || freshBounds <= freshRead ||
            invalidate <= freshBounds || staleGauges <= invalidate ||
            !postDialog.Contains("No report saved. Click Refresh and capture pedals again.", StringComparison.Ordinal))
            throw new Exception("FAILED: JSON export must reject a failed fresh WinMM read before writing");
        Console.WriteLine("PASS: JSON export requires fresh WinMM read and valid axis bounds (WPF source contract)");
        
        // Capture_Click makes a direct WinMM read outside Poll. A single
        // impossible axis value must invalidate the session before any new
        // GuidedCalibration is constructed, not wait for a later timer tick.
        int captureStart = source.IndexOf("private void Capture_Click(", StringComparison.Ordinal);
        int captureEnd = source.IndexOf("private void Poll(", captureStart, StringComparison.Ordinal);
        if (captureStart < 0 || captureEnd <= captureStart)
            throw new Exception("FAILED: direct WinMM capture handler not found");
        string captureBody = source[captureStart..captureEnd];
        int readIndex = captureBody.IndexOf("WinmmJoystick.TryRead(", StringComparison.Ordinal);
        int boundsIndex = captureBody.IndexOf("WinmmJoystick.IsSampleWithinAxisBounds(", StringComparison.Ordinal);
        int invalidIndex = captureBody.IndexOf("_winmmSessionInvalid = true;", StringComparison.Ordinal);
        int beginIndex = captureBody.IndexOf("new GuidedCalibration(", StringComparison.Ordinal);
        if (readIndex < 0 || boundsIndex <= readIndex ||
            invalidIndex <= boundsIndex || beginIndex <= invalidIndex)
            throw new Exception("FAILED: direct capture must reject out-of-range WinMM samples before starting calibration");
        Console.WriteLine("PASS: direct capture invalidates out-of-range WinMM session (WPF source contract)");

        // Direct Capture_Click also performs a WinMM read. When that read fails,
        // MarkLiveReadingsUnavailable labels earlier results STALE; the final
        // capture footer must not overwrite that warning with an unmarked error.
        // Source-order contract only: no WPF window or physical pedal exercised.
        int directCaptureStaleMark = captureBody.IndexOf(
            "MarkLiveReadingsUnavailable();", StringComparison.Ordinal);
        int directCaptureStaleFooter = captureBody.IndexOf(
            "CaptureFooter.Text = \"STALE — WinMM read failed or an axis was out of range.",
            StringComparison.Ordinal);
        if (directCaptureStaleMark < 0 ||
            directCaptureStaleFooter <= directCaptureStaleMark)
            throw new Exception("FAILED: direct WinMM capture read failure overwrote STALE footer");
        Console.WriteLine("PASS: direct capture read failure keeps visible STALE footer (WPF source contract)");

        // Changing the optional clutch selection while an idle WinMM check
        // is active must cancel it before Poll can publish a mixed-session
        // result. This is a source-level regression, not a WPF/pedal test.
        int clutchStart = source.IndexOf("private void ClutchCheck_Changed(", StringComparison.Ordinal);
        int clutchEnd = source.IndexOf("private void UpdateMappingLabels(", clutchStart, StringComparison.Ordinal);
        if (clutchStart < 0 || clutchEnd <= clutchStart)
            throw new Exception("FAILED: optional clutch handler not found");
        string clutchBody = source[clutchStart..clutchEnd];
        int cancelStart = clutchBody.IndexOf("if (_noise is not null)", StringComparison.Ordinal);
        int disabledBranch = clutchBody.IndexOf("if (ClutchCheck.IsChecked != true)", StringComparison.Ordinal);
        if (cancelStart < 0 || disabledBranch <= cancelStart)
            throw new Exception("FAILED: clutch selection must cancel an active idle check on enable or disable");
        string cancellation = clutchBody[cancelStart..disabledBranch];
        foreach (string expected in new[]
        {
            "_noise = null;",
            "CheckSignalButton.IsEnabled = true;",
            "LongSignalButton.IsEnabled = true;",
            "_lastNoise = null;",
            "_lastSignalHealth = null;",
            "signal check cancelled"
        })
            if (!cancellation.Contains(expected, StringComparison.Ordinal))
                throw new Exception($"FAILED: optional clutch idle-check cancellation missing: {expected}");
        Console.WriteLine("PASS: clutch selection cancels in-flight WinMM idle check (WPF source contract)");

        // A failed guided recapture leaves the earlier WinMM preview in the
        // same session. Tell the user explicitly that the old result was NOT
        // replaced; this source contract does not exercise WPF or a pedal.
        int failedRecapture = source.IndexOf(
            "CaptureQualityText.Text = \"Measurement not saved · Retry\";",
            StringComparison.Ordinal);
        int retainedWarning = source.IndexOf(
            "Previous WinMM preview retained (not remeasured); retry to replace it.",
            failedRecapture, StringComparison.Ordinal);
        int nextHandler = source.IndexOf(
            "private double GetPercent(", failedRecapture, StringComparison.Ordinal);
        if (failedRecapture < 0 || retainedWarning <= failedRecapture ||
            nextHandler <= retainedWarning ||
            !source[failedRecapture..retainedWarning].Contains(
                "_mappings.ContainsKey(guide.Kind)", StringComparison.Ordinal))
            throw new Exception("FAILED: failed guided recapture must disclose retained prior WinMM preview");
        Console.WriteLine("PASS: failed guided recapture discloses retained prior WinMM preview (WPF source contract)");
    }
}
