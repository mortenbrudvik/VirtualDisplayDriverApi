using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace VirtualDisplayDriver.UITests;

public class AppFixture : IDisposable
{
    private const string AppName = "VirtualDisplayDriver.ExampleApp";

    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; }

    public AppFixture()
    {
        var appPath = FindAppExecutable();
        App = Application.Launch(appPath);
        Automation = new UIA3Automation();

        // Wait for main window to appear
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        App?.Close();
        Automation?.Dispose();
        App?.Dispose();
    }

    private static string FindAppExecutable()
    {
        // Walk up from test bin directory to find the example app exe
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "examples", "VirtualDisplayDriver.ExampleApp", "bin", "Debug", "net8.0-windows", $"{AppName}.exe");
            if (File.Exists(candidate))
                return candidate;

            candidate = Path.Combine(dir, "examples", "VirtualDisplayDriver.ExampleApp", "bin", "Release", "net8.0-windows", $"{AppName}.exe");
            if (File.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Could not find {AppName}.exe. Build the example app first.");
    }
}
