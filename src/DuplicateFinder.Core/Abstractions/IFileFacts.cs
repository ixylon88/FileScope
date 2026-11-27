namespace DuplicateFinder.Core;

internal interface IFileFacts
{
    FileStamp Read(FileStream file);
    IReadOnlyList<NamedStream> Streams(FileStream file);
}
