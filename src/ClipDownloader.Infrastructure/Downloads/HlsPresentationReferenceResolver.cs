using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class HlsPresentationReferences(HlsPresentationGroup group, SelectedMediaLocator video,
    SelectedMediaLocator audio, long budgetUsed)
{
    internal HlsPresentationGroup Group { get; } = group;
    internal SelectedMediaLocator Video { get; } = video;
    internal SelectedMediaLocator Audio { get; } = audio;
    internal long BudgetUsed { get; } = budgetUsed;
}

internal sealed class HlsPresentationReferenceResolver(BoundedSourceHttpReader reader)
{
    internal async Task<OperationResult<HlsPresentationReferences>> ResolveAsync(SelectedSourceLocators source,
        SourceInspectionBudget budget, long alreadyReadBytes, CancellationToken token)
    {
        if (alreadyReadBytes < 0 || alreadyReadBytes > budget.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(alreadyReadBytes));
        var videos = source.PresentationReferences.Where(r => r.Format.Id == "270").ToArray();
        var audios = source.PresentationReferences.Where(r => r.Format.Id == "234").ToArray();
        if (videos.Length != 1 || audios.Length != 1) return Fail(OperationErrorCode.VerificationFailed);
        var video = videos[0];
        var audio = audios[0];
        if (source.Selection.Source.Platform != VideoPlatform.YouTube || source.Video.Format.Id != "137" || source.Audio?.Format.Id != "140" ||
            video?.ManifestUri is not { } master || audio?.ManifestUri != master ||
            video.Format.VideoStream != StreamPresence.Present || audio.Format.VideoStream != StreamPresence.Absent ||
            audio.Format.AudioStream == StreamPresence.Absent)
            return Fail(OperationErrorCode.VerificationFailed);
        long remaining = budget.MaximumBytes - alreadyReadBytes;
        if (remaining <= 0) return Fail(OperationErrorCode.UnsupportedFormat);
        var response = await reader.ReadAsync(new(master, SourceHttpMode.Resource, (int)Math.Min(remaining, 512 * 1024),
            headers: video.Headers), token).ConfigureAwait(false);
        if (response.Status == OperationStatus.Cancelled || token.IsCancellationRequested) return OperationResult<HlsPresentationReferences>.Cancelled();
        if (response.Status != OperationStatus.Succeeded || response.Bytes is not { } bytes)
            return Fail(response.Failure == SourceHttpFailureKind.SizeLimitExceeded ? OperationErrorCode.UnsupportedFormat :
                response.Failure == SourceHttpFailureKind.TransportFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed);
        var declared = new HlsPresentationGroupReader().Read(bytes, master, video.Format.Id, audio.Format.Id);
        if (declared.Status != OperationStatus.Succeeded) return OperationResult<HlsPresentationReferences>.Failed(declared.Error!);
        return OperationResult<HlsPresentationReferences>.Succeeded(new(declared.Data!,
            new(video.Format, declared.Data!.Video, master, video.Headers),
            new(audio.Format, declared.Data.Audio, master, audio.Headers), alreadyReadBytes + bytes.Length));
    }
    private static OperationResult<HlsPresentationReferences> Fail(OperationErrorCode code) => OperationResult<HlsPresentationReferences>.Failed(
        new(code, OperationErrorCategory.Execution, "선택 원본의 provider presentation 참조를 확인하지 못했습니다.", "ProviderPresentationReference"));
}
