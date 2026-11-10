using System.Text.Json;
using System.Text.Json.Serialization;

namespace barcodrod.io.Core.Inventory;

public sealed class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SKU { get; set; } = "";
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string BarcodeFormat { get; set; } = "CODE_128";
    public string Category { get; set; } = "";
    public string Location { get; set; } = "";
    public string Bin { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Unit { get; set; } = "each";
    public decimal Quantity { get; set; }
    public decimal Reserved { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal TargetStock { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public string Lot { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
    public string Notes { get; set; } = "";
    public bool Archived { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public decimal Available => Quantity - Reserved;
    [JsonIgnore] public bool LowStock => !Archived && ReorderLevel > 0 && Available <= ReorderLevel;
    [JsonIgnore] public decimal ReorderQuantity => Math.Max(0, Math.Max(TargetStock, ReorderLevel) - Available);
    [JsonIgnore] public decimal StockValue => Quantity * UnitCost;
    public InventoryItem Copy() => InventoryJson.Copy(this);
}

public enum StockAction { Receive, Issue, SetCount, Reserve, Release, FulfillReservation, Transfer }

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public string SKU { get; set; } = "";
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public decimal QuantityChange { get; set; }
    public decimal ReservedChange { get; set; }
    public decimal Balance { get; set; }
    public string Reference { get; set; } = "";
    public string Note { get; set; } = "";
    public string FromLocation { get; set; } = "";
    public string ToLocation { get; set; } = "";
}

public sealed class InventoryDocument
{
    public int Version { get; set; } = 1;
    public List<InventoryItem> Items { get; set; } = [];
    public List<StockMovement> Movements { get; set; } = [];
    public WorkbookLink? Excel { get; set; }
    public List<ColumnMapping> Columns { get; set; } = InventoryFields.DefaultColumns();
    public LabelOptions Labels { get; set; } = new();
}

public sealed class WorkbookLink
{
    public string Path { get; set; } = "";
    public string Sheet { get; set; } = "Inventory";
    public int HeaderRow { get; set; } = 1;
    public string NumberCulture { get; set; } = "";
    public List<ColumnMapping> Columns { get; set; } = InventoryFields.DefaultColumns();
    public bool AutoSync { get; set; }
    public DateTimeOffset? LastSyncUtc { get; set; }
    // Only mapped values form the baseline. Edits to unrelated sheets cannot become stock conflicts.
    public Dictionary<string, Dictionary<string, string>> Baseline { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ColumnMapping
{
    public string Field { get; set; } = "";
    public string Header { get; set; } = "";
    public bool Included { get; set; } = true;
}

public static class InventoryJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
