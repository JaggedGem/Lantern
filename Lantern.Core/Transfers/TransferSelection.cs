using Lantern.Models;

namespace Lantern.Transfers;

/// <summary>Bounded file/folder expansion. Links are rejected; absolute paths stay local.</summary>
public static class TransferSelection
{
    public static Task<IReadOnlyList<TransferSourceFile>> FromPathsAsync(
        IEnumerable<string> paths,
        TransferOptions options,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var selected = paths.Take(options.MaximumFilesPerTransfer + 1).ToArray();
        if (selected.Length < 1 || selected.Length > options.MaximumFilesPerTransfer)
            throw new ArgumentException("Invalid selection count.", nameof(paths));
        return Task.Run<IReadOnlyList<TransferSourceFile>>(
            () =>
            {
                var files = new List<TransferSourceFile>();
                long total = 0;
                foreach (var selectedPath in selected)
                {
                    token.ThrowIfCancellationRequested();
                    var path = Path.GetFullPath(selectedPath);
                    FileReceiver.RejectReparseAncestors(Path.GetDirectoryName(path)!);
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Selections cannot contain links or reparse points.");
                    if (!Directory.Exists(path))
                    {
                        Add(path, Path.GetFileName(path));
                        continue;
                    }
                    var rootName = new DirectoryInfo(path).Name;
                    var before = files.Count;
                    var visitedEntries = 0;
                    var pending = new Stack<(string Path, string Relative)>();
                    pending.Push((path, rootName));
                    while (pending.TryPop(out var directory))
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                        {
                            token.ThrowIfCancellationRequested();
                            if (++visitedEntries > options.MaximumFilesPerTransfer * 16)
                                throw new IOException(
                                    "The selection contains too many directory entries."
                                );
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                                throw new IOException(
                                    "Folders cannot contain links or reparse points."
                                );
                            var relative = directory.Relative + "/" + Path.GetFileName(entry);
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                // Bound directory depth and validate names even for directories without files.
                                _ = new TransferFile(Guid.NewGuid(), relative + "/placeholder", 0);
                                pending.Push((entry, relative));
                                if (pending.Count > options.MaximumFilesPerTransfer)
                                    throw new IOException(
                                        "The selection contains too many directories."
                                    );
                            }
                            else
                                Add(entry, relative);
                        }
                    }
                    if (files.Count == before)
                        throw new IOException("Empty folders have no transferable files.");
                }
                return files.AsReadOnly();
                void Add(string path, string relative)
                {
                    token.ThrowIfCancellationRequested();
                    if (files.Count >= options.MaximumFilesPerTransfer)
                        throw new IOException("The selection contains too many files.");
                    var file = TransferSourceFile.FromPath(path, relative);
                    if (file.Metadata.SizeBytes > options.MaximumTransferBytes - total)
                        throw new IOException("The selection exceeds the transfer size limit.");
                    total += file.Metadata.SizeBytes;
                    files.Add(file);
                }
            },
            token
        );
    }
}
