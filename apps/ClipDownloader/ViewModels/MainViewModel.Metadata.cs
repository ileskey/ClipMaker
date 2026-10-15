using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipDownloader.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty] private MetadataPresentation metadataPresentation = MetadataPresentation.Empty;
    [ObservableProperty] private string thumbnailStatus = "영상 정보 없음";

    private void RefreshMetadataPresentation() => MetadataPresentation = metadata is null
        ? MetadataPresentation.Empty : MetadataPresentation.Create(metadata, selection);

    private void ClearMetadataPresentation()
    {
        MetadataPresentation = MetadataPresentation.Empty;
        ThumbnailStatus = "영상 정보 없음";
    }
}
