using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Streams;
using ZXing;
using ZXing.QrCode.Internal;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace barcodrod.io.Views;

public sealed partial class EncodePage : Page
{
    private readonly BarcodeWriter writer = new BarcodeWriter();
    public Bitmap lastEncoded;
    private Color lastColor;
    private Color lastBgColor;
    private string lastSavedlocation;
    public EncodeViewModel ViewModel
    {
        get;
    }

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
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("temp.png", CreationCollisionOption.ReplaceExisting);
            lastEncoded.Save(file.Path, ImageFormat.Png);
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private void SetErrorCorrectionLevel(object sender, RoutedEventArgs e)
    {
        if (writer.Options.Hints.ContainsKey(EncodeHintType.ERROR_CORRECTION))
        {
            writer.Options.Hints.Remove(EncodeHintType.ERROR_CORRECTION);
        }
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
        if (lastEncoded != null)
        { lastEncoded.Dispose(); }
        if (BarcodeSelector.SelectedItem == null)
        {
            return;
        }
        var format = BarcodeSelector.SelectedItem.ToString();
        //set the writer formation to the selected format
        writer.Format = (ZXing.BarcodeFormat)Enum.Parse(typeof(ZXing.BarcodeFormat), format);

        //store the text of userWidth and userHeight into two variables

        int defaultHeight = 800;
        int defaultWidth = 800;
        int height;
        int width;
        //check if the user entered a value for width and height and it is a number
        if (int.TryParse(userHeight.Text, out height) == true)
        {
            writer.Options.Height = height;
        }
        else
        {
            writer.Options.Height = defaultHeight;
        }

        if (int.TryParse(userWidth.Text, out width) == true)
        {
            writer.Options.Width = width;
        }
        else
        {
            writer.Options.Width = defaultWidth;
        }
        writer.Options.PureBarcode = true;
        writer.Options.Margin = 0;

        if (TxtActivityLog.Text != null && TxtActivityLog.Text != "")
        {
            try
            {
                Bitmap barcode = writer.WriteAsBitmap(TxtActivityLog.Text);
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

    }

    public async void BitmapToImageSource(System.Drawing.Bitmap bitmap)
    {
        using (MemoryStream memory = new MemoryStream())
        {
            bitmap.Save(memory, ImageFormat.Bmp);
            memory.Position = 0;
            BitmapImage bitmapimage = new BitmapImage();
            await bitmapimage.SetSourceAsync(memory.AsRandomAccessStream());
            Image image = new Image();
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
        System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, bitmap.PixelFormat);

        // Get the number of bytes per pixel
        int bytesPerPixel = System.Drawing.Bitmap.GetPixelFormatSize(bitmap.PixelFormat) / 8;

        // Get the stride (number of bytes per row)
        int stride = bmpData.Stride;

        // Iterate over all pixels and replace black pixels with red
        byte[] pixelRow = new byte[stride];
        for (int y = 0; y < bmpData.Height; y++)
        {
            // Copy the pixel row into a byte array
            Marshal.Copy(bmpData.Scan0 + y * stride, pixelRow, 0, stride);

            for (int x = 0; x < bmpData.Width; x++)
            {
                // Get the pixel value at (x,y)
                byte b = pixelRow[x * bytesPerPixel];
                byte g = pixelRow[x * bytesPerPixel + 1];
                byte r = pixelRow[x * bytesPerPixel + 2];

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
        lastColor = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
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
        System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, bitmap.PixelFormat);

        // Get the number of bytes per pixel
        int bytesPerPixel = System.Drawing.Bitmap.GetPixelFormatSize(bitmap.PixelFormat) / 8;

        // Get the stride (number of bytes per row)
        int stride = bmpData.Stride;

        // Iterate over all pixels and replace black pixels with chosen color
        byte[] pixelRow = new byte[stride];
        for (int y = 0; y < bmpData.Height; y++)
        {
            // Copy the pixel row into a byte array
            Marshal.Copy(bmpData.Scan0 + y * stride, pixelRow, 0, stride);

            for (int x = 0; x < bmpData.Width; x++)
            {
                // Get the pixel value at (x,y)
                byte b = pixelRow[x * bytesPerPixel];
                byte g = pixelRow[x * bytesPerPixel + 1];
                byte r = pixelRow[x * bytesPerPixel + 2];




                // Check if the pixel is white or the same color as the last background color
                if ((r == 255 && g == 255 && b == 255) || (r == lastBgColor.R && g == lastBgColor.G && b == lastBgColor.B))
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
        lastBgColor = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        SameColorWarning.Visibility = Visibility.Collapsed;


    }

    private async void OpenColorPicker(object sender, RoutedEventArgs e)
    {
        TeachingTip.IsOpen = true;
    }


    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (lastSavedlocation != "" && lastSavedlocation != null)
        {

            var options = new Windows.System.LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedlocation), options);
        }
    }

    public async void SaveBitmapToFile(Bitmap bitmap, StorageFile file)
    {
        bitmap = lastEncoded;
        if (file != null)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                bitmap.Save(stream.AsStream(), ImageFormat.Bmp);
            }
        }

    }

    private async void SaveImage(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");

        var window = new Microsoft.UI.Xaml.Window();
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

        SaveBitmapToFile(lastEncoded, path);

        if (path != null)
        {
            OpenImageButton.IsEnabled = true;
            lastSavedlocation = path.Path;
        }

        else if (path == null)
        {
            OpenImageButton.IsEnabled = false;
        }


    }

    private void GenerateWiFiQRCode(object sender, RoutedEventArgs e)
    {
        var encryptionType = "";
        var networkName = SSID.Text;
        var password = Password.Text;
        if (WifiSecurityButtonWEP.IsChecked == true)
        {
            encryptionType = "WEP";
        }
        else if (WifiSecurityButtonWPA.IsChecked == true)
        {
            encryptionType = "WPA";
        }
        TxtActivityLog.Text = $"WIFI:T:{encryptionType};S:{networkName};P:{password};;";
    }

    private void GenerateEmailQRCode(object sender, RoutedEventArgs e)
    {
        var email = EmailAddress.Text;
        var subject = EmailSubject.Text;
        var body = EmailBody.Text;
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
        TxtActivityLog.Text = $"BEGIN:VCARD\nVERSION:3.0\nN:{lastName};{firstName};;;\nFN:{firstName} {lastName}\nTEL;TYPE=WORK,VOICE:{phoneNumber}\nTEL;TYPE=WORK,CELL:{cellNumber}\nEMAIL:{email}\nADR;TYPE=WORK,PREF:;;{address};;;\nORG:{company}\nTITLE:{jobTitle}\nURL:{website}\nEND:VCARD";
    }
}
