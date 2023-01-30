using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

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

    private void SizeChangedEventHandler(object sender, SizeChangedEventArgs args)
    {
        if (ePage.ActualHeight > 150)
        {
            var size = ePage.ActualHeight - EncodeButton.ActualHeight;
            //BarcodeViewer.MaxHeight = TxtActivityLog.ActualHeight;
            BarcodeViewer.MinHeight = TxtActivityLog.ActualHeight;
            TxtActivityLog.MaxHeight = ePage.ActualHeight - (SaveImageButton.ActualHeight*2);
            TxtActivityLog.MinHeight = ePage.ActualHeight - (SaveImageButton.ActualHeight * 2);
            
            
            BarcodeSelector.MaxHeight = ePage.ActualHeight - SaveImageButton.ActualHeight - EncodeButton.ActualHeight - 10;
            BarcodeSelector.MinHeight = ePage.ActualHeight - SaveImageButton.ActualHeight - EncodeButton.ActualHeight - 10;
            //TxtActivityLog.Height = dPage.ActualHeight - 30 - TxtCommandBar.ActualHeight;
            //TxtActivityLog.MaxHeight = TxtStackPanel.ActualHeight - 40 - TxtCommandBar.ActualHeight;
        }
    }

    public EncodePage()
    {
        //ViewModel = App.GetService<EncodeViewModel>();
        InitializeComponent();
        BarcodeSelector.MinHeight = ePage.ActualHeight - EncodeButton.ActualHeight;
        BarcodeSelector.MaxHeight = ePage.ActualHeight - EncodeButton.ActualHeight;
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
                EncodeError.Text = "";

            }
            catch (Exception ex)
            {
                EncodeError.Text = ex.Message;
                SaveImageButton.IsEnabled = false;
                CopyImageButton.IsEnabled = false;
                

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


}
