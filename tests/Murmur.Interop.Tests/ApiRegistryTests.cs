using MediatR;
using Murmur.Interop;
using Shouldly;

namespace Murmur.Interop.Tests;

public class ApiRegistryTests
{
    [Fact]
    public void Register_ThenGetRequestType_ReturnsCorrectType()
    {
        var registry = new ApiRegistry();
        registry.Register<FakeRequest>("testService", "doSomething");
        var type = registry.GetRequestType("testService", "doSomething");
        type.ShouldBe(typeof(FakeRequest));
    }

    [Fact]
    public void GetRequestType_ReturnsNull_WhenNotRegistered()
    {
        var registry = new ApiRegistry();
        var type = registry.GetRequestType("unknown", "method");
        type.ShouldBeNull();
    }

    private class FakeRequest : IRequest<string> { }
}
