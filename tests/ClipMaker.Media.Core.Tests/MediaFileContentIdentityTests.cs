using ClipMaker.Media.Core.Models;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class MediaFileContentIdentityTests
{
    [Fact]
    public void ByteIdentityIsImmutableNormalizedAndValueComparable()
    {
        var upper = new MediaFileContentIdentity(123, new string('A', 64));
        Assert.Equal(new MediaFileContentIdentity(123, new string('a', 64)), upper);
        Assert.NotEqual(new MediaFileContentIdentity(124, new string('a', 64)), upper);
        Assert.NotEqual(new MediaFileContentIdentity(123, new string('b', 64)), upper);
        Assert.Equal(new string('a', 64), upper.Sha256);
    }
    [Theory]
    [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(-1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(123, "short")]
    [InlineData(123, "zaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MissingOrInvalidContentCannotBecomeConfirmedIdentity(long size, string hash) =>
        Assert.ThrowsAny<ArgumentException>(() => new MediaFileContentIdentity(size, hash));
}
