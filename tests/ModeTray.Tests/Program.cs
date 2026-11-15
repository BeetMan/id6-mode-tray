using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using ID6ModeTray;

if (args.Length == 2 && args[0] == "--render-popup")
{
    return RenderPopup(args[1]);
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
    ("Mode tile supports keyboard and accessibility", ModeTileSupportsKeyboardAndAccessibility)
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
