namespace EMaigrator.Core.Model;

/// <summary>
/// Provider-neutral folder path. Always stored with the canonical '/' separator semantics.
/// (CONTRACTS.md §1)
/// </summary>
public sealed record FolderPath
{
    public IReadOnlyList<string> Segments { get; }
    public int Depth => Segments.Count;
    public string Name => Segments.Count == 0 ? "" : Segments[^1];
    public bool IsRoot => Segments.Count == 0;

    public FolderPath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments.ToArray();
    }

    public static FolderPath Parse(string path, char separator = '/')
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path
            .Split(separator)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        return new FolderPath(segments);
    }

    public string ToString(char separator) => string.Join(separator, Segments);

    public override string ToString() => ToString('/');

    public FolderPath Parent()
    {
        if (IsRoot)
            throw new InvalidOperationException("Root folder path has no parent.");
        return new FolderPath(Segments.Take(Segments.Count - 1).ToArray());
    }

    public bool Equals(FolderPath? other)
        => other is not null && Segments.SequenceEqual(other.Segments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var s in Segments)
            hash.Add(s);
        return hash.ToHashCode();
    }
}
