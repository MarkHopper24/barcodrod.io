using barcodrod.io.Core.Inventory;
using barcodrod.io.Helpers;
using System.Globalization;

namespace barcodrod.io.Services;

public sealed class InventoryService
{
    public InventoryStore Store { get; } = new(InventoryFolder());
    private static string InventoryFolder()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--inventory-data-directory");
        if (index < 0) return AppPaths.LocalDataFolder;
        if (index + 1 >= args.Length || !Path.IsPathFullyQualified(args[index + 1])) throw new ArgumentException("--inventory-data-directory requires an absolute folder path.");
        return Path.GetFullPath(args[index + 1]);
    }
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    public async Task<List<string>> ExcelSheetsAsync(string path) => await ExcelDesktopService.TrySheetsAsync(path) ?? await Task.Run(() => InventoryWorkbook.Sheets(path));
    public async Task<WorkbookSnapshot> ReadExcelAsync(WorkbookLink link)
    {
        InventoryWorkbook.ValidatePath(link.Path); InventoryWorkbook.ValidateSheet(link.Sheet, link.HeaderRow);
        var culture = CultureInfo.GetCultureInfo(link.NumberCulture);
        return await ExcelDesktopService.TryReadAsync(link.Path, link.Sheet, link.HeaderRow, link.Columns, culture)
            ?? await Task.Run(() => InventoryWorkbook.Read(link.Path, link.Sheet, link.HeaderRow, link.Columns, culture));
    }

    public async Task SyncAsync(bool saveOnly = false)
    {
        await _syncGate.WaitAsync();
        try
        {
            var document = Store.Snapshot; var link = document.Excel ?? throw new InvalidOperationException("Connect an Excel workbook first.");
            var snapshot = await ReadExcelAsync(link);
            var incoming = await Task.Run(() => InventoryTransfer.Parse(snapshot.Table, link.Columns,
                CultureInfo.GetCultureInfo(link.NumberCulture), document.Items));
            var plan = InventoryTransfer.PlanSync(document, incoming);
            if (plan.Conflicts.Count > 0) throw new InvalidDataException("Sync stopped. Resolve these values in Excel or the app, then retry:\n" + string.Join("\n", plan.Conflicts.Take(12)) + (plan.Conflicts.Count > 12 ? $"\n… and {plan.Conflicts.Count - 12} more." : ""));
            if (saveOnly && plan.Items.Any(i =>
                document.Items.FirstOrDefault(l => l.Id == i.Id) is not { } local ||
                !InventoryTransfer.Equal(InventoryFields.Values(local, link.Columns), InventoryFields.Values(i, link.Columns))))
                throw new InvalidOperationException("Excel has new edits. Use Sync both ways to import those changes before saving.");
            // Persist the merged local snapshot first, so a failed Excel save cannot lose imported edits.
            await Store.CommitAsync(d => InventoryTransfer.ApplyItems(d, plan.Items, "Excel sync"));
            if (snapshot.Revision.StartsWith("excel:", StringComparison.Ordinal))
            {
                if (!await ExcelDesktopService.TryWriteAsync(link, plan.Items, snapshot.Revision)) throw new IOException("The Excel workbook was closed during sync. Retry Sync.");
            }
            else await Task.Run(() => InventoryWorkbook.Write(link.Path, link.Sheet, link.HeaderRow, plan.Items, link.Columns, snapshot.Revision));
            await Store.CommitAsync(d =>
            {
                if (d.Excel == null || d.Excel.Path != link.Path) throw new InvalidOperationException("The workbook connection changed during sync.");
                InventoryTransfer.MarkSynced(d.Excel, plan.Items);
            });
        }
        finally { _syncGate.Release(); }
    }
}
