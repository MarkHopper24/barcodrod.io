using barcodrod.io.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using ZXing;
using ZXing.QrCode.Internal;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace barcodrod.io.Views;

public sealed partial class EncodePage : Page
{
    private readonly BarcodeWriter writer = new();
    private readonly BarcodeReader logoVerifyReader = new();
    public Bitmap lastEncoded;
    private Color lastColor;
    private Color lastBgColor;
    private string lastSavedlocation;
    private string lastEncodedType;
    private Bitmap? _logoBitmap;
    private string? _bulkEncodeOutputPath;
    private bool _autoEncodeOnPaste;

    private void SizeChangedEventHandler(object sender, SizeChangedEventArgs args)
    {
        BarcodeViewer.MinHeight = TxtActivityLog.ActualHeight;
    }

    public EncodePage()
    {
        InitializeComponent();
        Loaded += EncodePage_Loaded;
        writer.Options.NoPadding = true;
        writer.Options.Hints.Add(EncodeHintType.CHARACTER_SET, "UTF-8");
        logoVerifyReader.Options.TryHarder = true;
        logoVerifyReader.Options.PossibleFormats = new[] { BarcodeFormat.QR_CODE };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadEncodeSettings();
        await FocusEncodeInputAsync();
    }

    private async void EncodePage_Loaded(object sender, RoutedEventArgs e)
    {
        await FocusEncodeInputAsync();
    }

    private async Task FocusEncodeInputAsync()
    {
        await Task.Delay(20);

        if (TxtActivityLog.Focus(FocusState.Programmatic))
            return;

        await Task.Delay(80);
        TxtActivityLog.Focus(FocusState.Programmatic);
    }

    private async Task SaveEncodeSettings()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            JObject settings;
            if (File.Exists(settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(settingsFilePath);
                settings = (json != null && json != "") ? JObject.Parse(json) : new JObject();
            }
            else
            {
                settings = new JObject();
            }

            settings["EncodeBarcode"] = BarcodeSelector.SelectedItem?.ToString() ?? "QR_CODE";
            settings["EncodeWidth"] = userWidth.Text;
            settings["EncodeHeight"] = userHeight.Text;
            settings["EncodeMargin"] = userMargin.Text;
            settings["EncodeCorrectionLevel"] = CorrectionLevel.SelectedIndex;

            File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
        }
        catch
        {
        }
    }

    private async Task LoadEncodeSettings()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            if (!File.Exists(settingsFilePath)) return;

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrEmpty(json)) return;

            var settings = JObject.Parse(json);

            var barcodeType = settings["EncodeBarcode"]?.Value<string>();
            if (!string.IsNullOrEmpty(barcodeType))
            {
                foreach (var item in BarcodeSelector.Items)
                {
                    if (item.ToString() == barcodeType)
                    {
                        BarcodeSelector.SelectedItem = item;
                        break;
                    }
                }
            }

            var width = settings["EncodeWidth"]?.Value<string>();
            if (!string.IsNullOrEmpty(width))
                userWidth.Text = width;

            var height = settings["EncodeHeight"]?.Value<string>();
            if (!string.IsNullOrEmpty(height))
                userHeight.Text = height;

            var margin = settings["EncodeMargin"]?.Value<string>();
            if (!string.IsNullOrEmpty(margin))
                userMargin.Text = margin;

            var correctionLevel = settings["EncodeCorrectionLevel"];
            if (correctionLevel != null)
                CorrectionLevel.SelectedIndex = correctionLevel.Value<int>();

            var autoEncodeOnPaste = settings["AutoEncodeOnPaste"];
            _autoEncodeOnPaste = autoEncodeOnPaste != null && autoEncodeOnPaste.Value<bool>();
        }
        catch
        {
        }
    }

    private void TxtActivityLog_Paste(object sender, TextControlPasteEventArgs e)
    {
        if (!_autoEncodeOnPaste)
            return;

        DispatcherQueue.TryEnqueue(() => { CreateBarcode(this, new RoutedEventArgs()); });
    }

    //function to copy the decoded bitmap to the user's clipboard as a pastable image
    private async void CopyImage(object sender, RoutedEventArgs e)
    {
        if (lastEncoded != null)
        {
            var file = await (await AppPaths.GetLocalFolderAsync()).CreateFileAsync("temp.png",
                CreationCollisionOption.ReplaceExisting);
            lastEncoded.Save(file.Path, ImageFormat.Png);
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        ImageRightClickCommandBar.Hide();
    }

    private void SetErrorCorrectionLevel(object sender, RoutedEventArgs e)
    {
        if (writer.Options.Hints.ContainsKey(EncodeHintType.ERROR_CORRECTION))
            writer.Options.Hints.Remove(EncodeHintType.ERROR_CORRECTION);

        switch (CorrectionLevel.SelectedIndex)
        {
            case 0:
                writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.L);
                return;
            case 1:
                writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.M);
                return;
            case 2:
                writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.Q);
                return;
            case 3:
                writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.H);
                return;
            default:
                writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.L);
                return;
        }
    }

    //function to create a barcode from text
    private void CreateBarcode(object sender, RoutedEventArgs e)
    {
        if (lastEncoded != null) lastEncoded.Dispose();

        if (BarcodeSelector.SelectedItem == null) return;

        var format = BarcodeSelector.SelectedItem.ToString();
        //set the writer formation to the selected format
        writer.Format = (BarcodeFormat)Enum.Parse(typeof(BarcodeFormat), format);
        if (writer.Format != BarcodeFormat.QR_CODE) writer.Options.Hints.Remove(EncodeHintType.ERROR_CORRECTION);


        //store the text of userWidth and userHeight into two variables

        var defaultHeight = 800;
        var defaultWidth = 800;
        int height;
        int width;
        //check if the user entered a value for width and height and it is a number
        if (int.TryParse(userHeight.Text, out height) == true)
            writer.Options.Height = height;
        else
            writer.Options.Height = defaultHeight;

        if (int.TryParse(userWidth.Text, out width) == true)
            writer.Options.Width = width;
        else
            writer.Options.Width = defaultWidth;

        writer.Options.PureBarcode = true;
        int margin;
        if (int.TryParse(userMargin.Text, out margin) == true)
            writer.Options.Margin = margin;
        else
            writer.Options.Margin = 0;

        if (TxtActivityLog.Text != null && TxtActivityLog.Text != "")
            try
            {
                if (_logoBitmap != null && writer.Format == BarcodeFormat.QR_CODE)
                {
                    if (writer.Options.Hints.ContainsKey(EncodeHintType.ERROR_CORRECTION))
                        writer.Options.Hints.Remove(EncodeHintType.ERROR_CORRECTION);
                    writer.Options.Hints.Add(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.H);
                }

                var barcode = writer.WriteAsBitmap(TxtActivityLog.Text);

                if (_logoBitmap != null && writer.Format == BarcodeFormat.QR_CODE)
                {
                    barcode = OverlayLogoWithVerification(barcode, _logoBitmap, TxtActivityLog.Text);
                }

                BitmapToImageSource(barcode);
                BarcodeViewer.MaxHeight = TxtActivityLog.ActualHeight;
                BarcodeViewer.MinHeight = TxtActivityLog.ActualHeight;
                lastEncoded = barcode;
                SaveImageButton.IsEnabled = true;
                CopyImageButton.IsEnabled = true;
                ChangeBarcodeColorButton.IsEnabled = true;
                LogoOverlayButton.IsEnabled = writer.Format == BarcodeFormat.QR_CODE;
                EncodeError.Message = "";
                EncodeError.Title = "";
                EncodeError.IsOpen = false;
                lastEncodedType = format;
                addToHistory(TxtActivityLog.Text, barcode);
                _ = SaveEncodeSettings();
            }
            catch (Exception ex)
            {
                lastBgColor = Color.White;
                lastColor = Color.Black;
                BarcodeViewer.Source = null;
                EncodeError.Message = ex.Message;
                EncodeError.Title = "Error";
                EncodeError.IsOpen = true;
                EncodeError.Severity = InfoBarSeverity.Error;
                SaveImageButton.IsEnabled = false;
                CopyImageButton.IsEnabled = false;
                ChangeBarcodeColorButton.IsEnabled = false;
                LogoOverlayButton.IsEnabled = false;
            }
    }

    public async void BitmapToImageSource(Bitmap bitmap)
    {
        using (var memory = new MemoryStream())
        {
            bitmap.Save(memory, ImageFormat.Bmp);
            memory.Position = 0;
            var bitmapimage = new BitmapImage();
            await bitmapimage.SetSourceAsync(memory.AsRandomAccessStream());
            var image = new Image();
            image.Source = bitmapimage;
            BarcodeViewer.SetValue(Image.SourceProperty, image.Source);
            await memory.FlushAsync();
        }
    }

    //method to change all of the black pixels in BarcodeViewer to the user's selected color 
    private void ChangeColor(object sender, RoutedEventArgs e)
    {
        var bitmap = lastEncoded;
        var color = ColorPicker.Color;
        if (color.R == lastBgColor.R && color.B == lastBgColor.B && color.G == lastBgColor.G)
        {
            SameColorWarning.Visibility = Visibility.Visible;
            return;
        }

        SameColorWarning.Visibility = Visibility.Collapsed;

        // Lock the bitmap to access the pixel data
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bmpData =
            bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

        // Get the number of bytes per pixel
        var bytesPerPixel = System.Drawing.Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;

        // Get the stride (number of bytes per row)
        var stride = bmpData.Stride;

        // Iterate over all pixels and replace black pixels with red
        var pixelRow = new byte[stride];
        for (var y = 0; y < bmpData.Height; y++)
        {
            // Copy the pixel row into a byte array
            Marshal.Copy(bmpData.Scan0 + y * stride, pixelRow, 0, stride);

            for (var x = 0; x < bmpData.Width; x++)
            {
                // Get the pixel value at (x,y)
                var b = pixelRow[x * bytesPerPixel];
                var g = pixelRow[x * bytesPerPixel + 1];
                var r = pixelRow[x * bytesPerPixel + 2];

                // Check if the pixel is black
                if ((r == 0 && g == 0 && b == 0) || (r == lastColor.R && g == lastColor.G && b == lastColor.B))
                {
                    pixelRow[x * bytesPerPixel] = color.B;
                    pixelRow[x * bytesPerPixel + 1] = color.G;
                    pixelRow[x * bytesPerPixel + 2] = color.R;
                }
            }

            // Copy the modified pixel row back into the bitmap
            Marshal.Copy(pixelRow, 0, bmpData.Scan0 + y * stride, stride);
        }

        // Unlock the bitmap
        bitmap.UnlockBits(bmpData);
        BitmapToImageSource(bitmap);
        //convert color to system.drawing.color
        lastColor = Color.FromArgb(color.A, color.R, color.G, color.B);
        SameColorWarning.Visibility = Visibility.Collapsed;
    }

    //method to change the background color of the barcode image
    private void ChangeBackground(object sender, RoutedEventArgs e)
    {
        var bitmap = lastEncoded;
        var color = ColorPicker.Color;
        if (color.R == lastColor.R && color.B == lastColor.B && color.G == lastColor.G)
        {
            SameColorWarning.Visibility = Visibility.Visible;
            return;
        }

        // Lock the bitmap to access the pixel data
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bmpData =
            bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

        // Get the number of bytes per pixel
        var bytesPerPixel = System.Drawing.Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;

        // Get the stride (number of bytes per row)
        var stride = bmpData.Stride;

        // Iterate over all pixels and replace black pixels with chosen color
        var pixelRow = new byte[stride];
        for (var y = 0; y < bmpData.Height; y++)
        {
            // Copy the pixel row into a byte array
            Marshal.Copy(bmpData.Scan0 + y * stride, pixelRow, 0, stride);

            for (var x = 0; x < bmpData.Width; x++)
            {
                // Get the pixel value at (x,y)
                var b = pixelRow[x * bytesPerPixel];
                var g = pixelRow[x * bytesPerPixel + 1];
                var r = pixelRow[x * bytesPerPixel + 2];


                // Check if the pixel is white or the same color as the last background color
                if ((r == 255 && g == 255 && b == 255) ||
                    (r == lastBgColor.R && g == lastBgColor.G && b == lastBgColor.B))
                {
                    pixelRow[x * bytesPerPixel] = color.B;
                    pixelRow[x * bytesPerPixel + 1] = color.G;
                    pixelRow[x * bytesPerPixel + 2] = color.R;
                }
            }

            // Copy the modified pixel row back into the bitmap
            Marshal.Copy(pixelRow, 0, bmpData.Scan0 + y * stride, stride);
        }

        // Unlock the bitmap
        bitmap.UnlockBits(bmpData);
        BitmapToImageSource(bitmap);
        //convert color to system.drawing.color
        lastBgColor = Color.FromArgb(color.A, color.R, color.G, color.B);
        SameColorWarning.Visibility = Visibility.Collapsed;
    }

    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (lastSavedlocation != "" && lastSavedlocation != null)
        {
            var options = new Windows.System.LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedlocation),
                options);
        }
    }

    public async void SaveBitmapToFile(StorageFile file)
    {
        if (file != null)
        {
            var bitmap = lastEncoded;

            int targetWidth = writer.Options.Width > 0 ? writer.Options.Width : bitmap.Width;
            int targetHeight = writer.Options.Height > 0 ? writer.Options.Height : bitmap.Height;

            var resizedBitmap = new Bitmap(targetWidth, targetHeight);
            using (var g = Graphics.FromImage(resizedBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
            }
            resizedBitmap.Save(file.Path, ImageFormat.Png);
        }
    }

    private async void SaveImage(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");

        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = timestamp + "." + BarcodeSelector.SelectedItem;


        picker.FileTypeChoices.Add("PNG", new List<string>() { ".png" });
        picker.FileTypeChoices.Add("JPEG", new List<string>() { ".jpg" });
        picker.FileTypeChoices.Add("BMP", new List<string>() { ".bmp" });
        picker.FileTypeChoices.Add("GIF", new List<string>() { ".gif" });
        picker.FileTypeChoices.Add("TIFF", new List<string>() { ".tiff" });
        picker.FileTypeChoices.Add("ICO", new List<string>() { ".ico" });
        picker.FileTypeChoices.Add("DIB", new List<string>() { ".dib" });
        picker.FileTypeChoices.Add("WMF", new List<string>() { ".wmf" });
        picker.FileTypeChoices.Add("EMF", new List<string>() { ".emf" });
        picker.FileTypeChoices.Add("EXIF", new List<string>() { ".exif" });
        picker.FileTypeChoices.Add("WEBP", new List<string>() { ".webp" });
        picker.FileTypeChoices.Add("HEIF", new List<string>() { ".heif" });
        picker.FileTypeChoices.Add("HEIC", new List<string>() { ".heic" });
        picker.FileTypeChoices.Add("JFIF", new List<string>() { ".jfif" });
        picker.FileTypeChoices.Add("JPE", new List<string>() { ".jpe" });
        picker.FileTypeChoices.Add("JIF", new List<string>() { ".jif" });
        picker.FileTypeChoices.Add("JFI", new List<string>() { ".jfi" });
        picker.FileTypeChoices.Add("JP2", new List<string>() { ".jp2" });
        picker.FileTypeChoices.Add("J2K", new List<string>() { ".j2k" });
        picker.FileTypeChoices.Add("JPF", new List<string>() { ".jpf" });
        picker.FileTypeChoices.Add("JPX", new List<string>() { ".jpx" });
        picker.FileTypeChoices.Add("J2C", new List<string>() { ".j2c" });
        picker.FileTypeChoices.Add("FPX", new List<string>() { ".fpx" });

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var path = await picker.PickSaveFileAsync();

        SaveBitmapToFile(path);

        if (path != null)
        {
            OpenImageButton.IsEnabled = true;
            ShareCommandBarButton.Visibility = Visibility.Visible;
            lastSavedlocation = path.Path;
        }

        else if (path == null)
        {
            ShareCommandBarButton.Visibility = Visibility.Collapsed;
            OpenImageButton.IsEnabled = false;
        }
    }

    private void GenerateWiFiQRCode(object sender, RoutedEventArgs e)
    {
        var encryptionType = "";
        var networkName = SSID.Text;
        var password = Password.Text;
        if (WifiSecurityButtonWEP.IsChecked == true)
            encryptionType = "WEP";
        else if (WifiSecurityButtonWPA.IsChecked == true) encryptionType = "WPA";

        TxtActivityLog.Text = $"WIFI:T:{encryptionType};S:{networkName};P:{password};;";
    }

    private void GenerateEmailQRCode(object sender, RoutedEventArgs e)
    {
        var email = EmailAddress.Text;
        var subject = EmailSubject.Text;
        var body = "";
        EmailBody.TextDocument.GetText(Microsoft.UI.Text.TextGetOptions.None, out body);
        TxtActivityLog.Text = $"MATMSG:TO:{email};SUB:{subject};BODY:{body};;";
    }

    private void GeneratevCardQRCode(object sender, RoutedEventArgs e)
    {
        var firstName = vCardFirstName.Text;
        var lastName = vCardLastName.Text;
        var phoneNumber = vCardPhone.Text;
        var cellNumber = vCardCell.Text;
        var email = vCardEmail.Text;
        var address = vCardAddress.Text;
        var company = vCardCompany.Text;
        var jobTitle = vCardTitle.Text;
        var website = vCardWebsite.Text;
        TxtActivityLog.Text =
            $"BEGIN:VCARD\nVERSION:3.0\nN:{lastName};{firstName};;;\nFN:{firstName} {lastName}\nTEL;TYPE=WORK,VOICE:{phoneNumber}\nTEL;TYPE=WORK,CELL:{cellNumber}\nEMAIL:{email}\nADR;TYPE=WORK,PREF:;;{address};;;\nORG:{company}\nTITLE:{jobTitle}\nURL:{website}\nEND:VCARD";
    }

    private void ShowMenu(bool isTransient)
    {
        var myOption = new FlyoutShowOptions();
        myOption.ShowMode = FlyoutShowMode.Transient;
        ImageRightClickCommandBar.ShowAt(BarcodeViewer, myOption);
    }

    private async Task<bool> IsHistoryEnabled()
    {
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //get the history folder
        var historyFolder =
            await localFolder.CreateFolderAsync("history", CreationCollisionOption.OpenIfExists);

        //read HistoryEnabled from settings.json
        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
            return currentHistorySetting.Value<bool>();
        else
            return false;
    }

    //create 2 new files in the app's local folder. One for text and one for images
    private async Task addToHistory(string text, Bitmap bitmap)
    {
        var historyEnabled = await IsHistoryEnabled();
        if (historyEnabled == false)
        {
            return;
        }
        else
        {
            //create folder called history if it doesn't exist
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var historyFolder = await localFolder.CreateFolderAsync("History", CreationCollisionOption.OpenIfExists);

            var files = await historyFolder.GetFilesAsync();
            string textFile;
            string imageFile;
            string textPath;
            string imagePath;
            var timestamp = DateTime.Now.ToString("MMddyy.HHmmssfff");

            var imageFileName = lastEncodedType + "." + timestamp + ".png";
            var textFileName = lastEncodedType + "." + timestamp + ".txt";

            textPath = Path.Combine(historyFolder.Path, textFileName);
            imagePath = Path.Combine(historyFolder.Path, imageFileName);
            File.WriteAllText(textPath, text);
            bitmap.Save(imagePath, ImageFormat.Png);

            return;
        }
    }

    private async void SelectLogo(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".ico");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();

        if (file != null)
        {
            _logoBitmap?.Dispose();
            _logoBitmap = new Bitmap(file.Path);
            RemoveLogoButton.IsEnabled = true;
            LogoStatusText.Text = $"✓ {file.Name}";
            LogoStatusText.Visibility = Visibility.Visible;

            if (lastEncoded != null && lastEncodedType == "QR_CODE")
            {
                CreateBarcode(sender, e);
            }
        }
    }

    private void RemoveLogo(object sender, RoutedEventArgs e)
    {
        _logoBitmap?.Dispose();
        _logoBitmap = null;
        RemoveLogoButton.IsEnabled = false;
        LogoStatusText.Visibility = Visibility.Collapsed;

        if (lastEncoded != null && lastEncodedType == "QR_CODE")
        {
            CreateBarcode(sender, e);
        }
    }

    private Bitmap OverlayLogoWithVerification(Bitmap qrBitmap, Bitmap logo, string expectedText)
    {
        var maxLogoPercent = 0.20;
        var minLogoPercent = 0.05;
        var step = 0.02;

        for (var percent = maxLogoPercent; percent >= minLogoPercent; percent -= step)
        {
            var result = OverlayLogo(qrBitmap, logo, percent);
            var decoded = logoVerifyReader.Decode(result);
            if (decoded != null && decoded.Text == expectedText)
            {
                return result;
            }
            result.Dispose();
        }

        EncodeError.Message = "Logo is too large to overlay while keeping the QR code scannable. The QR code was generated without the logo.";
        EncodeError.Title = "Logo Overlay";
        EncodeError.Severity = InfoBarSeverity.Warning;
        EncodeError.IsOpen = true;
        return new Bitmap(qrBitmap);
    }

    private Bitmap OverlayLogo(Bitmap qrBitmap, Bitmap logo, double sizePercent)
    {
        var result = new Bitmap(qrBitmap);
        var logoWidth = (int)(result.Width * sizePercent);
        var logoHeight = (int)(result.Height * sizePercent);

        if (logo.Width > logo.Height)
        {
            logoHeight = (int)(logoWidth * ((double)logo.Height / logo.Width));
        }
        else if (logo.Height > logo.Width)
        {
            logoWidth = (int)(logoHeight * ((double)logo.Width / logo.Height));
        }

        var x = (result.Width - logoWidth) / 2;
        var y = (result.Height - logoHeight) / 2;

        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            var padding = Math.Max(2, logoWidth / 15);
            using (var bgBrush = new SolidBrush(Color.White))
            {
                g.FillRectangle(bgBrush, x - padding, y - padding,
                    logoWidth + padding * 2, logoHeight + padding * 2);
            }

            g.DrawImage(logo, x, y, logoWidth, logoHeight);
        }

        return result;
    }

    private async void BulkEncode(object sender, RoutedEventArgs e)
    {
        if (BulkEncodeTip.IsOpen)
        {
            KillBulkEncode();
            return;
        }

        // Pick CSV file
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var csvPicker = new FileOpenPicker();
        csvPicker.FileTypeFilter.Add(".csv");
        WinRT.Interop.InitializeWithWindow.Initialize(csvPicker, hwnd);
        var csvFile = await csvPicker.PickSingleFileAsync();

        if (csvFile == null) return;

        // Pick output folder
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        folderPicker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        var outputFolder = await folderPicker.PickSingleFolderAsync();

        if (outputFolder == null) return;

        _bulkEncodeOutputPath = outputFolder.Path;

        // Read and parse CSV
        var lines = await File.ReadAllLinesAsync(csvFile.Path);
        if (lines.Length < 2)
        {
            EncodeError.Title = "Error";
            EncodeError.Message = "CSV file must have a header row and at least one data row.";
            EncodeError.Severity = InfoBarSeverity.Error;
            EncodeError.IsOpen = true;
            return;
        }

        // Determine column indices from header
        if (!TryParseCsvLine(lines[0], out var header))
        {
            EncodeError.Title = "Error";
            EncodeError.Message = "The selected CSV is not in a valid format. Please make sure quoted values are properly closed.";
            EncodeError.Severity = InfoBarSeverity.Error;
            EncodeError.IsOpen = true;
            return;
        }

        var textCol = -1;
        var typeCol = -1;
        for (var i = 0; i < header.Length; i++)
        {
            var col = header[i].Trim().ToLowerInvariant();
            if (col == "text" || col == "content" || col == "data" || col == "value")
                textCol = i;
            else if (col == "type" || col == "barcodetype" || col == "barcode type" || col == "format")
                typeCol = i;
        }

        if (textCol == -1)
        {
            EncodeError.Title = "Error";
            EncodeError.Message = "CSV must contain a 'Text' column. Optionally include a 'Type' column (e.g., QR_CODE, CODE_128).";
            EncodeError.Severity = InfoBarSeverity.Error;
            EncodeError.IsOpen = true;
            return;
        }

        var rawDataLines = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var parsedDataLines = new List<string[]>();

        foreach (var rawLine in rawDataLines)
        {
            if (!TryParseCsvLine(rawLine, out var parsedLine) || parsedLine.Length != header.Length)
            {
                EncodeError.Title = "Error";
                EncodeError.Message = "The selected CSV is not in the proper format. Ensure each row has the same number of columns as the header and valid quoted values.";
                EncodeError.Severity = InfoBarSeverity.Error;
                EncodeError.IsOpen = true;
                return;
            }

            parsedDataLines.Add(parsedLine);
        }

        var totalCount = parsedDataLines.Count;
        if (totalCount == 0)
        {
            EncodeError.Title = "Error";
            EncodeError.Message = "No data rows found in the CSV file.";
            EncodeError.Severity = InfoBarSeverity.Error;
            EncodeError.IsOpen = true;
            return;
        }

        // Setup progress UI
        BulkEncodeButton.IsEnabled = false;
        BulkEncodeTip.Title = "Encoding...";
        BulkEncodeProgress.Minimum = 0;
        BulkEncodeProgress.Maximum = totalCount;
        BulkEncodeProgress.Value = 0;
        BulkEncodeProgress.IsActive = true;
        BulkEncodeTotalFiles.Text = "";
        BulkEncodeFailedStat.Text = "";
        BulkEncodeFailedStat.Visibility = Visibility.Collapsed;
        OpenBulkEncodeOutputButton.IsEnabled = false;
        BulkEncodeTip.IsOpen = true;

        var encodedCount = 0;
        var failedCount = 0;

        // Determine default format from current selection
        var defaultFormat = BarcodeSelector.SelectedItem?.ToString() ?? "QR_CODE";

        // Parse current dimension settings
        int.TryParse(userWidth.Text, out var encodeWidth);
        if (encodeWidth <= 0) encodeWidth = 800;
        int.TryParse(userHeight.Text, out var encodeHeight);
        if (encodeHeight <= 0) encodeHeight = 800;
        int.TryParse(userMargin.Text, out var encodeMargin);

        foreach (var cols in parsedDataLines)
        {
            if (!BulkEncodeTip.IsOpen)
            {
                KillBulkEncode();
                return;
            }

            if (textCol >= cols.Length || string.IsNullOrWhiteSpace(cols[textCol]))
            {
                failedCount++;
                BulkEncodeProgress.Value = encodedCount + failedCount;
                continue;
            }

            var text = cols[textCol].Trim();
            var formatStr = (typeCol >= 0 && typeCol < cols.Length && !string.IsNullOrWhiteSpace(cols[typeCol]))
                ? cols[typeCol].Trim()
                : defaultFormat;

            try
            {
                var bulkWriter = new BarcodeWriter();
                bulkWriter.Options.NoPadding = true;
                bulkWriter.Options.Hints.Add(EncodeHintType.CHARACTER_SET, "UTF-8");
                bulkWriter.Format = (BarcodeFormat)Enum.Parse(typeof(BarcodeFormat), formatStr, true);
                bulkWriter.Options.Width = encodeWidth;
                bulkWriter.Options.Height = encodeHeight;
                bulkWriter.Options.Margin = encodeMargin;
                bulkWriter.Options.PureBarcode = true;

                var barcode = bulkWriter.WriteAsBitmap(text);

                var safeFileName = string.Join("_", text.Split(Path.GetInvalidFileNameChars()));
                if (safeFileName.Length > 80) safeFileName = safeFileName.Substring(0, 80);
                var fileName = $"{encodedCount + 1}.{formatStr}.{safeFileName}.png";
                var filePath = Path.Combine(outputFolder.Path, fileName);

                barcode.Save(filePath, ImageFormat.Png);
                barcode.Dispose();
                encodedCount++;
            }
            catch
            {
                failedCount++;
            }

            BulkEncodeProgress.Value = encodedCount + failedCount;
            BulkEncodeTotalFiles.Text = encodedCount + "/" + totalCount + " encoded 🤠";

            if (failedCount > 0)
            {
                BulkEncodeFailedStat.Text = "Failed to encode " + failedCount + " row(s).";
                BulkEncodeFailedStat.Visibility = Visibility.Visible;
            }
            else
            {
                BulkEncodeFailedStat.Visibility = Visibility.Collapsed;
            }
        }

        BulkEncodeTip.Title = "Complete";
        OpenBulkEncodeOutputButton.IsEnabled = true;
        BulkEncodeButton.IsEnabled = true;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = "";
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current += c;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
        }

        fields.Add(current);
        return fields.ToArray();
    }

    private static bool TryParseCsvLine(string line, out string[] fields)
    {
        fields = ParseCsvLine(line);

        var quoteCount = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '"') continue;

            if (i + 1 < line.Length && line[i + 1] == '"')
            {
                i++;
                continue;
            }

            quoteCount++;
        }

        return quoteCount % 2 == 0;
    }

    private void KillBulkEncode()
    {
        BulkEncodeProgress.Value = 0;
        BulkEncodeProgress.IsActive = false;
        BulkEncodeTotalFiles.Text = "";
        BulkEncodeFailedStat.Text = "";
        BulkEncodeFailedStat.Visibility = Visibility.Collapsed;
        OpenBulkEncodeOutputButton.IsEnabled = false;
        BulkEncodeButton.IsEnabled = true;
    }

    private async void OpenBulkEncodeOutput(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_bulkEncodeOutputPath) && Directory.Exists(_bulkEncodeOutputPath))
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_bulkEncodeOutputPath);
            await Launcher.LaunchFolderAsync(folder);
        }
    }

    private async void ShowBulkEncodeInfo(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Bulk Encode – CSV Format",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Your CSV file should have a header row with at least a Text column. " +
                               "An optional Type column lets you specify the barcode format per row.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Accepted column names",
                        Style = (Style)Application.Current.Resources["BaseTextBlockStyle"]
                    },
                    new TextBlock
                    {
                        Text = "• Text column: Text, Content, Data, or Value\n" +
                               "• Type column (optional): Type, BarcodeType, Barcode Type, or Format",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Example CSV",
                        Style = (Style)Application.Current.Resources["BaseTextBlockStyle"]
                    },
                    new TextBlock
                    {
                        Text = "Text,Type\n" +
                               "https://github.com,QR_CODE\n" +
                               "1234567890128,EAN_13\n" +
                               "Hello World,CODE_128",
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        Padding = new Thickness(10),
                    },
                    new TextBlock
                    {
                        Text = "If no Type column is provided, the currently selected barcode format will be used for all rows.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.7
                    }
                }
            }
        };

        await dialog.ShowAsync();
    }
}