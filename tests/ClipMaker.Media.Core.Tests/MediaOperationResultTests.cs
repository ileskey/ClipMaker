using ClipMaker.Media.Core.Models;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class MediaOperationResultTests
{
    [Fact]
    public void PreservesExclusiveSuccessFailureAndCancellationPayloads()
    {
        var data = new object();
        var success = MediaOperationResult<object>.Succeeded(data);
        Assert.Equal(MediaOperationStatus.Succeeded, success.Status);
        Assert.Same(data, success.Data);
        Assert.Null(success.Failure);
        var failure = new MediaFailure(MediaFailureKind.TerminationUnconfirmed, "종료를 확인하지 못했습니다.");
        var failed = MediaOperationResult<object>.Failed(failure);
        Assert.Equal(MediaOperationStatus.Failed, failed.Status);
        Assert.Null(failed.Data);
        Assert.Same(failure, failed.Failure);
        var cancelled = MediaOperationResult<object>.Cancelled();
        Assert.Equal(MediaOperationStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Data);
        Assert.Null(cancelled.Failure);
    }

    [Fact]
    public void RejectsIncompleteFailureAndInvalidPayloadConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => MediaOperationResult<object>.Succeeded(null!));
        Assert.Throws<ArgumentNullException>(() => MediaOperationResult<object>.Failed(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaFailure((MediaFailureKind)99, "Failure"));
        Assert.Throws<ArgumentException>(() => new MediaFailure(MediaFailureKind.InvalidMedia, " "));
    }
}
