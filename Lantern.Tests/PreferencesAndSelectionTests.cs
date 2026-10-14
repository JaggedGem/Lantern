using Lantern.Configuration;
using Lantern.Diagnostics;
using Lantern.Models;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class PreferencesAndSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "lantern-preferences-" + Guid.NewGuid().ToString("N")
    );

    private string PathFor(string name) => Path.Combine(_root, name);

    [Fact]
    public void PreferencesRoundTrip_AndInvalidLimitsDoNotReplaceSavedSettings()
    {
        var store = new SettingsStore(PathFor("settings.json"));
        var settings = new LanternSettings
        {
            DeviceName = "Saved name",
            DownloadDirectory = PathFor("downloads"),
            ShowNotifications = false,
        };
        store.Save(settings);
        Assert.Equal(settings, store.Load());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Save(settings with { MaximumConcurrentTransfers = 1000 })
        );
        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void HistoryRetainsOnlyNewestTerminalSummaries_AndCanBeCleared()
    {
        var history = new TransferHistory(PathFor("history.json"));
        var sender = Guid.NewGuid();
        var receiver = Guid.NewGuid();
        for (int index = 0; index < 258; index++)
        {
            var time = DateTimeOffset.UtcNow;
            var transfer = Transfer
                .CreateSending(
                    sender,
                    receiver,
                    [new TransferFile(Guid.NewGuid(), "file", 1)],
                    time
                )
                .Cancel(time);
            history.Record(transfer);
        }
        Assert.Equal(256, history.GetEntries().Count);
        history = new TransferHistory(PathFor("history.json"));
        Assert.Equal(256, history.GetEntries().Count);
        history.Clear();
        Assert.Empty(new TransferHistory(PathFor("history.json")).GetEntries());
    }

    [Fact]
    public void DiagnosticLogRotates_WithoutUnboundedRetention()
    {
        var log = new DiagnosticLog(PathFor("events.jsonl"));
        for (int index = 0; index < 5; index++)
            log.Write("test", new string('x', 1024 * 1024));
        Assert.Equal(3, Directory.GetFiles(_root).Length);
        Assert.True(new FileInfo(log.LogPath).Length < 2 * 1024 * 1024);
    }

    [Fact]
    public void ProgressCalculatesAverageSpeedAndEstimatedRemaining()
    {
        var start = DateTimeOffset.UtcNow;
        var transfer = Transfer
            .CreateSending(
                Guid.NewGuid(),
                Guid.NewGuid(),
                [new TransferFile(Guid.NewGuid(), "file", 1000)],
                start
            )
            .MarkConnecting()
            .MarkWaitingForAcceptance()
            .Start(start)
            .ReportProgress(250);
        var progress = TransferProgress.Calculate(transfer, start.AddSeconds(2));
        Assert.Equal(0.25, progress.Fraction);
        Assert.Equal(125, progress.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(6), progress.EstimatedRemaining);
    }

    [Fact]
    public async Task FolderSelectionPreservesHierarchy_AndEnforcesFileAndByteLimits()
    {
        Directory.CreateDirectory(PathFor("photos/nested"));
        await File.WriteAllTextAsync(PathFor("photos/nested/one.txt"), "one");
        await File.WriteAllTextAsync(PathFor("photos/two.txt"), "two");
        var files = await TransferSelection.FromPathsAsync(
            [PathFor("photos")],
            new TransferOptions()
        );
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Metadata.RelativePath == "photos/nested/one.txt");
        Assert.Contains(files, f => f.Metadata.RelativePath == "photos/two.txt");
        await Assert.ThrowsAsync<IOException>(() =>
            TransferSelection.FromPathsAsync(
                [PathFor("photos")],
                new TransferOptions { MaximumFilesPerTransfer = 1 }
            )
        );
        await Assert.ThrowsAsync<IOException>(() =>
            TransferSelection.FromPathsAsync(
                [PathFor("photos")],
                new TransferOptions { MaximumTransferBytes = 1 }
            )
        );
    }

    [Fact]
    public async Task FolderSelectionHonorsCancellation_AndRejectsEmptyFolders()
    {
        Directory.CreateDirectory(PathFor("empty"));
        await Assert.ThrowsAsync<IOException>(() =>
            TransferSelection.FromPathsAsync([PathFor("empty")], new TransferOptions())
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TransferSelection.FromPathsAsync(
                [PathFor("empty")],
                new TransferOptions(),
                cancelled.Token
            )
        );
    }

    [Fact]
    public async Task FolderSelectionRejectsLinksInsteadOfFollowingThem()
    {
        if (OperatingSystem.IsWindows())
            return; // Creating links on Windows depends on OS privileges; validate there manually.
        Directory.CreateDirectory(PathFor("folder"));
        Directory.CreateSymbolicLink(PathFor("folder/loop"), PathFor("folder"));
        await Assert.ThrowsAsync<IOException>(() =>
            TransferSelection.FromPathsAsync([PathFor("folder")], new TransferOptions())
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
