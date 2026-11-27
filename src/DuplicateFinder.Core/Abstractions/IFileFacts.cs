using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Core.Abstractions;

internal interface IFileFacts
{
    FileStamp Read(FileStream file);
    IReadOnlyList<NamedStream> Streams(FileStream file);
}
