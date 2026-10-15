using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Imaging;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class ThumbnailLifetimeTests
{
    private static VideoInfo WithThumbnail(VideoInfo original) => new(original.Source, original.Title, original.Duration, original.Uploader,
        new("https://images.example.com/a.png"), null, original.State, original.Formats, original.Issues);
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Wait(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Fact]
    public Task WarningAcknowledgementAndReentrantShutdownWaitForImage() => DispatcherTest.Run(async () =>
    {
        var service = new DelayedService(); var s = new TestServices(thumbnails: service); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(service.Started.Task);
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        s.Jobs.Execute = (p, _, _) => Task.FromResult(new ClipJobResult(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, [], ["owned-fixture"]));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Task? reentrant = null;
        s.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(s.Vm.Lifetime)) reentrant = s.Vm.ShutdownAsync(); };
        var shutdown = s.Vm.ShutdownAsync(); Assert.Same(shutdown, reentrant);
        Assert.True(s.Vm.RequiresCloseAcknowledgement); Assert.False(s.Vm.CanAcknowledgeClose);
        service.Finish.SetResult(ThumbnailContentResult.Failed(ThumbnailFailure.HttpFailure));
        await Wait(shutdown); Assert.True(s.Vm.CanAcknowledgeClose);
    });

    [Fact]
    public Task FailedReloadClearsOldImageAndNeverFetchesAnother() => DispatcherTest.Run(async () =>
    {
        var service = new ImmediateService(); var s = new TestServices(thumbnails: service); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(s.Vm.ThumbnailCompletion); Assert.NotNull(s.Vm.ThumbnailImage);
        s.Load = (_, _) => Task.FromResult(OperationResult<VideoInfo>.Failed(new(OperationErrorCode.InvalidInput, OperationErrorCategory.Input, "safe")));
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        Assert.Equal(MetadataState.Failed, s.Vm.MetadataState); Assert.Null(s.Vm.ThumbnailImage);
        Assert.Equal(MetadataPresentation.Empty, s.Vm.MetadataPresentation); Assert.Equal(1, service.Calls);
        await s.Vm.ShutdownAsync();
    });

    [Fact]
    public Task LoadingAndCompletionLeavePreparedIdentityUntouchedAndRunOffDispatcher() => DispatcherTest.Run(async () =>
    {
        int dispatcher = Environment.CurrentManagedThreadId;
        var service = new DelayedService(); var decoder = new RecordingDecoder();
        var s = new TestServices(thumbnails: service, decoder: decoder); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(service.Started.Task);
        Assert.NotEqual(dispatcher, service.Thread); Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        await s.Vm.PrepareCommand.ExecuteAsync(null); var prepared = s.Jobs.Prepared;
        string confirmation = s.Vm.ConfirmationText;
        service.Finish.SetResult(ThumbnailContentResult.Success(ThumbnailDecodeTests.Image(16, 9), ThumbnailImageKind.Png));
        await Wait(s.Vm.ThumbnailCompletion);
        Assert.NotEqual(dispatcher, decoder.Thread); Assert.Equal(ThumbnailState.Ready, s.Vm.ThumbnailState);
        Assert.Equal(confirmation, s.Vm.ConfirmationText); Assert.True(s.Vm.ConfirmExecuteCommand.CanExecute(null));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null); Assert.Same(prepared, s.Jobs.Executed);
        Assert.Equal(1, s.Jobs.ExecuteCalls); await s.Vm.ShutdownAsync();
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task FailureOrCancellationDoesNotBlockPrepare(bool cancel) => DispatcherTest.Run(async () =>
    {
        var service = new DelayedService(); var s = new TestServices(thumbnails: service); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(service.Started.Task);
        if (cancel)
        {
            s.Vm.CancelThumbnailCommand.Execute(null);
            Assert.Equal(ThumbnailState.Loading, s.Vm.ThumbnailState); Assert.Contains("종료 대기", s.Vm.ThumbnailStatus);
            Assert.False(s.Vm.CancelThumbnailCommand.CanExecute(null)); Assert.True(service.Token.IsCancellationRequested);
        }
        service.Finish.SetResult(ThumbnailContentResult.Failed(ThumbnailFailure.RedirectRejected)); await Wait(s.Vm.ThumbnailCompletion);
        Assert.Equal(cancel ? ThumbnailState.Cancelled : ThumbnailState.Failed, s.Vm.ThumbnailState);
        Assert.True(s.Vm.PrepareCommand.CanExecute(null)); Assert.Null(s.Vm.ThumbnailImage);
        await s.Vm.ShutdownAsync();
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task OldResponseAndFinallyCannotOverwriteReloadOrUrlChange(bool sameUrl) => DispatcherTest.Run(async () =>
    {
        var service = new SerialService(); var s = new TestServices(thumbnails: service); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(service.FirstStarted.Task);
        if (!sameUrl) s.Vm.Url = "https://youtu.be/abcdefghijk";
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        Task latest = s.Vm.ThumbnailCompletion;
        Assert.Null(s.Vm.ThumbnailImage); Assert.Equal(1, service.Calls);
        service.FirstFinish.SetResult(ThumbnailContentResult.Success(ThumbnailDecodeTests.Image(16, 9), ThumbnailImageKind.Png));
        await Wait(service.SecondStarted.Task); Assert.Equal(2, service.Calls);
        Assert.Equal(ThumbnailState.Loading, s.Vm.ThumbnailState);
        service.SecondFinish.SetResult(ThumbnailContentResult.Failed(ThumbnailFailure.TooLarge)); await Wait(latest);
        Assert.Equal(ThumbnailState.Failed, s.Vm.ThumbnailState); Assert.Contains("크기 초과", s.Vm.ThumbnailStatus);
        Assert.Null(s.Vm.ThumbnailImage); await s.Vm.ShutdownAsync();
    });

    [Fact]
    public Task DecodeIgnoringCancellationSerializesNewFetchAndRepeatedWindowCloseWaits() => DispatcherTest.Run(async () =>
    {
        var decoder = new BlockingDecoder(); var service = new ImmediateService();
        var s = new TestServices(thumbnails: service, decoder: decoder); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(decoder.Started.Task);
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Calls); Assert.Null(s.Vm.ThumbnailImage);
        var window = new MainWindow(s.Vm); window.Show(); var closed = NewSignal(); window.Closed += (_, _) => closed.TrySetResult();
        window.Close(); window.Close(); var shutdown = s.Vm.ShutdownAsync(); Assert.Same(shutdown, s.Vm.ShutdownAsync());
        Assert.False(shutdown.IsCompleted); Assert.False(closed.Task.IsCompleted); Assert.False(s.Vm.CanAcknowledgeClose);
        decoder.Release.SetResult(); await Wait(shutdown); await Wait(closed.Task);
        Assert.Equal(1, service.Calls); Assert.Equal(AppLifetime.Closed, s.Vm.Lifetime); Assert.Null(s.Vm.ThumbnailImage);
    });

    [Fact]
    public Task SynchronousPreambleFailureIsObservedAndQualityDoesNotCancelImage() => DispatcherTest.Run(async () =>
    {
        var service = new DelayedService(); var s = new TestServices(thumbnails: service); s.Info = WithThumbnail(s.Info);
        await s.ReadyAsync(); await Wait(service.Started.Task);
        s.Vm.Quality = s.Vm.Qualities.Single(q => q.Height == 720); s.Vm.Start.Frames = "01";
        Assert.False(service.Token.IsCancellationRequested);
        service.Finish.SetException(new InvalidOperationException("sentinel raw secret")); await Wait(s.Vm.ThumbnailCompletion);
        Assert.Equal(ThumbnailState.Failed, s.Vm.ThumbnailState); Assert.DoesNotContain("sentinel", s.Vm.ThumbnailStatus);
        Assert.Equal("01", s.Vm.Start.Frames); await s.Vm.ShutdownAsync();
    });

    private sealed class DelayedService : IThumbnailContentService
    {
        public TaskCompletionSource Started = NewSignal();
        public TaskCompletionSource<ThumbnailContentResult> Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token; public int Thread;
        public Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken token)
        { Thread = Environment.CurrentManagedThreadId; Token = token; Started.TrySetResult(); return Finish.Task; }
    }
    private sealed class RecordingDecoder : IThumbnailDecoder
    {
        public int Thread;
        public ThumbnailDecodeResult Decode(ReadOnlyMemory<byte> bytes, ThumbnailImageKind kind, CancellationToken token)
        { Thread = Environment.CurrentManagedThreadId; return new WpfThumbnailDecoder().Decode(bytes, kind, token); }
    }
    private sealed class BlockingDecoder : IThumbnailDecoder
    {
        public TaskCompletionSource Started = NewSignal(), Release = NewSignal();
        public ThumbnailDecodeResult Decode(ReadOnlyMemory<byte> bytes, ThumbnailImageKind kind, CancellationToken token)
        { Started.TrySetResult(); Release.Task.GetAwaiter().GetResult(); return new WpfThumbnailDecoder().Decode(bytes, kind, CancellationToken.None); }
    }
    private sealed class ImmediateService : IThumbnailContentService
    {
        public int Calls;
        public Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken token)
        { Interlocked.Increment(ref Calls); return Task.FromResult(ThumbnailContentResult.Success(ThumbnailDecodeTests.Image(16, 9), ThumbnailImageKind.Png)); }
    }
    private sealed class SerialService : IThumbnailContentService
    {
        public int Calls;
        public TaskCompletionSource FirstStarted = NewSignal(), SecondStarted = NewSignal();
        public TaskCompletionSource<ThumbnailContentResult> FirstFinish = new(TaskCreationOptions.RunContinuationsAsynchronously),
            SecondFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken token)
        { if (Interlocked.Increment(ref Calls) == 1) { FirstStarted.TrySetResult(); return FirstFinish.Task; } SecondStarted.TrySetResult(); return SecondFinish.Task; }
    }
}
