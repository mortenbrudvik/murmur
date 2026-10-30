using Murmur.Core.Speech;
using Shouldly;
using Whisper.net.LibraryLoader;

namespace Murmur.Core.Tests.Speech;

public sealed class WhisperModelLoaderTests
{
    [Fact]
    public void RuntimeOrder_WithoutCuda_LeavesCudaOut()
    {
        WhisperModelLoader.RuntimeOrder(canUseCuda: false).ShouldBe([RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]);
    }

    [Fact]
    public void RuntimeOrder_WithCuda_TriesCudaFirst()
    {
        WhisperModelLoader.RuntimeOrder(canUseCuda: true).ShouldBe([RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]);
    }
}
