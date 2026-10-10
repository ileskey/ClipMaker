using System.Text;
using System.Text.Json;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (args[0])
        {
            case "arguments":
                Console.Write(JsonSerializer.Serialize(args[1..]));
                return 0;
            case "directory":
                Console.Write(Environment.CurrentDirectory);
                return 0;
            case "exit":
                Console.Write("stdout before exit");
                Console.Error.Write("stderr before exit");
                return int.Parse(args[1]);
            case "dual":
                int bytes = int.Parse(args[1]);
                await Task.WhenAll(WriteAsync(Console.OpenStandardOutput(), bytes, (byte)'O'),
                    WriteAsync(Console.OpenStandardError(), bytes, (byte)'E'));
                return 0;
            case "bytes":
                await WriteAsync(Console.OpenStandardOutput(), int.Parse(args[1]), (byte)'X');
                return 0;
            case "unicode":
                using (var stream = Console.OpenStandardOutput())
                {
                    // A complete multibyte character crosses the runner's 8192-byte read boundary.
                    byte[] text = Encoding.UTF8.GetBytes(new string('a', 8191) + "한😀\r\n끝");
                    foreach (byte value in text) await stream.WriteAsync(new byte[] { value });
                    await stream.FlushAsync();
                }
                return 0;
            case "invalid-utf8":
                using (var stream = Console.OpenStandardOutput())
                    await stream.WriteAsync(new byte[] { 0xef, 0xbb, 0xbf, 0xff, 0xe2 });
                return 0;
            case "wait":
                await Task.Delay(TimeSpan.FromSeconds(30));
                return 0;
            default:
                return 2;
        }
    }

    private static async Task WriteAsync(Stream stream, int count, byte value)
    {
        byte[] block = new byte[8192];
        Array.Fill(block, value);
        for (int written = 0; written < count;)
        {
            int length = Math.Min(block.Length, count - written);
            await stream.WriteAsync(block.AsMemory(0, length));
            written += length;
        }
        await stream.FlushAsync();
    }
}
