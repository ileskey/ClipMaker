using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.ViewModels;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class MetadataPresentationTests
{
    [Fact]
    public void MissingFieldsFractionalLongDurationAndSafeIssues()
    {
        var original = TestServices.Video();
        var info = new VideoInfo(original.Source, " ", TimeSpan.FromHours(30) + TimeSpan.FromTicks(1234567), null,
            null, null, VideoState.Completed, original.Formats,
            [new("duration", MetadataIssueReason.Rounded), new("https://sentinel/token", MetadataIssueReason.InvalidValue, 999)]);
        var p = MetadataPresentation.Create(info, null);
        Assert.Equal("제목 미확인", p.Title); Assert.Equal("업로더 미확인", p.Uploader);
        Assert.Equal("30:00:00.1234567", p.Duration); Assert.Equal("선택 포맷 없음", p.FrameRate);
        Assert.Contains("반올림", p.Issues); Assert.Contains("원본 format index 999", p.Issues);
        Assert.DoesNotContain("sentinel", p.Issues);
    }

    [Theory]
    [InlineData(30000, 1001, "29.970")]
    [InlineData(60000, 1001, "59.940")]
    public void FpsBelongsToSelectedFormatAndHeightsShareQualityHelper(int n, int d, string approximate)
    {
        FrameRate.TryCreate(n, d, out var rate, out _);
        var original = TestServices.Video(rate);
        var info = new VideoInfo(original.Source, new string('한', 5000), original.Duration, "a\nb", null, null, VideoState.Completed,
            original.Formats.Concat([original.Formats[0] with { Id = "duplicate" },
                original.Formats[0] with { Id = "audio", Height = 2160, VideoStream = StreamPresence.Absent },
                original.Formats[0] with { Id = "unknown", Height = 1080, VideoStream = StreamPresence.Unknown }]), []);
        var selected = new FormatSelector().Select(info, QualityOption.Best).Data;
        var p = MetadataPresentation.Create(info, selected);
        Assert.Equal($"{n}/{d} fps (약 {approximate})", p.FrameRate);
        Assert.Equal("1080p · 720p", p.Resolutions);
        Assert.Equal([1080, 720], MetadataPresentation.QualityOptions(info).Skip(1).Select(q => q.Height!.Value));
        Assert.Equal(info.Title, p.Title);
        Assert.Contains("FPS 미확인", MetadataPresentation.Create(TestServices.Video(unknown: true),
            new FormatSelector().Select(TestServices.Video(unknown: true), QualityOption.Best).Data).FrameRate);
    }

    [Fact]
    public Task ReadyNotificationHasConsistentFieldsAndBinding() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices();
        await s.Vm.RefreshToolsCommand.ExecuteAsync(null);
        s.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(s.Vm.MetadataState) && s.Vm.MetadataState == MetadataState.Ready)
                Assert.Equal("30000/1001 fps (약 29.970)", s.Vm.MetadataPresentation.FrameRate);
        };
        s.Vm.Url = s.Info.Source.PageUrl.ToString();
        await s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        var window = new Views.MainWindow(s.Vm); window.Show();
        await System.Windows.Threading.Dispatcher.Yield();
        Assert.Equal("A/B", s.Vm.MetadataPresentation.Title);
        s.Vm.Url = "invalid";
        Assert.Equal(MetadataPresentation.Empty, s.Vm.MetadataPresentation);
        window.Close(); await s.Vm.ShutdownAsync();
    });

    [Fact]
    public async Task StaleMetadataCannotRestoreFieldsOrDiagnosticsButRetainsResourceWarning()
    {
        var s = new TestServices(); await s.ReadyAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<OperationResult<VideoInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Load = (_, _) => { started.SetResult(); return result.Task; };
        var load = s.Vm.LoadMetadataCommand.ExecuteAsync(null); await started.Task;
        s.Vm.Url = "https://youtu.be/abcdefghijk";
        result.SetResult(OperationResult<VideoInfo>.Failed(new(OperationErrorCode.ProcessTerminationFailed,
            OperationErrorCategory.Execution, "old diagnostic")));
        await load;
        Assert.Equal(MetadataPresentation.Empty, s.Vm.MetadataPresentation);
        Assert.DoesNotContain("old diagnostic", s.Vm.OperationDetails);
        Assert.True(s.Vm.SessionLocked);
    }
}
