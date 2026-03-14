using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FluentAssertions;
using Xunit;

namespace VirtualDisplayDriver.UITests;

/// <summary>
/// Tests for adding and removing virtual displays.
/// Adapts to driver state: tests actual operations when connected,
/// tests error handling when disconnected.
/// </summary>
[Collection("App")]
public class DisplayOperationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private Window Window => _fixture.MainWindow;

    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(15);

    public DisplayOperationTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Navigate to Displays and wait for full page render.
    /// Always navigates away first to ensure a fresh transient VM.
    /// </summary>
    private void GoToDisplays()
    {
        // Navigate away first to force a fresh transient VM
        Window.NavigateTo("Activity Log");
        Thread.Sleep(500);

        Window.NavigateTo("Displays");

        // Wait for the page to fully render
        var found = Retry.WhileNull(
            () => Window.FindFirstDescendant(cf => cf.ByAutomationId("CurrentDisplayCount")),
            ElementTimeout,
            TimeSpan.FromMilliseconds(500));

        found.Result.Should().NotBeNull("Displays page should render with CurrentDisplayCount visible");
    }

    private bool IsPipeRunning()
    {
        return Window.FindFirstDescendant(cf => cf.ByText("Pipe: Running")) is not null;
    }

    private int GetCount()
    {
        var el = Window.FindById("CurrentDisplayCount", ElementTimeout);
        return int.Parse(el.Name);
    }

    /// <summary>
    /// Look for the error text via its AutomationId.
    /// Returns null if no error is visible.
    /// </summary>
    private string? GetError()
    {
        var el = Window.TryFindById("DisplayErrorText", TimeSpan.FromSeconds(8));
        return el?.Name;
    }

    // ──────────────────────────────────────────────
    // Add Display
    // ──────────────────────────────────────────────

    [Fact]
    public void AddDisplay_ShouldChangeCountOrShowError()
    {
        GoToDisplays();
        var before = GetCount();
        Window.TakeScreenshot("add_01_before");

        Window.FindById("AddDisplayButton", ElementTimeout).AsButton().Invoke();

        if (IsPipeRunning())
        {
            var after = Window.WaitForDisplayCountChange(before);
            after.Should().Be(before + 1);
            Window.TakeScreenshot("add_02_success");
        }
        else
        {
            var error = GetError();
            error.Should().NotBeNullOrEmpty("error message should appear when pipe is stopped");
            GetCount().Should().Be(before, "count should not change on error");
            Window.TakeScreenshot("add_02_error");
        }
    }

    // ──────────────────────────────────────────────
    // Remove Display
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveDisplay_ShouldChangeCountOrShowError()
    {
        GoToDisplays();
        var before = GetCount();

        if (before == 0 && IsPipeRunning())
        {
            // Add one first
            Window.FindById("AddDisplayButton", ElementTimeout).AsButton().Invoke();
            before = Window.WaitForDisplayCountChange(0);
        }

        if (before == 0)
        {
            // Count is 0 and can't add — just verify button exists
            Window.FindById("RemoveDisplayButton", ElementTimeout).Should().NotBeNull();
            return;
        }

        Window.TakeScreenshot("remove_01_before");
        Window.FindById("RemoveDisplayButton", ElementTimeout).AsButton().Invoke();

        if (IsPipeRunning())
        {
            var after = Window.WaitForDisplayCountChange(before);
            after.Should().Be(before - 1);
            Window.TakeScreenshot("remove_02_success");
        }
        else
        {
            var error = GetError();
            error.Should().NotBeNullOrEmpty("error should appear when pipe is stopped");
            Window.TakeScreenshot("remove_02_error");
        }
    }

    // ──────────────────────────────────────────────
    // Remove All
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveAll_ShouldClearCountOrShowError()
    {
        GoToDisplays();
        var before = GetCount();
        Window.TakeScreenshot("removeall_01_before");

        var removeAllBtn = Window.FindById("RemoveAllButton", ElementTimeout).AsButton();

        if (!removeAllBtn.IsEnabled)
        {
            // Count is already 0 — nothing to remove
            before.Should().Be(0, "button disabled means count is already 0");
            Window.TakeScreenshot("removeall_02_already_zero");
            return;
        }

        removeAllBtn.Invoke();

        if (IsPipeRunning())
        {
            Window.WaitForDisplayCount(0).Should().BeTrue("count should reach 0");
            Window.TakeScreenshot("removeall_02_success");
        }
        else
        {
            var error = GetError();
            error.Should().NotBeNullOrEmpty("error should appear when pipe is stopped");
            GetCount().Should().Be(before);
            Window.TakeScreenshot("removeall_02_error");
        }
    }

    // ──────────────────────────────────────────────
    // Set Count via Slider
    // ──────────────────────────────────────────────

    [Fact]
    public void SetCount_ShouldApplyOrShowError()
    {
        GoToDisplays();
        var before = GetCount();
        var target = before == 3 ? 2 : 3;

        var slider = Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Slider));
        slider.Should().NotBeNull();
        slider!.Patterns.RangeValue.PatternOrDefault?.SetValue(target);
        Thread.Sleep(300);

        Window.FindById("SetCountButton", ElementTimeout).AsButton().Invoke();

        if (IsPipeRunning())
        {
            Window.WaitForDisplayCount(target, TimeSpan.FromSeconds(35))
                .Should().BeTrue($"count should reach {target}");
            Window.TakeScreenshot("setcount_success");
        }
        else
        {
            var error = GetError();
            error.Should().NotBeNullOrEmpty("error should appear when pipe is stopped");
            Window.TakeScreenshot("setcount_error");
        }
    }

    // ──────────────────────────────────────────────
    // Round-trip: Add then Remove
    // ──────────────────────────────────────────────

    [Fact]
    public void AddThenRemove_ShouldRoundTrip()
    {
        GoToDisplays();

        if (!IsPipeRunning())
        {
            // Verify controls exist even when disconnected
            Window.FindById("AddDisplayButton", ElementTimeout).Should().NotBeNull();
            Window.FindById("RemoveDisplayButton", ElementTimeout).Should().NotBeNull();
            return;
        }

        var original = GetCount();

        Window.FindById("AddDisplayButton", ElementTimeout).AsButton().Invoke();
        var afterAdd = Window.WaitForDisplayCountChange(original);
        afterAdd.Should().Be(original + 1);
        Window.TakeScreenshot("roundtrip_added");

        Window.FindById("RemoveDisplayButton", ElementTimeout).AsButton().Invoke();
        var afterRemove = Window.WaitForDisplayCountChange(afterAdd);
        afterRemove.Should().Be(original);
        Window.TakeScreenshot("roundtrip_removed");
    }

    // ──────────────────────────────────────────────
    // Button State
    // ──────────────────────────────────────────────

    [Fact]
    public void Buttons_ShouldAllBePresent()
    {
        GoToDisplays();

        Window.FindById("AddDisplayButton", ElementTimeout).Should().NotBeNull();
        Window.FindById("RemoveDisplayButton", ElementTimeout).Should().NotBeNull();
        Window.FindById("SetCountButton", ElementTimeout).Should().NotBeNull();
        Window.FindById("RemoveAllButton", ElementTimeout).Should().NotBeNull();
    }

    [Fact]
    public void AddButton_ShouldAlwaysBeEnabled()
    {
        GoToDisplays();
        Window.FindById("AddDisplayButton", ElementTimeout).AsButton()
            .IsEnabled.Should().BeTrue();
    }

    // ──────────────────────────────────────────────
    // Slider
    // ──────────────────────────────────────────────

    [Fact]
    public void Slider_ShouldAcceptValues()
    {
        GoToDisplays();

        var slider = Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Slider));
        slider.Should().NotBeNull();

        var rv = slider!.Patterns.RangeValue.PatternOrDefault;
        rv.Should().NotBeNull();

        rv!.SetValue(5);
        Thread.Sleep(200);
        ((double)rv.Value).Should().BeApproximately(5, 0.5);

        rv.SetValue(0);
        Thread.Sleep(200);
        ((double)rv.Value).Should().BeApproximately(0, 0.5);
    }

    // ──────────────────────────────────────────────
    // Activity Log integration
    // ──────────────────────────────────────────────

    [Fact]
    public void ActivityLog_ShouldRecordDisplayOperations()
    {
        // Clear log
        Window.NavigateTo("Activity Log");
        Thread.Sleep(500);
        Window.FindById("ClearLogButton", ElementTimeout).AsButton().Invoke();
        Thread.Sleep(300);

        // Attempt Add Display
        GoToDisplays();
        Window.FindById("AddDisplayButton", ElementTimeout).AsButton().Invoke();
        Thread.Sleep(8000); // wait for success or connection timeout

        // Check log
        Window.NavigateTo("Activity Log");
        Thread.Sleep(500);

        var logList = Window.FindById("LogListBox", ElementTimeout);
        var items = logList.FindAllChildren();
        items.Should().NotBeEmpty("log should record the display operation attempt");

        var found = items.Any(item =>
            item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Any(t => t.Name.Contains("Display") || t.Name.Contains("display")));

        found.Should().BeTrue("log should have Display category entries");
        Window.TakeScreenshot("log_display_ops");
    }

    // ──────────────────────────────────────────────
    // UI Resilience
    // ──────────────────────────────────────────────

    [Fact]
    public void Page_ShouldStayFunctional_AfterOperation()
    {
        GoToDisplays();

        Window.FindById("AddDisplayButton", ElementTimeout).AsButton().Invoke();
        Thread.Sleep(8000);

        // All controls still present
        Window.FindById("DisplayHeader", ElementTimeout).Should().NotBeNull();
        Window.FindById("CurrentDisplayCount", ElementTimeout).Should().NotBeNull();
        Window.FindById("AddDisplayButton", ElementTimeout).Should().NotBeNull();
        Window.FindById("SetCountButton", ElementTimeout).Should().NotBeNull();
        Window.FindById("RemoveAllButton", ElementTimeout).Should().NotBeNull();
        Window.TakeScreenshot("resilience_check");
    }

    [Fact]
    public void CountReflects_OnStatusPage()
    {
        GoToDisplays();
        var displayCount = GetCount();

        Window.NavigateTo("Status");
        Retry.WhileNull(
            () => Window.FindFirstDescendant(cf => cf.ByAutomationId("DisplayCountValue")),
            ElementTimeout, TimeSpan.FromMilliseconds(500));

        var statusCount = Window.FindById("DisplayCountValue", ElementTimeout);
        int.TryParse(statusCount.Name, out var val).Should().BeTrue();
        val.Should().Be(displayCount);
    }
}
