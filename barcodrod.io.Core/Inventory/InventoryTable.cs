using System.Globalization;
using System.Text;

namespace barcodrod.io.Core.Inventory;

public sealed record InventoryTable(List<string> Headers, List<List<string>> Rows);
public sealed record ImportPlan(List<InventoryItem> Items, int Added, int Updated, int Unchanged);
public sealed record SyncPlan(List<InventoryItem> Items, List<string> Conflicts);

public static class InventoryTransfer
{
    public static List<InventoryItem> Parse(InventoryTable table, IEnumerable<ColumnMapping> mappings,
        CultureInfo culture, IEnumerable<InventoryItem>? existing = null)
    {
        var columns = mappings.Where(c => c.Included).ToList();
        InventoryFields.ValidateColumns(columns);
        if (table.Headers.Count != table.Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw new InvalidDataException("Input contains duplicate column headers.");
        var indices = columns.Select(c => (Mapping: c, Index: table.Headers.FindIndex(h => h.Trim().Equals(c.Header.Trim(), StringComparison.OrdinalIgnoreCase)))).ToList();
        var missing = indices.Where(c => c.Index < 0).Select(c => c.Mapping.Header).ToList();
        if (missing.Count > 0) throw new InvalidDataException("Missing columns: " + string.Join(", ", missing) + ". Exclude unused fields or map them to the input headers.");
        var old = (existing ?? []).ToDictionary(i => i.SKU, StringComparer.OrdinalIgnoreCase);
        var items = new List<InventoryItem>();
        foreach (var row in table.Rows)
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count != table.Headers.Count) throw new InvalidDataException($"Input row {items.Count + 2} has a different number of columns than the header.");
            var skuColumn = indices.Single(c => c.Mapping.Field == "SKU").Index;
            var sku = row[skuColumn].Trim();
            var item = old.TryGetValue(sku, out var current) ? current.Copy() : new InventoryItem();
            foreach (var c in indices) InventoryFields.Set(item, c.Mapping.Field, row[c.Index], culture);
            items.Add(item);
        }
        InventoryEngine.Validate(new InventoryDocument { Items = items });
        return items;
    }

    public static ImportPlan PlanImport(InventoryDocument document, List<InventoryItem> incoming, IEnumerable<ColumnMapping> columns)
    {
        var items = document.Items.Select(i => i.Copy()).ToList();
        var added = 0; var updated = 0; var unchanged = 0;
        foreach (var item in incoming)
        {
            var current = items.Find(i => i.SKU.Equals(item.SKU, StringComparison.OrdinalIgnoreCase));
            if (current == null) { items.Add(item); added++; }
            else if (Equal(InventoryFields.Values(current, columns), InventoryFields.Values(item, columns))) unchanged++;
            else { item.Id = current.Id; items[items.IndexOf(current)] = item; updated++; }
        }
        InventoryEngine.Validate(new InventoryDocument { Items = items });
        return new ImportPlan(items, added, updated, unchanged);
    }

    public static void ApplyItems(InventoryDocument document, IEnumerable<InventoryItem> items, string reason)
    {
        foreach (var item in items)
        {
            var previous = document.Items.Find(i => i.Id == item.Id);
            var all = InventoryFields.DefaultColumns();
            foreach (var key in (previous?.CustomFields.Keys.AsEnumerable() ?? []).Union(item.CustomFields.Keys)) all.Add(new() { Field = "Custom." + key, Header = key });
            if (previous == null || !Equal(InventoryFields.Values(previous, all), InventoryFields.Values(item, all))) InventoryEngine.Upsert(document, item, reason);
        }
    }

    public static SyncPlan PlanSync(InventoryDocument document, List<InventoryItem> remote)
    {
        var link = document.Excel ?? throw new InvalidOperationException("Connect an Excel workbook first.");
        var columns = link.Columns.Where(c => c.Included).ToList();
        var items = document.Items.Select(i => i.Copy()).ToList();
        var conflicts = new List<string>();
        foreach (var baseline in link.Baseline)
        {
            if (!remote.Any(i => i.SKU.Equals(baseline.Key, StringComparison.OrdinalIgnoreCase)))
                conflicts.Add($"{baseline.Key}: row removed from Excel. Restore it or disconnect the workbook; use Archived for intentional removals.");
        }
        foreach (var external in remote)
        {
            var local = items.Find(i => i.SKU.Equals(external.SKU, StringComparison.OrdinalIgnoreCase));
            if (local == null) { items.Add(external); continue; }
            var localValues = InventoryFields.Values(local, columns);
            var remoteValues = InventoryFields.Values(external, columns);
            var baseline = link.Baseline.FirstOrDefault(p => p.Key.Equals(local.SKU, StringComparison.OrdinalIgnoreCase)).Value;
            foreach (var column in columns)
            {
                var field = column.Field;
                var l = localValues[field]; var r = remoteValues[field];
                if (l == r) continue;
                if (baseline == null || !baseline.TryGetValue(field, out var b))
                {
                    conflicts.Add($"{local.SKU} / {field}: app '{l}', Excel '{r}'"); continue;
                }
                var localChanged = l != b; var remoteChanged = r != b;
                if (localChanged && remoteChanged) conflicts.Add($"{local.SKU} / {field}: app '{l}', Excel '{r}'");
                else if (remoteChanged) InventoryFields.Set(local, field, r, CultureInfo.InvariantCulture);
            }
        }
        if (conflicts.Count == 0) InventoryEngine.Validate(new InventoryDocument { Items = items });
        return new SyncPlan(items, conflicts);
    }

    public static void MarkSynced(WorkbookLink link, IEnumerable<InventoryItem> items)
    {
        link.Baseline = items.ToDictionary(i => i.SKU, i => InventoryFields.Values(i, link.Columns), StringComparer.OrdinalIgnoreCase);
        link.LastSyncUtc = DateTimeOffset.UtcNow;
    }

    public static bool Equal(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && p.Value == v);
}

public static class InventoryCsv
{
    public static InventoryTable Read(string text, char separator = ',')
    {
        if (separator is '"' or '\r' or '\n' || separator == '\0') throw new InvalidDataException("Choose a comma, semicolon, or tab delimiter.");
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closedQuote = false;
        text = text.TrimStart('\uFEFF');
        for (var n = 0; n < text.Length; n++)
        {
            var c = text[n];
            if (quoted)
            {
                if (c == '"')
                {
                    if (n + 1 < text.Length && text[n + 1] == '"') { field.Append('"'); n++; }
                    else { quoted = false; closedQuote = true; }
                }
                else field.Append(c);
            }
            else if (c == separator) { row.Add(field.ToString()); field.Clear(); closedQuote = false; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && n + 1 < text.Length && text[n + 1] == '\n') n++;
                row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; closedQuote = false;
            }
            else if (c == '"' && field.Length == 0 && !closedQuote) quoted = true;
            else if (closedQuote || c == '"') throw new InvalidDataException("CSV contains an unexpected character after a quote, or an unescaped quote.");
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("CSV contains an unclosed quoted field.");
        if (field.Length > 0 || row.Count > 0 || closedQuote) { row.Add(field.ToString()); rows.Add(row); }
        if (rows.Count == 0) throw new InvalidDataException("The input file is empty.");
        if (rows.Count > 50001 || rows[0].Count > 150) throw new InvalidDataException("Import is limited to 50,000 records and 150 columns per file.");
        return new InventoryTable(rows[0], rows.Skip(1).Where(r => r.Any(s => !string.IsNullOrWhiteSpace(s))).ToList());
    }

    public static string Write(IEnumerable<InventoryItem> items, IEnumerable<ColumnMapping> mappings, char separator = ',', CultureInfo? culture = null)
    {
        var columns = mappings.Where(c => c.Included).ToList(); InventoryFields.ValidateColumns(columns);
        culture ??= CultureInfo.InvariantCulture;
        string Escape(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        // Text beginning with a formula trigger is escaped for spreadsheet applications.
        string Safe(string s) => s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@') || s.StartsWith('\t') ? "'" + s : s;
        var result = new StringBuilder();
        result.AppendLine(string.Join(separator, columns.Select(c => Escape(Safe(c.Header)))));
        foreach (var item in items)
            result.AppendLine(string.Join(separator, columns.Select(c => Escape(InventoryFields.Numeric.Contains(c.Field)
                ? decimal.Parse(InventoryFields.Get(item, c.Field), CultureInfo.InvariantCulture).ToString(culture)
                : Safe(InventoryFields.Get(item, c.Field))))));
        return result.ToString();
    }
}
