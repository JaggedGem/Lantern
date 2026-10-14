using System.Text.Json;
using Lantern.Transfers;

namespace Lantern.Settings;

public sealed class TransferHistoryStore(string directory)
{
    private string FilePath => Path.Combine(directory, "history.json");
    public IReadOnlyList<TransferInfo> Load()
    {
        if (!File.Exists(FilePath)) return [];
        return (JsonSerializer.Deserialize<TransferInfo[]>(File.ReadAllText(FilePath)) ?? [])
            .Where(t => t.IsFinished).TakeLast(100).ToArray();
    }

    public void Save(IEnumerable<TransferInfo> transfers)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(transfers.Where(t => t.IsFinished).TakeLast(100)));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
