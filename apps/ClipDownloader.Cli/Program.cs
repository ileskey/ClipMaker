using ClipDownloader.Cli;
using ClipDownloader.Cli.Composition;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
var console = new ConsoleInteraction();
var parsed = CommandLineParser.Parse(args);
if (parsed.Help) { console.Out(CommandLineParser.HelpText); return 0; }
if (parsed.Error is not null) { console.Error(parsed.Error + "\n"); return 2; }
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
var logging = new LoggingSetup();
try
{
    logging.Logger?.Information("Command {Command} started", parsed.Command!.Name);
    int code;
    // Application finishes explicit cleanup before the provider disposes the source owner.
    using (var services = ServiceRegistration.Create(parsed.Command!, console, logging.Logger))
        code = await services.GetRequiredService<CliApplication>().RunAsync(parsed.Command!, cancellation.Token);
    logging.Logger?.Information("Command completed with exit code {ExitCode}", code);
    return code;
}
finally
{
    Console.CancelKeyPress -= cancel;
    logging.Dispose();
    if (logging.HasProblem) console.Error("주의: 진단 로그 기록/종료에 문제가 있었습니다. 작업 결과는 위 안내를 확인하세요.\n");
}
