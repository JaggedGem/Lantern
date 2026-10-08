namespace Lantern.Models;

/// <summary>Immutable logical file metadata; contains no local filesystem path or file I/O.</summary>
public sealed class TransferFile
{
    public TransferFile(Guid id, string relativePath, long sizeBytes, DateTimeOffset? modifiedAt = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The file identifier cannot be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "File size cannot be negative.");

        foreach (var segment in relativePath.Split('/'))
            ValidateSegment(segment, nameof(relativePath));

        Id = id;
        RelativePath = relativePath;
        FileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt?.ToUniversalTime();
    }

    public Guid Id { get; }
    public string RelativePath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public DateTimeOffset? ModifiedAt { get; }

    private static void ValidateSegment(string segment, string parameterName)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment[^1] is '.' or ' ')
            throw new ArgumentException("Paths must contain nonempty relative Windows filename segments.", parameterName);

        foreach (var character in segment)
        {
            if (char.IsControl(character) || character is '<' or '>' or ':' or '"' or '\\' or '|' or '?' or '*')
                throw new ArgumentException("The relative path contains an invalid Windows filename character.", parameterName);
        }

        // Windows device names remain reserved when followed by an extension.
        var stem = segment.Split('.')[0].TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDevice(stem))
            throw new ArgumentException("The relative path contains a reserved Windows device name.", parameterName);
    }

    private static bool IsNumberedDevice(string stem)
        => stem.Length == 4
           && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
               || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
           && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
}
