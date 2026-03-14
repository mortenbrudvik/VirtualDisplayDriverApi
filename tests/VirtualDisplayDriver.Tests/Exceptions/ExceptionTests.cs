using FluentAssertions;
using VirtualDisplayDriver;
using Xunit;

namespace VirtualDisplayDriver.Tests.Exceptions;

public class ExceptionTests
{
    [Fact]
    public void VddException_IsException()
    {
        var ex = new VddException("test");
        ex.Should().BeAssignableTo<Exception>();
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void VddException_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new VddException("test", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void PipeConnectionException_IsVddException()
    {
        var ex = new PipeConnectionException("pipe broken");
        ex.Should().BeAssignableTo<VddException>();
    }

    [Fact]
    public void PipeConnectionException_WithInnerException()
    {
        var inner = new IOException("pipe error");
        var ex = new PipeConnectionException("failed", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void CommandException_IsVddException()
    {
        var ex = new CommandException("bad response");
        ex.Should().BeAssignableTo<VddException>();
    }

    [Fact]
    public void CommandException_WithRawResponse()
    {
        var ex = new CommandException("parse failed", "RAW_DATA");
        ex.RawResponse.Should().Be("RAW_DATA");
        ex.Message.Should().Contain("parse failed");
    }
}
