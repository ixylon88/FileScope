namespace FileScope.Shared;

public interface IExternalLinkService
{
    bool TryOpen(Uri uri);
}
