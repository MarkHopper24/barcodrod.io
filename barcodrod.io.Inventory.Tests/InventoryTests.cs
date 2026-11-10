using barcodrod.io.Core.Inventory;
using ClosedXML.Excel;
using System.Globalization;
using System.Xml.Linq;
using ZXing;
using ZXing.Common;

namespace barcodrod.io.Inventory.Tests;

public sealed class InventoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "barcodrod-inventory-tests-" + Guid.NewGuid().ToString("N"));
    public InventoryTests() => Directory.CreateDirectory(_folder);
    private static InventoryItem Item(string sku = "000123") => new() { SKU = sku, Name = "Widget", Barcode = "001234567890", Quantity = 10, UnitCost = 3.25m, ReorderLevel = 4, TargetStock = 20, Notes = "Line one\nLine two, with \"quotes\"", CustomFields = new() { ["Brand"] = "Acme" } };
    private static List<ColumnMapping> Columns() => InventoryFields.DefaultColumns().Append(new ColumnMapping { Field = "Custom.Brand", Header = "Brand" }).ToList();

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 3, true)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, false)]
    public void MinimumWarningsOnlyApplyWhenAMinimumHasBeenConfigured(int quantity, int minimum, bool expected)
    {
        var item = new InventoryItem { SKU = "BOOK-1", Name = "Personal collection", Quantity = quantity, ReorderLevel = minimum };
        Assert.Equal(expected, item.LowStock);
        item.Archived = true;
        Assert.False(item.LowStock);
    }

    [Fact] public void NewItemsHaveAnEditableUniqueIdAndOneUnitWithoutMutatingTheInventory()
    {
        var document = new InventoryDocument();
        var first = InventoryEngine.CreateItem(document);
        Assert.Equal("ITEM-0001", first.SKU);
        Assert.Equal(1, first.Quantity);
        Assert.Empty(document.Items);
        first.Name = "Camera";
        InventoryEngine.Upsert(document, first);
        var occupied = new InventoryItem { SKU = "item-0002", Name = "Book" };
        InventoryEngine.Upsert(document, occupied);
        Assert.Equal("ITEM-0003", InventoryEngine.CreateItem(document).SKU);
        Assert.Equal(2, document.Items.Count);
    }

    [Theory]
    [InlineData("0.00000001")]
    [InlineData("12345678901234567890.123456789")]
    public void UnchangedNumberEditorsPreserveTheOriginalDecimalExactly(string text)
    {
        var original = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Equal(original, InventoryFields.EditorNumber((double)original, original));
    }

    [Fact] public void EditedSmallNumbersAreValidDecimalsAndNonfiniteValuesAreRejected()
    {
        Assert.Equal(0.00000001m, InventoryFields.EditorNumber(0.00000001, 0));
        Assert.Throws<OverflowException>(() => InventoryFields.EditorNumber(double.NaN, 1));
        Assert.Throws<OverflowException>(() => InventoryFields.EditorNumber(double.PositiveInfinity, 1));
    }

    [Fact] public async Task FailedStockOperationsLeavePersistedInventoryAndLedgerUntouched()
    {
        var store = new InventoryStore(_folder); var item = Item();
        await store.CommitAsync(d => InventoryEngine.Upsert(d, item));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Reserve, 6, "SO-1", "Hold"));
        var before = await File.ReadAllTextAsync(store.FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Issue, 5, "", "")));
        Assert.Equal(before, await File.ReadAllTextAsync(store.FilePath));
        Assert.Equal(10, store.Snapshot.Items.Single().Quantity); Assert.Equal(6, store.Snapshot.Items.Single().Reserved);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.SetCount, 5, "", "")));
        Assert.Equal(2, store.Snapshot.Movements.Count);
    }
    [Fact] public async Task StockLedgerSupportsReceiveIssueReservationsCountsAndFullBalanceTransfers()
    {
        var store = new InventoryStore(_folder); var item = Item();
        await store.CommitAsync(d => InventoryEngine.Upsert(d, item));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Receive, 2.5m, "PO", ""));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Reserve, 3, "SO", ""));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Release, 1, "", ""));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.FulfillReservation, 2, "SO", ""));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Issue, 1.5m, "", ""));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.SetCount, 8, "", "Count"));
        await store.CommitAsync(d => InventoryEngine.Move(d, item.Id, StockAction.Transfer, 8, "", "", "Warehouse B", "B-2"));
        var result = store.Snapshot.Items.Single(); Assert.Equal(8, result.Quantity); Assert.Equal(0, result.Reserved); Assert.Equal("Warehouse B", result.Location);
        Assert.Equal(8, store.Snapshot.Movements.Count); Assert.True(File.Exists(store.FilePath + ".bak"));
        var reloaded = new InventoryStore(_folder); await reloaded.LoadAsync(); Assert.Equal(8, reloaded.Snapshot.Items.Single().Quantity);
    }
    [Fact] public async Task StoreRejectsDuplicatesCorruptionAndConcurrentInstanceWrites()
    {
        var first = new InventoryStore(_folder); var second = new InventoryStore(_folder); await second.LoadAsync();
        await first.CommitAsync(d => InventoryEngine.Upsert(d, Item()));
        await Assert.ThrowsAsync<IOException>(() => second.CommitAsync(d => InventoryEngine.Upsert(d, Item("SECOND"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => first.CommitAsync(d => InventoryEngine.Upsert(d, Item())));
        var duplicateBarcode = Item("OTHER"); await Assert.ThrowsAsync<InvalidDataException>(() => first.CommitAsync(d => InventoryEngine.Upsert(d, duplicateBarcode)));
        await File.WriteAllTextAsync(first.FilePath, "broken data");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new InventoryStore(_folder).LoadAsync());
        Assert.Equal("broken data", await File.ReadAllTextAsync(first.FilePath));
    }
    [Theory] [InlineData(',')] [InlineData(';')] [InlineData('\t')]
    public void CsvRoundTripsLeadingZeroIdsMultilineTextQuotesAndCustomFields(char separator)
    {
        var original = Item(); var csv = InventoryCsv.Write([original], Columns(), separator);
        var table = InventoryCsv.Read(csv, separator); var imported = InventoryTransfer.Parse(table, Columns(), CultureInfo.InvariantCulture).Single();
        Assert.Equal(original.SKU, imported.SKU); Assert.Equal(original.Barcode, imported.Barcode); Assert.Equal(original.Notes, imported.Notes); Assert.Equal("Acme", imported.CustomFields["Brand"]);
    }
    [Fact] public void ImportMapsLocalizedNumbersAndRejectsMalformedOrDuplicateRowsAsAWhole()
    {
        var columns = new List<ColumnMapping> { new() { Field = "SKU", Header = "Part" }, new() { Field = "Name", Header = "Description" }, new() { Field = "Quantity", Header = "Qty" } };
        var table = InventoryCsv.Read("Part;Description;Qty\n001;Thing;1,5", ';');
        Assert.Equal(1.5m, InventoryTransfer.Parse(table, columns, CultureInfo.GetCultureInfo("de-DE")).Single().Quantity);
        Assert.Throws<InvalidDataException>(() => InventoryTransfer.Parse(InventoryCsv.Read("Part,Description,Qty\n001,A,1\n001,B,2"), columns, CultureInfo.InvariantCulture));
        Assert.Throws<InvalidDataException>(() => InventoryCsv.Read("SKU,Name\nA,\"broken"));
        Assert.Throws<InvalidDataException>(() => InventoryCsv.Read("SKU,Name\nA,\"value\"junk"));
    }
    [Fact] public void CsvNeutralizesTextFormulaInjectionWhileWorkbookKeepsLiteralValues()
    {
        var item = Item(); item.Name = "=HYPERLINK(\"https://invalid\")";
        Assert.Contains("'=HYPERLINK", InventoryCsv.Write([item], Columns()));
        var path = Path.Combine(_folder, "formulas.xlsx"); InventoryWorkbook.Write(path, "Inventory", 1, [item], Columns());
        using var book = new XLWorkbook(path); Assert.False(book.Worksheet("Inventory").Cell(2, 2).HasFormula); Assert.Equal(item.Name, book.Worksheet("Inventory").Cell(2, 2).GetString());
    }
    [Fact] public void WorkbookRoundTripsTextDatesDecimalsAndAuditLog()
    {
        var item = Item(); item.ExpiryDate = new DateOnly(2027, 1, 31);
        var path = Path.Combine(_folder, "inventory.xlsx");
        InventoryWorkbook.Write(path, "Inventory", 3, [item], Columns(), movements: [new StockMovement { SKU = item.SKU, QuantityChange = -1, Balance = 10 }]);
        var table = InventoryWorkbook.Read(path, "Inventory", 3);
        var imported = InventoryTransfer.Parse(table.Table, Columns(), CultureInfo.InvariantCulture).Single();
        Assert.Equal(item.SKU, imported.SKU); Assert.Equal(item.Barcode, imported.Barcode); Assert.Equal(item.UnitCost, imported.UnitCost); Assert.Equal(item.ExpiryDate, imported.ExpiryDate);
        using var book = new XLWorkbook(path); Assert.Equal(-1, book.Worksheet("Stock movements").Cell(2, 4).GetDouble()); Assert.Equal(XLDataType.Text, book.Worksheet("Inventory").Cell(4, 1).DataType);
    }
    [Fact] public void WorkbookUpsertPreservesUnmappedColumnsFormulasAndOtherSheetsAndMakesBackup()
    {
        var path = Path.Combine(_folder, "preserve.xlsx"); var item = Item(); var columns = Columns();
        InventoryWorkbook.Write(path, "Inventory", 1, [item], columns);
        using (var book = new XLWorkbook(path))
        {
            var ws = book.Worksheet("Inventory"); ws.Cell(1, columns.Count + 1).Value = "Calculation"; ws.Cell(2, columns.Count + 1).FormulaA1 = "I2*2";
            book.AddWorksheet("Unrelated").Cell("A1").Value = "Keep me"; book.Save();
        }
        var before = InventoryWorkbook.Read(path, "Inventory", 1); item.Quantity = 15;
        InventoryWorkbook.Write(path, "Inventory", 1, [item], columns, before.Revision);
        using var saved = new XLWorkbook(path); Assert.Equal("Keep me", saved.Worksheet("Unrelated").Cell("A1").GetString());
        Assert.True(saved.Worksheet("Inventory").Cell(2, columns.Count + 1).HasFormula); Assert.True(File.Exists(path + ".barcodrod.bak"));
        Assert.Throws<IOException>(() => InventoryWorkbook.Write(path, "Inventory", 1, [item], columns, before.Revision));
    }
    [Fact] public void WorkbookNumbersUseMappedFieldsRegardlessOfCurrencyDisplayOrSelectedCulture()
    {
        var path = Path.Combine(_folder, "localized.xlsx"); var columns = Columns(); columns.Single(c => c.Field == "UnitCost").Header = "Purchase cost";
        var item = Item(); InventoryWorkbook.Write(path, "Inventory", 1, [item], columns);
        using (var book = new XLWorkbook(path)) { book.Worksheet("Inventory").Cell(2, columns.FindIndex(c => c.Field == "UnitCost") + 1).Style.NumberFormat.Format = "$#,##0.00"; book.Save(); }
        var culture = CultureInfo.GetCultureInfo("de-DE"); var table = InventoryWorkbook.Read(path, "Inventory", 1, columns, culture);
        var imported = InventoryTransfer.Parse(table.Table, columns, culture).Single(); Assert.Equal(3.25m, imported.UnitCost);
    }
    [Fact] public void MappedFormulasArePreservedAndCannotBeOverwrittenByInventoryEdits()
    {
        var path = Path.Combine(_folder, "mapped-formula.xlsx"); var columns = Columns(); var item = Item();
        InventoryWorkbook.Write(path, "Inventory", 1, [item], columns);
        using (var book = new XLWorkbook(path)) { var cell = book.Worksheet("Inventory").Cell(2, columns.FindIndex(c => c.Field == "UnitCost") + 1); cell.FormulaA1 = "3.25"; cell.Style.NumberFormat.Format = "$0.00"; book.Save(); }
        var before = InventoryWorkbook.Read(path, "Inventory", 1, columns); InventoryWorkbook.Write(path, "Inventory", 1, [item], columns, before.Revision);
        item.UnitCost = 4; Assert.Throws<InvalidDataException>(() => InventoryWorkbook.Write(path, "Inventory", 1, [item], columns));
        using var saved = new XLWorkbook(path); Assert.True(saved.Worksheet("Inventory").Cell(2, columns.FindIndex(c => c.Field == "UnitCost") + 1).HasFormula);
    }
    [Fact] public void SyncMergesDisjointEditsAndDetectsSameFieldEditsAndDeletedRows()
    {
        var item = Item(); var document = new InventoryDocument { Items = [item], Excel = new WorkbookLink { Columns = Columns() } };
        InventoryTransfer.MarkSynced(document.Excel, document.Items);
        var external = item.Copy(); external.Name = "Renamed in Excel"; item.Quantity = 12;
        var result = InventoryTransfer.PlanSync(document, [external]); Assert.Empty(result.Conflicts); Assert.Equal(12, result.Items.Single().Quantity); Assert.Equal("Renamed in Excel", result.Items.Single().Name);
        external.Quantity = 11; result = InventoryTransfer.PlanSync(document, [external]); Assert.Contains(result.Conflicts, c => c.Contains("Quantity"));
        Assert.NotEmpty(InventoryTransfer.PlanSync(document, []).Conflicts);
    }
    [Fact] public void SyncImportsNewRowsButRetainsLocalOnlyFieldsAndMatchesMixedCaseSkus()
    {
        var item = Item("Mixed"); item.CustomFields["Private"] = "Keep";
        var columns = InventoryFields.DefaultColumns(); var document = new InventoryDocument { Items = [item], Excel = new WorkbookLink { Columns = columns } };
        InventoryTransfer.MarkSynced(document.Excel, document.Items);
        var external = item.Copy(); external.SKU = "MIXED"; external.Quantity = 13;
        var other = Item("New"); other.Barcode = "NEWCODE";
        var result = InventoryTransfer.PlanSync(document, [external, other]);
        Assert.Empty(result.Conflicts); Assert.Equal(2, result.Items.Count); Assert.Equal("Keep", result.Items.First().CustomFields["Private"]);
    }
    [Theory] [InlineData("CODE_128", "ABC123")] [InlineData("CODE_39", "ABC123")] [InlineData("QR_CODE", "ABC123")]
    [InlineData("DATA_MATRIX", "ABC123")] [InlineData("EAN_13", "1234567890128")] [InlineData("UPC_A", "012345678905")]
    public void PrintableSvgBarcodesDecodeToTheirExpectedPayload(string format, string payload)
    {
        var item = Item(); item.Barcode = payload; item.BarcodeFormat = format;
        var options = new LabelOptions { ShowName = false, ShowSKU = false, ShowPayload = false, BarcodeHeight = 20 };
        var document = InventoryLabels.Build([item], options);
        var start = document.Html.IndexOf("<svg", StringComparison.Ordinal); var end = document.Html.IndexOf("</svg>", start, StringComparison.Ordinal) + 6;
        var svg = XElement.Parse(document.Html[start..end]); var bounds = svg.Attribute("viewBox")!.Value.Split(' '); var w = int.Parse(bounds[2]); var h = int.Parse(bounds[3]);
        const int scale = 4; const int border = 16; var bitmap = new BitMatrix(w * scale + border * 2, h * scale + border * 2);
        foreach (var rect in svg.Descendants().Where(e => e.Name.LocalName == "rect" && e.Attribute("x") != null))
            bitmap.setRegion(int.Parse(rect.Attribute("x")!.Value) * scale + border, int.Parse(rect.Attribute("y")!.Value) * scale + border, int.Parse(rect.Attribute("width")!.Value) * scale, int.Parse(rect.Attribute("height")!.Value) * scale);
        var bytes = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++) bytes[y * bitmap.Width + x] = bitmap[x, y] ? (byte)0 : (byte)255;
        var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { TryHarder = true, PossibleFormats = [Enum.Parse<BarcodeFormat>(format)] } };
        var decoded = reader.Decode(new RGBLuminanceSource(bytes, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8));
        Assert.NotNull(decoded); Assert.Equal(payload, decoded.Text);
    }
    [Fact] public void LabelPresetsFitAndPartialSheetsPaginateCorrectly()
    {
        foreach (var preset in InventoryLabels.Presets) { var options = new LabelOptions(); options.Apply(preset); InventoryLabels.Validate(options); }
        var job = InventoryLabels.Build([Item()], new LabelOptions { Copies = 31, Skip = 2 }); Assert.Equal(31, job.Labels); Assert.Equal(2, job.Pages);
        Assert.Throws<InvalidDataException>(() => InventoryLabels.Build([Item()], new LabelOptions { PaperWidth = 50 }));
        Assert.Throws<InvalidDataException>(() => InventoryLabels.Build([Item()], new LabelOptions { ShowName = true, ShowSKU = true, ShowLocation = true, ShowPrice = true, ShowLotExpiry = true }));
        Assert.Throws<InvalidDataException>(() => InventoryLabels.Build([Item()], new LabelOptions { Payload = "{Unknown}" }));
    }
    [Fact] public void LabelTemplatesEscapeHtmlAndUseSkuFallbackAndCustomFields()
    {
        var item = Item(); item.Barcode = ""; item.Name = "<script>bad()</script>";
        var job = InventoryLabels.Build([item], new LabelOptions { Format = "QR_CODE", Payload = "{SKU}:{Custom.Brand}" });
        Assert.DoesNotContain("<script>", job.Html); Assert.Contains("&lt;script&gt;", job.Html); Assert.Contains("000123:Acme", job.Html);
        Assert.Equal(item.SKU, InventoryLabels.Payload(item, "{Barcode}"));
    }
    [Fact] public async Task ExplicitBackupRestoreRecoversCorruptStorageAndDisconnectsExcel()
    {
        var path = Path.Combine(_folder, "inventory.json"); await File.WriteAllTextAsync(path, "corrupt");
        var store = new InventoryStore(_folder); await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
        await store.RestoreAsync(new InventoryDocument { Items = [Item()], Excel = new WorkbookLink { Path = "unused" } });
        Assert.Single(store.Snapshot.Items); Assert.Null(store.Snapshot.Excel); Assert.Equal("corrupt", await File.ReadAllTextAsync(path + ".bak"));
        await store.CommitAsync(d => InventoryEngine.Move(d, d.Items[0].Id, StockAction.Receive, 1, "", "")); Assert.Equal(11, store.Snapshot.Items[0].Quantity);
    }
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
