﻿using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Streams;
using ZXing;
using ZXing.QrCode.Internal;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace barcodrod.io.Views;

public sealed partial class EncodePage : Page
{
    private readonly BarcodeWriter writer = new();
    public Bitmap lastEncoded;
    private Color lastColor;
    private Color lastBgColor;
    private string lastSavedlocation;
    private string lastEncodedType;
    public EncodeViewModel ViewModel { get; }

    private void SizeChangedEventHandler(object sender, SizeChangedEventArgs args)
    {
        BarcodeViewer.MinHeight = TxtActivityLog.ActualHeight;
    }

    public EncodePage()
    {
        InitializeComponent();
        writer.Options.NoPadding = true;
    }

    //function to copy the decoded bitmap to the user's clipboard as a pastable image
    private async void CopyImage(object sender, RoutedEventArgs e)
    {
        if (lastEncoded != null)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("temp.png",
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
                var barcode = writer.WriteAsBitmap(TxtActivityLog.Text);
                BitmapToImageSource(barcode);
                BarcodeViewer.MaxHeight = TxtActivityLog.ActualHeight;
                BarcodeViewer.MinHeight = TxtActivityLog.ActualHeight;
                lastEncoded = barcode;
                SaveImageButton.IsEnabled = true;
                CopyImageButton.IsEnabled = true;
                ChangeBarcodeColorButton.IsEnabled = true;
                EncodeError.Message = "";
                EncodeError.Title = "";
                EncodeError.IsOpen = false;
                lastEncodedType = format;
                addToHistory(TxtActivityLog.Text, barcode);
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
            //Convert BarcodeViewer.Source to bitmap

            //Bitmap bitmap = writer.WriteAsBitmap(TxtActivityLog.Text);

            bitmap.Save(file.Path, ImageFormat.Png);
            //resize the bitmap to the currently selected user width and height withouth changing the aspect ratio

            //get the ratio of the user's selected width and height
            var ratio = (double)writer.Options.Width / (double)writer.Options.Height;
            //get the ratio of the bitmap's width and height
            var bitmapRatio = (double)bitmap.Width / (double)bitmap.Height;
            //if the bitmap's ratio is greater than the user's ratio, then the bitmap's width is greater than the user's width
            //so we need to resize the bitmap's width to the user's width and then resize the height to maintain the aspect ratio
            if (bitmapRatio > ratio)
            {
                //resize the bitmap's width to the user's width
                var resizedBitmap = new Bitmap(bitmap,
                    new Size(writer.Options.Width, (int)(writer.Options.Width / bitmapRatio)));
                resizedBitmap.Save(file.Path, ImageFormat.Png);
                return;
            }
            //if the bitmap's ratio is less than the user's ratio, then the bitmap's height is greater than the user's height
            //so we need to resize the bitmap's height to the user's height and then resize the width to maintain the aspect ratio
            else if (bitmapRatio < ratio)
            {
                //resize the bitmap's height to the user's height
                var resizedBitmap = new Bitmap(bitmap,
                    new Size((int)(writer.Options.Height * bitmapRatio), writer.Options.Height));
                resizedBitmap.Save(file.Path, ImageFormat.Png);
                return;
            }

            //Bitmap resizedBitmap = new Bitmap(bitmap, new System.Drawing.Size(writer.Options.Width, writer.Options.Height));
            //resizedBitmap.Save(file.Path, ImageFormat.Png);
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
        var localFolder = ApplicationData.Current.LocalFolder;
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
            var localFolder = ApplicationData.Current.LocalFolder;
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
}