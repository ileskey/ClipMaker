using ClipMaker.Media.Core.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipDownloader.ViewModels;

public sealed partial class TimecodeInputViewModel : ValidatingViewModel
{
    [ObservableProperty] private string hours = "00";
    [ObservableProperty] private string minutes = "00";
    [ObservableProperty] private string seconds = "00";
    [ObservableProperty] private string frames = "00";
    public string Text => $"{Hours}:{Minutes}:{Seconds}:{Frames}";

    public bool TryGet(FrameRate? fps, out TimecodeInput? input, out Timecode? time)
    {
        ClearErrors();
        time = null;
        if (!TimecodeInput.TryParse(Text, out input, out var error))
        { SetErrors(error!.Field, new[] { error.Message }); return false; }
        if (input.Minutes > 59 || input.Seconds > 59)
        {
            SetErrors(input.Minutes > 59 ? nameof(Minutes) : nameof(Seconds), new[] { "분·초는 00~59 범위여야 합니다." });
            return false;
        }
        if (fps is not null && !Timecode.TryCreate(input, fps, out time, out error))
        { SetErrors(error!.Field, new[] { error.Message }); return false; }
        return true;
    }

    public void Reset() { Hours = Minutes = Seconds = Frames = "00"; }

    public void Normalize(string field)
    {
        string? value = field switch { nameof(Hours) => Hours, nameof(Minutes) => Minutes, nameof(Seconds) => Seconds, nameof(Frames) => Frames, _ => null };
        if (value is not { Length: 1 } || !char.IsAsciiDigit(value[0])) return;
        switch (field)
        {
            case nameof(Hours): Hours = "0" + value; break;
            case nameof(Minutes): Minutes = "0" + value; break;
            case nameof(Seconds): Seconds = "0" + value; break;
            case nameof(Frames): Frames = "0" + value; break;
        }
    }
}
