using System.Text;

namespace ClipMaker.Media.Infrastructure.ProcessExecution;

internal static class WindowsCommandLine
{
    // Encode an argv vector for CreateProcess/Windows CRT parsing. This is not shell syntax.
    public static string Encode(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            result.Append(character);
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }
}
