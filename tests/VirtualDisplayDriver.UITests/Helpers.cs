using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace VirtualDisplayDriver.UITests;

public static class Helpers
{
    /// <summary>
    /// Find a descendant by AutomationId, waiting up to the specified timeout.
    /// </summary>
    public static AutomationElement FindById(this AutomationElement root, string automationId, TimeSpan? timeout = null)
    {
        var ts = timeout ?? TimeSpan.FromSeconds(5);
        var result = Retry.WhileNull(
            () => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            ts,
            TimeSpan.FromMilliseconds(250));

        return result.Result ?? throw new ElementNotFoundException($"Element with AutomationId '{automationId}' not found within {ts.TotalSeconds}s");
    }

    /// <summary>
    /// Find a descendant by Name, waiting up to the specified timeout.
    /// </summary>
    public static AutomationElement FindByName(this AutomationElement root, string name, TimeSpan? timeout = null)
    {
        var ts = timeout ?? TimeSpan.FromSeconds(5);
        var result = Retry.WhileNull(
            () => root.FindFirstDescendant(cf => cf.ByName(name)),
            ts,
            TimeSpan.FromMilliseconds(250));

        return result.Result ?? throw new ElementNotFoundException($"Element with Name '{name}' not found within {ts.TotalSeconds}s");
    }

    /// <summary>
    /// Find a descendant by text content.
    /// </summary>
    public static AutomationElement FindByText(this AutomationElement root, string text, TimeSpan? timeout = null)
    {
        var ts = timeout ?? TimeSpan.FromSeconds(5);
        var result = Retry.WhileNull(
            () => root.FindFirstDescendant(cf => cf.ByText(text)),
            ts,
            TimeSpan.FromMilliseconds(250));

        return result.Result ?? throw new ElementNotFoundException($"Element with text '{text}' not found within {ts.TotalSeconds}s");
    }

    /// <summary>
    /// Try to find element by AutomationId without throwing.
    /// </summary>
    public static AutomationElement? TryFindById(this AutomationElement root, string automationId, TimeSpan? timeout = null)
    {
        var ts = timeout ?? TimeSpan.FromSeconds(2);
        var result = Retry.WhileNull(
            () => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            ts,
            TimeSpan.FromMilliseconds(250));

        return result.Result;
    }

    /// <summary>
    /// Click a navigation item in the sidebar by its name.
    /// </summary>
    public static void NavigateTo(this Window window, string pageName)
    {
        var navList = window.FindById("NavListBox");
        var items = navList.FindAllChildren();

        foreach (var item in items)
        {
            // ListBoxItem contains text with the page name
            var textElements = item.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.Text));

            foreach (var text in textElements)
            {
                if (text.Name == pageName)
                {
                    item.Click();
                    Thread.Sleep(500); // Wait for navigation + VM initialization
                    return;
                }
            }
        }

        throw new ElementNotFoundException($"Navigation item '{pageName}' not found in sidebar");
    }

    /// <summary>
    /// Take a screenshot of the main window and save to the test output directory.
    /// </summary>
    public static string TakeScreenshot(this Window window, string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}_{DateTime.Now:HHmmss}.png");

        var capture = FlaUI.Core.Capturing.Capture.Element(window);
        capture.ToFile(path);
        capture.Dispose();

        return path;
    }

    public class ElementNotFoundException : Exception
    {
        public ElementNotFoundException(string message) : base(message) { }
    }
}
