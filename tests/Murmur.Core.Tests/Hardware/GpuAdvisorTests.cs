using Murmur.Core.Hardware;
using Murmur.Core.Models;
using Shouldly;
using static Murmur.Core.Models.GpuAdviceState;

namespace Murmur.Core.Tests.Hardware;

public sealed class GpuAdvisorTests
{
    private const long TwelveGiB = 12L * 1024 * 1024 * 1024;

    /// <summary>One GPU as detection sees it: the DXGI adapter and, for NVIDIA, its CUDA device.</summary>
    private sealed record Gpu(GpuAdapter Adapter, CudaDevice? Device);

    private static readonly Gpu Rtx3090 = Nvidia("NVIDIA GeForce RTX 3090", 8, 6, luid: 10);
    private static readonly Gpu Rtx3060 = Nvidia("NVIDIA GeForce RTX 3060", 8, 6, luid: 11);
    private static readonly Gpu Rtx4070 = Nvidia("NVIDIA GeForce RTX 4070", 8, 9, luid: 12);
    private static readonly Gpu Rtx2080 = Nvidia("NVIDIA GeForce RTX 2080", 7, 5, luid: 13);
    private static readonly Gpu Gtx1080 = Nvidia("NVIDIA GeForce GTX 1080", 6, 1, luid: 14);

    private static Gpu Nvidia(string name, int major, int minor, long luid) =>
        new(new GpuAdapter(name, GpuAdvisor.NvidiaVendorId, TwelveGiB, luid), new CudaDevice(luid, major, minor));

    private static Gpu Amd(string name) => new(new GpuAdapter(name, 0x1002, TwelveGiB, 20), null);

    /// <summary>A one-GPU report. Without a CUDA device (not NVIDIA) there is no nvcuda.dll either, so no driver.</summary>
    private static GpuReport Report(Gpu gpu, int driver = GpuAdvisor.MinCudaDriverVersion) =>
        new([gpu.Adapter], gpu.Device is null ? null : new CudaDriver(driver, [gpu.Device]));

    private static GpuReport ReportWithoutCuda(Gpu gpu) => new([gpu.Adapter], null);

    public static TheoryData<string, GpuReport, bool, string?, GpuAdviceState, string?> Cases => new()
    {
        { "no GPU", GpuReport.Empty, false, null, NotApplicable, null },
        { "AMD only", Report(Amd("Radeon RX 7800 XT")), false, "Vulkan", NotApplicable, null },
        { "RTX 3060, no runtime", Report(Rtx3060, driver: 13000), false, "Vulkan", InstallCuda, "NVIDIA GeForce RTX 3060" },
        { "RTX 3060, still downloading", Report(Rtx3060, driver: 13000), false, null, InstallCuda, "NVIDIA GeForce RTX 3060" },
        { "RTX 4070, old driver", Report(Rtx4070, driver: 12080), false, "Vulkan", UpdateDriver, "NVIDIA GeForce RTX 4070" },
        { "old driver, runtime found", Report(Rtx4070, driver: 12080), true, "Vulkan", UpdateDriver, "NVIDIA GeForce RTX 4070" },
        { "RTX 2080 (Turing 7.5)", Report(Rtx2080, driver: 13000), false, "Vulkan", NotApplicable, null },
        { "GTX 1080 (Pascal 6.1)", Report(Gtx1080, driver: 13000), true, "Vulkan", NotApplicable, null },
        { "NVIDIA, no nvcuda", ReportWithoutCuda(Rtx3060), false, "Vulkan", NotApplicable, null },
        { "CUDA loaded", Report(Rtx3090, driver: 13000), true, "Cuda", CudaInUse, "NVIDIA GeForce RTX 3090" },
        { "runtime found, Vulkan", Report(Rtx3090, driver: 13000), true, "Vulkan", NotApplicable, null },
        { "runtime found, not loaded yet", Report(Rtx3090, driver: 13000), true, null, NotApplicable, null },
    };

    [Theory, MemberData(nameof(Cases), DisableDiscoveryEnumeration = true)]
    public void Decide_FollowsTheDecisionTable(string _, GpuReport report, bool runtimeFound, string? loaded, GpuAdviceState state, string? name)
        => GpuAdvisor.Decide(report, runtimeFound, loaded).ShouldBe(new GpuAdvice(state, name));

    /// <summary>The CUDA devices are listed in the other order, so pairing by position would pick the GTX 1080.</summary>
    [Fact]
    public void FindSupportedNvidia_TwoNvidiaAdapters_MatchesByLuid()
    {
        var gtx1080 = Nvidia("NVIDIA GeForce GTX 1080", 6, 1, luid: 1);
        var rtx3060 = Nvidia("NVIDIA GeForce RTX 3060", 8, 6, luid: 2);
        var report = new GpuReport(
            [gtx1080.Adapter, rtx3060.Adapter],
            new CudaDriver(13000, [rtx3060.Device!, gtx1080.Device!]));

        GpuAdvisor.FindSupportedNvidia(report).ShouldBe(rtx3060.Adapter);
    }

    [Fact]
    public void FindSupportedNvidia_OneAdapterAndOneDeviceWithDifferentLuids_StillMatches()
    {
        var report = new GpuReport([Rtx3060.Adapter], new CudaDriver(13000, [Rtx3060.Device! with { Luid = 999 }]));

        GpuAdvisor.FindSupportedNvidia(report).ShouldBe(Rtx3060.Adapter);
    }

    [Fact]
    public void CanUseCuda_SupportedGpuWithOldDriver_IsFalse() =>
        GpuAdvisor.CanUseCuda(Report(Rtx4070, driver: 12080)).ShouldBeFalse();

    [Fact]
    public void CanUseCuda_UnsupportedGpu_IsFalse() =>
        GpuAdvisor.CanUseCuda(Report(Rtx2080, driver: 13000)).ShouldBeFalse();

    [Fact]
    public void CanUseCuda_SupportedGpuWithCuda13Driver_IsTrue() =>
        GpuAdvisor.CanUseCuda(Report(Rtx3060, driver: 13000)).ShouldBeTrue();
}
