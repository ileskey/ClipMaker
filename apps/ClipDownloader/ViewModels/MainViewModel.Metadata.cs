using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Imaging;
using System.Windows.Media.Imaging;

namespace ClipDownloader.ViewModels;

public enum ThumbnailState { Empty, Unavailable, Loading, Ready, Failed, Cancelled }

public sealed partial class MainViewModel
{
    private readonly IThumbnailContentService thumbnails;
    private readonly IThumbnailDecoder thumbnailDecoder;
    private long thumbnailEpoch;
    private Task thumbnailTask = Task.CompletedTask;
    private ThumbnailSlot? thumbnailSlot;
    private sealed class ThumbnailSlot(long revision, long epoch, Uri uri)
    {
        public long Revision { get; } = revision;
        public long Epoch { get; } = epoch;
        public Uri Uri { get; } = uri;
        public CancellationTokenSource Cancellation { get; } = new();
    }
    [ObservableProperty] private MetadataPresentation metadataPresentation = MetadataPresentation.Empty;
    [ObservableProperty] private string thumbnailStatus = "영상 정보 없음";
    [ObservableProperty] private ThumbnailState thumbnailState;
    [ObservableProperty] private BitmapSource? thumbnailImage;
    public IRelayCommand CancelThumbnailCommand { get; private set; } = null!;
    internal Task ThumbnailCompletion => thumbnailTask;

    private bool CanCancelThumbnail() => Lifetime == AppLifetime.Running && ThumbnailState == ThumbnailState.Loading &&
        thumbnailSlot is { Cancellation.IsCancellationRequested: false };

    private void CancelThumbnail()
    {
        if (!CanCancelThumbnail()) return;
        thumbnailSlot!.Cancellation.Cancel();
        ThumbnailStatus = "취소 요청 · 이미지 작업 종료 대기";
        CancelThumbnailCommand.NotifyCanExecuteChanged();
    }

    private void RefreshMetadataPresentation() => MetadataPresentation = metadata is null
        ? MetadataPresentation.Empty : MetadataPresentation.Create(metadata, selection);

    private void ClearMetadataPresentation()
    {
        InvalidateThumbnail();
        MetadataPresentation = MetadataPresentation.Empty;
        ThumbnailStatus = "영상 정보 없음";
    }

    private void InvalidateThumbnail()
    {
        thumbnailEpoch++;
        thumbnailSlot?.Cancellation.Cancel();
        ThumbnailImage = null;
        ThumbnailState = ThumbnailState.Empty;
        CancelThumbnailCommand?.NotifyCanExecuteChanged();
    }

    private void StartThumbnail(VideoInfo info)
    {
        bool IsMetadataCurrent() => Lifetime == AppLifetime.Running && MetadataState == MetadataState.Ready && ReferenceEquals(metadata, info);
        if (!IsMetadataCurrent()) return;
        if (info.ThumbnailUrl is null)
        {
            ThumbnailState = ThumbnailState.Unavailable;
            if (IsMetadataCurrent()) ThumbnailStatus = "썸네일 없음 · 제공되지 않았거나 주소 정책에 따라 제외됨";
            return;
        }
        Task previous = thumbnailTask;
        var slot = new ThumbnailSlot(metadataRevision, ++thumbnailEpoch, info.ThumbnailUrl);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Register task and identity before the first notification or await.
        thumbnailSlot = slot; thumbnailTask = completion.Task;
        _ = RunAsync();

        bool IsCurrent() => Lifetime == AppLifetime.Running && MetadataState == MetadataState.Ready &&
            metadataRevision == slot.Revision && thumbnailEpoch == slot.Epoch && ReferenceEquals(thumbnailSlot, slot);

        async Task RunAsync()
        {
            var token = slot.Cancellation.Token;
            try
            {
                ThumbnailImage = null;
                if (IsCurrent()) ThumbnailState = ThumbnailState.Loading;
                if (IsCurrent()) ThumbnailStatus = "썸네일 로딩 중…";
                CancelThumbnailCommand.NotifyCanExecuteChanged();
                // Even notification-triggered invalidation must keep the previous operation in this chain.
                await previous;
                if (!IsCurrent() || token.IsCancellationRequested) return;
                var result = await Task.Run(async () =>
                {
                    var content = await thumbnails.GetAsync(slot.Uri, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (content.Status != ThumbnailContentStatus.Succeeded)
                        return new ThumbnailDecodeResult(null, content.Failure ?? ThumbnailFailure.HttpFailure);
                    return thumbnailDecoder.Decode(content.Content, content.Kind!.Value, token);
                });
                if (!IsCurrent() || token.IsCancellationRequested) return;
                if (result.Image is { IsFrozen: true } image)
                {
                    ThumbnailImage = image;
                    if (!IsCurrent() || token.IsCancellationRequested) return;
                    ThumbnailState = ThumbnailState.Ready;
                    if (IsCurrent() && !token.IsCancellationRequested) ThumbnailStatus = "썸네일 표시 완료";
                }
                else
                {
                    ThumbnailState = ThumbnailState.Failed;
                    if (IsCurrent() && !token.IsCancellationRequested)
                        ThumbnailStatus = FailureText(result.Failure ?? ThumbnailFailure.InvalidImage) + " · 영상 불러오기로 다시 조회하세요.";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                if (IsCurrent() && !token.IsCancellationRequested)
                {
                    ThumbnailState = ThumbnailState.Failed;
                    if (IsCurrent() && !token.IsCancellationRequested) ThumbnailStatus = "썸네일 처리 실패 · 영상 불러오기로 다시 조회하세요.";
                }
            }
            finally
            {
                if (IsCurrent() && token.IsCancellationRequested)
                {
                    ThumbnailImage = null;
                    if (IsCurrent()) ThumbnailState = ThumbnailState.Cancelled;
                    if (IsCurrent()) ThumbnailStatus = "썸네일 로딩 취소됨 · 영상 불러오기로 다시 조회하세요.";
                }
                if (ReferenceEquals(thumbnailSlot, slot)) thumbnailSlot = null;
                slot.Cancellation.Dispose();
                completion.TrySetResult();
                CancelThumbnailCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAcknowledgeClose));
            }
        }
    }

    private static string FailureText(ThumbnailFailure reason) => reason switch
    {
        ThumbnailFailure.UnsafeUri => "썸네일 주소 정책 거절",
        ThumbnailFailure.DestinationRejected => "썸네일 연결 목적지 거절",
        ThumbnailFailure.RedirectRejected => "썸네일 리디렉션 거절",
        ThumbnailFailure.TimedOut => "썸네일 시간 초과",
        ThumbnailFailure.HttpFailure => "썸네일 HTTP 연결/응답 실패",
        ThumbnailFailure.UnsupportedImage => "썸네일 형식 미지원 (JPEG/PNG만 지원)",
        ThumbnailFailure.TooLarge => "썸네일 크기 초과 (최대 4 MiB)",
        _ => "썸네일 이미지 검증/decode 실패"
    };
}
