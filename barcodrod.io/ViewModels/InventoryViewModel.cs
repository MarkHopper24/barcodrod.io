using barcodrod.io.Core.Inventory;
using barcodrod.io.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace barcodrod.io.ViewModels;

public sealed class InventoryRow(InventoryItem item)
{
    public InventoryItem Item { get; } = item;
    public string SKU => Item.SKU;
    public string Name => Item.Name;
    public string Barcode => Item.Barcode;
    public string Location => string.Join(" / ", new[] { Item.Location, Item.Bin }.Where(s => s.Length > 0));
    public string Context => string.Join(" · ", new[] { SKU, Item.Category, Location }.Where(s => s.Length > 0));
    public string OnHand => Item.Quantity.ToString("0.###", CultureInfo.CurrentCulture);
    public string Available => Item.Available.ToString("0.###", CultureInfo.CurrentCulture);
    public string Quantity => $"{OnHand} {Item.Unit}";
    public string Availability => $"{Available} available";
    public string State => Item.Archived ? "Archived" : Item.ExpiryDate < DateOnly.FromDateTime(DateTime.Today) ? "Expired"
        : Item.Available == 0 ? Item.Quantity > 0 ? "All reserved" : "None available"
        : Item.LowStock ? "Below minimum" : Item.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)) ? "Expires soon" : "Available";
}

public sealed class MovementRow(StockMovement movement)
{
    public string Summary => $"{movement.TimestampUtc.ToLocalTime():g} · {InventoryActions.Name(movement.Action)} · {movement.QuantityChange:+0.###;-0.###;0}";
    public string Detail => $"Total {movement.Balance:0.###}" + (movement.ReservedChange != 0 ? $" · Reserved change {movement.ReservedChange:+0.###;-0.###;0}" : "") + $" · {movement.Reference} {movement.Note}";
}

public partial class InventoryViewModel : ObservableObject
{
    public InventoryService Service { get; }
    public InventoryViewModel(InventoryService service)
    {
        Service = service; Query = ""; Filter = "Active items"; Sort = "Name"; ScanAction = "Look up";
        ScanQuantity = 1; ScanReference = ""; Status = "Add an item or import a workbook to get started.";
        Summary = "0 active items"; ExcelStatus = "Excel workbook: not connected";
        DetailTitle = "Choose an item"; DetailText = "Select an item to see its quantity, location, notes, and activity.";
    }
    public ObservableCollection<InventoryRow> Rows { get; } = [];
    public ObservableCollection<MovementRow> RecentMovements { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["Active items", "Below minimum", "None available", "Expired", "Expiring in 30 days", "Archived", "All items"];
    public IReadOnlyList<string> Sorts { get; } = ["Name", "Item ID", "Available quantity", "Location", "Most recently updated"];
    public IReadOnlyList<string> ScanActions { get; } = ["Look up", "Add quantity", "Remove quantity"];
    [ObservableProperty] public partial string Query { get; set; }
    [ObservableProperty] public partial string Filter { get; set; }
    [ObservableProperty] public partial string Sort { get; set; }
    [ObservableProperty] public partial string ScanAction { get; set; }
    [ObservableProperty] public partial double ScanQuantity { get; set; }
    [ObservableProperty] public partial string ScanReference { get; set; }
    [ObservableProperty] public partial string Status { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsRecovery))] public partial bool IsError { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanWork)), NotifyPropertyChangedFor(nameof(CanRestore))] public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanWork)), NotifyPropertyChangedFor(nameof(NeedsRecovery))] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool HasWorkbook { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SelectionSummary))] public partial int SelectedCount { get; set; }
    [ObservableProperty] public partial string Summary { get; set; }
    [ObservableProperty] public partial string ExcelStatus { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))] public partial InventoryRow? Selected { get; set; }
    [ObservableProperty] public partial string DetailTitle { get; set; }
    [ObservableProperty] public partial string DetailText { get; set; }
    public bool CanWork => IsReady && !IsBusy;
    public bool CanRestore => !IsBusy;
    public bool HasSelection => Selected != null;
    public bool CanEditSelection => CanWork && SelectedCount == 1;
    public bool CanChangeQuantity => CanEditSelection && Selected?.Item.Archived == false;
    public bool CanLabelSelection => CanWork && SelectedCount > 0;
    public bool CanSync => CanWork && HasWorkbook;
    public bool CanOpenRecord => CanSync && CanEditSelection;
    public bool NeedsRecovery => !IsReady && IsError;
    public string SelectionSummary => SelectedCount == 0 ? "No items selected" : $"{SelectedCount:N0} selected";
    public bool IsRefreshingRows { get; private set; }
    public event Action? RowsRefreshing;
    public event Action? RowsRefreshed;
    partial void OnQueryChanged(string value) => Refresh();
    partial void OnFilterChanged(string value) => Refresh();
    partial void OnSortChanged(string value) => Refresh();
    partial void OnSelectedChanged(InventoryRow? value) { UpdateDetails(); UpdateCommands(); }
    partial void OnSelectedCountChanged(int value) { UpdateDetails(); UpdateCommands(); }
    partial void OnIsBusyChanged(bool value) => UpdateCommands();
    partial void OnIsReadyChanged(bool value) => UpdateCommands();
    partial void OnHasWorkbookChanged(bool value) => UpdateCommands();
    private void UpdateCommands()
    {
        foreach (var name in new[] { nameof(CanEditSelection), nameof(CanChangeQuantity), nameof(CanLabelSelection), nameof(CanSync), nameof(CanOpenRecord) }) OnPropertyChanged(name);
        SyncCommand.NotifyCanExecuteChanged(); SaveExcelCommand.NotifyCanExecuteChanged(); OpenExcelCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        if (IsReady) { Refresh(); return; }
        await RunAsync(async () => { await Service.Store.LoadAsync(); IsReady = true; }, "Inventory ready.");
    }

    public async Task<bool> RunAsync(Func<Task> action, string success)
    {
        if (IsBusy) return false;
        IsBusy = true; IsError = false;
        try { await action(); Status = success; return true; }
        catch (Exception ex) { IsError = true; Status = ex.Message; return false; }
        finally { IsBusy = false; Refresh(); }
    }
    public void Report(string message, bool error = false) { Status = message; IsError = error; }

    public void Refresh()
    {
        if (!IsReady) return;
        var document = Service.Store.Snapshot;
        var selectedId = Selected?.Item.Id;
        var today = DateOnly.FromDateTime(DateTime.Today);
        IEnumerable<InventoryItem> items = document.Items.Where(i => Filter switch
        {
            "All items" => true, "Archived" => i.Archived, "Below minimum" => i.LowStock,
            "None available" => !i.Archived && i.Available == 0, "Expired" => !i.Archived && i.ExpiryDate < today,
            "Expiring in 30 days" => !i.Archived && i.ExpiryDate >= today && i.ExpiryDate <= today.AddDays(30), _ => !i.Archived
        });
        if (!string.IsNullOrWhiteSpace(Query)) items = items.Where(i =>
            string.Join(" ", i.SKU, i.Name, i.Barcode, i.Category, i.Location, i.Bin, i.Supplier, i.Lot, i.SerialNumber,
                i.Notes, string.Join(" ", i.CustomFields.Values)).Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase));
        items = Sort switch { "Item ID" => items.OrderBy(i => i.SKU), "Available quantity" => items.OrderBy(i => i.Available), "Location" => items.OrderBy(i => i.Location).ThenBy(i => i.Bin), "Most recently updated" => items.OrderByDescending(i => i.UpdatedUtc), _ => items.OrderBy(i => i.Name) };
        RowsRefreshing?.Invoke(); IsRefreshingRows = true;
        try { Rows.Clear(); foreach (var item in items) Rows.Add(new InventoryRow(item)); Selected = Rows.FirstOrDefault(r => r.Item.Id == selectedId); }
        finally { IsRefreshingRows = false; RowsRefreshed?.Invoke(); }
        var active = document.Items.Where(i => !i.Archived).ToList();
        Summary = $"{Rows.Count:N0} shown / {active.Count:N0} active" + (active.Any(i => i.LowStock) ? $" · {active.Count(i => i.LowStock):N0} below minimum" : "");
        HasWorkbook = document.Excel != null;
        ExcelStatus = document.Excel is { } excel
            ? $"{Path.GetFileName(excel.Path)} · {excel.Sheet} · {(excel.LastSyncUtc is { } time ? $"Synced {time.ToLocalTime():g}" : "Not synced yet")}{(excel.AutoSync ? " · Auto-sync on" : "")}" : "Excel workbook: not connected";
        UpdateDetails();
    }
    private void UpdateDetails()
    {
        RecentMovements.Clear();
        if (SelectedCount > 1) { DetailTitle = $"{SelectedCount:N0} items selected"; DetailText = "Print labels, export, or archive this selection. Select a single item to edit it or update its quantity."; return; }
        if (Selected is not { } row) { DetailTitle = "Choose an item"; DetailText = "Select an item to see its quantity, location, notes, and activity."; return; }
        var i = row.Item;
        DetailTitle = i.Name;
        var lines = new List<string> { $"Item ID  {i.SKU}", $"Quantity  {i.Quantity:0.###} {i.Unit}" };
        void Add(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) lines.Add(label + "  " + value); }
        if (i.Reserved > 0) lines.Add($"Available  {i.Available:0.###} · Reserved  {i.Reserved:0.###}");
        Add("Location", row.Location); Add("Category", i.Category); Add("Barcode", i.Barcode.Length > 0 ? i.Barcode + " · " + i.BarcodeFormat : "");
        Add("Batch / lot", i.Lot); Add("Serial number", i.SerialNumber); Add("Expiry", i.ExpiryDate?.ToString("d", CultureInfo.CurrentCulture) ?? "");
        if (i.ReorderLevel > 0 || i.TargetStock > 0)
            lines.Add($"\nMinimum  {i.ReorderLevel:0.###} · Preferred  {i.TargetStock:0.###}\nTo reach target  {i.ReorderQuantity:0.###}");
        Add("Source / supplier", i.Supplier);
        if (i.UnitCost > 0) lines.Add($"Value per unit  {i.UnitCost:C} · Total value  {i.StockValue:C}");
        if (i.UnitPrice > 0) lines.Add($"Selling price  {i.UnitPrice:C}");
        if (i.Notes.Length > 0) lines.Add("\n" + i.Notes);
        DetailText = string.Join("\n", lines);
        if (i.CustomFields.Count > 0) DetailText += "\n\n" + string.Join("\n", i.CustomFields.Select(p => p.Key + "  " + p.Value));
        foreach (var movement in Service.Store.Snapshot.Movements.Where(m => m.ItemId == i.Id).OrderByDescending(m => m.TimestampUtc).Take(25)) RecentMovements.Add(new(movement));
    }

    public async Task SaveItemAsync(InventoryItem item)
    {
        await Service.Store.CommitAsync(d =>
        {
            InventoryEngine.Upsert(d, item);
            foreach (var key in item.CustomFields.Keys)
                if (!d.Columns.Any(c => c.Field.Equals("Custom." + key, StringComparison.OrdinalIgnoreCase)))
                    d.Columns.Add(new() { Field = "Custom." + key, Header = d.Columns.Any(c => c.Header.Equals(key, StringComparison.OrdinalIgnoreCase)) ? "Custom " + key : key });
        });
        await AutoSyncAsync();
    }
    public async Task ArchiveAsync(List<InventoryItem> items, bool archive)
    {
        await Service.Store.CommitAsync(d =>
        {
            foreach (var item in items) { item.Archived = archive; InventoryEngine.Upsert(d, item, archive ? "Archived" : "Restored"); }
        });
        await AutoSyncAsync();
    }
    public async Task StockAsync(Guid id, StockAction action, decimal amount, string reference, string note, string destination = "", string bin = "")
    {
        await Service.Store.CommitAsync(d => InventoryEngine.Move(d, id, action, amount, reference, note, destination, bin));
        await AutoSyncAsync();
    }
    private async Task AutoSyncAsync()
    {
        if (Service.Store.Snapshot.Excel?.AutoSync == true)
            try { await Service.SyncAsync(); }
            catch (Exception ex) { throw new IOException("Saved locally. Automatic Excel sync needs attention: " + ex.Message, ex); }
    }
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync() => await RunAsync(() => Service.SyncAsync(), "Excel and inventory synchronized.");
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SaveExcelAsync() => await RunAsync(() => Service.SyncAsync(saveOnly: true), "Saved directly to the connected Excel workbook.");
    [RelayCommand(CanExecute = nameof(CanOpenRecord))]
    private async Task OpenExcelAsync()
    {
        if (Selected is not { } selected) { Report("Select an inventory record first.", true); return; }
        var link = Service.Store.Snapshot.Excel;
        if (link == null) { Report("Connect an Excel workbook first.", true); return; }
        await RunAsync(() => ExcelDesktopService.OpenRecordAsync(link, selected.SKU), "Record opened in Excel.");
    }
}
