namespace ClipMaker.Media.Infrastructure.Tools;

public interface IToolLocator
{
    ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken);
}
