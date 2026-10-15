using System.Diagnostics;
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
            case "stdin-eof":
                Console.Write((await Console.In.ReadToEndAsync()).Length);
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
                Console.Write("READY");
                Console.Out.Flush();
                await Task.Delay(TimeSpan.FromSeconds(30));
                return 0;
            case "tree":
            case "root-exit":
            case "root-exit-silent":
            case "root-exit-intermediate":
                RecordPid(args[1]);
                using (var child = StartChild(args[1], args[0] == "tree" ? 1 : 0,
                    redirect: args[0] == "root-exit-silent",
                    mode: args[0] == "root-exit-intermediate" ? "worker-exit" : "tree-worker"))
                {
                    int expected = args[0] is "tree" or "root-exit-intermediate" ? 3 : 2;
                    var deadline = Stopwatch.StartNew();
                    while (Directory.GetFiles(args[1], "process-*.json").Length < expected)
                    {
                        if (deadline.Elapsed > TimeSpan.FromSeconds(10)) return 3;
                        await Task.Delay(10);
                    }
                    Console.Write("READY");
                    Console.Out.Flush();
                    if (args[0] == "root-exit-intermediate") await child.WaitForExitAsync();
                    if (args[0] != "tree") return 0;
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    return 0;
                }
            case "tree-worker":
                RecordPid(args[1]);
                if (int.Parse(args[2]) > 0)
                    using (var child = StartChild(args[1], int.Parse(args[2]) - 1, redirect: false))
                        await Task.Delay(TimeSpan.FromSeconds(60));
                else await Task.Delay(TimeSpan.FromSeconds(60));
                return 0;
            case "worker-exit":
                RecordPid(args[1]);
                using (var child = StartChild(args[1], 0, redirect: false))
                {
                    while (Directory.GetFiles(args[1], "process-*.json").Length < 3) await Task.Delay(10);
                    return 0;
                }
            default:
                return 2;
        }
    }

    private static void RecordPid(string directory)
    {
        using var current = Process.GetCurrentProcess();
        string path = Path.Combine(directory, $"process-{current.Id}.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new
        {
            Id = current.Id, StartTicks = current.StartTime.ToUniversalTime().Ticks
        }));
        File.Move(path + ".tmp", path);
    }

    private static Process StartChild(string directory, int depth, bool redirect, string mode = "tree-worker")
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = redirect, RedirectStandardError = redirect
        };
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(directory);
        info.ArgumentList.Add(depth.ToString());
        return Process.Start(info)!;
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
