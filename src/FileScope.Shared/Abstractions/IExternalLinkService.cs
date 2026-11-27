namespace FileScope.Shared.Abstractions;

public interface IExternalLinkService
{
    bool TryOpen(Uri uri);
}
