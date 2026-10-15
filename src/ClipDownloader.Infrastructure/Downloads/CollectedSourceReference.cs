using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

// Local byte identity shared by collectors; transport coordinates remain in the derived types.
internal abstract class CollectedSourceReference(OwnedMediaFile file, long bytes, long budgetUsed, string sha256)
{
    internal OwnedMediaFile File { get; } = file;
    internal long Bytes { get; } = bytes;
    internal long BudgetUsed { get; } = budgetUsed;
    internal string Sha256 { get; } = sha256;
}
