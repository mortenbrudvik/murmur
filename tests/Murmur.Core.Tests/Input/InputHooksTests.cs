using Murmur.Core.Input;
using Shouldly;

namespace Murmur.Core.Tests.Input;

public sealed class InputHooksTests
{
    [Fact]
    public void IsAltGrFakeCtrl_SyntheticLeftCtrlFromAltGr_True()
    {
        InputHooks.IsAltGrFakeCtrl(0xA2, 0x21D).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0xA2u, 0x1Du)]  // real Left Ctrl
    [InlineData(0xA3u, 0x1Du)]  // Right Ctrl (extended flag is separate; scan code stays 0x1D)
    [InlineData(0xA5u, 0x38u)]  // Right Alt itself
    [InlineData(0xA3u, 0x21Du)] // only Left Ctrl is ever faked
    public void IsAltGrFakeCtrl_OtherKeys_False(uint vk, uint scanCode)
    {
        InputHooks.IsAltGrFakeCtrl(vk, scanCode).ShouldBeFalse();
    }
}
