using FluentAssertions;
using VirtualDisplayDriver;
using Xunit;

namespace VirtualDisplayDriver.Tests.Exceptions;

public class SetupExceptionTests
{
    [Fact]
    public void SetupException_IsVddException()
    {
        var ex = new SetupException("setup failed");
        ex.Should().BeAssignableTo<VddException>();
        ex.Message.Should().Be("setup failed");
    }

    [Fact]
    public void SetupException_WithExitCode()
    {
        var ex = new SetupException("pnputil failed", 1);
        ex.ExitCode.Should().Be(1);
        ex.Message.Should().Contain("pnputil failed");
    }

    [Fact]
    public void SetupException_WithoutExitCode_HasNullExitCode()
    {
        var ex = new SetupException("generic failure");
        ex.ExitCode.Should().BeNull();
    }

    [Fact]
    public void SetupException_WithInnerException()
    {
        var inner = new IOException("disk error");
        var ex = new SetupException("failed", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
