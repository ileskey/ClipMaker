namespace ClipDownloader.Cli;

public interface ICliConsole
{
    bool InputRedirected { get; }
    bool ErrorRedirected { get; }
    void Out(string text);
    void Error(string text);
    Task<string?> ReadLineAsync(CancellationToken token);
}

public sealed class ConsoleInteraction : ICliConsole
{
    public bool InputRedirected => Console.IsInputRedirected;
    public bool ErrorRedirected => Console.IsErrorRedirected;
    public void Out(string text) => Console.Out.WriteLine(text);
    public void Error(string text) => Console.Error.Write(text);
    // Console's synchronized reader can block synchronously. Waiting is cancellable even
    // when the terminal has not supplied a line; the background input owns no job resources.
    public Task<string?> ReadLineAsync(CancellationToken token) => Task.Run(Console.ReadLine, CancellationToken.None).WaitAsync(token);

    public static async Task<bool> ConfirmAsync(ICliConsole console, CancellationToken token)
    {
        while (true)
        {
            console.Error("위 설정으로 클립을 생성하시겠습니까? [y/N] ");
            string? answer = (await console.ReadLineAsync(token).ConfigureAwait(false))?.Trim();
            if (answer is null || answer.Length == 0 || answer.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase)) return true;
            console.Error("Y 또는 N을 입력해 주세요.\n");
        }
    }
    public static string Safe(string? value) => value is null ? "미확인" : new string(value.Where(c => !char.IsControl(c)).ToArray());
}
