using barcodrod.io.ViewModels;
using System.Diagnostics;
using System.Drawing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using ZXing.Windows.Compatibility;
using Windows.Storage;
using Microsoft.UI.Xaml.Controls;
using Image = Microsoft.UI.Xaml.Controls.Image;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture.Frames;
using System.Runtime.InteropServices.WindowsRuntime;
using ZXing;
using System.Net.NetworkInformation;

namespace barcodrod.io.Views;

public sealed partial class EncodePage : Page
{
    private readonly BarcodeWriter writer = new BarcodeWriter();
    public Bitmap lastEncoded;
    private string lastSavedlocation;
    public EncodeViewModel ViewModel
    {
        get;
    }

    public EncodePage()
    {
        ViewModel = App.GetService<EncodeViewModel>();
        InitializeComponent();
    }


    //function to create a barcode from text
    private void CreateBarcode(object sender, RoutedEventArgs e)
    {
        if (BarcodeSelector.SelectedItem == null)
        {
            return;
        }
        var format = BarcodeSelector.SelectedItem.ToString();
        //set the writer formation to the selected format
        writer.Format = (ZXing.BarcodeFormat)Enum.Parse(typeof(ZXing.BarcodeFormat), format);

        writer.Options = new ZXing.Common.EncodingOptions
        {
            Height = 400,
            Width = 400,
            Margin = 4,
        };
        if (TxtActivityLog.Text != null && TxtActivityLog.Text != "")
        {
            try
            {
                Bitmap barcode = writer.WriteAsBitmap(TxtActivityLog.Text);
                BitmapToImageSource(barcode);
                lastEncoded = barcode;
                SaveImageButton.IsEnabled = true;
                EncodeError.Text = "";
            }
            catch (Exception ex)
            {
                EncodeError.Text = ex.Message;

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
        }
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
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = "Generated" + BarcodeSelector.SelectedItem;


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


}
