using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace barcodrod.io.Views;


public partial class DecodePage : Page
{



    private readonly BarcodeReader reader = new BarcodeReader();
    private MediaCapture mediaCaptureManager;
    private MediaFrameReader mediaFrameReader;
    private bool captureManagerInitialized = false;
    private string lastSavedlocation;
    private string lastSavedTextLocation;

    private Image imagePreviewElement;
    private SoftwareBitmap backBitmapBuffer;
    private bool taskFrameRenderRunning = false;
    public Bitmap lastDecoded;
    public string lastDecodedType;
    bool screenshotSuccess;
    public EncodeViewModel ViewModel
    {
        get;
    }


    private void SizeChangedEventHandler(object sender, SizeChangedEventArgs args)
    {

        

        if (dPage.ActualHeight > 100)
        {
            var size = dPage.ActualHeight - (TxtCommandBar.ActualHeight *2);
            BarcodeViewer.MaxHeight = size;
            TxtActivityLog.MaxHeight = size;
            BarcodeViewer.MinHeight = size;
            TxtActivityLog.MinHeight = size;
        }
    }


    protected async override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (captureManagerInitialized != false)
        {
            await CleanupMediaCaptureAsync();
        }
    }


    public DecodePage()
    {


        //ViewModel = App.GetService<DecodeViewModel>();
        InitializeComponent();
        //TxtActivityLog.TextAlignment = TextAlignment.Left;
        reader.Options.TryHarder = true;
        reader.Options.TryInverted = true;
        reader.AutoRotate = true;
        reader.Options.PossibleFormats = new ZXing.BarcodeFormat[]
        {
            ZXing.BarcodeFormat.QR_CODE,
            ZXing.BarcodeFormat.CODE_128,
            ZXing.BarcodeFormat.CODE_39,
            ZXing.BarcodeFormat.EAN_13,
            ZXing.BarcodeFormat.EAN_8,
            ZXing.BarcodeFormat.ITF,
            ZXing.BarcodeFormat.UPC_A,
            ZXing.BarcodeFormat.UPC_E,
            ZXing.BarcodeFormat.CODABAR,
            ZXing.BarcodeFormat.DATA_MATRIX,
            ZXing.BarcodeFormat.MAXICODE,
            ZXing.BarcodeFormat.PDF_417,
            ZXing.BarcodeFormat.RSS_14,
            ZXing.BarcodeFormat.RSS_EXPANDED,
            ZXing.BarcodeFormat.AZTEC,
            ZXing.BarcodeFormat.MSI,
            ZXing.BarcodeFormat.PLESSEY
        };
    }



    public string DecodeBitmap(Bitmap bitmap)
    {
        OpenTextButton.IsEnabled = false;
        OpenImageButton.IsEnabled = false;
        var decoded = reader.Decode(bitmap);
        if (decoded == null)
        {
            var result = "No barcode found. Please try again.";
            stateManager(false);
            return result;

        }
        else
        {
            stateManager(true);
            var result = decoded.Text;
            lastDecodedType = decoded.BarcodeFormat.ToString();
            lastDecoded = bitmap;
            BarcodeType.Message = lastDecodedType;
            return result;
        }
    }


    public bool SilentDecodeBitmap(Bitmap bitmap)
    {
        var decoded = reader.Decode(bitmap);
        if (decoded == null)
        {
            return false;
        }

        else
        {
            return true;
        }
    }

    private void stateManager(bool state)
    {
        if (state == true)
        {

            TxtActivityLog.IsEnabled = true;
            SaveImageButton.IsEnabled = true;
            //OpenImageButton.IsEnabled = true;
            CopyImageButton.IsEnabled = true;
            SaveTextButton.IsEnabled = true;

            CopyTextButton.IsEnabled = true;
            BarcodeType.IsOpen = true;
        }
        else if (state == false)
        {
            TxtActivityLog.IsEnabled = false;
            SaveImageButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            CopyImageButton.IsEnabled = false;
            SaveTextButton.IsEnabled = false;
            OpenTextButton.IsEnabled = false;
            CopyTextButton.IsEnabled = false;
            BarcodeType.Message = "";
            BarcodeType.IsOpen = false;
        }
    }

    private async void ClearState(object sender, RoutedEventArgs e)
    {


        TakePhotoButton.IsEnabled = false;
        TakePhotoBar.Visibility = Visibility.Collapsed;
        TakePhotoButton.Visibility = Visibility.Collapsed;
        TakePhotoBar.IsEnabled = false;
        captureManagerInitialized = false;
        BarcodeViewer.Source = null;
        await CleanupMediaCaptureAsync();
        WebcamButton.Text = "Webcam";

        if (lastDecoded != null)
        {
            lastDecoded.Dispose();
        }
        lastDecodedType = "";
        lastSavedlocation = "";
        lastSavedTextLocation = "";
        stateManager(false);
        DecodeFromFileButton.IsEnabled = true;
        DecodeFromClipboardButton.IsEnabled = true;
        DecodeFromSnippingToolButton.IsEnabled = true;
        TxtActivityLog.Text = "";

    }

    private async void DecodeFromSnippingTool(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now;
        var uri = new System.Uri("ms-screenclip:");
        App.MainWindow.Hide();
        await (Windows.System.Launcher.LaunchUriAsync(uri)).AsTask();
        var screenshotFolder =
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) + @"\Screenshots\";
        //Process.Start("ScreenClippingHost");
        var snippingTool = Process.GetProcessesByName("ScreenClippingHost");
        //.Where(p => p.StartTime > timestamp).FirstOrDefault();
        Process ourSnippingTool = null;
        foreach (var item in snippingTool)
        {
            if (item.StartTime > timestamp)
            {
                ourSnippingTool = item;
                break;
            }
            else
            {
                TxtActivityLog.Text = "Unable to determine if Snipping Tool succesfully launched.";
                return;
            }

        }

        if (ourSnippingTool != null)
        {
            //hide the main app window so the user can't see it
            
            await ourSnippingTool.WaitForExitAsync();
            App.MainWindow.Show();
        }

        DateTime lastWritten = Directory.GetLastWriteTime(screenshotFolder);

        if (lastWritten > timestamp)
        {
            var files = Directory.GetFiles(screenshotFolder);
            //get the most recent created file in files
            foreach (var file in files)
            {
                var fileTime = System.IO.File.GetCreationTime(file);
                if (fileTime > timestamp)
                {
                    //get file path of current object
                    DecodeFromFile(file);
                }
            }
        }
        else if (lastWritten < timestamp)
        {
            TxtActivityLog.Text = "No Snipping Tool screenshot detected. Please make sure you have 'Automatically save screenshots' enabled in Snipping Tool > Settings.";
            BarcodeViewer.Source = null;
        }
    }

    public void DecodeFromFile(string filepath)
    {
        StorageFile file = StorageFile.GetFileFromPathAsync(filepath).GetAwaiter().GetResult();
        var bitmap = new System.Drawing.Bitmap(file.Path);
        var result = DecodeBitmap(bitmap);
        if (result != null)
        {
            TxtActivityLog.Text = result;
            result.GetType().ToString();
            BitmapToImageSource(bitmap);
        }
        else
        {
            TxtActivityLog.Text = "No barcode found. Please try again.";
            BarcodeViewer.Source = null;
        }
    }

    private async void DecodeFromClipboard(object sender, RoutedEventArgs e)
    {
        DataPackageView clipboardContent = Clipboard.GetContent();
        if (clipboardContent.Contains(StandardDataFormats.Bitmap))
        {
            var data = await clipboardContent.GetBitmapAsync();
            var bit = await data.OpenReadAsync();
            System.IO.Stream stream = bit.AsStreamForRead();
            System.Drawing.Bitmap bitmap = new Bitmap(stream);

            var result = DecodeBitmap(bitmap);
            TxtActivityLog.Text = result;
            BitmapToImageSource(bitmap);
        }
    }

    public async void DecodeFromFilePicker(object sender, RoutedEventArgs e)
    {
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".tiff");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".ico");
        picker.FileTypeFilter.Add(".dib");
        picker.FileTypeFilter.Add(".wmf");
        picker.FileTypeFilter.Add(".emf");
        picker.FileTypeFilter.Add(".exif");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".heif");
        picker.FileTypeFilter.Add(".heic");
        picker.FileTypeFilter.Add(".jfif");
        picker.FileTypeFilter.Add(".jpe");
        picker.FileTypeFilter.Add(".jif");
        picker.FileTypeFilter.Add(".jfi");
        picker.FileTypeFilter.Add(".jp2");
        picker.FileTypeFilter.Add(".j2k");
        picker.FileTypeFilter.Add(".jpf");
        picker.FileTypeFilter.Add(".jpx");
        picker.FileTypeFilter.Add(".j2c");
        picker.FileTypeFilter.Add(".fpx");
        picker.FileTypeFilter.Add(".pcd");
        picker.FileTypeFilter.Add(".svg");
        picker.FileTypeFilter.Add(".svgz");
        picker.FileTypeFilter.Add(".ai");
        picker.FileTypeFilter.Add(".eps");
        picker.FileTypeFilter.Add(".ps");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();

        if (file != null)
        {
            var bitmap = new System.Drawing.Bitmap(file.Path);
            var result = DecodeBitmap(bitmap);
            TxtActivityLog.Text = result;
            BitmapToImageSource(bitmap);
        }

    }




    public async void SaveBitmapToFile(Bitmap bitmap, StorageFile file)
    {
        bitmap = lastDecoded;
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
        picker.SuggestedFileName = timestamp + "." + lastDecodedType;


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

        SaveBitmapToFile(lastDecoded, path);

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

    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (lastSavedlocation != "" && lastSavedlocation != null)
        {
            var options = new Windows.System.LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedlocation), options);
        }
    }

    //function to copy the decoded bitmap to the user's clipboard as a pastable image
    private async void CopyImage(object sender, RoutedEventArgs e)
    {
        if (lastDecoded != null)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("temp.png", CreationCollisionOption.ReplaceExisting);
            lastDecoded.Save(file.Path, ImageFormat.Png);
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private async void SaveText(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedFileName = timestamp;

        picker.FileTypeChoices.Add("Text", new List<string>() { ".txt" });

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var path = await picker.PickSaveFileAsync();

        if (path != null)
        {
            await FileIO.WriteTextAsync(path, TxtActivityLog.Text);
            lastSavedTextLocation = path.Path;
            OpenTextButton.IsEnabled = true;
        }



    }

    private async void OpenText(object sender, RoutedEventArgs e)
    {
        if (lastSavedTextLocation != "")
        {

            var options = new Windows.System.LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedTextLocation), options);
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
            lastDecoded = bitmap;
        }
    }

    private async void DecodeFromWebcam(object sender, RoutedEventArgs e)
    {
        if (captureManagerInitialized == false)
        {
            StartPreviewAsync();
            WebcamButton.Text = "Stop Webcam";
            //CopyImageButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            SaveImageButton.IsEnabled = false;
            CopyImageButton.IsEnabled = false;
            TakePhotoButton.IsEnabled = true;
            TakePhotoButton.Visibility = Visibility.Visible;
            TakePhotoBar.IsEnabled = true;
            TakePhotoBar.Visibility = Visibility.Visible;
            DecodeFromClipboardButton.IsEnabled = false;
            DecodeFromFileButton.IsEnabled = false;
            DecodeFromClipboardButton.IsEnabled = false;
            DecodeFromSnippingToolButton.IsEnabled = false;

            await CleanupMediaCaptureAsync();



        }
        else if (captureManagerInitialized == true)
        {
            BarcodeViewer.Source = null;
            await CleanupMediaCaptureAsync();
            WebcamButton.Text = "Webcam";
            TakePhotoButton.IsEnabled = false;
            TakePhotoButton.Visibility = Visibility.Collapsed;
            TakePhotoBar.IsEnabled = false;
            TakePhotoBar.Visibility = Visibility.Collapsed;
            captureManagerInitialized = false;
            DecodeFromFileButton.IsEnabled = true;
            DecodeFromClipboardButton.IsEnabled = true;
            DecodeFromSnippingToolButton.IsEnabled = true;
        }


    }

    private async void StartPreviewAsync()
    {
        if (captureManagerInitialized == true)
        {
            return;
        }

        try
        {
            captureManagerInitialized = true;
            //1. Select frame sources and frame source groups//
            var frameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();
            if (frameSourceGroups.Count <= 0)
            {
                TxtActivityLog.Text = "No source groups found.";
                return;
            }

            //Get the first frame source group and first frame source, Or write your code to select them//
            MediaFrameSourceGroup selectedFrameSourceGroup = frameSourceGroups[0];
            MediaFrameSourceInfo frameSourceInfo = selectedFrameSourceGroup.SourceInfos[0];

            //2. Initialize the MediaCapture object to use the selected frame source group//
            mediaCaptureManager = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                SourceGroup = selectedFrameSourceGroup,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

            await mediaCaptureManager.InitializeAsync(settings);

            //3. Initialize Image Preview Element with xaml Image Element.//
            imagePreviewElement = BarcodeViewer;
            imagePreviewElement.Source = new SoftwareBitmapSource();

            //4. Create a frame reader for the frame source//
            MediaFrameSource mediaFrameSource = mediaCaptureManager.FrameSources[frameSourceInfo.Id];
            mediaFrameReader = await mediaCaptureManager.CreateFrameReaderAsync(mediaFrameSource, MediaEncodingSubtypes.Argb32);
            mediaFrameReader.FrameArrived += FrameReader_FrameArrived;
            await mediaFrameReader.StartAsync();

            captureManagerInitialized = true;
            TxtActivityLog.Text = "Webcam preview from device: " + selectedFrameSourceGroup.DisplayName;

        }
        catch (Exception Exc)
        {
            TxtActivityLog.Text = "MediaCapture initialization failed: " + Exc.Message;
        }
    }

    private async Task CleanupMediaCaptureAsync()
    {
        if (mediaCaptureManager != null)
        {
            using (var mediaCapture = mediaCaptureManager)
            {
                mediaCaptureManager = null;

                mediaFrameReader.FrameArrived -= FrameReader_FrameArrived;
                await mediaFrameReader.StopAsync();
                mediaFrameReader.Dispose();
            }
        }

        captureManagerInitialized = false;
        stateManager(false);
        TxtActivityLog.Text = "Webcam preview has canceled.";
    }

    private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        var mediaFrameReference = sender.TryAcquireLatestFrame();
        var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
        var softwareBitmap = videoMediaFrame?.SoftwareBitmap;

        //HERE IS WHERE WE CAN WORK WITH THE BITMAP SOURCE

        if (softwareBitmap != null)
        {
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            // Swap the processed frame to backBuffer and dispose of the unused image.
            softwareBitmap = Interlocked.Exchange(ref backBitmapBuffer, softwareBitmap);
            softwareBitmap?.Dispose();

            // Changes to XAML ImageElement must happen on UI thread through Dispatcher
            //var task = imagePreviewElement.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            _ = imagePreviewElement.DispatcherQueue.TryEnqueue(async () =>
            {
                // Don't let two copies of this task run at the same time.
                if (taskFrameRenderRunning)
                {
                    return;
                }
                taskFrameRenderRunning = true;

                // Keep draining frames from the backbuffer until the backbuffer is empty.
                SoftwareBitmap latestBitmap;

                while (((latestBitmap = Interlocked.Exchange(ref backBitmapBuffer, null)) != null) && (screenshotSuccess == false))
                {
                    var imageSource = (SoftwareBitmapSource)imagePreviewElement.Source;
                    await imageSource.SetBitmapAsync(latestBitmap);
                    //HERE IS WHERE WE CAN WORK WITH THE BITMAP SOURCE

                    latestBitmap.Dispose();
                }

                taskFrameRenderRunning = false;
            });
        }

        if (mediaFrameReference != null)
        {
            mediaFrameReference.Dispose();
        }
    }

    private async void CapturePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (captureManagerInitialized == false)
        {
            return;
        }

        try
        {
            ImageEncodingProperties imgFormat = ImageEncodingProperties.CreateBmp();

            StorageFolder picturesFolder = KnownFolders.PicturesLibrary;
            StorageFolder barcodeFolder = await picturesFolder.CreateFolderAsync("barcodrod.io", CreationCollisionOption.OpenIfExists);

            if (Directory.Exists(barcodeFolder.Path))
            {

                var file = await barcodeFolder.CreateFileAsync("webcamphoto.png", CreationCollisionOption.ReplaceExisting);
                //get the current image in BarcodeViewer and save it to the file variable
                await mediaCaptureManager.CapturePhotoToStorageFileAsync(imgFormat, file);
                screenshotSuccess = true;
                //set the image source to the file variable
                BarcodeViewer.Source = new BitmapImage(new Uri(file.Path));
                await CleanupMediaCaptureAsync();
                BarcodeViewer.Source = null;
                WebcamButton.Text = "Webcam";
                stateManager(false);
                DecodeFromFile(file.Path);


                screenshotSuccess = false;
                DecodeFromSnippingToolButton.IsEnabled = true;
                DecodeFromFileButton.IsEnabled = true;
                DecodeFromClipboardButton.IsEnabled = true;
                TakePhotoButton.Visibility = Visibility.Collapsed;
                TakePhotoBar.Visibility = Visibility.Collapsed;

            }
        }
        catch (Exception Exc)
        {
            TxtActivityLog.Text = Exc.Message;
        }
    }

    private void CopyTextToClipboard(object sender, RoutedEventArgs e)
    {
        DataPackage dataPackage = new DataPackage();
        dataPackage.SetText(TxtActivityLog.Text);
        Clipboard.SetContent(dataPackage);
    }

}