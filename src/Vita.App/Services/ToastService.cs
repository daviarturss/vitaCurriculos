namespace Vita.App.Services;

public sealed record ToastMessage(Guid Id, string Text, string Type);

public sealed class ToastService
{
    public event Action<ToastMessage>? OnShow;

    public void Success(string text) => Show(text, "success");
    public void Error(string text) => Show(text, "error");
    public void Info(string text) => Show(text, "info");

    public void Show(string text, string type = "success")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        OnShow?.Invoke(new ToastMessage(Guid.NewGuid(), text, type));
    }
}
