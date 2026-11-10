using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ZXing;

namespace barcodrod.io.Core.Inventory;

public sealed record LabelPreset(string Name, double PaperWidth, double PaperHeight, double Width, double Height,
    int Columns, int Rows, double Left, double Top, double HorizontalGap, double VerticalGap)
{
    public override string ToString() => Name;
}

public sealed class LabelOptions
{
    public string Preset { get; set; } = "Avery 5160 / 5260 · 30 labels";
    public double PaperWidth { get; set; } = 215.9;
    public double PaperHeight { get; set; } = 279.4;
    public double Width { get; set; } = 66.675;
    public double Height { get; set; } = 25.4;
    public int Columns { get; set; } = 3;
    public int Rows { get; set; } = 10;
    public double Left { get; set; } = 4.7625;
    public double Top { get; set; } = 12.7;
    public double HorizontalGap { get; set; } = 3.175;
    public double VerticalGap { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double BarcodeHeight { get; set; } = 10;
    public double FontSize { get; set; } = 8;
    public string Format { get; set; } = "Item format";
    public string Payload { get; set; } = "{Barcode}";
    public int Copies { get; set; } = 1;
    public string QuantityMode { get; set; } = "Copies per item";
    public int Skip { get; set; }
    public bool ShowName { get; set; } = true;
    public bool ShowSKU { get; set; } = true;
    public bool ShowLocation { get; set; }
    public bool ShowPrice { get; set; }
    public bool ShowLotExpiry { get; set; }
    public bool ShowPayload { get; set; } = true;
    public void Apply(LabelPreset p)
    {
        Preset = p.Name; PaperWidth = p.PaperWidth; PaperHeight = p.PaperHeight; Width = p.Width; Height = p.Height;
        Columns = p.Columns; Rows = p.Rows; Left = p.Left; Top = p.Top; HorizontalGap = p.HorizontalGap; VerticalGap = p.VerticalGap;
        OffsetX = OffsetY = 0; Skip = 0;
    }
}

public sealed record LabelDocument(string Html, int Labels, int Pages, LabelOptions Options);

public static partial class InventoryLabels
{
    public static readonly string[] Formats = ["Item format", "CODE_128", "CODE_39", "QR_CODE", "DATA_MATRIX", "EAN_13", "EAN_8", "UPC_A", "UPC_E", "ITF", "PDF_417", "AZTEC", "CODABAR", "MSI", "PLESSEY"];
    public static readonly LabelPreset[] Presets =
    [
        new("Avery 5160 / 5260 · 30 labels", 215.9, 279.4, 66.675, 25.4, 3, 10, 4.7625, 12.7, 3.175, 0),
        new("Avery 5163 / 5263 · 10 labels", 215.9, 279.4, 101.6, 50.8, 2, 5, 4.7625, 12.7, 3.175, 0),
        new("Avery L7160 · A4 21 labels", 210, 297, 63.5, 38.1, 3, 7, 7.25, 15.15, 2.5, 0),
        new("Avery L7159 · A4 24 labels", 210, 297, 63.5, 33.9, 3, 8, 7.25, 12.9, 2.5, 0),
        new("Thermal · 2 × 1 inch", 50.8, 25.4, 50.8, 25.4, 1, 1, 0, 0, 0, 0),
        new("Thermal · 4 × 2 inch", 101.6, 50.8, 101.6, 50.8, 1, 1, 0, 0, 0, 0),
        new("Thermal · 4 × 6 inch", 101.6, 152.4, 101.6, 152.4, 1, 1, 0, 0, 0, 0)
    ];
    [GeneratedRegex(@"\{([\p{L}][\p{L}\p{N}_. /-]*)\}")]
    private static partial Regex Tokens();
    public static string Payload(InventoryItem item, string template)
    {
        return Tokens().Replace(template, match =>
        {
            var field = match.Groups[1].Value;
            if (field == "Barcode") return string.IsNullOrEmpty(item.Barcode) ? item.SKU : item.Barcode;
            if (field == "Available") return InventoryFields.Number(item.Available);
            return InventoryFields.Get(item, field);
        });
    }

    public static void Validate(LabelOptions o)
    {
        double[] measures = [o.PaperWidth, o.PaperHeight, o.Width, o.Height, o.Left, o.Top, o.HorizontalGap, o.VerticalGap, o.OffsetX, o.OffsetY, o.BarcodeHeight, o.FontSize];
        if (measures.Any(v => !double.IsFinite(v))) throw new InvalidDataException("All label measurements must be finite numbers.");
        if (o.PaperWidth is < 10 or > 600 || o.PaperHeight is < 10 or > 1000 || o.Width < 10 || o.Height < 10 || o.Columns is < 1 or > 20 || o.Rows is < 1 or > 100)
            throw new InvalidDataException("Enter valid paper and label sizes, 1–20 columns, and 1–100 rows.");
        if (o.Left < 0 || o.Top < 0 || o.HorizontalGap < 0 || o.VerticalGap < 0 || o.BarcodeHeight < 5 || o.FontSize is < 6 or > 24)
            throw new InvalidDataException("Margins and gaps must be nonnegative; barcode height is at least 5 mm; text size is 6–24 pt.");
        if (o.Left + o.OffsetX < 0 || o.Top + o.OffsetY < 0 || o.Left + o.OffsetX + o.Columns * o.Width + (o.Columns - 1) * o.HorizontalGap > o.PaperWidth + .01 || o.Top + o.OffsetY + o.Rows * o.Height + (o.Rows - 1) * o.VerticalGap > o.PaperHeight + .01)
            throw new InvalidDataException("The labels and calibration offsets extend beyond the selected paper.");
        var lines = (o.ShowName ? 1 : 0) + (o.ShowSKU ? 1 : 0) + (o.ShowLocation ? 1 : 0) + (o.ShowPrice ? 1 : 0) + (o.ShowLotExpiry ? 1 : 0) + (o.ShowPayload ? 1 : 0);
        if (4 + o.BarcodeHeight + lines * o.FontSize * 25.4 / 72 * 1.15 > o.Height)
            throw new InvalidDataException("The barcode and text do not fit this label height. Hide fields, reduce text/barcode size, or choose a taller label.");
        if (o.Copies is < 1 or > 1000 || o.Skip < 0 || o.Skip >= o.Columns * o.Rows) throw new InvalidDataException("Copies must be 1–1,000; skipped labels must fit the first sheet.");
        if (!Formats.Contains(o.Format) || string.IsNullOrWhiteSpace(o.Payload)) throw new InvalidDataException("Select a barcode format and enter a payload template.");
        if (o.QuantityMode is not ("Copies per item" or "On-hand whole units" or "Available whole units" or "Reorder whole units")) throw new InvalidDataException("Select a label quantity mode.");
    }

    public static LabelDocument Build(IEnumerable<InventoryItem> selection, LabelOptions options)
    {
        var o = InventoryJson.Copy(options); Validate(o);
        var labels = new List<string>();
        var inv = CultureInfo.InvariantCulture;
        string D(double n) => n.ToString("0.####", inv);
        string E(string s) => WebUtility.HtmlEncode(s);
        foreach (var item in selection)
        {
            var countValue = o.QuantityMode switch { "On-hand whole units" => decimal.Floor(item.Quantity), "Available whole units" => decimal.Floor(item.Available), "Reorder whole units" => decimal.Ceiling(item.ReorderQuantity), _ => o.Copies };
            if (countValue > 2000 || labels.Count + countValue > 2000) throw new InvalidDataException("A print job is limited to 2,000 labels. Reduce quantities or print a smaller selection.");
            var count = (int)countValue; if (count <= 0) continue;
            var payload = Payload(item, o.Payload); if (string.IsNullOrEmpty(payload)) throw new InvalidDataException($"{item.SKU}: the barcode payload is empty.");
            var formatName = o.Format == "Item format" ? item.BarcodeFormat : o.Format;
            if (!Enum.TryParse<BarcodeFormat>(formatName, out var format) || !Formats.Contains(formatName)) throw new InvalidDataException($"{item.SKU}: unsupported barcode format '{formatName}'.");
            string svg;
            try
            {
                var square = format is BarcodeFormat.QR_CODE or BarcodeFormat.DATA_MATRIX or BarcodeFormat.AZTEC;
                var matrix = new MultiFormatWriter().encode(payload, format, 0, square ? 0 : 16,
                    new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = square ? 4 : 16, [EncodeHintType.CHARACTER_SET] = "UTF-8" });
                var physicalWidth = square ? Math.Min(o.Width - 4, o.BarcodeHeight) : o.Width - 4;
                if (physicalWidth / matrix.Width < (square ? .20 : .19)) throw new InvalidDataException("Payload is too dense for this label. Use a wider label, shorter payload, or a 2D code.");
                var shape = new StringBuilder();
                for (var y = 0; y < matrix.Height; y++)
                    for (var x = 0; x < matrix.Width; x++)
                    {
                        if (!matrix[x, y]) continue;
                        var start = x; while (x + 1 < matrix.Width && matrix[x + 1, y]) x++;
                        shape.Append($"<rect x='{start}' y='{y}' width='{x - start + 1}' height='1'/>");
                    }
                svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {matrix.Width} {matrix.Height}' role='img' aria-label='Barcode' style='width:{D(physicalWidth)}mm;height:{D(o.BarcodeHeight)}mm' preserveAspectRatio='{(square ? "xMidYMid meet" : "none")}' shape-rendering='crispEdges'><rect width='100%' height='100%' fill='white'/><g fill='black'>{shape}</g></svg>";
            }
            catch (Exception ex) { throw new InvalidDataException($"{item.SKU}: {ex.Message}", ex); }
            var label = new StringBuilder("<div class='label'>");
            if (o.ShowName) label.Append("<div class='name'>" + E(item.Name) + "</div>");
            label.Append(svg);
            if (o.ShowPayload) label.Append("<div class='payload'>" + E(payload) + "</div>");
            if (o.ShowSKU) label.Append("<div>SKU: " + E(item.SKU) + "</div>");
            if (o.ShowLocation) label.Append("<div>" + E(item.Location + (item.Bin.Length > 0 ? " / " + item.Bin : "")) + "</div>");
            if (o.ShowPrice) label.Append("<div>" + E(item.UnitPrice.ToString("C", CultureInfo.CurrentCulture)) + "</div>");
            if (o.ShowLotExpiry) label.Append("<div>" + E($"Lot: {item.Lot} · Exp: {item.ExpiryDate?.ToString("yyyy-MM-dd", inv)}") + "</div>");
            label.Append("</div>");
            for (var n = 0; n < count; n++) labels.Add(label.ToString());
        }
        if (labels.Count == 0) throw new InvalidDataException("The selection produced no labels. Change the quantity mode or select items with stock.");
        var slots = Enumerable.Repeat("<div class='blank'></div>", o.Skip).Concat(labels).ToList();
        var perPage = o.Columns * o.Rows; var pages = (int)Math.Ceiling((double)slots.Count / perPage);
        var html = new StringBuilder($"<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:;\"><title>Inventory labels</title><style>@page{{size:{D(o.PaperWidth)}mm {D(o.PaperHeight)}mm;margin:0}}*{{box-sizing:border-box}}body{{margin:0;background:#ddd;color:#000;font-family:Arial,sans-serif}}.sheet{{width:{D(o.PaperWidth)}mm;height:{D(o.PaperHeight)}mm;padding:{D(o.Top + o.OffsetY)}mm 0 0 {D(o.Left + o.OffsetX)}mm;background:white;margin:12px auto;overflow:hidden;break-after:page}}.sheet:last-child{{break-after:auto}}.labels{{display:grid;grid-template-columns:repeat({o.Columns},{D(o.Width)}mm);grid-auto-rows:{D(o.Height)}mm;column-gap:{D(o.HorizontalGap)}mm;row-gap:{D(o.VerticalGap)}mm}}.label{{padding:2mm;display:flex;flex-direction:column;align-items:center;justify-content:center;font-size:{D(o.FontSize)}pt;line-height:1.15;overflow:hidden;text-align:center;outline:.1mm dashed #bbb}}.label div{{max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex-shrink:0}}.name{{font-weight:bold}}svg{{flex-shrink:0}}@media print{{body{{background:white}}.sheet{{margin:0}}.label{{outline:none}}}}</style></head><body>");
        for (var p = 0; p < pages; p++) html.Append("<section class='sheet'><div class='labels'>" + string.Concat(slots.Skip(p * perPage).Take(perPage)) + "</div></section>");
        html.Append("<style>@media screen{.sheet{zoom:var(--preview-zoom,1)}}</style></body></html>");
        return new(html.ToString(), labels.Count, pages, o);
    }
}
