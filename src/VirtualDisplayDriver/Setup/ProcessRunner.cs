using System.ComponentModel;
using System.Diagnostics;

namespace VirtualDisplayDriver.Setup;

internal interface IProcessRunner
{
    Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken ct = default);
    Task<string> RunAndCaptureOutputAsync(string fileName, string arguments, CancellationToken ct = default);
}

internal class ProcessRunner : IProcessRunner
{
    public async Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new SetupException($"Failed to start elevated process: {fileName}");

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new SetupException(
                "Administrator privileges are required. The UAC prompt was declined.", ex);
        }
    }

    public async Task<string> RunAndCaptureOutputAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new SetupException($"Failed to start process: {fileName}");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return output;
    }
}
