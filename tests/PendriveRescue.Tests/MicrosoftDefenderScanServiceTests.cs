using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class MicrosoftDefenderScanServiceTests
{
    [Fact]
    public void BuildScanStartInfo_UsesArgumentListForUsbPath()
    {
        var startInfo = MicrosoftDefenderScanService.BuildScanStartInfo(
            @"C:\Program Files\Windows Defender\MpCmdRun.exe",
            @"E:\",
            isQuickScan: false);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            ["-Scan", "-ScanType", "3", "-File", @"E:\"],
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void BuildScanStartInfo_QuickScanDoesNotAcceptUsbContentAsArguments()
    {
        var startInfo = MicrosoftDefenderScanService.BuildScanStartInfo(
            @"C:\Program Files\Windows Defender\MpCmdRun.exe",
            targetPath: null,
            isQuickScan: true);

        Assert.Equal(["-Scan", "-ScanType", "1"], startInfo.ArgumentList.ToArray());
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(2, false, true)]
    [InlineData(5, false, false)]
    public void InterpretScanResult_MapsDocumentedExitCodes(
        int exitCode,
        bool expectedSuccess,
        bool expectedRequiresAction)
    {
        var result = MicrosoftDefenderScanService.InterpretScanResult(exitCode, "scan output");

        Assert.True(result.Available);
        Assert.Equal(expectedSuccess, result.Success);
        Assert.Equal(expectedRequiresAction, result.RequiresAction);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal("scan output", result.Output);
    }
}
