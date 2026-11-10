using barcodrod.io.Core.Inventory;
using barcodrod.io.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace barcodrod.io.Views;

public sealed partial class LabelPrintWindow : Window
{
    private static readonly List<LabelPrintWindow> OpenWindows = [];
    private readonly List<InventoryItem> _items;
    private readonly LabelOptions _options;
    private readonly Func<LabelOptions, Task>? _saveOptions;
    private readonly Dictionary<string, NumberBox> _numbers = [];
    private readonly Dictionary<string, CheckBox> _checks = [];
    private LabelDocument? _document;
    private string? _previewFile;
    private bool _initializing = true;
    private bool _busy;
    private bool _closed;
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static Task OpenAsync(List<InventoryItem> items, LabelOptions? options = null, Func<LabelOptions, Task>? saveOptions = null)
    {
        var window = new LabelPrintWindow(items, options ?? new LabelOptions(), saveOptions); OpenWindows.Add(window); window.Activate(); return Task.CompletedTask;
    }
    public LabelPrintWindow(List<InventoryItem> items, LabelOptions options, Func<LabelOptions, Task>? saveOptions)
    {
        _items = items; _options = InventoryJson.Copy(options); _saveOptions = saveOptions;
        InitializeComponent(); Title = "barcodrod.io · Print labels";
        Root.RequestedTheme = App.GetService<barcodrod.io.Contracts.Services.IThemeSelectorService>().Theme;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1160 * scale), area.Width - 32), Math.Min((int)(820 * scale), area.Height - 32)));
        Preset.ItemsSource = InventoryLabels.Presets.Select(p => p.Name).Append("Custom").ToList();
        Format.ItemsSource = InventoryLabels.Formats;
        QuantityMode.ItemsSource = new[] { "Copies per item", "On-hand whole units", "Available whole units", "Reorder whole units" };
        foreach (var field in new[] { ("ShowName", "Item name"), ("ShowSKU", "Item ID"), ("ShowPayload", "Human-readable code"), ("ShowLocation", "Location / shelf"), ("ShowPrice", "Selling price (optional)"), ("ShowLotExpiry", "Batch / expiry") })
        {
            var check = InventoryDialogs.Identify(new CheckBox { Content = field.Item2 }, "Label" + field.Item1); _checks[field.Item1] = check; FieldsPanel.Children.Add(check);
        }
        foreach (var field in new[] { ("PaperWidth", "Paper width (mm)"), ("PaperHeight", "Paper height (mm)"), ("Width", "Label width (mm)"), ("Height", "Label height (mm)"), ("Columns", "Columns"), ("Rows", "Rows"), ("Left", "Left margin (mm)"), ("Top", "Top margin (mm)"), ("HorizontalGap", "Horizontal gap (mm)"), ("VerticalGap", "Vertical gap (mm)"), ("OffsetX", "Horizontal calibration (mm)"), ("OffsetY", "Vertical calibration (mm)"), ("BarcodeHeight", "Barcode height (mm)"), ("FontSize", "Text size (pt)") })
        {
            var number = InventoryDialogs.Identify(new NumberBox { Header = field.Item2, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact }, "Label" + field.Item1); _numbers[field.Item1] = number; DimensionsPanel.Children.Add(number);
        }
        SetEditors(); _initializing = false;
        Preview.SizeChanged += async (_, _) => await FitPreviewAsync();
        Root.Loaded += async (_, _) => await RenderAsync();
        Closed += (_, _) => { _closed = true; Preview.Close(); OpenWindows.Remove(this); try { if (_previewFile != null && File.Exists(_previewFile)) File.Delete(_previewFile); } catch (IOException) { } };
    }

    private void SetEditors()
    {
        var previous = _initializing; _initializing = true;
        Preset.SelectedItem = _options.Preset; Format.SelectedItem = _options.Format; Payload.Text = _options.Payload;
        QuantityMode.SelectedItem = _options.QuantityMode; Copies.Value = _options.Copies; Skip.Value = _options.Skip;
        foreach (var field in _numbers) field.Value.Value = Convert.ToDouble(typeof(LabelOptions).GetProperty(field.Key)!.GetValue(_options), CultureInfo.InvariantCulture);
        foreach (var field in _checks) field.Value.IsChecked = (bool)typeof(LabelOptions).GetProperty(field.Key)!.GetValue(_options)!;
        _initializing = previous;
    }
    private LabelOptions ReadEditors()
    {
        var options = InventoryJson.Copy(_options);
        options.Preset = Preset.SelectedItem?.ToString() ?? "Custom"; options.Format = Format.SelectedItem?.ToString() ?? "";
        options.Payload = Payload.Text; options.QuantityMode = QuantityMode.SelectedItem?.ToString() ?? "";
        if (!double.IsFinite(Copies.Value) || !double.IsFinite(Skip.Value) || Copies.Value != Math.Truncate(Copies.Value) || Skip.Value != Math.Truncate(Skip.Value)) throw new InvalidDataException("Copies and used positions must be whole numbers.");
        options.Copies = checked((int)Copies.Value); options.Skip = checked((int)Skip.Value);
        foreach (var field in _numbers)
        {
            var property = typeof(LabelOptions).GetProperty(field.Key)!;
            if (property.PropertyType == typeof(int)) { if (!double.IsFinite(field.Value.Value) || field.Value.Value != Math.Truncate(field.Value.Value)) throw new InvalidDataException("Rows and columns must be whole numbers."); property.SetValue(options, checked((int)field.Value.Value)); }
            else property.SetValue(options, field.Value.Value);
        }
        foreach (var field in _checks) typeof(LabelOptions).GetProperty(field.Key)!.SetValue(options, field.Value.IsChecked == true);
        return options;
    }
    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var preset = InventoryLabels.Presets.FirstOrDefault(p => p.Name == Preset.SelectedItem?.ToString());
        if (preset == null) return;
        try { var current = ReadEditors(); foreach (var property in typeof(LabelOptions).GetProperties()) property.SetValue(_options, property.GetValue(current)); } catch (Exception) { }
        _options.Apply(preset); SetEditors();
        PrintButton.IsEnabled = PdfButton.IsEnabled = false; JobSummary.Text = "Label stock changed. Update the preview before printing.";
    }
    private void Message(string message, bool error = false) { Status.Message = message; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Status, message); Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Status.IsOpen = true; }
    private async void UpdatePreview(object sender, RoutedEventArgs e) => await RenderAsync();
    private async Task RenderAsync()
    {
        if (_busy) return; _busy = true; UpdateButton.IsEnabled = PrintButton.IsEnabled = PdfButton.IsEnabled = false;
        try
        {
            var options = ReadEditors(); var document = await Task.Run(() => InventoryLabels.Build(_items, options));
            if (Preview.CoreWebView2 == null)
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync("", Path.Combine(AppPaths.LocalDataFolder, "LabelBrowser"), new CoreWebView2EnvironmentOptions());
                await Preview.EnsureCoreWebView2Async(environment);
                Preview.CoreWebView2.Settings.IsScriptEnabled = false; Preview.CoreWebView2.Settings.IsWebMessageEnabled = false;
                Preview.CoreWebView2.Settings.AreDevToolsEnabled = false; Preview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Preview.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
                Preview.CoreWebView2.NavigationStarting += (_, args) => { if (args.Uri != "about:blank" && (_previewFile == null || args.Uri != new Uri(_previewFile).AbsoluteUri)) args.Cancel = true; };
            }
            var old = _previewFile; _previewFile = Path.Combine(AppPaths.LocalDataFolder, "labels-" + Guid.NewGuid().ToString("N") + ".html");
            await File.WriteAllTextAsync(_previewFile, document.Html);
            var completed = new TaskCompletionSource<bool>();
            void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs args) { completed.TrySetResult(args.IsSuccess); }
            Preview.CoreWebView2.NavigationCompleted += Loaded;
            try { Preview.CoreWebView2.Navigate(new Uri(_previewFile).AbsoluteUri); if (!await completed.Task.WaitAsync(TimeSpan.FromSeconds(30))) throw new IOException("The label preview could not be loaded."); }
            finally { Preview.CoreWebView2.NavigationCompleted -= Loaded; }
            if (old != null && File.Exists(old)) File.Delete(old);
            _document = document;
            await FitPreviewAsync();
            if (_saveOptions != null) await _saveOptions(document.Options);
            JobSummary.Text = $"{document.Labels:N0} label(s) · {document.Pages:N0} page(s) · {document.Options.PaperWidth:0.##} × {document.Options.PaperHeight:0.##} mm";
            Message("Preview ready. Review the content and paper dimensions before printing."); PrintButton.IsEnabled = PdfButton.IsEnabled = true;
        }
        catch (Exception ex) { _document = null; if (!_closed) Message(ex.Message, true); }
        finally { _busy = false; if (!_closed) UpdateButton.IsEnabled = true; }
    }
    private async Task FitPreviewAsync()
    {
        if (_closed || _document == null || Preview.CoreWebView2 == null || Preview.ActualWidth <= 24) return;
        var zoom = Math.Min(1, (Preview.ActualWidth - 24) / (_document.Options.PaperWidth * 96 / 25.4));
        try { await Preview.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.setProperty('--preview-zoom','" + zoom.ToString("0.####", CultureInfo.InvariantCulture) + "')"); }
        catch (COMException) { }
    }
    private bool PreviewMatchesEditors()
    {
        if (_document == null) return false;
        if (System.Text.Json.JsonSerializer.Serialize(ReadEditors()) == System.Text.Json.JsonSerializer.Serialize(_document.Options)) return true;
        Message("Settings have changed. Update the preview before saving or printing.", true); return false;
    }
    private void Print(object sender, RoutedEventArgs e)
    {
        try { if (PreviewMatchesEditors()) Preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch (Exception ex) { Message(ex.Message, true); }
    }
    private async void SavePdf(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            if (!PreviewMatchesEditors()) return;
            _busy = true; PdfButton.IsEnabled = PrintButton.IsEnabled = UpdateButton.IsEnabled = CloseButton.IsEnabled = false;
            var picker = new FileSavePicker { SuggestedFileName = "Inventory labels", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" }); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file == null || _closed) return;
            var settings = Preview.CoreWebView2.Environment.CreatePrintSettings();
            settings.PageWidth = _document!.Options.PaperWidth / 25.4; settings.PageHeight = _document.Options.PaperHeight / 25.4;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
            settings.ScaleFactor = 1; settings.ShouldPrintHeaderAndFooter = false; settings.ShouldPrintBackgrounds = true;
            var staging = Path.Combine(Path.GetDirectoryName(file.Path)!, "." + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                if (!await Preview.CoreWebView2.PrintToPdfAsync(staging, settings)) throw new IOException("PDF export did not complete. Try saving again.");
                File.Move(staging, file.Path, true);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            Message("Label PDF saved: " + file.Name);
        }
        catch (Exception ex) { if (!_closed) Message(ex.Message, true); }
        finally { _busy = false; if (!_closed) { PdfButton.IsEnabled = PrintButton.IsEnabled = _document != null; UpdateButton.IsEnabled = CloseButton.IsEnabled = true; } }
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
