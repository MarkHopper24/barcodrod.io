using System.Security.Cryptography;
using System.Text.Json;

namespace barcodrod.io.Core.Inventory;

/// <summary>Commits validated snapshots atomically. A failed write never mutates the in-memory inventory.</summary>
public sealed class InventoryStore(string folder)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InventoryDocument _document = new();
    private string? _revision;
    private bool _loaded;
    public string FilePath { get; } = Path.Combine(folder, "inventory.json");
    public InventoryDocument Snapshot => InventoryJson.Copy(_document);

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;
            Directory.CreateDirectory(folder);
            if (File.Exists(FilePath))
            {
                var bytes = await File.ReadAllBytesAsync(FilePath);
                _document = JsonSerializer.Deserialize<InventoryDocument>(bytes, InventoryJson.Options)
                    ?? throw new InvalidDataException("Inventory data is empty. Restore inventory.json.bak before continuing.");
                InventoryEngine.Validate(_document);
                _revision = Hash(bytes);
            }
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public async Task CommitAsync(Action<InventoryDocument> change)
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            var candidate = Snapshot;
            change(candidate);
            InventoryEngine.Validate(candidate);
            await PersistAsync(candidate, false);
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreAsync(InventoryDocument backup)
    {
        var candidate = InventoryJson.Copy(backup); candidate.Excel = null;
        InventoryEngine.Validate(candidate); InventoryFields.ValidateColumns(candidate.Columns);
        await _gate.WaitAsync();
        try { Directory.CreateDirectory(folder); await PersistAsync(candidate, !_loaded); _loaded = true; }
        finally { _gate.Release(); }
    }

    private async Task PersistAsync(InventoryDocument candidate, bool recovery)
    {
            using var fileLock = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var actual = File.Exists(FilePath) ? Hash(await File.ReadAllBytesAsync(FilePath)) : null;
            if (!recovery && actual != _revision) throw new IOException("Inventory changed in another app instance. Restart the app before saving.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate, InventoryJson.Options);
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _document = candidate;
            _revision = Hash(bytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
