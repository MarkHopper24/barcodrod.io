using barcodrod.io.Core.Inventory;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace barcodrod.io.Views;

/// <summary>Native form controls shared by inventory dialogs. Domain validation remains in Core.</summary>
internal static class InventoryDialogs
{
    public static T Identify<T>(T element, string id, string? name = null) where T : DependencyObject
    { AutomationProperties.SetAutomationId(element, id); if (name != null) AutomationProperties.SetName(element, name); return element; }
    public static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    public static ContentDialog Dialog(XamlRoot root, string title, object content, string primary = "Save")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = title, Content = content, PrimaryButtonText = primary,
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(720, root.Size.Width - 32);
        return dialog;
    }
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string action) =>
        await Dialog(root, title, Text(message), action).ShowAsync() == ContentDialogResult.Primary;

    public static async Task<InventoryItem?> EditAsync(XamlRoot root, InventoryItem source, bool lockedSKU, InventoryDocument existing)
    {
        var item = source.Copy();
        var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(root.Size.Width - 96, 260, 560) };
        var editors = new Dictionary<string, Control>();
        foreach (var field in InventoryFields.Names.Where(n => n is not "Archived"))
        {
            Control control;
            if (InventoryFields.Numeric.Contains(field)) control = Identify(new NumberBox { Header = Label(field), Value = (double)decimal.Parse(InventoryFields.Get(item, field), CultureInfo.InvariantCulture), Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact }, "Item" + field);
            else if (field == "BarcodeFormat") control = Identify(new ComboBox { Header = "Barcode format", ItemsSource = InventoryLabels.Formats.Skip(1).ToList(), SelectedItem = item.BarcodeFormat, HorizontalAlignment = HorizontalAlignment.Stretch }, "ItemBarcodeFormat");
            else control = Identify(new TextBox { Header = Label(field), Text = InventoryFields.Get(item, field), IsReadOnly = field == "SKU" && lockedSKU, AcceptsReturn = field == "Notes", TextWrapping = field == "Notes" ? TextWrapping.Wrap : TextWrapping.NoWrap }, "Item" + field);
            editors[field] = control;
        }
        Grid Fields(params string[] fields)
        {
            var columns = panel.Width < 440 ? 1 : 2;
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var n = 0; n < fields.Length; n++)
            {
                if (n % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var control = editors[fields[n]]; Grid.SetRow(control, n / columns); Grid.SetColumn(control, n % columns); grid.Children.Add(control);
            }
            return grid;
        }
        void Section(string title, string id, params string[] fields)
        {
            panel.Children.Add(Identify(new Expander { Header = title, Content = Fields(fields), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch }, id));
        }
        panel.Children.Add(Text("Only the name is required. You can change the generated item ID. Leave the barcode blank to print that ID."));
        if (lockedSKU) panel.Children.Add(Text("Item ID is stored as SKU in the linked workbook. Disconnect Excel before changing this ID."));
        var error = Identify(new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }, "ItemValidation"); panel.Children.Add(error);
        panel.Children.Add(editors["Name"]);
        panel.Children.Add(Fields("SKU", "Quantity", "Unit", "Location"));
        Section("Codes and organization", "ItemCodes", "Barcode", "BarcodeFormat", "Category", "Bin", "Lot", "SerialNumber", "ExpiryDate");
        Section("Reservations and quantity targets", "ItemQuantities", "Reserved", "ReorderLevel", "TargetStock");
        Section("Value and sourcing (optional)", "ItemValue", "UnitCost", "UnitPrice", "Supplier");
        var customs = new StackPanel { Spacing = 8 }; var customRows = new List<(TextBox Key, TextBox Value, Grid Row)>();
        var nextCustomId = 0;
        void AddCustom(string key, string value)
        {
            var row = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            var compact = panel.Width < 440;
            row.ColumnDefinitions.Add(new() { Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(160) });
            row.ColumnDefinitions.Add(new() { Width = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            if (!compact) row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            else { row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            var n = nextCustomId++; var k = Identify(new TextBox { Header = "Field name", Text = key }, "CustomFieldName" + n); var v = Identify(new TextBox { Header = "Value", Text = value }, "CustomFieldValue" + n);
            var remove = Identify(new Button { Content = "Remove", VerticalAlignment = VerticalAlignment.Bottom }, "CustomFieldRemove" + n);
            if (compact) { Grid.SetColumnSpan(k, 2); Grid.SetRow(v, 1); Grid.SetRow(remove, 1); Grid.SetColumn(remove, 1); }
            else { Grid.SetColumn(v, 1); Grid.SetColumn(remove, 2); }
            row.Children.Add(k); row.Children.Add(v); row.Children.Add(remove); customs.Children.Add(row); customRows.Add((k, v, row));
            remove.Click += (_, _) => { customs.Children.Remove(row); customRows.RemoveAll(c => c.Row == row); };
        }
        foreach (var field in item.CustomFields) AddCustom(field.Key, field.Value);
        var add = Identify(new Button { Content = "Add custom field" }, "ItemAddCustom"); add.Click += (_, _) => AddCustom("", "");
        var notes = new StackPanel { Spacing = 12 }; notes.Children.Add(editors["Notes"]); notes.Children.Add(customs); notes.Children.Add(add);
        panel.Children.Add(Identify(new Expander { Header = "Notes and custom fields", Content = notes, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = item.CustomFields.Count > 0 || item.Notes.Length > 0 }, "ItemCustom"));
        var dialog = Dialog(root, source.Name.Length == 0 ? "New inventory item" : "Edit inventory item", new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(root.Size.Height - 240, 200, 600) });
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                foreach (var field in editors)
                {
                    var value = field.Value switch { NumberBox number => InventoryFields.EditorNumber(number.Value, decimal.Parse(InventoryFields.Get(source, field.Key), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture), ComboBox combo => combo.SelectedItem?.ToString() ?? "", TextBox text => text.Text, _ => "" };
                    InventoryFields.Set(item, field.Key, value, CultureInfo.InvariantCulture);
                }
                if (string.IsNullOrWhiteSpace(item.SKU) && !lockedSKU) item.SKU = InventoryEngine.CreateItem(existing).SKU;
                item.CustomFields.Clear();
                foreach (var row in customRows)
                {
                    if (string.IsNullOrWhiteSpace(row.Key.Text)) throw new InvalidDataException("Name each custom field or remove its row.");
                    if (!item.CustomFields.TryAdd(row.Key.Text.Trim(), row.Value.Text)) throw new InvalidDataException("Custom field names must be unique.");
                }
                InventoryEngine.Upsert(InventoryJson.Copy(existing), item);
            }
            catch (Exception ex) { args.Cancel = true; error.Message = ex.Message; AutomationProperties.SetName(error, ex.Message); error.IsOpen = true; error.StartBringIntoView(); }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? item : null;
    }

    public static string Label(string field) => field switch
    {
        "SKU" => "Item ID", "Name" => "Item name *", "Quantity" => "Quantity", "Reserved" => "Reserved quantity",
        "ReorderLevel" => "Minimum quantity (0 = off)", "TargetStock" => "Preferred quantity", "UnitCost" => "Cost / value per unit", "UnitPrice" => "Selling price (optional)",
        "Supplier" => "Source / supplier", "Bin" => "Shelf / bin", "Lot" => "Batch / lot", "SerialNumber" => "Serial number", "ExpiryDate" => "Expiry date (yyyy-MM-dd)", _ => field
    };

    private sealed record QuantityAction(StockAction Value, string Label);

    public static async Task<(StockAction Action, decimal Amount, string Reference, string Note, string Destination, string Bin)?> StockAsync(XamlRoot root, InventoryItem item)
    {
        var actions = Enum.GetValues<StockAction>().Select(a => new QuantityAction(a, InventoryActions.Name(a))).ToList();
        var action = Identify(new ComboBox { Header = "Action", ItemsSource = actions, DisplayMemberPath = nameof(QuantityAction.Label), SelectedItem = actions[0], HorizontalAlignment = HorizontalAlignment.Stretch }, "StockAction");
        var amount = Identify(new NumberBox { Header = "Quantity", Value = 1, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact }, "StockAmount");
        var reference = Identify(new TextBox { Header = "Reference (optional)", PlaceholderText = "Person, project, event, or order" }, "StockReference"); var note = Identify(new TextBox { Header = "Note (optional)" }, "StockNote");
        var destination = Identify(new TextBox { Header = "New location", Text = item.Location, Visibility = Visibility.Collapsed }, "StockDestination");
        var bin = Identify(new TextBox { Header = "New shelf / bin", Text = item.Bin, Visibility = Visibility.Collapsed }, "StockDestinationBin");
        var hint = Text("");
        var preview = Identify(Text(""), "StockPreview");
        StockAction SelectedAction() => ((QuantityAction)action.SelectedItem).Value;
        decimal Amount() => InventoryFields.EditorNumber(amount.Value, SelectedAction() is StockAction.SetCount or StockAction.Transfer ? item.Quantity : 0);
        void PreviewChange()
        {
            try
            {
                var candidate = new InventoryDocument { Items = [item.Copy()] };
                InventoryEngine.Move(candidate, item.Id, SelectedAction(), Amount(), "", "", destination.Text, bin.Text);
                var result = candidate.Items[0];
                preview.Text = $"After this change: {result.Quantity:0.###} {item.Unit} total · {result.Available:0.###} available · {result.Reserved:0.###} reserved";
            }
            catch (Exception ex) { preview.Text = ex.Message; }
        }
        void ConfigureAction()
        {
            var value = SelectedAction(); var transfer = value == StockAction.Transfer;
            destination.Visibility = bin.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
            amount.IsEnabled = !transfer; amount.Header = value == StockAction.SetCount ? "New total quantity" : "Quantity";
            amount.Value = value is StockAction.SetCount or StockAction.Transfer ? (double)item.Quantity : 1;
            hint.Text = value switch
            {
                StockAction.Receive => "Add items you acquired, received, or returned to this inventory.",
                StockAction.Issue => "Remove items that were used, given away, or taken out. Reserved quantities are protected.",
                StockAction.SetCount => "Replace the total with an exact count. This is the new total, not an amount to add.",
                StockAction.Reserve => "Set aside available items for a person, project, or future use. The total stays the same.",
                StockAction.Release => "Make reserved items available again. The total stays the same.",
                StockAction.FulfillReservation => "Remove items from both the reserved quantity and the total.",
                _ => "Move this item's entire quantity to another location. To track quantities in separate places, use a separate item ID for each."
            };
            PreviewChange();
        }
        action.SelectionChanged += (_, _) => ConfigureAction(); amount.ValueChanged += (_, _) => PreviewChange();
        destination.TextChanged += (_, _) => PreviewChange(); bin.TextChanged += (_, _) => PreviewChange();
        ConfigureAction();
        var error = Identify(new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false }, "StockValidation");
        var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(root.Size.Width - 96, 260, 440) }; panel.Children.Add(Text($"{item.SKU} · {item.Name}\nTotal {item.Quantity:0.###} · Reserved {item.Reserved:0.###} · Available {item.Available:0.###}"));
        foreach (var control in new UIElement[] { error, action, hint, amount, destination, bin, preview, reference, note }) panel.Children.Add(control);
        var dialog = Dialog(root, "Update quantity or location", new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(root.Size.Height - 240, 200, 600) }, "Apply change");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { InventoryEngine.Move(new InventoryDocument { Items = [item.Copy()] }, item.Id, SelectedAction(), Amount(), reference.Text, note.Text, destination.Text, bin.Text); }
            catch (Exception ex) { args.Cancel = true; error.Message = ex.Message; AutomationProperties.SetName(error, ex.Message); error.IsOpen = true; error.StartBringIntoView(); }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (SelectedAction(), Amount(), reference.Text, note.Text, destination.Text, bin.Text) : null;
    }
}
