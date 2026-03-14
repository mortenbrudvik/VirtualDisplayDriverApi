using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace VirtualDisplayDriver.UITests;

[Collection("App")]
public class DashboardTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private Window Window => _fixture.MainWindow;

    public DashboardTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    // ──────────────────────────────────────────────
    // 1. App Launch & Main Window
    // ──────────────────────────────────────────────

    [Fact]
    public void App_ShouldLaunch_WithCorrectTitle()
    {
        Window.Title.Should().Contain("Virtual Display Driver");
        Window.TakeScreenshot("01_app_launched");
    }

    [Fact]
    public void MainWindow_ShouldHave_SidebarNavigation()
    {
        var navList = Window.FindById("NavListBox");
        navList.Should().NotBeNull();

        var items = navList.FindAllChildren();
        items.Should().HaveCount(5, "there should be 5 navigation items: Status, Displays, Settings, GPU, Activity Log");
    }

    [Fact]
    public void MainWindow_ShouldShow_ActivePageContent()
    {
        // Navigate to Status and verify content is rendered in the content area
        Window.NavigateTo("Status");
        var header = Window.FindById("StatusHeader");
        header.Should().NotBeNull("page content should be visible in the content area");
    }

    [Fact]
    public void MainWindow_ShouldShow_StatusBar()
    {
        // Status bar should show pipe status text (running or stopped)
        var pipeRunning = Window.FindFirstDescendant(cf => cf.ByText("Pipe: Running"));
        var pipeStopped = Window.FindFirstDescendant(cf => cf.ByText("Pipe: Stopped"));
        (pipeRunning is not null || pipeStopped is not null)
            .Should().BeTrue("status bar should show pipe status");
    }

    // ──────────────────────────────────────────────
    // 2. Status Page
    // ──────────────────────────────────────────────

    [Fact]
    public void StatusPage_ShouldLoad_ByDefault()
    {
        Window.NavigateTo("Status");

        var header = Window.FindById("StatusHeader");
        header.Should().NotBeNull();
        header.Name.Should().Be("Status Overview");
        Window.TakeScreenshot("02_status_page");
    }

    [Fact]
    public void StatusPage_ShouldShow_StatCards()
    {
        Window.NavigateTo("Status");

        // Check for stat card labels
        var pipeStatus = Window.FindById("PipeStatusValue");
        pipeStatus.Should().NotBeNull();
        pipeStatus.Name.Should().BeOneOf("Yes", "No");

        var driverInstalled = Window.FindById("DriverInstalledValue");
        driverInstalled.Should().NotBeNull();
        driverInstalled.Name.Should().BeOneOf("Yes", "No");

        var displayCount = Window.FindById("DisplayCountValue");
        displayCount.Should().NotBeNull();
    }

    [Fact]
    public void StatusPage_ShouldHave_RefreshAndPingButtons()
    {
        Window.NavigateTo("Status");

        var refreshBtn = Window.FindById("RefreshStatusButton").AsButton();
        refreshBtn.Should().NotBeNull();
        refreshBtn.IsEnabled.Should().BeTrue();

        var pingBtn = Window.FindById("PingButton").AsButton();
        pingBtn.Should().NotBeNull();
        pingBtn.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StatusPage_RefreshButton_ShouldBeClickable()
    {
        Window.NavigateTo("Status");

        var refreshBtn = Window.FindById("RefreshStatusButton").AsButton();
        refreshBtn.Invoke();

        // Give it a moment to process
        Thread.Sleep(1000);

        // Stat cards should still be present after refresh
        var pipeStatus = Window.FindById("PipeStatusValue");
        pipeStatus.Should().NotBeNull();
        Window.TakeScreenshot("02_status_after_refresh");
    }

    [Fact]
    public void StatusPage_PingButton_ShouldBeClickable()
    {
        Window.NavigateTo("Status");

        var pingBtn = Window.FindById("PingButton").AsButton();
        pingBtn.Invoke();

        Thread.Sleep(1000);

        // Page should still be intact after ping
        var header = Window.FindById("StatusHeader");
        header.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    // 3. Display Management Page
    // ──────────────────────────────────────────────

    [Fact]
    public void DisplayPage_ShouldLoad_WithAllControls()
    {
        Window.NavigateTo("Displays");

        var header = Window.FindById("DisplayHeader");
        header.Should().NotBeNull();
        header.Name.Should().Be("Display Management");

        var countDisplay = Window.FindById("CurrentDisplayCount");
        countDisplay.Should().NotBeNull();

        Window.TakeScreenshot("03_display_page");
    }

    [Fact]
    public void DisplayPage_ShouldHave_AddRemoveButtons()
    {
        Window.NavigateTo("Displays");

        var addBtn = Window.FindById("AddDisplayButton").AsButton();
        addBtn.Should().NotBeNull();

        var removeBtn = Window.FindById("RemoveDisplayButton").AsButton();
        removeBtn.Should().NotBeNull();
    }

    [Fact]
    public void DisplayPage_ShouldHave_SetCountButton()
    {
        Window.NavigateTo("Displays");

        var setBtn = Window.FindById("SetCountButton").AsButton();
        setBtn.Should().NotBeNull();
        setBtn.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void DisplayPage_ShouldHave_RemoveAllButton()
    {
        Window.NavigateTo("Displays");

        var removeAllBtn = Window.FindById("RemoveAllButton").AsButton();
        removeAllBtn.Should().NotBeNull();
    }

    [Fact]
    public void DisplayPage_AddButton_ShouldBeClickable()
    {
        Window.NavigateTo("Displays");

        var addBtn = Window.FindById("AddDisplayButton").AsButton();
        addBtn.Invoke();

        Thread.Sleep(1000);

        // Page should still be intact
        var header = Window.FindById("DisplayHeader");
        header.Should().NotBeNull();
        Window.TakeScreenshot("03_display_after_add");
    }

    // ──────────────────────────────────────────────
    // 4. Settings Page
    // ──────────────────────────────────────────────

    [Fact]
    public void SettingsPage_ShouldLoad_WithHeader()
    {
        Window.NavigateTo("Settings");

        var header = Window.FindById("SettingsHeader");
        header.Should().NotBeNull();
        header.Name.Should().Be("Driver Settings");

        Window.TakeScreenshot("04_settings_page");
    }

    [Fact]
    public void SettingsPage_ShouldHave_HdrPlusToggle()
    {
        Window.NavigateTo("Settings");

        var toggle = Window.FindById("ToggleHdrPlus");
        toggle.Should().NotBeNull();
    }

    [Fact]
    public void SettingsPage_ShouldHave_Sdr10BitToggle()
    {
        Window.NavigateTo("Settings");

        var toggle = Window.FindById("ToggleSdr10Bit");
        toggle.Should().NotBeNull();
    }

    [Fact]
    public void SettingsPage_ShouldHave_LoggingToggles()
    {
        Window.NavigateTo("Settings");

        var debugToggle = Window.FindById("ToggleDebugLogging");
        debugToggle.Should().NotBeNull();

        var loggingToggle = Window.FindById("ToggleLogging");
        loggingToggle.Should().NotBeNull();
    }

    [Fact]
    public void SettingsPage_ShouldHave_RefreshButton()
    {
        Window.NavigateTo("Settings");

        var refreshBtn = Window.FindById("RefreshSettingsButton").AsButton();
        refreshBtn.Should().NotBeNull();
        refreshBtn.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SettingsPage_ShouldShow_ReloadWarning()
    {
        Window.NavigateTo("Settings");

        // The reload warning badge text should be visible
        var warning = Window.FindByText("Triggers driver reload (30s cooldown)", TimeSpan.FromSeconds(3));
        warning.Should().NotBeNull("reload warning should be visible for feature settings");
    }

    [Fact]
    public void SettingsPage_ToggleClick_ShouldWork()
    {
        Window.NavigateTo("Settings");

        var toggle = Window.FindById("ToggleHdrPlus");
        var toggleBtn = toggle.AsToggleButton();

        var initialState = toggleBtn.ToggleState;
        toggleBtn.Toggle();

        Thread.Sleep(1000);

        // After toggle + potential error revert, we just verify the page is still intact
        var header = Window.FindById("SettingsHeader");
        header.Should().NotBeNull();
        Window.TakeScreenshot("04_settings_after_toggle");
    }

    // ──────────────────────────────────────────────
    // 5. GPU Page
    // ──────────────────────────────────────────────

    [Fact]
    public void GpuPage_ShouldLoad_WithHeader()
    {
        Window.NavigateTo("GPU");

        var header = Window.FindById("GpuHeader");
        header.Should().NotBeNull();
        header.Name.Should().Be("GPU Management");

        Window.TakeScreenshot("05_gpu_page");
    }

    [Fact]
    public void GpuPage_ShouldHave_GpuComboBox()
    {
        Window.NavigateTo("GPU");

        var comboBox = Window.FindById("GpuComboBox");
        comboBox.Should().NotBeNull();
    }

    [Fact]
    public void GpuPage_ShouldHave_SetGpuButton()
    {
        Window.NavigateTo("GPU");

        var setBtn = Window.FindById("SetGpuButton").AsButton();
        setBtn.Should().NotBeNull();
    }

    [Fact]
    public void GpuPage_ShouldHave_RefreshButton()
    {
        Window.NavigateTo("GPU");

        var refreshBtn = Window.FindById("RefreshGpuButton").AsButton();
        refreshBtn.Should().NotBeNull();
        refreshBtn.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GpuPage_ShouldShow_InfoCards()
    {
        Window.NavigateTo("GPU");

        // Check for the card labels
        var d3dLabel = Window.FindByText("D3D DEVICE GPU", TimeSpan.FromSeconds(3));
        d3dLabel.Should().NotBeNull();

        var iddCxLabel = Window.FindByText("IDDCX VERSION", TimeSpan.FromSeconds(2));
        iddCxLabel.Should().NotBeNull();

        var assignedLabel = Window.FindByText("ASSIGNED GPU", TimeSpan.FromSeconds(2));
        assignedLabel.Should().NotBeNull();
    }

    [Fact]
    public void GpuPage_ShouldShow_ReloadWarning()
    {
        Window.NavigateTo("GPU");

        var warning = Window.FindByText("Triggers driver reload (30s cooldown)", TimeSpan.FromSeconds(3));
        warning.Should().NotBeNull("GPU selection reload warning should be visible");
    }

    // ──────────────────────────────────────────────
    // 6. Activity Log Page
    // ──────────────────────────────────────────────

    [Fact]
    public void ActivityLogPage_ShouldLoad_WithHeader()
    {
        Window.NavigateTo("Activity Log");

        var header = Window.FindById("ActivityLogHeader");
        header.Should().NotBeNull();
        header.Name.Should().Be("Activity Log");

        Window.TakeScreenshot("06_activity_log_page");
    }

    [Fact]
    public void ActivityLogPage_ShouldHave_Toolbar()
    {
        Window.NavigateTo("Activity Log");

        var clearBtn = Window.FindById("ClearLogButton").AsButton();
        clearBtn.Should().NotBeNull();

        var copyBtn = Window.FindById("CopyLogButton").AsButton();
        copyBtn.Should().NotBeNull();

        var autoScroll = Window.FindById("AutoScrollCheckBox").AsCheckBox();
        autoScroll.Should().NotBeNull();
        autoScroll.IsChecked.Should().BeTrue("auto-scroll should default to checked");
    }

    [Fact]
    public void ActivityLogPage_ShouldHave_LogEntries()
    {
        // Navigate around first to generate log entries
        Window.NavigateTo("Status");
        Thread.Sleep(500);
        Window.NavigateTo("Activity Log");
        Thread.Sleep(500);

        var logList = Window.FindById("LogListBox");
        logList.Should().NotBeNull();

        // There should be some log entries from the initial status page load
        var items = logList.FindAllChildren();
        items.Should().NotBeEmpty("log should have entries from initial app startup and navigation");
        Window.TakeScreenshot("06_activity_log_with_entries");
    }

    [Fact]
    public void ActivityLogPage_ClearButton_ShouldClearLog()
    {
        // Generate some entries first
        Window.NavigateTo("Status");
        Thread.Sleep(500);
        Window.NavigateTo("Activity Log");
        Thread.Sleep(500);

        var clearBtn = Window.FindById("ClearLogButton").AsButton();
        clearBtn.Invoke();

        Thread.Sleep(500);

        var logList = Window.FindById("LogListBox");
        var items = logList.FindAllChildren();
        items.Should().BeEmpty("log should be empty after clicking Clear");
        Window.TakeScreenshot("06_activity_log_cleared");
    }

    // ──────────────────────────────────────────────
    // 7. Navigation Between Pages
    // ──────────────────────────────────────────────

    [Fact]
    public void Navigation_ShouldSwitch_BetweenAllPages()
    {
        // Status
        Window.NavigateTo("Status");
        Window.FindById("StatusHeader").Should().NotBeNull();
        Window.TakeScreenshot("07_nav_status");

        // Displays
        Window.NavigateTo("Displays");
        Window.FindById("DisplayHeader").Should().NotBeNull();
        Window.TakeScreenshot("07_nav_displays");

        // Settings
        Window.NavigateTo("Settings");
        Window.FindById("SettingsHeader").Should().NotBeNull();
        Window.TakeScreenshot("07_nav_settings");

        // GPU
        Window.NavigateTo("GPU");
        Window.FindById("GpuHeader").Should().NotBeNull();
        Window.TakeScreenshot("07_nav_gpu");

        // Activity Log
        Window.NavigateTo("Activity Log");
        Window.FindById("ActivityLogHeader").Should().NotBeNull();
        Window.TakeScreenshot("07_nav_activity_log");
    }

    [Fact]
    public void Navigation_ShouldHighlight_SelectedItem()
    {
        Window.NavigateTo("Displays");

        var navList = Window.FindById("NavListBox");
        var items = navList.FindAllChildren();

        // The second item (Displays) should be selected
        var displaysItem = items[1]; // 0=Status, 1=Displays
        var selectionItemPattern = displaysItem.Patterns.SelectionItem.PatternOrDefault;
        selectionItemPattern.Should().NotBeNull();
        selectionItemPattern!.IsSelected.Value.Should().BeTrue("Displays nav item should be selected");
    }

    // ──────────────────────────────────────────────
    // 8. Sidebar Status Card
    // ──────────────────────────────────────────────

    [Fact]
    public void Sidebar_ShouldShow_PipeStatus()
    {
        var pipeRunning = Window.FindFirstDescendant(cf => cf.ByText("Pipe: Running"));
        var pipeStopped = Window.FindFirstDescendant(cf => cf.ByText("Pipe: Stopped"));
        (pipeRunning is not null || pipeStopped is not null)
            .Should().BeTrue("sidebar should show pipe status");
    }

    [Fact]
    public void Sidebar_ShouldShow_DriverStatus()
    {
        var driverText = Window.FindByText("Driver: Installed", TimeSpan.FromSeconds(1))
                         ?? Window.FindByText("Driver: Not Found", TimeSpan.FromSeconds(1));
        driverText.Should().NotBeNull("sidebar should show driver installed status");
    }
}
