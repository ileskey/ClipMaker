using ClipDownloader.Application.Models;
namespace ClipDownloader.Application.Contracts;
public interface IVideoSourceParser { OperationResult<VideoSourceReference> Parse(Uri input); }
