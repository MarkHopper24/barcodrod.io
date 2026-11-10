using barcodrod.io.Core.Inventory;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace barcodrod.io.Services;

/// <summary>Optional desktop Excel integration. File-based .xlsx support works without Office.</summary>
public static class ExcelDesktopService
{
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    public static Task<T> OnStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private sealed class ComObjects : IDisposable
    {
        private readonly List<object> _objects = [];
        public dynamic Keep(object value) { _objects.Add(value); return value; }
        public void Dispose() { foreach (var o in _objects.AsEnumerable().Reverse()) Release(o); }
    }
    private static void Release(object? o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }

    private static object? FindWorkbook(string path, ComObjects objects)
    {
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out var rot)); objects.Keep(rot);
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out var context)); objects.Keep(context);
        rot.EnumRunning(out var enumerator); objects.Keep(enumerator);
        var monikers = new IMoniker[1];
        while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(context, null, out var name);
                if (!name.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
                rot.GetObject(monikers[0], out var result);
                return objects.Keep(result);
            }
            catch (COMException) { /* An unrelated ROT entry may be inaccessible. */ }
            finally { Release(monikers[0]); }
        }
        return null;
    }

    public static Task<List<string>?> TrySheetsAsync(string path) => OnStaAsync<List<string>?>(() =>
    {
        using var objects = new ComObjects(); dynamic? book = FindWorkbook(path, objects); if (book == null) return null;
        dynamic sheets = objects.Keep(book.Worksheets); var names = new List<string>();
        for (var i = 1; i <= (int)sheets.Count; i++) { dynamic sheet = sheets.Item[i]; try { names.Add((string)sheet.Name); } finally { Release(sheet); } }
        return names;
    });

    private static string CellText(dynamic sheet, int row, int column, object? raw, string field)
    {
        if (raw == null) return "";
        if (field is "SKU" or "Barcode")
        {
            object? cell = null; object? cells = null;
            try { cells = sheet.Cells; cell = ((dynamic)cells).Item[row, column]; return Convert.ToString(((dynamic)cell).Text, CultureInfo.InvariantCulture) ?? ""; }
            finally { Release(cell); Release(cells); }
        }
        return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
    }

    private static WorkbookSnapshot Snapshot(dynamic sheet, int headerRow, ComObjects objects, List<ColumnMapping> mappings, CultureInfo culture)
    {
        dynamic used = objects.Keep(sheet.UsedRange); dynamic usedRows = objects.Keep(used.Rows); dynamic usedColumns = objects.Keep(used.Columns);
        var lastRow = (int)used.Row + (int)usedRows.Count - 1; var lastCol = (int)used.Column + (int)usedColumns.Count - 1;
        if (lastRow - headerRow > 50000 || lastCol > 150) throw new InvalidDataException("Excel range is limited to 50,000 records and 150 columns.");
        dynamic cells = objects.Keep(sheet.Cells); dynamic first = objects.Keep(cells.Item[headerRow, 1]); dynamic last = objects.Keep(cells.Item[Math.Max(headerRow, lastRow), Math.Max(1, lastCol)]);
        dynamic range = objects.Keep(sheet.Range[first, last]); object values = range.Value2; object formulas = range.Formula;
        object? At(object data, int r, int c) => data is object[,] array ? array[r, c] : (r == 1 && c == 1 ? data : null);
        var headers = Enumerable.Range(1, Math.Max(1, lastCol)).Select(c => Convert.ToString(At(values, 1, c), CultureInfo.InvariantCulture)?.Trim() ?? "").ToList();
        while (headers.Count > 0 && headers[^1].Length == 0) headers.RemoveAt(headers.Count - 1);
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("The selected Excel header row needs a name for each column.");
        var dataRows = new List<List<string>>(); var fingerprint = new StringBuilder();
        for (var r = 1; r <= Math.Max(1, lastRow - headerRow + 1); r++)
        {
            var row = new List<string>();
            for (var c = 1; c <= headers.Count; c++)
            {
                var value = At(values, r, c);
                var field = mappings.FirstOrDefault(m => m.Included && m.Header.Equals(headers[c - 1], StringComparison.OrdinalIgnoreCase))?.Field ?? "";
                string text = CellText(sheet, headerRow + r - 1, c, value, field);
                if (value is double number && InventoryFields.Numeric.Contains(field)) text = number.ToString("R", culture);
                // Excel stores dates as serial doubles. Convert only the explicitly named expiry field.
                if (r > 1 && field == "ExpiryDate" && value is double serial)
                    text = DateTime.FromOADate(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                row.Add(text); var formula = Convert.ToString(At(formulas, r, c), CultureInfo.InvariantCulture) ?? "";
                var rawText = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                fingerprint.Append(rawText.Length).Append(':').Append(rawText).Append(formula.Length).Append(':').Append(formula);
            }
            if (r > 1) dataRows.Add(row);
        }
        return new(new(headers, dataRows), "excel:" + InventoryWorkbook.Revision(Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }

    public static Task<WorkbookSnapshot?> TryReadAsync(string path, string sheet, int headerRow, List<ColumnMapping> mappings, CultureInfo culture) => OnStaAsync<WorkbookSnapshot?>(() =>
    {
        using var objects = new ComObjects(); dynamic? book = FindWorkbook(path, objects); if (book == null) return null;
        dynamic sheets = objects.Keep(book.Worksheets); dynamic ws = objects.Keep(sheets.Item[sheet]); return Snapshot(ws, headerRow, objects, mappings, culture);
    });

    public static Task<bool> TryWriteAsync(WorkbookLink link, List<InventoryItem> items, string expectedRevision) => OnStaAsync(() =>
    {
        using var objects = new ComObjects(); dynamic? book = FindWorkbook(link.Path, objects); if (book == null) return false;
        if ((bool)book.ReadOnly) throw new IOException("The workbook is read-only in Excel. Save an editable copy and connect it.");
        dynamic sheets = objects.Keep(book.Worksheets); dynamic ws = objects.Keep(sheets.Item[link.Sheet]);
        WorkbookSnapshot snapshot = Snapshot(ws, link.HeaderRow, objects, link.Columns, CultureInfo.InvariantCulture);
        if (snapshot.Revision != expectedRevision) throw new IOException("Excel changed during synchronization. Try Sync again.");
        var columns = link.Columns.Where(c => c.Included).ToList();
        var positions = columns.ToDictionary(c => c.Field, c => snapshot.Table.Headers.FindIndex(h => h.Equals(c.Header, StringComparison.OrdinalIgnoreCase)) + 1);
        if (positions.Values.Any(v => v == 0)) throw new InvalidDataException("A mapped Excel column was removed. Update the connection mapping.");
        var skuColumn = positions["SKU"] - 1;
        var rows = snapshot.Table.Rows.Select((r, n) => (SKU: r[skuColumn].Trim(), Row: link.HeaderRow + n + 1))
            .Where(r => r.SKU.Length > 0).ToDictionary(r => r.SKU, r => r.Row, StringComparer.OrdinalIgnoreCase);
        dynamic cells = objects.Keep(ws.Cells);
        var updates = new List<(int Row, int Column, string Field, string Value)>();
        var lastRow = link.HeaderRow + snapshot.Table.Rows.Count;
        foreach (var item in items)
        {
            var row = rows.TryGetValue(item.SKU, out var foundRow) ? foundRow : ++lastRow;
            foreach (var column in columns)
            {
                dynamic cell = cells.Item[row, positions[column.Field]];
                try
                {
                    var value = InventoryFields.Get(item, column.Field);
                    if ((bool)cell.HasFormula)
                    {
                        var copy = item.Copy(); InventoryFields.Set(copy, column.Field, Convert.ToString(cell.Value2, CultureInfo.InvariantCulture) ?? "", CultureInfo.InvariantCulture);
                        if (InventoryFields.Get(copy, column.Field) != value) throw new InvalidDataException($"{item.SKU} / {column.Header} contains a formula. Exclude it before editing this field.");
                    }
                    else updates.Add((row, positions[column.Field], column.Field, value));
                }
                finally { Release(cell); }
            }
        }
        // SaveCopyAs backs up both saved and pending Excel edits before the first mutation.
        book.SaveCopyAs(link.Path + ".barcodrod.bak.xlsx");
        foreach (var update in updates)
        {
            dynamic cell = cells.Item[update.Row, update.Column];
            try
            {
                if (InventoryFields.Numeric.Contains(update.Field)) cell.Value2 = (double)decimal.Parse(update.Value, CultureInfo.InvariantCulture);
                else if (update.Field == "Archived") cell.Value2 = update.Value == "true";
                else { cell.NumberFormat = "@"; cell.Value2 = update.Value; }
            }
            finally { Release(cell); }
        }
        book.Save(); return true;
    });

    public static Task OpenRecordAsync(WorkbookLink link, string sku) => OnStaAsync(() =>
    {
        InventoryWorkbook.ValidatePath(link.Path);
        using var objects = new ComObjects(); dynamic? book = FindWorkbook(link.Path, objects);
        dynamic app;
        if (book == null)
        {
            var type = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Desktop Excel is not installed. Workbook import, save, and sync still work; install Excel to jump directly to records.");
            app = objects.Keep(Activator.CreateInstance(type)!); app.Visible = true; app.AutomationSecurity = 3;
            dynamic workbooks = objects.Keep(app.Workbooks); book = objects.Keep(workbooks.Open(link.Path, 0, false));
        }
        else app = objects.Keep(book.Application);
        dynamic sheets = objects.Keep(book.Worksheets); dynamic ws = objects.Keep(sheets.Item[link.Sheet]);
        dynamic cells = objects.Keep(ws.Cells); dynamic used = objects.Keep(ws.UsedRange); dynamic usedColumns = objects.Keep(used.Columns); dynamic usedRows = objects.Keep(used.Rows);
        var column = 0;
        var header = link.Columns.Single(c => c.Included && c.Field == "SKU").Header;
        for (var c = 1; c <= (int)used.Column + (int)usedColumns.Count - 1; c++)
        {
            dynamic cell = cells.Item[link.HeaderRow, c];
            try { if (string.Equals(Convert.ToString(cell.Value2, CultureInfo.InvariantCulture), header, StringComparison.OrdinalIgnoreCase)) { column = c; break; } }
            finally { Release(cell); }
        }
        if (column == 0) throw new InvalidDataException("The SKU column was not found in Excel.");
        var lastRow = (int)used.Row + (int)usedRows.Count - 1;
        if (lastRow - link.HeaderRow > 50000) throw new InvalidDataException("Excel navigation is limited to 50,000 records.");
        for (var r = link.HeaderRow + 1; r <= lastRow; r++)
        {
            dynamic cell = cells.Item[r, column];
            try
            {
                if (!string.Equals(Convert.ToString(cell.Text, CultureInfo.InvariantCulture)?.Trim(), sku, StringComparison.OrdinalIgnoreCase)) continue;
                app.Visible = true; book.Activate(); ws.Activate(); app.Goto(cell, true); SetForegroundWindow(new IntPtr((int)app.Hwnd)); return true;
            }
            finally { Release(cell); }
        }
        throw new InvalidOperationException("This record is not in Excel yet. Sync the workbook first.");
    });
}
