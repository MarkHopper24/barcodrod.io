using ClosedXML.Excel;
using System.Globalization;
using System.Security.Cryptography;

namespace barcodrod.io.Core.Inventory;

public sealed record WorkbookSnapshot(InventoryTable Table, string Revision);

public static class InventoryWorkbook
{
    public static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a full path to an .xlsx workbook. Macro-enabled and legacy workbooks are not modified.");
    }
    public static void ValidateSheet(string sheet, int headerRow)
    {
        if (string.IsNullOrWhiteSpace(sheet) || sheet.Length > 31 || sheet.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) >= 0 || sheet.StartsWith('\'') || sheet.EndsWith('\''))
            throw new InvalidDataException("Enter a valid Excel worksheet name (1–31 characters).");
        if (headerRow < 1 || headerRow > 1000) throw new InvalidDataException("Header row must be between 1 and 1,000.");
    }
    public static List<string> Sheets(string path)
    {
        ValidatePath(path); using var workbook = new XLWorkbook(path);
        return workbook.Worksheets.Select(s => s.Name).ToList();
    }
    public static WorkbookSnapshot Read(string path, string sheet, int headerRow, IReadOnlyList<ColumnMapping>? mapping = null, CultureInfo? numberCulture = null)
    {
        ValidatePath(path); ValidateSheet(sheet, headerRow);
        if (new FileInfo(path).Length > 100 * 1024 * 1024) throw new InvalidDataException("Choose a workbook smaller than 100 MB.");
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes); using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet(sheet, out var ws)) throw new InvalidDataException($"Worksheet '{sheet}' was not found.");
        var lastColumn = ws.Row(headerRow).LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
        var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? headerRow;
        if (lastColumn == 0) throw new InvalidDataException("The selected header row is empty.");
        if (lastColumn > 150 || lastRow - headerRow > 50000) throw new InvalidDataException("Import is limited to 50,000 records and 150 columns.");
        var headers = Enumerable.Range(1, lastColumn).Select(c => ws.Cell(headerRow, c).GetString().Trim()).ToList();
        if (headers.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Every input column needs a nonempty header.");
        var numericHeaders = (mapping ?? InventoryFields.DefaultColumns()).Where(c => c.Included && InventoryFields.Numeric.Contains(c.Field)).Select(c => c.Header).ToHashSet(StringComparer.OrdinalIgnoreCase);
        numberCulture ??= CultureInfo.InvariantCulture;
        string ReadCell(IXLCell cell, string header) => cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : (cell.DataType == XLDataType.Number || cell.HasFormula) && numericHeaders.Contains(header) ? cell.GetDouble().ToString("R", numberCulture)
            : cell.GetFormattedString(CultureInfo.InvariantCulture);
        var rows = Enumerable.Range(headerRow + 1, lastRow - headerRow).Select(r => Enumerable.Range(1, lastColumn).Select(c => ReadCell(ws.Cell(r, c), headers[c - 1])).ToList())
            .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        return new(new(headers, rows), Revision(bytes));
    }

    public static void Write(string path, string sheet, int headerRow, IEnumerable<InventoryItem> inventory,
        IEnumerable<ColumnMapping> mapping, string? expectedRevision = null, IEnumerable<StockMovement>? movements = null)
    {
        ValidatePath(path); ValidateSheet(sheet, headerRow); InventoryFields.ValidateColumns(mapping);
        var items = inventory.ToList(); InventoryEngine.Validate(new InventoryDocument { Items = items });
        var columns = mapping.Where(c => c.Included).ToList();
        var exists = File.Exists(path);
        var bytes = exists ? File.ReadAllBytes(path) : null;
        if (expectedRevision != null && (bytes == null || Revision(bytes) != expectedRevision)) throw new IOException("Excel changed during synchronization. Try Sync again.");
        using var input = bytes == null ? null : new MemoryStream(bytes);
        using var book = input == null ? new XLWorkbook() : new XLWorkbook(input);
        var ws = book.TryGetWorksheet(sheet, out var found) ? found : book.AddWorksheet(sheet);
        var headerCount = ws.Row(headerRow).LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 1; c <= headerCount; c++)
        {
            var header = ws.Cell(headerRow, c).GetString().Trim();
            if (header.Length > 0 && !positions.TryAdd(header, c)) throw new InvalidDataException($"Duplicate Excel header: {header}.");
        }
        foreach (var column in columns)
        {
            if (!positions.ContainsKey(column.Header)) { positions[column.Header] = ++headerCount; ws.Cell(headerRow, headerCount).Value = column.Header; }
        }
        var skuPosition = positions[columns.Single(c => c.Field == "SKU").Header];
        var rows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? headerRow;
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var sku = ws.Cell(r, skuPosition).GetFormattedString(CultureInfo.InvariantCulture).Trim();
            if (sku.Length > 0 && !rows.TryAdd(sku, r)) throw new InvalidDataException($"Duplicate Excel SKU: {sku}.");
        }
        foreach (var item in items)
        {
            var row = rows.TryGetValue(item.SKU, out var existingRow) ? existingRow : ++lastRow;
            foreach (var column in columns)
            {
                var cell = ws.Cell(row, positions[column.Header]); var value = InventoryFields.Get(item, column.Field);
                if (cell.HasFormula)
                {
                    var formulaValue = InventoryFields.Numeric.Contains(column.Field)
                        ? cell.GetDouble().ToString("R", CultureInfo.InvariantCulture) : cell.GetFormattedString(CultureInfo.InvariantCulture);
                    var computed = item.Copy(); InventoryFields.Set(computed, column.Field, formulaValue, CultureInfo.InvariantCulture);
                    if (InventoryFields.Get(computed, column.Field) != value) throw new InvalidDataException($"{item.SKU} / {column.Header} contains a formula. Exclude it from the mapping before changing it in the app.");
                    continue;
                }
                if (InventoryFields.Numeric.Contains(column.Field)) cell.Value = decimal.Parse(value, CultureInfo.InvariantCulture);
                else if (column.Field == "Archived") cell.Value = item.Archived;
                else if (column.Field == "ExpiryDate" && item.ExpiryDate.HasValue) { cell.Value = item.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; }
                else { cell.Style.NumberFormat.Format = "@"; cell.Value = value; }
            }
        }
        if (!exists)
        {
            ws.Row(headerRow).Style.Font.Bold = true; ws.SheetView.FreezeRows(headerRow);
            ws.Columns(1, headerCount).Width = 18; ws.Column(positions[columns.Single(c => c.Field == "Name").Header]).Width = 30;
            if (lastRow > headerRow) ws.Range(headerRow, 1, lastRow, headerCount).SetAutoFilter();
            if (movements != null)
            {
                var log = book.AddWorksheet(sheet.Equals("Stock movements", StringComparison.OrdinalIgnoreCase) ? "Movement log" : "Stock movements");
                string[] headings = ["Timestamp UTC", "SKU", "Action", "Quantity change", "Reserved change", "Balance", "Reference", "Note", "From", "To"];
                for (var c = 0; c < headings.Length; c++) log.Cell(1, c + 1).Value = headings[c];
                var r = 2;
                foreach (var m in movements)
                {
                    log.Cell(r, 1).Value = m.TimestampUtc.UtcDateTime; log.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    log.Cell(r, 2).Value = m.SKU; log.Cell(r, 3).Value = m.Action; log.Cell(r, 4).Value = m.QuantityChange;
                    log.Cell(r, 5).Value = m.ReservedChange; log.Cell(r, 6).Value = m.Balance; log.Cell(r, 7).Value = m.Reference;
                    log.Cell(r, 8).Value = m.Note; log.Cell(r, 9).Value = m.FromLocation; log.Cell(r, 10).Value = m.ToLocation; r++;
                }
                log.Row(1).Style.Font.Bold = true; log.Columns().Width = 22; log.SheetView.FreezeRows(1);
            }
        }
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            book.SaveAs(temporary);
            // Recheck immediately before replacing. Excel's exclusive handle also prevents replacement while open.
            if (bytes != null && Revision(File.ReadAllBytes(path)) != Revision(bytes)) throw new IOException("The workbook changed during save. Try Sync again.");
            if (exists) File.Replace(temporary, path, path + ".barcodrod.bak"); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
