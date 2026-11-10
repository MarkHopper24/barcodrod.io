using barcodrod.io.Core.Inventory;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace barcodrod.io.Views;

internal sealed record TransferOptions(string Sheet, int HeaderRow, string Culture, char Separator, bool AutoSync);

internal static class InventoryTransferDialogs
{
    public static List<ColumnMapping> Suggest(InventoryTable table, List<ColumnMapping> saved)
    {
        string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var aliases = new Dictionary<string, string[]> { ["SKU"] = ["sku", "itemid", "itemcode", "partnumber", "partno"], ["Name"] = ["name", "item", "product", "description", "productname"], ["Barcode"] = ["barcode", "upc", "ean", "code"], ["Quantity"] = ["quantity", "qty", "onhand", "stock"], ["UnitCost"] = ["unitcost", "cost"], ["UnitPrice"] = ["unitprice", "price"], ["ReorderLevel"] = ["reorderlevel", "reorder", "minimum", "minstock"] };
        var result = InventoryJson.Copy(saved);
        foreach (var column in result)
        {
            var match = table.Headers.FirstOrDefault(h => h.Equals(column.Header, StringComparison.OrdinalIgnoreCase))
                ?? table.Headers.FirstOrDefault(h => Normalize(h) == Normalize(column.Field) || aliases.TryGetValue(column.Field, out var names) && names.Contains(Normalize(h)));
            column.Included = match != null; if (match != null) column.Header = match;
        }
        foreach (var header in table.Headers.Where(h => !result.Any(c => c.Included && c.Header.Equals(h, StringComparison.OrdinalIgnoreCase))))
            result.Add(new() { Field = "Custom." + header, Header = header });
        return result;
    }

    public static async Task<TransferOptions?> OptionsAsync(XamlRoot root, string title, string path, List<string>? sheets, WorkbookLink? saved = null, bool connecting = false)
    {
        var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(root.Size.Width - 96, 260, 440) }; panel.Children.Add(InventoryDialogs.Text(Path.GetFileName(path)));
        panel.Children.Add(InventoryDialogs.Text(connecting ? "Connect for ongoing two-way updates between this inventory and the workbook." : "Import copies the selected data into this inventory once. It does not create an Excel connection."));
        var sheet = InventoryDialogs.Identify(new ComboBox { Header = "Worksheet", ItemsSource = sheets, SelectedItem = sheets?.Contains(saved?.Sheet ?? "Inventory") == true ? saved?.Sheet ?? "Inventory" : sheets?.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = sheets == null ? Visibility.Collapsed : Visibility.Visible }, "TransferSheet");
        var row = InventoryDialogs.Identify(new NumberBox { Header = "Header row", Value = saved?.HeaderRow ?? 1, Minimum = 1, Maximum = 1000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Visibility = sheets == null ? Visibility.Collapsed : Visibility.Visible }, "TransferHeaderRow");
        var culture = InventoryDialogs.Identify(new TextBox { Header = "Number / date culture", Text = saved?.NumberCulture ?? "", PlaceholderText = "Blank = invariant (1.25); or en-US, de-DE…" }, "TransferCulture");
        var delimiter = InventoryDialogs.Identify(new ComboBox { Header = "Delimiter", ItemsSource = new[] { "Comma", "Semicolon", "Tab" }, SelectedIndex = Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? 2 : 0, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = sheets == null ? Visibility.Visible : Visibility.Collapsed }, "TransferDelimiter");
        var auto = InventoryDialogs.Identify(new ToggleSwitch { Header = "Sync Excel after every saved inventory change", IsOn = saved?.AutoSync ?? false, Visibility = connecting ? Visibility.Visible : Visibility.Collapsed }, "TransferAutoSync");
        var error = InventoryDialogs.Identify(new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }, "TransferValidation");
        foreach (var control in new UIElement[] { sheet, row, culture, delimiter, auto, error }) panel.Children.Add(control);
        if (connecting) panel.Children.Add(InventoryDialogs.Text("Save and sync update mapped columns and preserve other sheets and columns. Saving a workbook open in desktop Excel also saves its other pending edits."));
        var dialog = InventoryDialogs.Dialog(root, title, new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(root.Size.Height - 240, 200, 600) }, "Continue");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { CultureInfo.GetCultureInfo(culture.Text.Trim()); if (sheets != null) InventoryWorkbook.ValidateSheet(sheet.SelectedItem?.ToString() ?? "", checked((int)row.Value)); }
            catch (Exception ex) { args.Cancel = true; error.Message = ex.Message; error.IsOpen = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return new(sheet.SelectedItem?.ToString() ?? "Inventory", (int)row.Value, culture.Text.Trim(), delimiter.SelectedIndex switch { 1 => ';', 2 => '\t', _ => ',' }, auto.IsOn);
    }

    public static async Task<List<ColumnMapping>?> ColumnsAsync(XamlRoot root, List<ColumnMapping> source, IReadOnlyList<string>? headers = null)
    {
        var mappings = InventoryJson.Copy(source); var panel = new StackPanel { Spacing = 8, Width = Math.Clamp(root.Size.Width - 96, 260, 540) };
        panel.Children.Add(InventoryDialogs.Text("Map item fields to your column headers. SKU stores the item ID; SKU and Name are required. Custom fields use Custom.field-name. Existing workbook field names stay compatible."));
        var rows = new List<(CheckBox Included, TextBox Field, Control Header)>();
        var list = new StackPanel { Spacing = 8 }; panel.Children.Add(list);
        void AddRow(ColumnMapping mapping)
        {
            var n = rows.Count; var grid = new Grid { ColumnSpacing = 8 };
            var compact = panel.Width < 440;
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(32) }); grid.ColumnDefinitions.Add(new() { Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(170) });
            if (!compact) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            else { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            var included = InventoryDialogs.Identify(new CheckBox { IsChecked = mapping.Included, VerticalAlignment = VerticalAlignment.Center }, "ColumnIncluded" + n, "Include " + mapping.Field);
            var field = InventoryDialogs.Identify(new TextBox { Text = mapping.Field, IsReadOnly = !mapping.Field.StartsWith("Custom.", StringComparison.Ordinal) }, "ColumnField" + n, "Inventory field");
            Control header = headers == null ? InventoryDialogs.Identify(new TextBox { Text = mapping.Header }, "ColumnHeader" + n, "Output column header")
                : InventoryDialogs.Identify(new ComboBox { ItemsSource = headers, SelectedItem = headers.FirstOrDefault(h => h.Equals(mapping.Header, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch }, "ColumnHeader" + n, "Input column header");
            Grid.SetColumn(field, 1); Grid.SetColumn(header, compact ? 1 : 2); if (compact) Grid.SetRow(header, 1);
            grid.Children.Add(included); grid.Children.Add(field); grid.Children.Add(header); list.Children.Add(grid); rows.Add((included, field, header));
        }
        foreach (var mapping in mappings) AddRow(mapping);
        var add = InventoryDialogs.Identify(new Button { Content = "Add custom column" }, "ColumnAddCustom"); add.Click += (_, _) => AddRow(new() { Field = "Custom.", Header = "" }); panel.Children.Add(add);
        var error = InventoryDialogs.Identify(new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }, "ColumnValidation"); panel.Children.Add(error);
        var dialog = InventoryDialogs.Dialog(root, "Input / output columns", new ScrollViewer { Content = panel, MaxHeight = 480 }, "Use these columns");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                mappings = rows.Select(r => new ColumnMapping { Included = r.Included.IsChecked == true, Field = r.Field.Text.Trim(), Header = (r.Header is TextBox text ? text.Text : ((ComboBox)r.Header).SelectedItem?.ToString() ?? "").Trim() }).ToList();
                InventoryFields.ValidateColumns(mappings);
            }
            catch (Exception ex) { args.Cancel = true; error.Message = ex.Message; error.IsOpen = true; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? mappings : null;
    }
}
