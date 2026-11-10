namespace barcodrod.io.Core.Inventory;

public static class InventoryEngine
{
    public static InventoryItem CreateItem(InventoryDocument document)
    {
        var ids = document.Items.Select(i => i.SKU).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = 1;
        while (ids.Contains($"ITEM-{next:D4}")) next++;
        return new InventoryItem { SKU = $"ITEM-{next:D4}", Quantity = 1 };
    }

    public static void Validate(InventoryDocument document)
    {
        if (document.Version != 1) throw new InvalidDataException("This inventory was saved by a newer version of the app.");
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        foreach (var item in document.Items)
        {
            if (item.Id == Guid.Empty || !ids.Add(item.Id)) throw new InvalidDataException("Inventory record IDs must be unique.");
            if (string.IsNullOrWhiteSpace(item.SKU) || item.SKU.Length > 200) throw new InvalidDataException("Every item needs an item ID of 1–200 characters.");
            if (string.IsNullOrWhiteSpace(item.Name)) throw new InvalidDataException($"{item.SKU}: item name is required.");
            if (!InventoryLabels.Formats.Skip(1).Contains(item.BarcodeFormat)) throw new InvalidDataException($"{item.SKU}: choose a supported barcode format.");
            if (!skus.Add(item.SKU.Trim())) throw new InvalidDataException($"Duplicate item ID (SKU): {item.SKU}.");
            if (!string.IsNullOrWhiteSpace(item.Barcode) && !codes.Add(item.Barcode.Trim())) throw new InvalidDataException($"Duplicate barcode: {item.Barcode}.");
            if (item.Quantity < 0 || item.Reserved < 0 || item.Reserved > item.Quantity)
                throw new InvalidDataException($"{item.SKU}: quantities must be nonnegative; reserved stock cannot exceed on-hand stock.");
            if (item.ReorderLevel < 0 || item.TargetStock < 0 || item.UnitCost < 0 || item.UnitPrice < 0)
                throw new InvalidDataException($"{item.SKU}: stock thresholds and prices cannot be negative.");
            try { _ = item.StockValue; } catch (OverflowException) { throw new InvalidDataException($"{item.SKU}: quantity and unit cost produce a stock value that is too large."); }
            if (item.CustomFields.Count > 100 || item.CustomFields.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 100) || item.CustomFields.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.CustomFields.Count)
                throw new InvalidDataException($"{item.SKU}: use at most 100 named custom fields.");
        }
    }

    public static void Upsert(InventoryDocument document, InventoryItem edited, string reason = "Item edited")
    {
        var previous = document.Items.FirstOrDefault(i => i.Id == edited.Id);
        var item = edited.Copy();
        item.SKU = item.SKU.Trim(); item.Name = item.Name.Trim(); item.Barcode = item.Barcode.Trim();
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        if (previous == null) document.Items.Add(item);
        else document.Items[document.Items.IndexOf(previous)] = item;
        Validate(document);
        document.Movements.Add(new StockMovement
        {
            ItemId = item.Id, SKU = item.SKU, Action = previous == null ? "Created" : reason,
            QuantityChange = item.Quantity - (previous?.Quantity ?? 0), ReservedChange = item.Reserved - (previous?.Reserved ?? 0),
            Balance = item.Quantity, Note = previous == null ? "Opening balance" : reason,
            FromLocation = previous?.Location ?? "", ToLocation = item.Location
        });
    }

    public static void Move(InventoryDocument document, Guid id, StockAction action, decimal amount,
        string reference, string note, string destination = "", string destinationBin = "")
    {
        var item = document.Items.Single(i => i.Id == id);
        if (item.Archived) throw new InvalidOperationException("Restore this archived item before recording stock movements.");
        if (amount < 0 || (amount == 0 && action != StockAction.SetCount && action != StockAction.Transfer))
            throw new InvalidOperationException("Enter a positive quantity, or zero for a stock count.");
        var before = item.Copy();
        switch (action)
        {
            case StockAction.Receive: item.Quantity += amount; break;
            case StockAction.Issue:
                if (amount > item.Available) throw new InvalidOperationException("The quantity to remove exceeds the available quantity. Release reserved items first if needed.");
                item.Quantity -= amount; break;
            case StockAction.SetCount: item.Quantity = amount; break;
            case StockAction.Reserve: item.Reserved += amount; break;
            case StockAction.Release: item.Reserved -= amount; break;
            case StockAction.FulfillReservation: item.Reserved -= amount; item.Quantity -= amount; break;
            case StockAction.Transfer:
                if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("Enter the destination location.");
                if (amount != item.Quantity) throw new InvalidOperationException("Moving an item changes the location of its entire quantity. Use separate item IDs to track quantities in different locations.");
                item.Location = destination.Trim(); item.Bin = destinationBin.Trim(); break;
        }
        Validate(document);
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        document.Movements.Add(new StockMovement
        {
            ItemId = id, SKU = item.SKU, Action = action.ToString(), QuantityChange = item.Quantity - before.Quantity,
            ReservedChange = item.Reserved - before.Reserved, Balance = item.Quantity,
            Reference = reference.Trim(), Note = note.Trim(), FromLocation = before.Location, ToLocation = item.Location
        });
    }

    public static InventoryItem? Lookup(InventoryDocument document, string value)
    {
        value = value.Trim();
        return document.Items.FirstOrDefault(i => !i.Archived &&
            (i.SKU.Equals(value, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(i.Barcode) && i.Barcode.Equals(value, StringComparison.OrdinalIgnoreCase))));
    }
}
