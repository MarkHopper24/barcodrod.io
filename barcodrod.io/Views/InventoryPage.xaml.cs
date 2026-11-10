using barcodrod.io.Core.Inventory;
using barcodrod.io.Services;
using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace barcodrod.io.Views;

public sealed partial class InventoryPage : Page
{
    public InventoryViewModel ViewModel { get; } = App.GetService<InventoryViewModel>();
    private bool _dialogOpen;
    private bool _restoringSelection;
    private HashSet<Guid> _selectionBeforeRefresh = [];
    public InventoryPage()
    {
        InitializeComponent(); NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.Status) or nameof(ViewModel.IsError)) InventoryStatus.Severity = ViewModel.IsError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        };
        ViewModel.RowsRefreshing += () => _selectionBeforeRefresh = InventoryList.SelectedItems.OfType<InventoryRow>().Select(r => r.Item.Id).ToHashSet();
        ViewModel.RowsRefreshed += () =>
        {
            _restoringSelection = true;
            try
            {
                foreach (var row in ViewModel.Rows.Where(r => _selectionBeforeRefresh.Contains(r.Item.Id))) InventoryList.SelectedItems.Add(row);
            }
            finally { _restoringSelection = false; UpdateSelection(true); }
        };
        ViewModel.Rows.CollectionChanged += (_, _) => EmptyText.Visibility = ViewModel.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void PageLoaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();
    private void PageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 1000; DetailColumn.Width = new GridLength(wide ? 320 : 0); DetailPane.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        ToolsScroller.MaxHeight = Math.Clamp(e.NewSize.Height * 0.35, 160, 280);
        var narrow = e.NewSize.Width < 720;
        SearchColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
        Grid.SetRow(SearchPanel, 0); Grid.SetColumn(SearchPanel, 0); Grid.SetColumnSpan(SearchPanel, narrow ? 2 : 1);
        Grid.SetRow(FilterBox, narrow ? 1 : 0); Grid.SetColumn(FilterBox, narrow ? 0 : 1);
        Grid.SetRow(SortBox, narrow ? 1 : 0); Grid.SetColumn(SortBox, narrow ? 1 : 2);
        SortColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(ExcelTools, narrow ? 1 : 0); Grid.SetColumn(ExcelTools, narrow ? 0 : 1);
        Grid.SetColumnSpan(ScanTools, narrow ? 2 : 1); Grid.SetColumnSpan(ExcelTools, narrow ? 2 : 1);
    }
    private void ItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ViewModel.IsRefreshingRows && !_restoringSelection) UpdateSelection();
    }
    private void UpdateSelection(bool keepCurrent = false)
    {
        var rows = InventoryList.SelectedItems.OfType<InventoryRow>().ToList();
        ViewModel.SelectedCount = rows.Count;
        ViewModel.Selected = keepCurrent && rows.Contains(ViewModel.Selected!) ? ViewModel.Selected : rows.LastOrDefault();
    }
    private void RevealItem(Guid id)
    {
        ViewModel.Query = ""; ViewModel.Filter = "Active items";
        InventoryList.SelectedItems.Clear();
        if (ViewModel.Rows.FirstOrDefault(r => r.Item.Id == id) is { } row) { InventoryList.SelectedItems.Add(row); InventoryList.ScrollIntoView(row); }
    }
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void RecordContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not InventoryRow row) return;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(args.ItemContainer, "InventoryItem_" + row.Item.Id.ToString("N"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, $"{row.SKU}: {row.Name}, {row.OnHand} on hand, {row.Available} available, {row.State}");
    }
    private InventoryItem? Current() { if (ViewModel.Selected == null) ViewModel.Report("Select an inventory item first.", true); return ViewModel.Selected?.Item; }
    private async Task WithDialogAsync(Func<Task> action, bool allowRecovery = false)
    {
        if (_dialogOpen || ViewModel.IsBusy || (!ViewModel.IsReady && !allowRecovery)) return;
        _dialogOpen = true;
        try { await action(); } catch (Exception ex) { ViewModel.Report(ex.Message, true); } finally { _dialogOpen = false; }
    }
    private async void NewItem(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var document = ViewModel.Service.Store.Snapshot;
        var item = await InventoryDialogs.EditAsync(XamlRoot, InventoryEngine.CreateItem(document), false, document);
        if (item != null)
        {
            await ViewModel.RunAsync(() => ViewModel.SaveItemAsync(item), $"Added {item.SKU}.");
            if (ViewModel.Service.Store.Snapshot.Items.Any(i => i.Id == item.Id)) RevealItem(item.Id);
        }
    });
    private async void EditItem(object sender, RoutedEventArgs e) => await EditCurrentAsync();
    private async void ItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await EditCurrentAsync();
    private async Task EditCurrentAsync() => await WithDialogAsync(async () =>
    {
        if (!ViewModel.CanEditSelection) return;
        if (Current() is not { } current) return;
        var item = await InventoryDialogs.EditAsync(XamlRoot, current, ViewModel.Service.Store.Snapshot.Excel != null, ViewModel.Service.Store.Snapshot);
        if (item != null) await ViewModel.RunAsync(() => ViewModel.SaveItemAsync(item), $"Saved {item.SKU}.");
    });
    private async void RecordStock(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        if (Current() is not { } current) return;
        if (await InventoryDialogs.StockAsync(XamlRoot, current) is not { } movement) return;
        await ViewModel.RunAsync(() => ViewModel.StockAsync(current.Id, movement.Action, movement.Amount, movement.Reference, movement.Note, movement.Destination, movement.Bin), $"{InventoryActions.Name(movement.Action)} recorded for {current.SKU}.");
    });
    private async void ScanSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!ViewModel.CanWork || _dialogOpen) return;
        var match = InventoryEngine.Lookup(ViewModel.Service.Store.Snapshot, args.QueryText);
        if (match == null && ViewModel.ScanAction == "Look up" && !string.IsNullOrWhiteSpace(args.QueryText) && ViewModel.Rows.Count > 0)
        {
            if (ViewModel.Rows.Count == 1)
            {
                InventoryList.SelectedItems.Clear(); InventoryList.SelectedItems.Add(ViewModel.Rows[0]); InventoryList.ScrollIntoView(ViewModel.Rows[0]);
            }
            ViewModel.Report($"{ViewModel.Rows.Count:N0} matching item(s). Select an item for details, quantity changes, or labels.");
            return;
        }
        if (match == null) { ViewModel.Report("No active item matches this item ID or barcode. Check the code or add a new item.", true); return; }
        if (ViewModel.ScanAction != "Look up")
        {
            if (!double.IsFinite(ViewModel.ScanQuantity) || ViewModel.ScanQuantity <= 0 || ViewModel.ScanQuantity > (double)decimal.MaxValue) { ViewModel.Report("Enter a valid positive scan quantity.", true); return; }
            if (!await ViewModel.RunAsync(() => ViewModel.StockAsync(match.Id, ViewModel.ScanAction == "Add quantity" ? StockAction.Receive : StockAction.Issue, (decimal)ViewModel.ScanQuantity, ViewModel.ScanReference, "Barcode scan"), $"{ViewModel.ScanAction}: {match.SKU} · {ViewModel.ScanQuantity:0.###}")) return;
        }
        ViewModel.Filter = "Active items"; ViewModel.Query = "";
        var row = ViewModel.Rows.FirstOrDefault(i => i.Item.Id == match.Id); InventoryList.SelectedItems.Clear(); if (row != null) { InventoryList.SelectedItems.Add(row); InventoryList.ScrollIntoView(row); }
        if (ViewModel.ScanAction == "Look up") ViewModel.Report($"Found {match.SKU} · {match.Name}.");
        SearchBox.Focus(FocusState.Programmatic);
    }
    private void SelectAll(object sender, RoutedEventArgs e) => InventoryList.SelectAll();
    private void ClearSelection(object sender, RoutedEventArgs e) => InventoryList.SelectedItems.Clear();
    private async void ShowDetails(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        if (Current() == null) return;
        var activity = string.Join("\n\n", ViewModel.RecentMovements.Select(m => m.Summary + "\n" + m.Detail));
        var detail = ViewModel.DetailText + (activity.Length > 0 ? "\n\nRecent activity\n\n" + activity : "\n\nNo activity recorded yet.");
        var dialog = InventoryDialogs.Dialog(XamlRoot, ViewModel.DetailTitle, new ScrollViewer { Content = InventoryDialogs.Identify(InventoryDialogs.Text(detail), "InventoryDetailContent"), MaxHeight = 480 });
        dialog.PrimaryButtonText = ""; dialog.CloseButtonText = "Close"; await dialog.ShowAsync();
    });
    private async void PrintLabels(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var selected = InventoryList.SelectedItems.OfType<InventoryRow>().Select(i => i.Item.Copy()).ToList();
        if (selected.Count == 0) { ViewModel.Report("Select one or more items to print labels.", true); return; }
        await LabelPrintWindow.OpenAsync(selected, ViewModel.Service.Store.Snapshot.Labels, async options => await ViewModel.Service.Store.CommitAsync(d => d.Labels = options));
    });
    private async void ArchiveItems(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var selected = InventoryList.SelectedItems.OfType<InventoryRow>().Select(r => r.Item.Copy()).ToList();
        if (selected.Count == 0) { ViewModel.Report("Select items to archive or restore.", true); return; }
        var archive = selected.Any(i => !i.Archived);
        if (archive && selected.Any(i => i.Reserved > 0)) throw new InvalidOperationException("Release reserved stock before archiving an item.");
        if (!await InventoryDialogs.ConfirmAsync(XamlRoot, archive ? "Archive selected items?" : "Restore selected items?", $"{selected.Count} item(s): {string.Join(", ", selected.Take(5).Select(i => i.SKU))}. Archived records and their movement history are retained.", archive ? "Archive" : "Restore")) return;
        await ViewModel.RunAsync(() => ViewModel.ArchiveAsync(selected, archive), archive ? "Items archived." : "Items restored.");
    });
    private static void Owner(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
    private static async Task<StorageFile?> PickOpenAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; foreach (var ext in extensions) picker.FileTypeFilter.Add(ext); Owner(picker); return await picker.PickSingleFileAsync();
    }
    private static async Task<StorageFile?> PickSaveAsync(string name, params (string Name, string Extension)[] types)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = name }; foreach (var type in types) picker.FileTypeChoices.Add(type.Name, new List<string> { type.Extension }); Owner(picker); return await picker.PickSaveFileAsync();
    }
    private async void ConfigureColumns(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var document = ViewModel.Service.Store.Snapshot;
        var mappings = document.Columns.ToList();
        foreach (var field in document.Items.SelectMany(i => i.CustomFields.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!mappings.Any(c => c.Field.Equals("Custom." + field, StringComparison.OrdinalIgnoreCase))) mappings.Add(new() { Field = "Custom." + field, Header = field });
        if (await InventoryTransferDialogs.ColumnsAsync(XamlRoot, mappings) is { } columns)
            await ViewModel.RunAsync(() => ViewModel.Service.Store.CommitAsync(d => d.Columns = columns), "Default import / export columns saved. Reconnect Excel to change its mapping.");
    });
    private async void ImportFile(object sender, RoutedEventArgs e) => await ImportOrConnectAsync(false);
    private async void ConnectExcel(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var document = ViewModel.Service.Store.Snapshot;
        if (document.Excel != null)
        {
            var panel = new StackPanel { Spacing = 12, Width = 420 }; panel.Children.Add(InventoryDialogs.Text(document.Excel.Path + "\nWorksheet: " + document.Excel.Sheet));
            var auto = InventoryDialogs.Identify(new ToggleSwitch { Header = "Sync after every saved inventory change", IsOn = document.Excel.AutoSync }, "ExcelAutoSync"); panel.Children.Add(auto);
            var dialog = InventoryDialogs.Dialog(XamlRoot, "Excel connection", panel, "Save settings"); dialog.SecondaryButtonText = "Disconnect";
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) await ViewModel.RunAsync(() => ViewModel.Service.Store.CommitAsync(d => d.Excel!.AutoSync = auto.IsOn), "Excel sync settings saved.");
            if (result == ContentDialogResult.Secondary) await ViewModel.RunAsync(() => ViewModel.Service.Store.CommitAsync(d => d.Excel = null), "Excel disconnected. Inventory records are retained.");
            return;
        }
        var choose = InventoryDialogs.Dialog(XamlRoot, "Connect Excel", InventoryDialogs.Text("Create a linked workbook from this inventory, or connect an existing workbook and review its imported records."), "Connect existing");
        choose.SecondaryButtonText = "Create linked workbook";
        var choice = await choose.ShowAsync();
        if (choice == ContentDialogResult.Primary) await TransferAsync(true);
        else if (choice == ContentDialogResult.Secondary) await CreateLinkedWorkbookAsync();
    });
    private async Task CreateLinkedWorkbookAsync()
    {
        var file = await PickSaveAsync("Inventory", ("Excel workbook", ".xlsx")); if (file == null) return;
        await ViewModel.RunAsync(async () =>
        {
            var document = ViewModel.Service.Store.Snapshot;
            var staging = Path.Combine(Path.GetDirectoryName(file.Path)!, "." + Guid.NewGuid().ToString("N") + ".xlsx");
            try { await Task.Run(() => InventoryWorkbook.Write(staging, "Inventory", 1, document.Items, document.Columns, movements: document.Movements)); File.Move(staging, file.Path, true); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            var link = new WorkbookLink { Path = file.Path, Columns = InventoryJson.Copy(document.Columns) };
            InventoryTransfer.MarkSynced(link, document.Items);
            await ViewModel.Service.Store.CommitAsync(d => d.Excel = link);
        }, "Created and connected an Excel workbook. Save to Excel or Sync both ways to keep it up to date.");
    }
    private async Task ImportOrConnectAsync(bool connect) => await WithDialogAsync(() => TransferAsync(connect));
    private async Task TransferAsync(bool connect)
    {
        var file = connect ? await PickOpenAsync(".xlsx") : await PickOpenAsync(".xlsx", ".csv", ".tsv"); if (file == null) return;
        var document = ViewModel.Service.Store.Snapshot; var isExcel = file.FileType.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
        var sheets = isExcel ? await ViewModel.Service.ExcelSheetsAsync(file.Path) : null;
        var options = await InventoryTransferDialogs.OptionsAsync(XamlRoot, connect ? "Connect Excel workbook" : "Import inventory", file.Path, sheets, connecting: connect); if (options == null) return;
        var link = new WorkbookLink { Path = file.Path, Sheet = options.Sheet, HeaderRow = options.HeaderRow, NumberCulture = options.Culture, AutoSync = options.AutoSync };
        InventoryTable table; string? revision = null;
        if (isExcel) { var snapshot = await ViewModel.Service.ReadExcelAsync(link); table = snapshot.Table; revision = snapshot.Revision; }
        else table = await Task.Run(async () => InventoryCsv.Read(await File.ReadAllTextAsync(file.Path), options.Separator));
        var columns = await InventoryTransferDialogs.ColumnsAsync(XamlRoot, InventoryTransferDialogs.Suggest(table, document.Columns), table.Headers); if (columns == null) return;
        link.Columns = columns;
        if (isExcel)
        {
            var normalized = await ViewModel.Service.ReadExcelAsync(link); if (normalized.Revision != revision) throw new IOException("The workbook changed while choosing columns. Start the import again.");
            table = normalized.Table;
        }
        var incoming = await Task.Run(() => InventoryTransfer.Parse(table, columns, CultureInfo.GetCultureInfo(options.Culture), document.Items));
        var plan = InventoryTransfer.PlanImport(document, incoming, columns);
        var preview = $"{plan.Added} new · {plan.Updated} updated · {plan.Unchanged} unchanged\n\n{string.Join("\n", incoming.Take(5).Select(i => $"{i.SKU} · {i.Name} · {i.Quantity:0.###} {i.Unit}"))}\n\nMatching item IDs (SKU) use the imported values. Other local items are retained.";
        if (connect) preview += "\n\nConnect and sync also writes local-only items to this workbook. Conflicting later edits stop sync until resolved.";
        if (!await InventoryDialogs.ConfirmAsync(XamlRoot, "Review inventory import", preview, connect ? "Connect and sync" : "Import")) return;
        if (isExcel)
        {
            var latest = await ViewModel.Service.ReadExcelAsync(link); if (latest.Revision != revision) throw new IOException("The workbook changed while the preview was open. Start the import again.");
        }
        await ViewModel.RunAsync(async () =>
        {
            await ViewModel.Service.Store.CommitAsync(d =>
            {
                InventoryTransfer.ApplyItems(d, plan.Items, "Imported"); d.Columns = columns;
                if (connect) { InventoryTransfer.MarkSynced(link, incoming); link.LastSyncUtc = null; d.Excel = link; }
            });
            if (connect || ViewModel.Service.Store.Snapshot.Excel?.AutoSync == true) await ViewModel.Service.SyncAsync();
        }, connect ? "Excel connected and synchronized." : $"Imported {plan.Added} new and {plan.Updated} updated items.");
    }
    private async void ExportFile(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var document = ViewModel.Service.Store.Snapshot;
        var selected = InventoryList.SelectedItems.OfType<InventoryRow>().Select(r => r.Item).ToList();
        var items = selected.Count > 0 ? selected : ViewModel.Rows.Select(r => r.Item).ToList();
        if (items.Count == 0) throw new InvalidOperationException("No items to export in this view.");
        var columns = await InventoryTransferDialogs.ColumnsAsync(XamlRoot, document.Columns); if (columns == null) return;
        var file = await PickSaveAsync("Inventory", ("Excel workbook", ".xlsx"), ("CSV", ".csv"), ("Tab-separated values", ".tsv")); if (file == null) return;
        await ViewModel.RunAsync(async () =>
        {
            if (file.FileType.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                // The Windows save picker creates a zero-byte destination. A fresh export must replace it.
                var staging = Path.Combine(Path.GetDirectoryName(file.Path)!, "." + Guid.NewGuid().ToString("N") + ".xlsx");
                try { await Task.Run(() => InventoryWorkbook.Write(staging, "Inventory", 1, items, columns, movements: document.Movements.Where(m => items.Any(i => i.Id == m.ItemId)))); File.Move(staging, file.Path, true); }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            }
            else await File.WriteAllTextAsync(file.Path, InventoryCsv.Write(items, columns, file.FileType == ".tsv" ? '\t' : ','), new UTF8Encoding(true));
        }, $"Exported {items.Count} item(s) to {file.Name}. Select items to export a subset; otherwise the current filtered view is exported.");
    });
    private async void ExportMovements(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var file = await PickSaveAsync("Stock movements", ("CSV", ".csv")); if (file == null) return;
        var movements = ViewModel.Service.Store.Snapshot.Movements;
        string Quote(string s) => "\"" + (s.Length > 0 && "=+-@".Contains(s.TrimStart().FirstOrDefault()) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        var csv = "Timestamp UTC,SKU,Action,Quantity change,Reserved change,Balance,Reference,Note,From location,To location\r\n" +
            string.Join("\r\n", movements.Select(m => string.Join(",", Quote(m.TimestampUtc.ToString("O")), Quote(m.SKU), Quote(m.Action), InventoryFields.Number(m.QuantityChange), InventoryFields.Number(m.ReservedChange), InventoryFields.Number(m.Balance), Quote(m.Reference), Quote(m.Note), Quote(m.FromLocation), Quote(m.ToLocation))));
        await ViewModel.RunAsync(() => File.WriteAllTextAsync(file.Path, csv, new UTF8Encoding(true)), $"Exported {movements.Count} stock movements.");
    });
    private async void BackupInventory(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var file = await PickSaveAsync("Inventory backup " + DateTime.Now.ToString("yyyy-MM-dd"), ("Inventory backup", ".json")); if (file == null) return;
        await ViewModel.RunAsync(() => File.WriteAllTextAsync(file.Path, JsonSerializer.Serialize(ViewModel.Service.Store.Snapshot, InventoryJson.Options)), "Inventory backup saved, including movements and configurations.");
    });
    private async void RestoreInventory(object sender, RoutedEventArgs e) => await WithDialogAsync(async () =>
    {
        var file = await PickOpenAsync(".json"); if (file == null) return;
        if (new FileInfo(file.Path).Length > 100 * 1024 * 1024) throw new InvalidDataException("Choose an inventory backup smaller than 100 MB.");
        var restored = JsonSerializer.Deserialize<InventoryDocument>(await File.ReadAllTextAsync(file.Path), InventoryJson.Options) ?? throw new InvalidDataException("The backup is empty.");
        InventoryEngine.Validate(restored);
        if (!await InventoryDialogs.ConfirmAsync(XamlRoot, "Restore inventory backup?", $"Replace the current inventory with {restored.Items.Count} items and {restored.Movements.Count} movements from {file.Name}? The previous inventory is kept as inventory.json.bak. Excel is disconnected on restore to avoid automatic workbook changes.", "Restore")) return;
        await ViewModel.RunAsync(async () => { await ViewModel.Service.Store.RestoreAsync(restored); ViewModel.IsReady = true; }, "Inventory backup restored. Connect Excel when ready.");
    }, allowRecovery: true);
}
