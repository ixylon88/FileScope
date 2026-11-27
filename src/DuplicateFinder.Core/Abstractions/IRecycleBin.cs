namespace DuplicateFinder.Core.Abstractions;

/// <summary>Invoke the guard immediately before handing this exact item to the shell for recycling.</summary>
public interface IRecycleBin
{
    void Recycle(string path, Action validate);
}
