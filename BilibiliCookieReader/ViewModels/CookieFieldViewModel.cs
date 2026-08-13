using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BilibiliCookieReader.ViewModels;

public partial class CookieFieldViewModel : ViewModelBase
{
    public CookieFieldViewModel(string envName, string secretName)
    {
        EnvName = envName;
        SecretName = secretName;
    }

    public Func<CookieFieldViewModel, Task>? CopyRequested { get; set; }

    public string EnvName { get; }
    public string SecretName { get; }

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MetaText { get; set; } = "尚未讀取";

    [ObservableProperty]
    public partial bool HasValue { get; set; }

    [ObservableProperty]
    public partial bool IsExpired { get; set; }

    [ObservableProperty]
    public partial bool IsExpiringSoon { get; set; }

    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    public string SecretHint => EnvName == SecretName ? string.Empty : SecretName;

    public string DisplayValue => !IsLoaded
        ? string.Empty
        : !HasValue
            ? "（檔案中沒有這個欄位）"
            : IsRevealed
                ? Value
                : Masked;

    private string Masked => Value.Length <= 10
        ? new string('•', Math.Max(Value.Length, 6))
        : $"{Value[..6]}…{Value[^4..]}";

    partial void OnValueChanged(string value) => NotifyDisplay();
    partial void OnHasValueChanged(bool value) => NotifyDisplay();
    partial void OnIsRevealedChanged(bool value) => NotifyDisplay();
    partial void OnIsLoadedChanged(bool value) => NotifyDisplay();

    public void Apply(Services.CookieField field, bool reveal)
    {
        Value = field.Value;
        HasValue = field.HasValue;
        IsExpired = field.IsExpired;
        IsExpiringSoon = field.IsExpiringSoon;
        MetaText = field.HasValue ? field.MetaText : "檔案中沒有這個欄位";
        IsRevealed = reveal;
        IsLoaded = true;
        NotifyDisplay();
    }

    public void Clear(bool reveal)
    {
        Value = string.Empty;
        HasValue = false;
        IsExpired = false;
        IsExpiringSoon = false;
        MetaText = "尚未讀取";
        IsRevealed = reveal;
        IsLoaded = false;
        NotifyDisplay();
    }

    private void NotifyDisplay() => OnPropertyChanged(nameof(DisplayValue));

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (CopyRequested is not null)
            await CopyRequested(this);
    }
}
