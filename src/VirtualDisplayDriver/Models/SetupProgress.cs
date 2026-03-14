namespace VirtualDisplayDriver;

public record SetupProgress(SetupStage Stage, double PercentComplete, string Message);

public enum SetupStage
{
    FetchingRelease,
    Downloading,
    Extracting,
    InstallingDriver,
    Complete
}
