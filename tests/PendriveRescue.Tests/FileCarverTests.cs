using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class FileCarverTests
{
    [Fact]
    public void Carve_FindsKnownSignatureAtExpectedOffset()
    {
        var carver = new FileCarver();
        var buffer = new byte[] { 0x00, 0x11, 0x25, 0x50, 0x44, 0x46, 0x33 };

        var files = carver.Carve(buffer, 1024);

        Assert.Single(files);
        Assert.Equal(".pdf", files[0].Extension);
        Assert.Equal(1026, files[0].StartOffset);
    }
}
