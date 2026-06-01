namespace EMaigrator.Core.Abstractions;

/// <summary>Declared destination constraints used by pre-flight and folder transforms (CONTRACTS.md §2).</summary>
public sealed record ProviderConstraints
{
    public int MaxFolderDepth { get; init; } = int.MaxValue;
    public int MaxPathLengthChars { get; init; } = int.MaxValue;
    public IReadOnlyCollection<char> IllegalNameChars { get; init; } = [];
    public long MaxMessageBytes { get; init; } = long.MaxValue;
    public long MaxAttachmentBytes { get; init; } = long.MaxValue;
    public char FolderSeparator { get; init; } = '/';
    public IReadOnlyCollection<string> ReservedFolderNames { get; init; } = [];
}
