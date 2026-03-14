namespace VirtualDisplayDriver.Pipe;

internal static class PipeConstants
{
    public const string PipeName = "MTTVirtualDisplayPipe";
    public const int ReadBufferSize = 512;
    public const int MaxCommandLengthChars = 127; // VDD driver silently truncates beyond this
    public const int MaxResponseSize = 65536; // 64KB max response to prevent memory exhaustion
}
