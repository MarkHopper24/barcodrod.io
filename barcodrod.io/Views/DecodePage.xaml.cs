using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using WinRT;
using WinRT.Interop;
using ZXing.Windows.Compatibility;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace barcodrod.io.Views;

public partial class DecodePage : Page
{
    private BarcodeReader? reader = new();
    private FilterInfoCollection VideoCaptureDevices;
    private VideoCaptureDevice? SelectedDSSource;
    private Bitmap? detectedCode;
    private string? lastSavedlocation;
    private string? lastSavedTextLocation;
    private string? lastSavedCSVLocation;
    private string? WifiWithTags;
    private double? frameCount;
    private Bitmap? lastDecoded;
    private string? lastDecodedType;
    private StreamWriter? sw;
    private LauncherOptions? _launcherOptions;
    private StorageFile? currentLogPath;
    private StorageFolder? localFolder;


    public DecodePage()
    {
        InitializeComponent();
        InitializeLog();
        VideoCaptureDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        if (VideoCaptureDevices.Count > 0)
        {
            foreach (FilterInfo VideoCaptureDevice in VideoCaptureDevices) comboBox1.Items.Add(VideoCaptureDevice.Name);

            if (SelectedDSSource != null)
                for (var i = 0; i < SelectedDSSource.VideoCapabilities.Length; i++)
                {
                    var resolution_size = SelectedDSSource.VideoCapabilities[i].FrameSize.Width.ToString() + " x " +
                                          SelectedDSSource.VideoCapabilities[i].FrameSize.Height.ToString();


                    comboBox2.Items.Add(resolution_size);
                }

            comboBox2.SelectedIndex = 0;

            //check if windows camera app is installed through uri scheme
            var uri = new Uri("microsoft.windows.camera:");
            var canLaunch = Launcher
                .QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri, "Microsoft.WindowsCamera_8wekyb3d8bbwe").AsTask()
                .Result;
            if (canLaunch == LaunchQuerySupportStatus.Available) comboBox1.Items.Add("Windows Camera app");
        }

        if (comboBox1.Items.Count == 0)
        {
            DirectShowButton.IsEnabled = false;
            comboBox1.Visibility = Visibility.Collapsed;
            comboBox2.Visibility = Visibility.Collapsed;
        }

        reader.Options.TryHarder = true;
        reader.Options.TryInverted = true;
        reader.AutoRotate = true;
        reader.Options.PureBarcode = false;

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

    public async Task InitializeLog()
    {
        try
        {
            localFolder = ApplicationData.Current.LocalFolder;

            //create log file based on current date and time
            var logFileName = "log.txt";

            //get the size of log.txt if it exists
            if (File.Exists(Path.Combine(localFolder.Path, logFileName)))
            {
                currentLogPath = await localFolder.GetFileAsync(logFileName);
                Log("Decode page loaded.");
                Log(VideoCaptureDevices.Count.ToString() + " capture devices found.");
                Log("BarcodeReader initialized.");
            }

            return;
        }
        catch
        {
            return;
        }
    }

    public void Log(string message)
    {
        try
        {
            if (currentLogPath != null)
            {
                var logMessage = DateTime.Now.ToString() + ": " + message + "\n";
                File.AppendAllText(currentLogPath.Path, logMessage);
            }
        }
        catch
        {
            return;
        }
    }

    private bool DidDecodeSucceed(int scanResult)
    {
        Log("Decode result: " + scanResult.ToString());
        switch (scanResult)
        {
            case 0:
                ZoomToggle.IsEnabled = true;
                ScanResult.Severity = InfoBarSeverity.Success;
                ScanResult.Title = "Success! ";
                ScanResult.Message = lastDecodedType + " detected.";
                ScanResult.IsOpen = true;
                return true;

            case 1:
                TxtActivityLog.Text = "";
                ZoomToggle.IsEnabled = false;
                ScanResult.Severity = InfoBarSeverity.Informational;
                ScanResult.Title = "No barcode detected. ";
                ScanResult.Message = "Please try again.";
                ScanResult.IsOpen = true;
                OpenTextWithButton.IsEnabled = false;
                return false;
            case 2:
                TxtActivityLog.Text = "";
                ZoomToggle.IsEnabled = false;
                ScanResult.Title = "Error";
                ScanResult.Severity = InfoBarSeverity.Error;
                ScanResult.Message = "Snipping Tool failed to launch. Please try again.";
                ScanResult.IsOpen = true;
                OpenTextWithButton.IsEnabled = false;
                return false;
            case 3:
                TxtActivityLog.Text = "";
                ZoomToggle.IsEnabled = false;
                ScanResult.Title = "No screenshot detected.";
                ScanResult.Severity = InfoBarSeverity.Informational;
                ScanResult.Message =
                    "Please make sure 'Automatically save screenshots' is enabled in Snipping Tool > Settings.";
                ScanResult.IsOpen = true;
                OpenTextWithButton.IsEnabled = false;
                return false;
            case 4:
                TxtActivityLog.Text = "";
                ZoomToggle.IsEnabled = false;
                ScanResult.Title = "Error";
                ScanResult.Severity = InfoBarSeverity.Error;
                ScanResult.Message = "No image files detected in selected folder.";
                ImageFolderButton.IsEnabled = true;
                ScanResult.IsOpen = true;
                OpenTextWithButton.IsEnabled = false;
                return false;
            case 5:
                TxtActivityLog.Text = "";
                ZoomToggle.IsEnabled = false;
                ScanResult.Title = "No result";
                OpenTextWithButton.IsEnabled = false;
                ScanResult.Severity = InfoBarSeverity.Informational;
                ScanResult.Message = "Did not recieve an image from the Windows Camera app.";
                ScanResult.IsOpen = true;
                return false;
            case 6:
                ScanResult.Title = "No apps found";
                ScanResult.Severity = InfoBarSeverity.Informational;
                ScanResult.Message = "Please check 'Apps > Default apps' in Windows settings.";
                ScanResult.IsOpen = true;

                return true;
            default: return false;
        }
    }

    private void ShowMenu(bool isTransient)
    {
        var myOption = new FlyoutShowOptions();
        myOption.ShowMode = FlyoutShowMode.Transient;
        ImageRightClickCommandBar.ShowAt(BarcodeViewer, myOption);
    }

    public string DecodeBitmap(Bitmap bitmap)
    {
        Log("Decoding Bitmap.");
        BarcodeViewer.Visibility = Visibility.Visible;
        OpenTextButton.IsEnabled = false;
        OpenImageButton.IsEnabled = false;
        ShareCommandBarButton.Visibility = Visibility.Collapsed;
        var decoded = reader.Decode(bitmap);
        if (decoded == null)
        {
            DidDecodeSucceed(1);
            string? result = null;
            stateManager(false);
            return result;
        }
        else
        {
            stateManager(true);
            var result = decoded.Text;
            lastDecodedType = decoded.BarcodeFormat.ToString();
            Log("Decoded type: " + lastDecodedType);
            lastDecoded = bitmap;
            if (lastDecoded != null)
            {
                Log("Adding to history folder.");
                addToHistory(result, lastDecoded);
            }

            DidDecodeSucceed(0);
            if (IsWifiCode(result))
                ClearTagsButton.Visibility = Visibility.Visible;
            else
                ClearTagsButton.Visibility = Visibility.Collapsed;

            return result;
        }
    }

    public bool IsWifiCode(string result)
    {
        var pattern = @"WIFI:T:(?<sec>[^;]+);S:(?<ssid>[^;]+);P:(?<password>[^;]+);";
        var match = Regex.Match(result, pattern);

        if (match.Success)
        {
            Log("Result contains wifi tags.");
            WifiWithTags = result;
            return true;
        }
        else
        {
            return false;
        }
    }

    public void ClearWifiTags(object sender, RoutedEventArgs e)
    {
        var pattern = @"WIFI:T:(?<sec>[^;]+);S:(?<ssid>[^;]+);P:(?<password>[^;]+);";
        var match = Regex.Match(TxtActivityLog.Text, pattern);
        if (match.Success)
        {
            var ssid = match.Groups["ssid"].Value;
            var password = match.Groups["password"].Value;
            //string sec = match.Groups["sec"].Value;
            var result = $"{ssid}\n{password}";
            TxtActivityLog.Text = result;
            ClearTagsButton.Icon = new SymbolIcon(Symbol.Add);
            Log("Removing Wi-fi tags.");
        }
        else
        {
            Log("Re-adding Wi-fi tags.");
            TxtActivityLog.Text = WifiWithTags;
            ClearTagsButton.Icon = new SymbolIcon(Symbol.Remove);
        }
    }

    private void DirectShowSourceChanged(object sender, RoutedEventArgs e)
    {
        if (comboBox1.SelectedItem != null) DirectShowButton.IsEnabled = true;

        killVideoFeed();

        if (comboBox1.Items.Count > 0)
        {
            if (comboBox1.SelectedItem.ToString() == "Windows Camera app")
            {
                comboBox2.Visibility = Visibility.Collapsed;
                DirectShowButton.Tapped -= InitializeDirectShowCam;
                DirectShowButton.Tapped += CustomCameraCaptureUI;
            }

            if (comboBox1.SelectedItem.ToString() != "Windows Camera app")
            {
                comboBox2.Visibility = Visibility.Visible;

                DirectShowButton.Tapped -= CustomCameraCaptureUI;
                DirectShowButton.Tapped += InitializeDirectShowCam;
                //killVideoFeed();
                SelectedDSSource = new VideoCaptureDevice(VideoCaptureDevices[comboBox1.SelectedIndex].MonikerString);
            }

            Log("DirectShow source changed to " + comboBox1.SelectedItem.ToString());
        }


        if (SelectedDSSource != null)
        {
            comboBox2.Items.Clear();
            for (var i = 0; i < SelectedDSSource.VideoCapabilities.Length; i++)
            {
                var resolution_size = SelectedDSSource.VideoCapabilities[i].FrameSize.Width.ToString() + " x " +
                                      SelectedDSSource.VideoCapabilities[i].FrameSize.Height.ToString();
                comboBox2.Items.Add(resolution_size);
                comboBox2.SelectedIndex = 0;
            }
        }
    }

    private void killVideoFeed()
    {
        Log("Killing DirectShow feed.");
        if (SelectedDSSource == null)
        {
            DirectShowButtonTextBlock.Text = "Webcam";
            return;
        }

        SelectedDSSource.SignalToStop();
        //SelectedDSSource.Stop();
        SelectedDSSource.NewFrame -= new NewFrameEventHandler(SelectedDSSource_NewFrame);
        SelectedDSSource = null;
        DirectShowButtonTextBlock.Text = "Webcam";
        BarcodeViewer.ClearValue(Image.SourceProperty);
    }

    private async void StopVideoFeed(object sender, RoutedEventArgs e)
    {
        killVideoFeed();
        DirectShowButtonTextBlock.Text = "Webcam";
    }

    public async void InitializeDirectShowCam(object sender, RoutedEventArgs e)
    {
        if (SelectedDSSource == null)
            SelectedDSSource = new VideoCaptureDevice(VideoCaptureDevices[comboBox1.SelectedIndex].MonikerString);

        if (SelectedDSSource.IsRunning == true)
        {
            BarcodeViewer.ClearValue(Image.SourceProperty);

            killVideoFeed();
            return;
        }

        frameCount = 0;
        detectedCode = null;

        SelectedDSSource.Source = VideoCaptureDevices[comboBox1.SelectedIndex].MonikerString;
        SelectedDSSource.VideoResolution = SelectedDSSource.VideoCapabilities[comboBox2.SelectedIndex];


        //BarcodeViewer.MaxHeight = SelectedDSSource.VideoCapabilities[comboBox2.SelectedIndex].FrameSize.Height;
        //BarcodeViewer.MaxWidth = SelectedDSSource.VideoCapabilities[comboBox2.SelectedIndex].FrameSize.Width;
        SelectedDSSource.NewFrame += new NewFrameEventHandler(SelectedDSSource_NewFrame);

        SelectedDSSource.Start();
        if (SelectedDSSource.IsRunning == true) DirectShowButtonTextBlock.Text = "Stop Video";

        //wait for a barcode to be detected


        while (detectedCode == null) await Task.Delay(1000);

        var result = DecodeBitmap(detectedCode);
        if (result != null)
        {
            DidDecodeSucceed(0);
            TxtActivityLog.Text = result;
            BitmapToImageSource(detectedCode);
            DirectShowButtonTextBlock.Text = "Webcam";
        }
    }

    private void SelectedDSSource_NewFrame(object sender, NewFrameEventArgs frameEventArgs)
    {
        frameCount++;
        using (var video = (Bitmap)frameEventArgs.Frame.Clone())
        {
            var memory = new MemoryStream();
            video.Save(memory, ImageFormat.Bmp);
            memory.Position = 0;
            if (frameCount % 10 == 0)
                if (SilentDecodeBitmap(video) == true)
                {
                    detectedCode = (Bitmap)video.Clone();
                    DispatcherQueue.TryEnqueue(() => { killVideoFeed(); });
                    return;
                }

            if (DispatcherQueue == null)
            {
                killVideoFeed();
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                var _ = new BitmapImage();
                _.SetSource(memory.AsRandomAccessStream());
                BarcodeViewer.SetValue(Image.SourceProperty, _);
            });
        }
    }

    private async Task<bool> IsResultURI()
    {
        Uri uri;
        try
        {
            uri = new Uri(TxtActivityLog.Text);
            var success = await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri);
            //var success = await Launcher.Launch­Uri­Async(uri);
            if (success != 0)
            {
                return false;
            }
            else
            {
                Log("Result is a URI and at least 1 supporting app has been found.");

                return true;
            }
        }
        catch (Exception ex)
        {
            Log("Exception details:");
            Log(ex.ToString());
            Log(ex.GetBaseException().ToString());
            Log(ex.Message);
            //not a registered URI
            return false;
        }
    }

    public bool SilentDecodeBitmap(Bitmap bitmap)
    {
        var decoded = reader.Decode(bitmap);
        if (decoded == null)
        {
            bitmap.Dispose();
            return false;
        }

        else
        {
            return true;
        }
    }

    private async Task<bool> IsHistoryEnabled()
    {
        try
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
            {
                Log("History enabled: " + currentHistorySetting.Value<bool>().ToString());
                return currentHistorySetting.Value<bool>();
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log("Error loading settings file. Exception details:");
            Log(ex.ToString());
            Log(ex.GetBaseException().ToString());
            Log(ex.Message);
            return false;
        }
    }

    //create 2 new files in the app's local folder. One for text and one for images
    private async Task addToHistory(string text, Bitmap bitmap)
    {
        try
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
                var historyFolder =
                    await localFolder.CreateFolderAsync("History", CreationCollisionOption.OpenIfExists);

                var files = await historyFolder.GetFilesAsync();
                string textFile;
                string imageFile;
                string textPath;
                string imagePath;
                var timestamp = DateTime.Now.ToString("MMddyy.HHmmssfff");

                var imageFileName = lastDecodedType + "." + timestamp + ".png";
                var textFileName = lastDecodedType + "." + timestamp + ".txt";

                textPath = Path.Combine(historyFolder.Path, textFileName);
                imagePath = Path.Combine(historyFolder.Path, imageFileName);
                File.WriteAllText(textPath, text);
                bitmap.Save(imagePath, ImageFormat.Png);

                return;
            }
        }
        catch (Exception ex)
        {
            Log("Error adding item to history. Exception details:");
            Log(ex.ToString());
            Log(ex.GetBaseException().ToString());
            Log(ex.Message);
            return;
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
            ZoomToggle.IsEnabled = true;
        }
        else if (state == false)
        {
            ZoomSlider.IsEnabled = false;
            ZoomToggle.IsEnabled = false;
            TxtActivityLog.IsEnabled = false;
            SaveImageButton.IsEnabled = false;
            OpenImageButton.IsEnabled = false;
            CopyImageButton.IsEnabled = false;
            SaveTextButton.IsEnabled = false;
            OpenTextButton.IsEnabled = false;
            CopyTextButton.IsEnabled = false;
        }
    }

    private async void ClearState(object sender, RoutedEventArgs e)
    {
        ScanResult.IsOpen = false;
        ZoomToggle.Icon = new FontIcon
            { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Glyph = "\xe9a6" };
        ZoomToggle.IsEnabled = false;
        ZoomSlider.IsEnabled = false;
        BarcodeScroller.Visibility = Visibility.Collapsed;
        //WebcamButton.Text = "Windows Camera";

        if (SelectedDSSource != null) killVideoFeed();

        if (lastDecoded != null) lastDecoded.Dispose();

        lastDecodedType = "";
        lastSavedlocation = "";
        lastSavedTextLocation = "";
        stateManager(false);
        OpenTextWithButton.IsEnabled = false;
        DecodeFromFileButton.IsEnabled = true;
        DecodeFromClipboardButton.IsEnabled = true;
        DecodeFromSnippingToolButton.IsEnabled = true;
        TxtActivityLog.Text = "";
        BarcodeViewer.Source = null;
        BarcodeViewer.ClearValue(Image.SourceProperty);
        WifiWithTags = null;
        ClearTagsButton.Visibility = Visibility.Collapsed;
    }

    private static bool CompareBitmaps(Bitmap bmp1, Bitmap bmp2)
    {
        //convert bmp1 to base64
        using (var ms = new MemoryStream())
        {
            bmp1.Save(ms, ImageFormat.Png);
            var byteImage = ms.ToArray();
            var base64String = Convert.ToBase64String(byteImage);
            //convert bmp2 to base64
            using (var ms2 = new MemoryStream())
            {
                bmp2.Save(ms2, ImageFormat.Png);
                var byteImage2 = ms2.ToArray();
                var base64String2 = Convert.ToBase64String(byteImage2);
                //compare base64 strings
                if (base64String == base64String2)
                    return true;
                else
                    return false;
            }
        }
    }

    private async void DecodeFromSnippingTool(object sender, RoutedEventArgs e)
    {
        var Proc = new Process();
        var startInfo = new ProcessStartInfo();
        startInfo.UseShellExecute = true;
        startInfo.FileName = "ms-screenclip:";
        Process[] startingProcesses;
        Process[] postLaunchProcesses;
        Bitmap? startingBitmap = null;
        var startingClipboardContent = Clipboard.GetContent();

        if (startingClipboardContent != null)
            if (startingClipboardContent.Contains(StandardDataFormats.Bitmap))
            {
                Log("Bitmap found in clipboard.");
                var data = await startingClipboardContent.GetBitmapAsync();
                var bit = await data.OpenReadAsync();
                var stream = bit.AsStreamForRead();
                startingBitmap = new Bitmap(stream);
            }


        startingProcesses = Process.GetProcessesByName("SnippingTool");
        Process[] startingProcesses2 = Process.GetProcessesByName("ScreenClippingHost");
        //combine startingProcesses and startingProcesses2 
        var startingProcesses3 = startingProcesses.Concat(startingProcesses2);
        Log("Checking for running Snipping Tool processes.");
        Log(startingProcesses3.Count().ToString() + " processes found.");

        App.MainWindow.WindowState = WindowState.Minimized;
        var uri = new Uri("ms-screenclip:");
        Log("Launching Snipping Tool.");

        var launchResult = await Launcher.LaunchUriAsync(uri);
        if (launchResult == false)
        {
            Log("Failed to launch.");
            DidDecodeSucceed(2);
            App.MainWindow.WindowState = WindowState.Normal;
        }

        if (launchResult == true)
        {
            Log("Succesfully launched.");
            Log("Checking for running Snipping Tool processes.");
            postLaunchProcesses = Process.GetProcessesByName("SnippingTool");
            Process[] postLaunchProcesses2 = Process.GetProcessesByName("ScreenClippingHost");

            //combine postLaunchProcesses and postLaunchProcesses2
            var postLaunchProcesses3 = postLaunchProcesses.Concat(postLaunchProcesses2);

            //get the difference between preLaunchProcesses3 and postLaunchProcesses3
            var difference = postLaunchProcesses3.Except(startingProcesses3);
            Proc = difference.FirstOrDefault();
            if (Proc != null)
            {
                Log("barcodrod.io Snipping Tool process found. PID: " + Proc.Id.ToString());

                await Proc.WaitForExitAsync();

                // Check if the clipboard contains a bitmap
                var clipboardContent = Clipboard.GetContent();

                if (clipboardContent != null)
                    if (clipboardContent.Contains(StandardDataFormats.Bitmap))
                    {
                        var data = await clipboardContent.GetBitmapAsync();
                        var bit = await data.OpenReadAsync();
                        var stream = bit.AsStreamForRead();
                        var bitmap = new Bitmap(stream);

                        if (bitmap != null)
                        {
                            Log("Bitmap found in clipboard.");
                            if (startingBitmap != null)
                            {
                                Log(
                                    "Checking if this bitmap is the same bitmap found prior to launching Snipping Tool.");

                                var bitmapIsSame = CompareBitmaps(startingBitmap, bitmap);
                                if (bitmapIsSame == true)
                                {
                                    Log("Same bitmap. Returning.");

                                    DidDecodeSucceed(1);
                                    BarcodeViewer.Source = null;
                                    App.MainWindow.WindowState = WindowState.Normal;
                                    return;
                                }
                            }

                            Log("New bitmap. Decoding.");
                            var result = DecodeBitmap(bitmap);
                            if (result != null)
                            {
                                TxtActivityLog.Text = result;
                                result.GetType().ToString();
                                BitmapToImageSource(bitmap);

                                DidDecodeSucceed(0);

                                var Uri = await IsResultURI();
                                if (Uri == true) OpenTextWithButton.IsEnabled = true;
                            }
                            else
                            {
                                DidDecodeSucceed(1);
                                BarcodeViewer.Source = null;
                            }
                        }
                        else
                        {
                            DidDecodeSucceed(1);
                            BarcodeViewer.Source = null;
                        }

                        App.MainWindow.WindowState = WindowState.Normal;
                    }
            }
            else
            {
                DidDecodeSucceed(2);
                App.MainWindow.WindowState = WindowState.Normal;
                return;
            }
        }
    }

    public async void DecodeFromFile(string filepath)
    {
        //StorageFile file;
        //file = await StorageFile.GetFileFromPathAsync(filepath);
        var file = StorageFile.GetFileFromPathAsync(filepath).GetAwaiter().GetResult();
        //check if file is 0 bytes in size
        var fileProperties = await file.GetBasicPropertiesAsync();
        if (fileProperties.Size == 0)
        {
            DidDecodeSucceed(5);
            return;
        }

        var bitmap = new Bitmap(file.Path);
        var result = DecodeBitmap(bitmap);
        if (result != null)
        {
            DidDecodeSucceed(0);
            TxtActivityLog.Text = result;

            result.GetType().ToString();
            BitmapToImageSource(bitmap);

            var Uri = await IsResultURI();
            if (Uri == true) OpenTextWithButton.IsEnabled = true;
        }
        else
        {
            DidDecodeSucceed(1);
            BarcodeViewer.Source = null;
        }

        bitmap.Dispose();
    }

    private async void DecodeFromClipboard(object sender, RoutedEventArgs e)
    {
        var clipboardContent = Clipboard.GetContent();
        if (clipboardContent.Contains(StandardDataFormats.Bitmap))
        {
            var data = await clipboardContent.GetBitmapAsync();
            var bit = await data.OpenReadAsync();
            var stream = bit.AsStreamForRead();
            var bitmap = new Bitmap(stream);
            var result = DecodeBitmap(bitmap);
            if (result != null)
            {
                TxtActivityLog.Text = result;
                result.GetType().ToString();
                BitmapToImageSource(bitmap);

                DidDecodeSucceed(0);
                var Uri = await IsResultURI();
                if (Uri == true) OpenTextWithButton.IsEnabled = true;
            }
            else
            {
                DidDecodeSucceed(1);
                BarcodeViewer.Source = null;
            }
        }
    }

    public async void CustomCameraCaptureUI(object sender, RoutedEventArgs e)
    {
        _launcherOptions = new LauncherOptions();
        var window = new Window();

        var hndl = WindowNative.GetWindowHandle(window);

        _launcherOptions.TreatAsUntrusted = false;

        _launcherOptions.DisplayApplicationPicker = false;
        _launcherOptions.TargetApplicationPackageFamilyName = "Microsoft.WindowsCamera_8wekyb3d8bbwe";

        window.SetIsAlwaysOnTop(true);
        //place window over the main window


        InitializeWithWindow.Initialize(_launcherOptions, hndl);

        var file = await CaptureFileAsync(CameraCaptureUIMode.Photo);

        if (file == null)
        {
            window.Content = null;
            window = null;
            return;
        }
        else
        {
            DecodeFromFile(file.Path);
            //await file.DeleteAsync();
            window.Content = null;
            window = null;
        }
    }

    public async Task<StorageFile> CaptureFileAsync(CameraCaptureUIMode mode)
    {
        //App.MainWindow.Hide();

        var tempFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
        var tempFileName = $"temp.png";

        var tempFile = await tempFolder.CreateFileAsync(tempFileName, CreationCollisionOption.GenerateUniqueName);
        var token = SharedStorageAccessManager.AddFile(tempFile);

        var set = new ValueSet();

        set.Add("MediaType", "photo");
        set.Add("PhotoFileToken", token);
        set.Add("MaxResolution", (int)CameraCaptureUIMaxPhotoResolution.HighestAvailable);
        set.Add("Format", 1);


        var uri = new Uri("microsoft.windows.camera.picker:" + token);
        var result = await Launcher.LaunchUriForResultsAsync(uri, _launcherOptions, set);

        //DecodeFromFile(tempFile.Path);

        if (result.Status == LaunchUriStatus.Success && tempFile != null)
        {
            var file = tempFile;
            return file;
        }
        else
        {
            return null;
        }
    }

    public async void DecodeFromFilePicker(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
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

        InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();

        if (file != null)
        {
            var bitmap = new Bitmap(file.Path);
            var result = DecodeBitmap(bitmap);
            if (result != null)
            {
                DidDecodeSucceed(0);
                TxtActivityLog.Text = result;
                BitmapToImageSource(bitmap);
                if (await IsResultURI() == true)
                    OpenTextWithButton.IsEnabled = true;
                else
                    OpenTextWithButton.IsEnabled = false;
            }
            else
            {
                DidDecodeSucceed(1);
                BarcodeViewer.Source = null;
            }
        }
    }

    public async void SaveBitmapToFile(Bitmap bitmap, StorageFile file)
    {
        bitmap = lastDecoded;
        if (file != null)
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                bitmap.Save(stream.AsStream(), ImageFormat.Bmp);
            }
    }

    private async void SaveImage(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");
        var window = new Window();
        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileSavePicker();
        picker.SuggestedFileName = timestamp + "." + lastDecodedType;
        picker.FileTypeChoices.Add("PNG", new List<string>() { ".png" });
        picker.FileTypeChoices.Add("JPEG", new List<string>() { ".jpg" });
        picker.FileTypeChoices.Add("BMP", new List<string>() { ".bmp" });
        picker.FileTypeChoices.Add("GIF", new List<string>() { ".gif" });
        picker.FileTypeChoices.Add("TIFF", new List<string>() { ".tiff" });
        picker.FileTypeChoices.Add("ICO", new List<string>() { ".ico" });
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

        InitializeWithWindow.Initialize(picker, hwnd);
        var path = await picker.PickSaveFileAsync();

        SaveBitmapToFile(lastDecoded, path);

        if (path != null)
        {
            OpenImageButton.IsEnabled = true;
            ShareCommandBarButton.Visibility = Visibility.Visible;
            lastSavedlocation = path.Path;
        }

        else if (path == null)
        {
            ShareCommandBarButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (lastSavedlocation != "" && lastSavedlocation != null)
        {
            var options = new LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedlocation),
                options);
        }
    }

    //function to copy the decoded bitmap to the user's clipboard as a pastable image
    private async void CopyImage(object sender, RoutedEventArgs e)
    {
        if (lastDecoded != null)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("temp.png",
                CreationCollisionOption.ReplaceExisting);
            lastDecoded.Save(file.Path, ImageFormat.Png);
            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Clipboard.SetContent(dataPackage);
        }

        if (ImageRightClickCommandBar.IsOpen == true) ImageRightClickCommandBar.Hide();
    }

    private async void SaveText(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");
        var window = new Window();
        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileSavePicker();
        picker.SuggestedFileName = timestamp;

        picker.FileTypeChoices.Add("Text", new List<string>() { ".txt" });

        InitializeWithWindow.Initialize(picker, hwnd);
        var path = await picker.PickSaveFileAsync();

        if (path != null)
        {
            await FileIO.WriteTextAsync(path, TxtActivityLog.Text);
            lastSavedTextLocation = path.Path;
            OpenTextButton.IsEnabled = true;
        }
    }

    private async void SaveCSV(object sender, RoutedEventArgs e)
    {
        var timestamp = DateTime.Now.ToString("MMddyy.HHmm");
        var window = new Window();
        var hwnd = WindowNative.GetWindowHandle(window);
        var picker = new FileSavePicker();
        picker.SuggestedFileName = timestamp;

        picker.FileTypeChoices.Add("CSV (Comma delimited)", new List<string>() { ".csv" });

        InitializeWithWindow.Initialize(picker, hwnd);
        var path = await picker.PickSaveFileAsync();
        var tempCSVExists = await ApplicationData.Current.LocalFolder.TryGetItemAsync("temp.csv");

        if (path != null && tempCSVExists != null)
        {
            var csv = await ApplicationData.Current.LocalFolder.GetFileAsync("temp.csv");
            await csv.CopyAndReplaceAsync(path);
            await csv.DeleteAsync();


            lastSavedCSVLocation = path.Path;
            OpenCsv.IsEnabled = true;
        }
    }

    private async void OpenCSV(object sender, RoutedEventArgs e)
    {
        var options = new LauncherOptions();
        options.DisplayApplicationPicker = true;
        await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedCSVLocation),
            options);
        TeachingTip.IsOpen = false;
    }

    private async void OpenText(object sender, RoutedEventArgs e)
    {
        if (lastSavedTextLocation != "")
        {
            var options = new LauncherOptions();
            options.DisplayApplicationPicker = true;
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(lastSavedTextLocation),
                options);
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
            lastDecoded = bitmap;
        }
    }

    private void CopyTextToClipboard(object sender, RoutedEventArgs e)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(TxtActivityLog.Text);
        Clipboard.SetContent(dataPackage);
    }

    //function to share/open decoded text content with default app
    private async void ShareText(object sender, RoutedEventArgs e)
    {
        if (TxtActivityLog.Text != "" && TxtActivityLog.Text != null)
        {
            var options = new LauncherOptions();
            options.DisplayApplicationPicker = true;
            if (await IsResultURI() == false)
            {
                DidDecodeSucceed(6);
                OpenTextWithButton.IsEnabled = false;
                return;
            }

            var uri = new Uri(TxtActivityLog.Text);

            await Launcher.LaunchUriAsync(uri, options);
        }
        else
        {
            OpenTextWithButton.IsEnabled = false;
        }
    }

    private async void BulkDecode(object sender, RoutedEventArgs e)
    {
        OpenCsv.IsEnabled = false;
        //KillBulkDecode();
        var decodedCount = 0;
        var noBarCodeCount = 0;
        var fileCount = 0;
        FailedDecodeProgressStat.Text = "";

        ImageFolderButton.IsEnabled = false;
        if (TeachingTip.IsOpen == true)
        {
            KillBulkDecode();
            return;
        }


        //ImageFolderButton.IsEnabled = false;
        SaveCsv.IsEnabled = false;
        progressRing.IsActive = false;
        progressRing.Value = 0;
        TeachingTip.IsOpen = false;

        StorageFile file;

        var folderPicker = new FolderPicker();

        //Get the Window's HWND
        var hwnd = App.MainWindow.As<IWindowNative>().WindowHandle;

        //Make folder Picker work in Win32

        var initializeWithWindow = folderPicker.As<IInitializeWithWindow>();
        initializeWithWindow.Initialize(hwnd);
        folderPicker.FileTypeFilter.Add("*");

        var folder = await folderPicker.PickSingleFolderAsync();


        if (folder == null)
        {
            ImageFolderButton.IsEnabled = true;
            return;
        }

        string[] extensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".gif", ".heif", ".hiec" };
        var filePaths = Directory.GetFiles(folder.Path)
            .Where(file => extensions.Contains(Path.GetExtension(file).ToLower())).ToArray();

        fileCount = filePaths.Length;
        if (fileCount == 0)
        {
            DidDecodeSucceed(4);
            return;
        }

        TeachingTip.Title = "Decoding...";
        progressRing.Minimum = 0;
        progressRing.Maximum = fileCount - 1;
        TeachingTip.IsOpen = true;
        progressRing.IsActive = true;
        var result = string.Empty;
        var ScanResult = string.Empty;

        var csv = await ApplicationData.Current.LocalFolder.CreateFileAsync("temp.csv",
            CreationCollisionOption.ReplaceExisting);
        var csvPath = csv.Path;

        sw = new StreamWriter(csvPath);
        sw.AutoFlush = true;
        sw.WriteLine("File Name,Barcode Type,Decoded Result");

        foreach (var filePath in filePaths)
        {
            var error = "";
            if (TeachingTip.IsOpen == false)
            {
                KillBulkDecode();
                return;
            }

            result = string.Empty;
            ScanResult = string.Empty;
            try
            {
                file = await StorageFile.GetFileFromPathAsync(filePath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log(ex.ToString());
                Log(ex.GetBaseException().ToString());
                Log(ex.Message);
                continue;
            }

            if (error == "")
            {
                var bitmap = new Bitmap(file.Path);
                var decoded = reader.Decode(bitmap);
                if (decoded == null)
                {
                    error = "BARCODE NOT FOUND";
                    noBarCodeCount += 1;

                    result = error;
                    ScanResult = "NONE";
                }
                else
                {
                    result = decoded.Text;
                    ScanResult = decoded.BarcodeFormat.ToString();
                    decodedCount += 1;
                }

                TotalFiles.Text = decodedCount + "/" + fileCount + " images decoded 🤠";

                //DecodeProgressStat.Text = decodedCount + " file(s) succesfully decoded.";
                if (noBarCodeCount > 0)
                {
                    FailedDecodeProgressStat.Text = "Unable to decode " + noBarCodeCount +
                                                    " image(s). These have been logged in the .CSV output.";
                    FailedDecodeProgressStat.Visibility = Visibility.Visible;
                }
                else if (noBarCodeCount == 0)
                {
                    FailedDecodeProgressStat.Visibility = Visibility.Collapsed;
                }

                bitmap.Dispose();
            }


            progressRing.Value = decodedCount + noBarCodeCount;
            sw.WriteLine(file.Name + "," + ScanResult + "," + result);
        }

        TeachingTip.Title = "Complete";
        if (noBarCodeCount >= 1)
        {
        }

        SaveCsv.IsEnabled = true;
        sw.Dispose();
        sw.Close();
        ImageFolderButton.IsEnabled = true;
    }

    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("EECDBF0E-BAE9-4CB6-A68E-9598E1CB57BB")]
    internal interface IWindowNative
    {
        IntPtr WindowHandle { get; }
    }

    private async void KillBulkDecode()
    {
        FailedDecodeProgressStat.Text = "";
        //DecodeProgressStat.Text = "";
        sw.Close();
        SaveCsv.IsEnabled = false;
        progressRing.Value = 0;
        progressRing.Minimum = 0;
        progressRing.Maximum = 0;
        progressRing.IsActive = false;
        ImageFolderButton.IsEnabled = true;


        var tempCSVExists = await ApplicationData.Current.LocalFolder.TryGetItemAsync("temp.csv");

        if (tempCSVExists != null)
        {
            var csv = await ApplicationData.Current.LocalFolder.GetFileAsync("temp.csv");
            await csv.DeleteAsync();
        }
    }

    private async Task<string> BulkDecodePicker()
    {
        var window = new Window();
        var hwnd = WindowNative.GetWindowHandle(window);
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        InitializeWithWindow.Initialize(folderPicker, hwnd);


        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            return folder.Path;
        }
        else
        {
            ImageFolderButton.IsEnabled = true;
            var path = "NotSelected";
            return path;
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (BarcodeScroller != null) BarcodeScroller.ChangeView(null, null, (float)e.NewValue);
    }

    private async void ToggleZoom(object sender, RoutedEventArgs e)
    {
        if (ZoomSlider.IsEnabled == false)
        {
            ZoomSlider.IsEnabled = true;
            ZoomSlider.Visibility = Visibility.Visible;
            BarcodeViewer.Visibility = Visibility.Collapsed;

            BarcodeScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;

            BarcodeScroller.HorizontalScrollMode = ScrollMode.Enabled;

            BarcodeScroller.VerticalScrollMode = ScrollMode.Enabled;
            BarcodeScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;

            BarcodeScroller.IsEnabled = true;
            BarcodeScroller.Visibility = Visibility.Visible;

            ZoomToggle.Icon = new FontIcon
                { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Glyph = "\ue71e" };
            ZoomToggle.Label = "Zoom Mode";

            using (var memory = new MemoryStream())
            {
                if (lastDecoded != null)
                {
                    var image = new Image();
                    lastDecoded.Save(memory, ImageFormat.Bmp);
                    memory.Position = 0;
                    var bitmapimage = new BitmapImage();
                    await bitmapimage.SetSourceAsync(memory.AsRandomAccessStream());

                    image.Source = bitmapimage;
                    image.SetValue(Image.SourceProperty, image.Source);
                    image.HorizontalAlignment = HorizontalAlignment.Center;
                    image.VerticalAlignment = VerticalAlignment.Top;
                    ZoomSlider.IsEnabled = true;
                    BarcodeScroller.Content = image;
                }
            }

            return;
        }

        if (ZoomSlider.IsEnabled == true)
            if (BarcodeViewer != null)
            {
                ZoomSlider.Visibility = Visibility.Collapsed;
                BarcodeViewer.Source = null;


                BarcodeViewer.Visibility = Visibility.Visible;
                BarcodeViewer.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform;


                BarcodeScroller.Visibility = Visibility.Collapsed;

                ZoomSlider.IsEnabled = false;
                ZoomToggle.Icon = new FontIcon
                    { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Glyph = "\xe9a6" };
                ZoomToggle.Label = "Fill Mode";

                if (lastDecoded != null) BitmapToImageSource(lastDecoded);
            }
    }
}