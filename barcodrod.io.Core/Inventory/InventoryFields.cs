using System.Globalization;

namespace barcodrod.io.Core.Inventory;

public static class InventoryFields
{
    public static readonly string[] Names = ["SKU", "Name", "Barcode", "BarcodeFormat", "Category", "Location", "Bin", "Supplier", "Unit", "Quantity", "Reserved", "ReorderLevel", "TargetStock", "UnitCost", "UnitPrice", "Lot", "SerialNumber", "ExpiryDate", "Notes", "Archived"];
    public static readonly HashSet<string> Numeric = new(["Quantity", "Reserved", "ReorderLevel", "TargetStock", "UnitCost", "UnitPrice"], StringComparer.OrdinalIgnoreCase);
    public static List<ColumnMapping> DefaultColumns() => Names.Select(n => new ColumnMapping { Field = n, Header = n }).ToList();

    public static void ValidateColumns(IEnumerable<ColumnMapping> mappings)
    {
        var columns = mappings.Where(m => m.Included).ToList();
        if (!columns.Any(c => c.Field == "SKU") || !columns.Any(c => c.Field == "Name"))
            throw new InvalidDataException("Include SKU and Name in your column mapping.");
        if (columns.Any(c => string.IsNullOrWhiteSpace(c.Header) || (!Names.Contains(c.Field) && !c.Field.StartsWith("Custom.", StringComparison.Ordinal)) || c.Field == "Custom."))
            throw new InvalidDataException("Every included column needs a header and an inventory field (or Custom.field-name).");
        if (columns.Select(c => c.Header.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count || columns.Select(c => c.Field).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
            throw new InvalidDataException("Column headers and inventory fields must be unique.");
    }

    public static string Get(InventoryItem i, string field) => field switch
    {
        "SKU" => i.SKU, "Name" => i.Name, "Barcode" => i.Barcode, "BarcodeFormat" => i.BarcodeFormat, "Category" => i.Category,
        "Location" => i.Location, "Bin" => i.Bin, "Supplier" => i.Supplier, "Unit" => i.Unit,
        "Quantity" => Number(i.Quantity), "Reserved" => Number(i.Reserved), "ReorderLevel" => Number(i.ReorderLevel),
        "TargetStock" => Number(i.TargetStock), "UnitCost" => Number(i.UnitCost), "UnitPrice" => Number(i.UnitPrice),
        "Lot" => i.Lot, "SerialNumber" => i.SerialNumber, "ExpiryDate" => i.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        "Notes" => i.Notes, "Archived" => i.Archived ? "true" : "false",
        _ when field.StartsWith("Custom.", StringComparison.Ordinal) => i.CustomFields.FirstOrDefault(p => p.Key.Equals(field[7..], StringComparison.OrdinalIgnoreCase)).Value ?? "",
        _ => throw new InvalidDataException($"Unknown field: {field}.")
    };
    public static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    // NumberBox exposes double. An unchanged editor must not round an existing precise decimal.
    public static decimal EditorNumber(double value, decimal original) => value.Equals((double)original) ? original : checked((decimal)value);

    public static void Set(InventoryItem i, string field, string value, CultureInfo culture)
    {
        decimal n = 0;
        if (Numeric.Contains(field) && !string.IsNullOrWhiteSpace(value) && !decimal.TryParse(value, NumberStyles.Number, culture, out n))
            throw new InvalidDataException($"{i.SKU}: '{value}' is not a valid {field} number for {culture.Name}.");
        switch (field)
        {
            case "SKU": i.SKU = value.Trim(); break; case "Name": i.Name = value.Trim(); break;
            case "Barcode": i.Barcode = value.Trim(); break; case "Category": i.Category = value; break;
            case "BarcodeFormat": i.BarcodeFormat = value.Trim(); break;
            case "Location": i.Location = value; break; case "Bin": i.Bin = value; break;
            case "Supplier": i.Supplier = value; break; case "Unit": i.Unit = value; break;
            case "Quantity": i.Quantity = n; break; case "Reserved": i.Reserved = n; break;
            case "ReorderLevel": i.ReorderLevel = n; break; case "TargetStock": i.TargetStock = n; break;
            case "UnitCost": i.UnitCost = n; break; case "UnitPrice": i.UnitPrice = n; break;
            case "Lot": i.Lot = value; break; case "SerialNumber": i.SerialNumber = value; break;
            case "ExpiryDate":
                i.ExpiryDate = string.IsNullOrWhiteSpace(value) ? null : DateOnly.TryParse(value, culture, DateTimeStyles.None, out var d) ? d
                    : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial >= 1 && serial < 2958466
                        ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
                        : throw new InvalidDataException($"{i.SKU}: expiry date '{value}' is invalid. Use yyyy-MM-dd."); break;
            case "Notes": i.Notes = value; break;
            case "Archived":
                var b = value.Trim().ToLowerInvariant();
                i.Archived = b is "true" or "1" or "yes";
                if (b is not ("true" or "1" or "yes" or "false" or "0" or "no" or "")) throw new InvalidDataException($"{i.SKU}: Archived must be true or false.");
                break;
            default:
                if (!field.StartsWith("Custom.", StringComparison.Ordinal)) throw new InvalidDataException($"Unknown field: {field}.");
                i.CustomFields[field[7..]] = value; break;
        }
    }

    public static Dictionary<string, string> Values(InventoryItem item, IEnumerable<ColumnMapping> columns) =>
        columns.Where(c => c.Included).ToDictionary(c => c.Field, c => Get(item, c.Field));
}
