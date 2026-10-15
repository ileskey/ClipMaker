using ClipDownloader.Application.Models;
using ClipDownloader.ViewModels;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class InputTests
{
    [Fact]
    public async Task MetadataReadyNotificationCannotReenterBeforeTrackedTaskCompletes()
    {
        var s = new TestServices();
        await s.Vm.RefreshToolsCommand.ExecuteAsync(null);
        bool observed = false;
        bool guarded = false;
        s.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(s.Vm.MetadataState) && s.Vm.MetadataState == MetadataState.Ready)
            { observed = true; guarded = !s.Vm.CanEdit && !s.Vm.LoadMetadataCommand.CanExecute(null); }
        };
        s.Vm.Url = s.Info.Source.PageUrl.ToString();
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        Assert.True(observed); Assert.True(guarded); Assert.True(s.Vm.CanEdit);
    }

    [Fact]
    public async Task QualityChangeRevalidatesFramesWithoutConversion()
    {
        FrameRate.TryCreate(60000, 1001, out var high, out _);
        var original = TestServices.Video();
        var info = new VideoInfo(original.Source, original.Title, original.Duration, null, null, null, VideoState.Completed,
            original.Formats.Concat([new("high", 1920, 1080, high, "h264", "aac", StreamPresence.Present, StreamPresence.Present)]), []);
        var s = new TestServices { Info = info }; await s.ReadyAsync();
        s.Vm.Start.Frames = "45"; Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        s.Vm.Quality = s.Vm.Qualities.Single(q => q.Height == 720);
        Assert.Equal("45", s.Vm.Start.Frames);
        Assert.False(s.Vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelledLateMetadataDoesNotRestoreOldVideoOrAllowDuplicateLoad()
    {
        var s = new TestServices(); await s.ReadyAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<OperationResult<VideoInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        s.Load = (_, _) => { Interlocked.Increment(ref calls); started.SetResult(); return delayed.Task; };
        var loading = s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        Assert.Equal("선택 정보 없음", s.Vm.SelectionText);
        Assert.Single(s.Vm.Qualities);
        Assert.False(s.Vm.CanEdit);
        s.Vm.CancelCommand.Execute(null);
        Assert.False(s.Vm.CancelCommand.CanExecute(null));
        Assert.False(s.Vm.CanEdit);
        delayed.SetResult(OperationResult<VideoInfo>.Succeeded(s.Info));
        await loading;
        Assert.Equal(MetadataState.Empty, s.Vm.MetadataState);
        Assert.False(s.Vm.PrepareCommand.CanExecute(null));
        Assert.Equal("영상 정보 없음", s.Vm.MetadataText);
    }

    [Theory]
    [InlineData("Minutes", "60")]
    [InlineData("Seconds", "60")]
    [InlineData("Hours", "-1")]
    [InlineData("Frames", "３０")]
    [InlineData("Hours", "")]
    [InlineData("Hours", "999999999999999999999999999999")]
    public async Task InvalidTimeIsAttributedToFieldAndBlocksPreparation(string field, string value)
    {
        var s = new TestServices(); await s.ReadyAsync();
        typeof(TimecodeInputViewModel).GetProperty(field)!.SetValue(s.Vm.Start, value);
        Assert.True(s.Vm.Start.HasErrors);
        Assert.NotEmpty(s.Vm.Start.GetErrors(field).Cast<string>());
        Assert.False(s.Vm.PrepareCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(30000, 1001, "29", "30")]
    [InlineData(60000, 1001, "59", "60")]
    public async Task FractionalFpsUsesActualFrameLimit(int numerator, int denominator, string valid, string invalid)
    {
        FrameRate.TryCreate(numerator, denominator, out var rate, out _);
        var s = new TestServices { Info = TestServices.Video(rate) }; await s.ReadyAsync();
        s.Vm.Start.Frames = valid; Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        s.Vm.Start.Frames = invalid; Assert.False(s.Vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnknownFpsDefersRangeAndDoesNotInventRate()
    {
        var s = new TestServices { Info = TestServices.Video(unknown: true) }; await s.ReadyAsync();
        s.Vm.Start.Frames = "99"; s.Vm.End.Seconds = "00";
        Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        Assert.Null(s.Jobs.Prepared!.Range);
        Assert.Contains("FPS 미확인", s.Vm.ConfirmationText);
    }

    [Fact]
    public async Task RangeAndDurationAreValidatedAndTwentyFourHoursAllowed()
    {
        var s = new TestServices(); await s.ReadyAsync();
        s.Vm.Start.Hours = "24"; s.Vm.End.Hours = "24";
        Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        s.Vm.End.Seconds = "00"; Assert.False(s.Vm.PrepareCommand.CanExecute(null));
        s.Vm.End.Hours = "31"; Assert.False(s.Vm.PrepareCommand.CanExecute(null));
    }

    [Fact]
    public async Task RelativeReservedExistingAndMissingDirectoryPathsAreRejectedWithoutWriting()
    {
        var s = new TestServices(); await s.ReadyAsync();
        string[] paths = ["relative.mp4", Path.Combine(Environment.CurrentDirectory, "CON.mp4"),
            Path.Combine(Environment.CurrentDirectory, "missing-" + Guid.NewGuid(), "clip.mp4"), typeof(InputTests).Assembly.Location];
        foreach (string path in paths)
        { s.Vm.OutputPath = path; await s.WaitPathAsync(); Assert.False(s.Vm.PrepareCommand.CanExecute(null)); }
    }

    [Fact]
    public async Task DialogCancellationPreservesOutputAndFilenameIsSafe()
    {
        var s = new TestServices(); await s.ReadyAsync(); string previous = s.Vm.OutputPath;
        s.Vm.BrowseOutputCommand.Execute(null); Assert.Equal(previous, s.Vm.OutputPath);
        Assert.StartsWith("_CON_", OutputFileNameSuggestion.Create("CON", null, "00:00:00:00", "00:00:10:00"));
        Assert.StartsWith("_CON .txt_", OutputFileNameSuggestion.Create("CON .txt", null, "00", "00"));
        Assert.StartsWith("A_B_", OutputFileNameSuggestion.Create("A\u007fB", null, "00", "00"));
        Assert.StartsWith("A_B_", OutputFileNameSuggestion.Create("A/B. ", null, "00:00:00:00", "00:00:10:00"));
        string name = OutputFileNameSuggestion.Create(new string('a', 79) + "😀", null, "00", "00");
        Assert.False(char.IsHighSurrogate(name[79]));
    }
}
