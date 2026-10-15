using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipDownloader.ViewModels;

public abstract class ValidatingViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string[]> errors = [];
    public bool HasErrors => errors.Count != 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) => string.IsNullOrEmpty(propertyName)
        ? errors.Values.SelectMany(x => x).ToArray() : errors.GetValueOrDefault(propertyName) ?? [];

    protected void SetErrors(string property, string[] values)
    {
        if (values.Length == 0) errors.Remove(property); else errors[property] = values;
        ErrorsChanged?.Invoke(this, new(property));
        OnPropertyChanged(nameof(HasErrors));
    }

    protected void ClearErrors()
    {
        foreach (string property in errors.Keys.ToArray()) SetErrors(property, []);
    }
}
