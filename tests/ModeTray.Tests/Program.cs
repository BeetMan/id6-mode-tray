using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using ID6ModeTray;

if (args.Length == 2 && args[0] == "--render-popup")
{
    return RenderPopup(args[1]);
}
if (args.Length == 4 && args[0] == "--lock-worker")
{
    return RunLockWorker(args[1], args[2], args[3]);
}

var tests = new List<(string Name, Action Body)>
{
    ("Profile discovery isolates malformed files", ProfileDiscoveryIsolatesMalformedFiles),
    ("Mode catalog caches until fingerprint changes", ModeCatalogCachesUntilFingerprintChanges),
    ("Mode catalog preserves snapshot after load failure", ModeCatalogPreservesSnapshotAfterLoadFailure),
    ("Mode catalog rechecks changes during load", ModeCatalogRechecksChangesDuringLoad),
    ("Mode state reads the active Turbo profile", ModeStateReadsActiveTurboProfile),
    ("Mode state classifies missing and malformed files", ModeStateClassifiesMissingAndMalformedFiles),
    ("Mode state retries a transient malformed file", ModeStateRetriesTransientMalformedFile),
    ("Mode state classifies a locked file", ModeStateClassifiesLockedFile),
    ("Mode state rejects a missing operating mode", ModeStateRejectsMissingOperatingMode),
    ("Mode popup replaces only custom tiles", ModePopupReplacesOnlyCustomTiles),
    ("Mode catalog starts when initial fingerprinting fails", ModeCatalogStartsWhenInitialFingerprintingFails),
    ("Cleared profile alias does not return from cache", ClearedProfileAliasDoesNotReturnFromCache),
    ("Mode popup preserves busy status during refresh", ModePopupPreservesBusyStatusDuringRefresh),
    ("Popup placement supports all taskbar edges", PopupPlacementSupportsAllTaskbarEdges),
    ("Popup placement clamps negative and small monitors", PopupPlacementClampsNegativeAndSmallMonitors),
    ("Toast placement supports negative monitor coordinates", ToastPlacementSupportsNegativeMonitorCoordinates),
    ("Mode tile supports keyboard and accessibility", ModeTileSupportsKeyboardAndAccessibility),
    ("Mode popup uses compact readable styling", ModePopupUsesCompactReadableStyling),
    ("CLI status emits active mode and JSON state", CliStatusEmitsActiveModeAndJsonState),
    ("CLI list exposes executable command IDs", CliListExposesExecutableCommandIds),
    ("CLI JSON errors have stable failure schema", CliJsonErrorsHaveStableFailureSchema),
    ("Control lock serializes separate processes", ControlLockSerializesSeparateProcesses),
    ("CLI exception output remains valid JSON", CliExceptionOutputRemainsValidJson),
    ("CLI list IDs round-trip through switch parsing", CliListIdsRoundTripThroughSwitchParsing),
    ("Control lock recovers from abandoned mutex", ControlLockRecoversFromAbandonedMutex),
    ("Control lock times out and releases cleanly", ControlLockTimesOutAndReleasesCleanly)
};
var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL: {test.Name}: {ex}");
    }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static void ProfileDiscoveryIsolatesMalformedFiles()
{
    var directory = Directory.CreateDirectory(Path.Combine(
        AppContext.BaseDirectory,
        "test-data",
        Guid.NewGuid().ToString("N")));
    try
    {
        var configured = Path.Combine(directory.FullName, "profiles.json");
        var first = Path.Combine(directory.FullName, "Mode4_Profile1.json");
        var malformed = Path.Combine(directory.FullName, "Mode4_Profile2.json");
        var inactive = Path.Combine(directory.FullName, "Mode4_Profile3.json");
        var fourth = Path.Combine(directory.FullName, "Mode4_Profile4.json");
        File.WriteAllText(configured, "[]");
        File.WriteAllText(first, "{\"Activated\":true,\"CustomizeName\":\"1\"}");
        File.WriteAllText(malformed, "{not-json");
        File.WriteAllText(inactive, "{\"Activated\":false}");
        File.WriteAllText(fourth, "{\"Activated\":true,\"CustomizeName\":\"Custom name\"}");

        var modes = ModeDefinition.Load(
            configured,
            new[] { (0, first), (1, malformed), (2, inactive), (3, fourth) },
            new Dictionary<int, string> { [0] = "One", [3] = "Four" });

        AssertEqual(2, modes.Count);
        AssertEqual(0, modes[0].ProfileIndex);
        AssertEqual("Profile 1 - One", modes[0].Label);
        AssertEqual(3, modes[1].ProfileIndex);
        AssertEqual("Profile 4 - Four", modes[1].Label);
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static void ModeCatalogCachesUntilFingerprintChanges()
{
    var fingerprint = "a";
    var loads = 0;
    var catalog = new ModeCatalog(
        () => fingerprint,
        () => { loads++; return new ModeDefinition[] { new($"Mode {loads}", "ACTION", 0, 0) }; });

    AssertEqual(1, loads);
    AssertFalse(catalog.Refresh(), "Unchanged fingerprint must not reload.");
    fingerprint = "b";
    AssertTrue(catalog.Refresh(), "Changed fingerprint must reload.");
    AssertEqual("Mode 2", catalog.Modes.Single().Label);
    AssertFalse(catalog.Refresh(), "New fingerprint must be cached.");
    AssertEqual(2, loads);
}

static void ModeCatalogPreservesSnapshotAfterLoadFailure()
{
    var fingerprint = "a";
    var fail = false;
    var loads = 0;
    var catalog = new ModeCatalog(
        () => fingerprint,
        () =>
        {
            loads++;
            if (fail) { throw new InvalidDataException("bad profile"); }
            return new ModeDefinition[] { new($"Mode {loads}", "ACTION", 0, 0) };
        });

    fingerprint = "b";
    fail = true;
    AssertFalse(catalog.Refresh(), "Failed reload must report no updated snapshot.");
    AssertEqual("Mode 1", catalog.Modes.Single().Label);
    fail = false;
    AssertTrue(catalog.Refresh(), "A later refresh must retry after failure.");
    AssertEqual("Mode 3", catalog.Modes.Single().Label);
}

static void ModeCatalogRechecksChangesDuringLoad()
{
    var fingerprint = "a";
    var loads = 0;
    var catalog = new ModeCatalog(
        () => fingerprint,
        () =>
        {
            loads++;
            if (loads == 2) { fingerprint = "c"; }
            return new ModeDefinition[] { new($"Mode {loads}", "ACTION", 0, 0) };
        });

    fingerprint = "b";
    AssertTrue(catalog.Refresh(), "Changed fingerprint must reload.");
    AssertTrue(catalog.Refresh(), "A change during load must force another refresh.");
    AssertEqual(3, loads);
}

static void ModeStateReadsActiveTurboProfile()
{
    var directory = CreateTestDirectory();
    try
    {
        var main = Path.Combine(directory.FullName, "MainOption.json");
        var turbo = Path.Combine(directory.FullName, "Mode3_Profile1.json");
        WriteMainState(main, operatingMode: 2);
        File.WriteAllText(turbo, "{\"Silent\":1,\"Extreme\":0}");

        var result = ModeState.ReadFrom(main, _ => turbo, maxAttempts: 1);

        AssertEqual<ModeStateError?>(null, result.Error);
        AssertEqual(2, result.State!.Value.OperatingMode);
        AssertEqual(1, result.State.Value.TurboSilent);
        AssertEqual(0, result.State.Value.TurboExtreme);
        AssertEqual(150, result.State.Value.TurboGpuOcOffset);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModeStateClassifiesMissingAndMalformedFiles()
{
    var directory = CreateTestDirectory();
    try
    {
        var main = Path.Combine(directory.FullName, "MainOption.json");
        var missing = ModeState.ReadFrom(main, _ => main, maxAttempts: 1);
        AssertEqual(ModeStateErrorKind.NotFound, missing.Error!.Kind);

        File.WriteAllText(main, "{not-json");
        var malformed = ModeState.ReadFrom(main, _ => main, maxAttempts: 1);
        AssertEqual(ModeStateErrorKind.InvalidData, malformed.Error!.Kind);
        AssertEqual<ModeState?>(null, malformed.State);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModeStateRetriesTransientMalformedFile()
{
    var directory = CreateTestDirectory();
    try
    {
        var main = Path.Combine(directory.FullName, "MainOption.json");
        File.WriteAllText(main, "{not-json");
        var repair = Task.Run(async () =>
        {
            await Task.Delay(150);
            WriteMainState(main);
        });

        var result = ModeState.ReadFrom(main, _ => main, maxAttempts: 10);
        repair.Wait();
        AssertEqual<ModeStateError?>(null, result.Error);
        AssertEqual(3, result.State!.Value.OperatingMode);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModeStateClassifiesLockedFile()
{
    var directory = CreateTestDirectory();
    try
    {
        var main = Path.Combine(directory.FullName, "MainOption.json");
        WriteMainState(main);
        using var locked = new FileStream(main, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = ModeState.ReadFrom(main, _ => main, maxAttempts: 2);
        AssertEqual(ModeStateErrorKind.IO, result.Error!.Kind);
        AssertEqual<ModeState?>(null, result.State);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModeStateRejectsMissingOperatingMode()
{
    var directory = CreateTestDirectory();
    try
    {
        var main = Path.Combine(directory.FullName, "MainOption.json");
        File.WriteAllText(main, "{\"CustomProfileIndex\":1}");
        var result = ModeState.ReadFrom(main, _ => main, maxAttempts: 1);
        AssertEqual(ModeStateErrorKind.InvalidData, result.Error!.Kind);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModePopupReplacesOnlyCustomTiles()
{
    RunInSta(() =>
    {
        var standard = new ModeDefinition("Office", "ACTION", 0, 0);
        var first = new ModeDefinition("First", "ACTION", 3, 0);
        var second = new ModeDefinition("Second", "ACTION", 3, 1);
        var third = new ModeDefinition("Third", "ACTION", 3, 2);
        using var popup = new ModePopup(
            new[] { standard, first },
            _ => { },
            _ => { },
            _ => { },
            () => { },
            () => { });
        var tilesField = typeof(ModePopup).GetField(
            "modeTiles",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        List<(ModeTile Tile, ModeDefinition Mode)> Tiles() =>
            (List<(ModeTile Tile, ModeDefinition Mode)>)tilesField.GetValue(popup)!;
        var standardTile = Tiles().Single(entry => entry.Mode.OperatingMode == 0).Tile;
        var oldCustomTile = Tiles().Single(entry => entry.Mode.OperatingMode == 3).Tile;

        popup.SetModes(new[] { standard, second, third });

        AssertEqual(3, Tiles().Count);
        AssertTrue(ReferenceEquals(standardTile, Tiles().Single(entry => entry.Mode.OperatingMode == 0).Tile), "Standard tile must be preserved.");
        AssertTrue(oldCustomTile.IsDisposed, "Removed custom tile must be disposed.");
        AssertEqual("Second", Tiles().Single(entry => entry.Mode.ProfileIndex == 1).Tile.Text);
        AssertEqual("Third", Tiles().Single(entry => entry.Mode.ProfileIndex == 2).Tile.Text);
    });
}

static void ModeCatalogStartsWhenInitialFingerprintingFails()
{
    var fingerprintAttempts = 0;
    var loads = 0;
    var catalog = new ModeCatalog(
        () =>
        {
            fingerprintAttempts++;
            if (fingerprintAttempts == 1) { throw new DirectoryNotFoundException("missing OEM directory"); }
            return "available";
        },
        () => { loads++; return new ModeDefinition[] { new($"Mode {loads}", "ACTION", 0, 0) }; });

    AssertEqual("Mode 1", catalog.Modes.Single().Label);
    AssertTrue(catalog.Refresh(), "Initial fingerprint failure must be retried.");
    AssertEqual("Mode 2", catalog.Modes.Single().Label);
    AssertFalse(catalog.Refresh(), "Recovered fingerprint must be cached.");
    AssertEqual(2, loads);
}

static void ClearedProfileAliasDoesNotReturnFromCache()
{
    var directory = CreateTestDirectory();
    try
    {
        var settings = Path.Combine(directory.FullName, "settings.dat");
        File.WriteAllText(
            settings,
            "[{\"Mode\": \"Custom\", \"Index\": \"0\", \"AliasName\": \"One\"}]",
            Encoding.Unicode);
        AssertEqual("One", ControlCenterProfileNames.Load(settings)[0]);

        File.WriteAllText(settings, "[]", Encoding.Unicode);
        AssertEqual(0, ControlCenterProfileNames.Load(settings).Count);
        File.Delete(settings);
        AssertEqual(0, ControlCenterProfileNames.Load(settings).Count);
    }
    finally { directory.Delete(recursive: true); }
}

static void ModePopupPreservesBusyStatusDuringRefresh()
{
    RunInSta(() =>
    {
        using var popup = new ModePopup(
            new ModeDefinition[] { new("Office", "ACTION", 0, 0) },
            _ => { }, _ => { }, _ => { }, () => { }, () => { });
        var statusField = typeof(ModePopup).GetField(
            "statusLabel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var status = (SafeTextLabel)statusField.GetValue(popup)!;
        var error = new ModeStateError(ModeStateErrorKind.IO, "locked");

        popup.SetBusy(true);
        var busyText = status.Text;
        popup.UpdateState(null, error, preserveStatus: true);
        AssertEqual(busyText, status.Text);
        popup.SetBusy(false);
        popup.UpdateState(null, error);
        AssertEqual(error.UserMessage, status.Text);
    });
}

static void RunInSta(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { action(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) { throw failure; }
}

static void ModePopupUsesCompactReadableStyling()
{
    RunInSta(() =>
    {
        using var popup = new ModePopup(
            new ModeDefinition[] { new("Office", "ACTION", 0, 0) },
            _ => { }, _ => { }, _ => { }, () => { }, () => { });
        popup.UpdateState(new ModeState(3, 1, 0, -1, -1, 150, 0));
        var tilesField = typeof(ModePopup).GetField(
            "modeTiles",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var tiles = (List<(ModeTile Tile, ModeDefinition Mode)>)tilesField.GetValue(popup)!;
        AssertTrue(tiles.All(entry => entry.Tile.Height == 60), "Mode tiles should use compact 60px rows.");
        var ocButton = (Button)typeof(ModePopup).GetField(
            "turboOcButton",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(popup)!;
        var contrast = ContrastRatio(ocButton.ForeColor, ocButton.BackColor);
        AssertTrue(contrast >= 4.5, $"Disabled OC text contrast {contrast:F2} must be at least 4.5:1.");
    });
}

static double ContrastRatio(Color foreground, Color background)
{
    static double Channel(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
    static double Luminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    var first = Luminance(foreground);
    var second = Luminance(background);
    return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
}

static void PopupPlacementSupportsAllTaskbarEdges()
{
    var popup = new Size(705, 1140);
    AssertEqual(
        new Point(1139, 388),
        PopupPlacement.Calculate(
            new Rectangle(0, 0, 2560, 1600),
            new Rectangle(0, 0, 2560, 1536),
            popup,
            new Point(1820, 1552)));
    AssertEqual(
        new Point(1139, 72),
        PopupPlacement.Calculate(
            new Rectangle(0, 0, 2560, 1600),
            new Rectangle(0, 64, 2560, 1536),
            popup,
            new Point(1820, 100)));
    AssertEqual(
        new Point(88, 8),
        PopupPlacement.Calculate(
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(80, 0, 1920, 1080),
            new Size(470, 760),
            new Point(30, 600)));
    AssertEqual(
        new Point(1362, 124),
        PopupPlacement.Calculate(
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(0, 0, 1840, 1080),
            new Size(470, 300),
            new Point(1870, 400)));
}

static void PopupPlacementClampsNegativeAndSmallMonitors()
{
    AssertEqual(
        new Point(-526, 412),
        PopupPlacement.Calculate(
            new Rectangle(-1600, 0, 1600, 1200),
            new Rectangle(-1600, 0, 1600, 1120),
            new Size(500, 700),
            new Point(-50, 1100)));
    AssertEqual(
        new Point(100, 100),
        PopupPlacement.Calculate(
            new Rectangle(100, 100, 600, 500),
            new Rectangle(100, 100, 600, 500),
            new Size(800, 900),
            new Point(650, 550)));
    AssertEqual(568, PopupPlacement.FitHeight(1000, 600, 32));
    AssertEqual(1, PopupPlacement.FitHeight(100, 20, 32));
    AssertEqual(470, PopupPlacement.FitWidth(470, 1000, 16));
    AssertEqual(184, PopupPlacement.FitWidth(470, 200, 16));
    AssertEqual(1, PopupPlacement.FitWidth(470, 12, 16));
}

static void ToastPlacementSupportsNegativeMonitorCoordinates()
{
    AssertEqual(
        new Point(-378, 990),
        PopupPlacement.BottomRight(
            new Rectangle(-1600, 0, 1600, 1120),
            new Size(360, 112),
            18));
}

static void ModeTileSupportsKeyboardAndAccessibility()
{
    RunInSta(() =>
    {
        using var tile = new ModeTile("Office");
        var clicks = 0;
        tile.Click += (_, _) => clicks++;
        AssertTrue(tile.TabStop, "Mode tile must participate in tab order.");
        AssertEqual(AccessibleRole.PushButton, tile.AccessibleRole);
        AssertEqual("Office", tile.AccessibleName);
        var onKeyDown = typeof(ModeTile).GetMethod(
            "OnKeyDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var key in new[] { Keys.Enter, Keys.Space })
        {
            var args = new KeyEventArgs(key);
            onKeyDown.Invoke(tile, new object[] { args });
            AssertTrue(args.Handled, "Activation key must be handled.");
        }
        AssertEqual(2, clicks);
        tile.SetSelected(true);
        AssertTrue(tile.AccessibilityObject.State.HasFlag(AccessibleStates.Selected), "Selected state must be exposed to UIA.");
        tile.AccessibilityObject.DoDefaultAction();
        AssertEqual(3, clicks);
    });
}

static void PerformLayoutTree(Control control)
{
    control.PerformLayout();
    foreach (Control child in control.Controls)
    {
        PerformLayoutTree(child);
    }
}

static void DrawControlTree(Control control, Bitmap bitmap, Point parentOrigin)
{
    var destination = new Rectangle(
        parentOrigin.X + control.Location.X,
        parentOrigin.Y + control.Location.Y,
        control.Width,
        control.Height);
    control.DrawToBitmap(bitmap, destination);
    foreach (Control child in control.Controls)
    {
        DrawControlTree(child, bitmap, destination.Location);
    }
}

static void CreateControlTree(Control control)
{
    control.CreateControl();
    foreach (Control child in control.Controls)
    {
        CreateControlTree(child);
    }
}

static int RenderPopup(string outputPath)
{
    RunInSta(() =>
    {
        var configuredPath = Path.Combine(
            Environment.CurrentDirectory,
            "ModeTray",
            "profiles.json");
        var modes = ModeDefinition.Load(
            configuredPath,
            ControlCenterPaths.CustomProfiles(),
            ControlCenterProfileNames.Load());
        using var popup = new ModePopup(
            modes,
            _ => { }, _ => { }, _ => { }, () => { }, () => { });
        var current = ModeState.Read().State
            ?? new ModeState(3, 1, 0, -1, -1, 150, 0);
        popup.UpdateState(current);
        var content = (FlowLayoutPanel)typeof(ModePopup).GetField(
            "content",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(popup)!;
        var desiredHeight = content.Padding.Vertical;
        foreach (Control control in content.Controls)
        {
            desiredHeight += control.Height + control.Margin.Vertical;
        }
        Console.WriteLine($"Render modes={modes.Count}, controls={content.Controls.Count}, desiredHeight={desiredHeight + popup.Padding.Vertical}");
        popup.MaximumSize = new Size(5000, 5000);
        popup.ClientSize = new Size(470, desiredHeight + popup.Padding.Vertical);
        content.AutoScroll = false;
        CreateControlTree(popup);
        PerformLayoutTree(popup);
        using (var warmUp = new Bitmap(popup.Width, popup.Height))
        {
            DrawControlTree(popup, warmUp, Point.Empty);
        }
        using var bitmap = new Bitmap(popup.Width, popup.Height);
        DrawControlTree(popup, bitmap, Point.Empty);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bitmap.Save(fullPath, ImageFormat.Png);
        Console.WriteLine($"Rendered {popup.Width}x{popup.Height} popup to {fullPath}");
    });
    return 0;
}

static void CliStatusEmitsActiveModeAndJsonState()
{
    var modes = TestModes();
    var state = new ModeState(3, 1, 0, -1, -1, 150, 0);
    var output = new List<string>();
    var exitCode = CommandLineInterface.Execute(
        new[] { "--status", "--json" },
        output.Add,
        modes,
        new ModeReadResult(state, null));
    using var json = System.Text.Json.JsonDocument.Parse(output.Single());
    AssertEqual(0, exitCode);
    AssertEqual(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
    AssertEqual("custom:2", json.RootElement.GetProperty("mode").GetString());
    AssertEqual("Profile 2 - 静音办公", json.RootElement.GetProperty("label").GetString());
    AssertEqual(150, json.RootElement.GetProperty("state").GetProperty("turboGpuOcOffset").GetInt32());
    AssertFalse(json.RootElement.GetProperty("state").GetProperty("fanBoostEnabled").GetBoolean(), "Fan must be false.");

    output.Clear();
    exitCode = CommandLineInterface.Execute(new[] { "--status" }, output.Add, modes, new ModeReadResult(state, null));
    AssertEqual(0, exitCode);
    AssertTrue(output[0].Contains("Profile 2 - 静音办公"), "Human status must contain the active label.");
}

static void CliListExposesExecutableCommandIds()
{
    var output = new List<string>();
    var exitCode = CommandLineInterface.Execute(
        new[] { "--list", "--json" },
        output.Add,
        TestModes(),
        new ModeReadResult(new ModeState(3, 1, 0, -1, -1, 150, 0), null));
    using var json = System.Text.Json.JsonDocument.Parse(output.Single());
    var modes = json.RootElement.GetProperty("modes").EnumerateArray().ToArray();
    AssertEqual(0, exitCode);
    AssertEqual(9, modes.Length);
    AssertEqual("office", modes[0].GetProperty("id").GetString());
    AssertEqual("turbo:silent", modes[2].GetProperty("id").GetString());
    AssertEqual("custom:5", modes[8].GetProperty("id").GetString());
    AssertTrue(modes[5].GetProperty("active").GetBoolean(), "Custom Profile 2 must be active.");
}

static void CliJsonErrorsHaveStableFailureSchema()
{
    var output = new List<string>();
    var error = new ModeStateError(ModeStateErrorKind.IO, "locked");
    var exitCode = CommandLineInterface.Execute(
        new[] { "--json", "--status" },
        output.Add,
        TestModes(),
        new ModeReadResult(null, error));
    using var json = System.Text.Json.JsonDocument.Parse(output.Single());
    AssertEqual(1, exitCode);
    AssertFalse(json.RootElement.GetProperty("ok").GetBoolean(), "Status must report failure.");
    AssertEqual("IO", json.RootElement.GetProperty("error").GetProperty("kind").GetString());
    AssertEqual(error.UserMessage, json.RootElement.GetProperty("error").GetProperty("message").GetString());
}

static void ControlLockSerializesSeparateProcesses()
{
    var directory = CreateTestDirectory();
    try
    {
        var events = Path.Combine(directory.FullName, "events.csv");
        var gate = Path.Combine(directory.FullName, "start.gate");
        var mutexName = "Local\\ID6ModeTray.Test." + Guid.NewGuid().ToString("N");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No test executable path.");
        var children = Enumerable.Range(0, 2).Select(_ =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--lock-worker \"{events}\" {mutexName} \"{gate}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Unable to start lock worker.")).ToArray();
        foreach (var child in children)
        {
            AssertTrue(WaitForFile($"{events}.ready.{child.Id}", 5000), "Lock worker did not reach start gate.");
        }
        File.WriteAllText(gate, "go");
        foreach (var child in children)
        {
            AssertTrue(child.WaitForExit(10000), "Lock worker timed out.");
            AssertEqual(0, child.ExitCode);
            child.Dispose();
        }
        var intervals = File.ReadAllLines(events)
            .Select(line => line.Split(','))
            .GroupBy(parts => parts[0])
            .Select(group => new
            {
                Start = long.Parse(group.Single(parts => parts[1] == "enter")[2]),
                End = long.Parse(group.Single(parts => parts[1] == "exit")[2])
            })
            .OrderBy(interval => interval.Start)
            .ToArray();
        AssertEqual(2, intervals.Length);
        AssertTrue(intervals[0].End <= intervals[1].Start, "Critical sections in separate processes overlapped.");
    }
    finally { directory.Delete(recursive: true); }
}

static void CliExceptionOutputRemainsValidJson()
{
    using var json = System.Text.Json.JsonDocument.Parse(
        CommandLineInterface.ExceptionJson(new InvalidDataException("broken profile")));
    AssertEqual(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
    AssertFalse(json.RootElement.GetProperty("ok").GetBoolean(), "Exception JSON must be unsuccessful.");
    AssertEqual("Exception", json.RootElement.GetProperty("error").GetProperty("kind").GetString());
    AssertEqual("broken profile", json.RootElement.GetProperty("error").GetProperty("message").GetString());
}

static void CliListIdsRoundTripThroughSwitchParsing()
{
    var modes = TestModes().Concat(new ModeDefinition[]
    {
        new("Unknown", "ACTION", 4, 0),
        new("Invalid Turbo", "ACTION", 2, 0, 0, 0),
        new("Duplicate Office", "ACTION", 0, 0)
    }).ToArray();
    var output = new List<string>();
    var exitCode = CommandLineInterface.Execute(
        new[] { "--list", "--json" },
        output.Add,
        modes,
        new ModeReadResult(new ModeState(3, 1, 0, -1, -1, 150, 0), null));
    using var json = System.Text.Json.JsonDocument.Parse(output.Single());
    var listed = json.RootElement.GetProperty("modes").EnumerateArray().ToArray();
    AssertEqual(0, exitCode);
    AssertEqual(9, listed.Length);
    foreach (var mode in listed)
    {
        var id = mode.GetProperty("id").GetString()!;
        var parsed = ModeDefinition.Parse(id, TestModes());
        AssertEqual(mode.GetProperty("label").GetString(), parsed.Label);
    }
}

static void ControlLockRecoversFromAbandonedMutex()
{
    var name = "Local\\ID6ModeTray.Test.Abandoned." + Guid.NewGuid().ToString("N");
    var thread = new Thread(() =>
    {
        var abandoned = new Mutex(false, name);
        abandoned.WaitOne();
    });
    thread.Start();
    thread.Join();
    using var recovered = ControlOperationLock.Enter(name, TimeSpan.FromSeconds(1));
}

static void ControlLockTimesOutAndReleasesCleanly()
{
    var name = "Local\\ID6ModeTray.Test.Timeout." + Guid.NewGuid().ToString("N");
    using (var held = ControlOperationLock.Enter(name))
    {
        Exception? contention = null;
        var contender = new Thread(() =>
        {
            try
            {
                using var second = ControlOperationLock.Enter(name, TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex)
            {
                contention = ex;
            }
        });
        contender.Start();
        contender.Join();
        AssertTrue(contention is TimeoutException, "A held lock must time out when requested from another thread.");
    }
    using var reacquired = ControlOperationLock.Enter(name, TimeSpan.FromSeconds(1));
}

static bool WaitForFile(string path, int timeoutMs)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path)) { return true; }
        Thread.Sleep(25);
    }
    return File.Exists(path);
}

static int RunLockWorker(string path, string mutexName, string gatePath)
{
    File.WriteAllText($"{path}.ready.{Environment.ProcessId}", "ready");
    if (!WaitForFile(gatePath, 5000)) { throw new TimeoutException("Start gate timed out."); }
    using var lease = ControlOperationLock.Enter(mutexName);
    var processId = Environment.ProcessId;
    File.AppendAllText(path, $"{processId},enter,{DateTime.UtcNow.Ticks}\n");
    Thread.Sleep(400);
    File.AppendAllText(path, $"{processId},exit,{DateTime.UtcNow.Ticks}\n");
    return 0;
}

static IReadOnlyList<ModeDefinition> TestModes() =>
[
    new("办公模式", "OPERATING_OFFICE_MODE", 0, 0),
    new("游戏模式", "OPERATING_GAMING_MODE", 1, 0),
    new("狂暴 · 静技", "OPERATING_TURBO_MODE", 2, 0, 1, 0),
    new("狂暴 · 极速", "OPERATING_TURBO_MODE", 2, 0, 0, 1),
    new("Profile 1 - 静音均衡", "OPERATING_CUSTOM_MODE", 3, 0),
    new("Profile 2 - 静音办公", "OPERATING_CUSTOM_MODE", 3, 1),
    new("Profile 3 - 静音游戏", "OPERATING_CUSTOM_MODE", 3, 2),
    new("Profile 4 - 测试", "OPERATING_CUSTOM_MODE", 3, 3),
    new("Profile 5 - 你好", "OPERATING_CUSTOM_MODE", 3, 4)
];

static DirectoryInfo CreateTestDirectory() => Directory.CreateDirectory(Path.Combine(
    AppContext.BaseDirectory,
    "test-data",
    Guid.NewGuid().ToString("N")));

static void WriteMainState(string path, int operatingMode = 3) => File.WriteAllText(
    path,
    $"{{\"OperatingMode\":{operatingMode},\"CustomProfileIndex\":1,\"TurboProfileIndex\":0,\"TurboGPUOCOffset\":150,\"FanBoostEnable\":0}}");

static void AssertTrue(bool value, string message)
{
    if (!value) { throw new InvalidOperationException(message); }
}

static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected <{expected}>, actual <{actual}>.");
    }
}
