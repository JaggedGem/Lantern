using Lantern.Models;
using Xunit;

namespace Lantern.Tests;

public sealed class TransferFileTests
{
    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("Trip/day-1/photo.jpg", "photo.jpg")]
    [InlineData("旅行/фото.png", "фото.png")]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData("COM10.txt", "COM10.txt")]
    [InlineData("notes..txt", "notes..txt")]
    public void PreservesLogicalPathAndDerivesFileName(string path, string name)
    {
        var id = Guid.NewGuid();
        var modifiedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(3));
        var file = new TransferFile(id, path, 123, modifiedAt);
        Assert.Equal(id, file.Id);
        Assert.Equal(path, file.RelativePath);
        Assert.Equal(name, file.FileName);
        Assert.Equal(123, file.SizeBytes);
        Assert.Equal(modifiedAt.ToUniversalTime(), file.ModifiedAt);
        Assert.Equal(TimeSpan.Zero, file.ModifiedAt!.Value.Offset);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3_000_000_000L)]
    [InlineData(long.MaxValue)]
    public void SupportsZeroByteAndLargeFiles(long size)
    {
        var file = new TransferFile(Guid.NewGuid(), "file.bin", size);
        Assert.Equal(size, file.SizeBytes);
        Assert.Null(file.ModifiedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/photo.jpg")]
    [InlineData("folder/")]
    [InlineData("folder//photo.jpg")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../file.txt")]
    [InlineData("folder/./file.txt")]
    [InlineData("folder/../file.txt")]
    [InlineData("C:/file.txt")]
    [InlineData("C:file.txt")]
    [InlineData("//server/share/file.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("file?.txt")]
    [InlineData("file*.txt")]
    [InlineData("file|.txt")]
    [InlineData("file<.txt")]
    [InlineData("file>.txt")]
    [InlineData("file\".txt")]
    [InlineData("file\0.txt")]
    [InlineData("file\n.txt")]
    [InlineData("file\u007f.txt")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN.log")]
    [InlineData("aux.tar.gz")]
    [InlineData("NUL")]
    [InlineData("COM1.txt")]
    [InlineData("com9")]
    [InlineData("lpt1.log")]
    [InlineData("LPT9")]
    [InlineData("COM¹.txt")]
    [InlineData("LPT²")]
    [InlineData("LPT³.txt")]
    [InlineData("CON .txt")]
    [InlineData("NUL/child.txt")]
    public void RejectsInvalidWindowsRelativePaths(string path)
        => Assert.Throws<ArgumentException>(() => new TransferFile(Guid.NewGuid(), path, 1));

    [Fact]
    public void RejectsMissingPathEmptyIdAndNegativeSize()
    {
        Assert.Throws<ArgumentNullException>(() => new TransferFile(Guid.NewGuid(), null!, 0));
        Assert.Throws<ArgumentException>(() => new TransferFile(Guid.Empty, "file.txt", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferFile(Guid.NewGuid(), "file.txt", -1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void RejectsUndefinedFailureKinds(int kind)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TransferError((TransferFailureKind)kind, "Failure"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingFailureDescriptions(string? description)
        => Assert.ThrowsAny<ArgumentException>(() => new TransferError(TransferFailureKind.Protocol, description!));
}
