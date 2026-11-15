using Murmur.Core.Hardware;
using Murmur.Core.Services;
using Murmur.Core.Tests.Helpers;
using Shouldly;

namespace Murmur.Core.Tests.Hardware;

/// <summary>Runs the real detection on this PC's GPU and NVIDIA driver.</summary>
public sealed class GpuInfoTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDirectory _temp = new();

    private GpuReport Detect()
    {
        var report = new GpuInfo(new MurmurPaths(_temp.Path)).Report;
        output.WriteLine(GpuInfo.Describe(report));
        return report;
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Report_OnThisPc_DoesNotThrow()
    {
        var report = Detect();

        report.ShouldNotBeNull();
        report.Adapters.ShouldAllBe(a => a.Name.Length > 0);
    }

    [Fact]
    public void Report_WithAnNvidiaGpu_HasTheDriverAndComputeCapability()
    {
        var report = Detect();
        Assert.SkipUnless(report.Adapters.Any(a => a.VendorId == 0x10DE), "no NVIDIA GPU");

        report.Cuda.ShouldNotBeNull();
        report.Cuda.Version.ShouldBeGreaterThan(0);
        report.Cuda.Devices.ShouldNotBeEmpty();
        report.Cuda.Devices.ShouldAllBe(d => d.ComputeMajor > 0);
    }

    [Fact]
    public void Report_ReadTwice_IsDetectedOnce()
    {
        var gpu = new GpuInfo(new MurmurPaths(_temp.Path));

        gpu.Report.ShouldBeSameAs(gpu.Report);
    }

#if DEBUG
    [Fact]
    public void Report_SimulateNoGpuFlag_IsEmpty()
    {
        var paths = new MurmurPaths(_temp.Path);
        Directory.CreateDirectory(_temp.Path);
        File.WriteAllText(paths.SimulateNoGpuFile, "");

        new GpuInfo(paths).Report.ShouldBeSameAs(GpuReport.Empty);
    }
#endif
}
