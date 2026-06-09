using barcodrod.io.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using AForge.Video;
using AForge.Video.DirectShow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
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
    private DeviceInformationCollection? VideoCaptureDevices;
    private MediaCapture? m_mediaCapture;
    private MediaFrameSource? m_frameSource;
    private MediaPlayer? m_mediaPlayer;
    private MediaFrameReader? m_frameReader;
    private bool m_isPreviewing;
    private CancellationTokenSource? _scanningCancellation;
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
    private bool _hasProcessedLaunchArgument;
    private bool _useLegacyDirectShow;
    private FilterInfoCollection? _directShowVideoDevices;
    private VideoCaptureDevice? _directShowVideoDevice;
    private int _isProcessingDirectShowFrame;
    private Task? _cameraInitializationTask;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico", ".dib", ".wmf", ".emf", ".exif", ".webp",
        ".heif", ".jfif", ".jpe", ".jif", ".jfi", ".jp2", ".j2k", ".jpf", ".jpx", ".j2c", ".fpx", ".pcd", ".svg",
        ".svgz", ".ai", ".eps", ".ps"
    };


    public DecodePage()
    {
        InitializeComponent();
        InitializeLog();
        LoadPasteToDecodeSetting();

        _cameraInitializationTask = InitializeCameraBackendAsync();

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

    private async Task InitializeCameraBackendAsync()
    {
        await LoadLegacyWebcamSettingAsync();
        await InitializeCameraDevicesAsync();
    }

    private static bool IsLegacyDirectShowSupportedArchitecture()
    {
        return RuntimeInformation.OSArchitecture == Architecture.X64 || RuntimeInformation.OSArchitecture == Architecture.X86;
    }

    private async Task LoadLegacyWebcamSettingAsync()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");
            if (!File.Exists(settingsFilePath))
            {
                _useLegacyDirectShow = false;
                return;
            }

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _useLegacyDirectShow = false;
                return;
            }

            var settings = JObject.Parse(json);
            _useLegacyDirectShow = IsLegacyDirectShowSupportedArchitecture() && settings["UseLegacyDirectShow"]?.Value<bool>() == true;
        }
        catch
        {
            _useLegacyDirectShow = false;
        }
    }

    public async Task InitializeLog()
    {
        try
        {
            localFolder = (await AppPaths.GetLocalFolderAsync());

            //create log file based on current date and time
            var logFileName = "log.txt";

            //get the size of log.txt if it exists
            if (File.Exists(Path.Combine(localFolder.Path, logFileName)))
            {
                currentLogPath = await localFolder.GetFileAsync(logFileName);
                Log("Decode page loaded.");
                Log((VideoCaptureDevices?.Count ?? 0).ToString() + " capture devices found.");
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

    private async Task InitializeCameraDevicesAsync()
    {
        try
        {
            comboBox1.Items.Clear();
            comboBox2.Items.Clear();
            comboBox2.Visibility = Visibility.Collapsed;
            DirectShowButton.Tapped -= InitializeMediaCaptureCam;
            DirectShowButton.Tapped -= CustomCameraCaptureUI;
            DirectShowButton.Tapped -= InitializeLegacyDirectShowCam;

            if (_useLegacyDirectShow)
            {
                _directShowVideoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (_directShowVideoDevices.Count > 0)
                {
                    foreach (FilterInfo device in _directShowVideoDevices)
                        comboBox1.Items.Add(device.Name);

                    DirectShowButton.Tapped += InitializeLegacyDirectShowCam;

                    if (comboBox1.Items.Count > 0)
                    {
                        comboBox1.SelectedIndex = 0;
                        PopulateLegacyDirectShowResolutions();
                    }
                }
            }
            else
            {
                VideoCaptureDevices = await DeviceInformation.FindAllAsync(MediaDevice.GetVideoCaptureSelector());

                if (VideoCaptureDevices.Count > 0)
                {
                    foreach (var device in VideoCaptureDevices)
                        comboBox1.Items.Add(device.Name);

                    var uri = new Uri("microsoft.windows.camera:");
                    var canLaunch = await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri, "Microsoft.WindowsCamera_8wekyb3d8bbwe");
                    if (canLaunch == LaunchQuerySupportStatus.Available)
                        comboBox1.Items.Add("Windows Camera app");
                }
            }

            if (comboBox1.Items.Count == 0)
            {
                DirectShowButton.IsEnabled = false;
                comboBox1.Visibility = Visibility.Collapsed;
                comboBox2.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Log("Error initializing camera devices: " + ex.Message);
            DirectShowButton.IsEnabled = false;
            comboBox1.Visibility = Visibility.Collapsed;
            comboBox2.Visibility = Visibility.Collapsed;
        }
    }

    private async void LoadPasteToDecodeSetting()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            if (!File.Exists(settingsFilePath)) return;

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrEmpty(json)) return;

            var settings = JObject.Parse(json);
            var pasteToDecode = settings["PasteToDecode"];
            if (pasteToDecode != null && pasteToDecode.Value<bool>())
                PasteAccelerator.IsEnabled = true;
        }
        catch
        {
            // Setting not found or invalid; leave accelerator disabled.
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
            _ = AutoCopyIfEnabled(result);
            _ = AutoLaunchUrlIfEnabled(result);

            if (IsWifiCode(result))
                ClearTagsButton.Visibility = Visibility.Visible;
            else
                ClearTagsButton.Visibility = Visibility.Collapsed;

            return result;
        }
    }

    private async Task AutoCopyIfEnabled(string text)
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            if (!File.Exists(settingsFilePath)) return;

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrEmpty(json)) return;

            var settings = JObject.Parse(json);
            var autoCopy = settings["AutoCopyToClipboard"];
            if (autoCopy == null || !autoCopy.Value<bool>()) return;

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Log("Auto-copied decoded text to clipboard.");

            ScanResult.Message = lastDecodedType + " detected. Copied to clipboard.";
        }
        catch (Exception ex)
        {
            Log("Error auto-copying to clipboard: " + ex.Message);
        }
    }

    private async Task AutoLaunchUrlIfEnabled(string text)
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            if (!File.Exists(settingsFilePath)) return;

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrEmpty(json)) return;

            var settings = JObject.Parse(json);
            var autoOpen = settings["AutoOpenUrl"];
            if (autoOpen == null || !autoOpen.Value<bool>()) return;

            if (!TryNormalizeUri(text, out var uri)) return;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

            Log("Auto-launching URL in default browser: " + uri.AbsoluteUri);
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            Log("Error auto-launching URL: " + ex.Message);
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

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_cameraInitializationTask != null)
        {
            try { await _cameraInitializationTask; } catch { }
        }
        await LoadWebcamSettings();

        if (_hasProcessedLaunchArgument)
            return;

        _hasProcessedLaunchArgument = true;

        if (e.Parameter is string launchArgument)
        {
            if (TryExtractLaunchImagePath(launchArgument, out var imagePath))
            {
                DecodeFromFile(imagePath);
            }
        }
    }

    private bool TryExtractLaunchImagePath(string argument, out string imagePath)
    {
        imagePath = string.Empty;

        if (string.IsNullOrWhiteSpace(argument))
            return false;

        var trimmedArgument = argument.Trim();

        if (trimmedArgument.StartsWith('"') && trimmedArgument.EndsWith('"') && trimmedArgument.Length > 1)
            trimmedArgument = trimmedArgument[1..^1];

        if (!File.Exists(trimmedArgument))
            return false;

        var extension = Path.GetExtension(trimmedArgument);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedImageExtensions.Contains(extension))
            return false;

        imagePath = trimmedArgument;
        return true;
    }

    private async Task SaveWebcamSettings()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            JObject settings;
            if (File.Exists(settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(settingsFilePath);
                settings = (!string.IsNullOrEmpty(json)) ? JObject.Parse(json) : new JObject();
            }
            else
            {
                settings = new JObject();
            }

            settings["WebcamSourceIndex"] = comboBox1.SelectedIndex;
            settings["WebcamSourceName"] = comboBox1.SelectedItem?.ToString();
            File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
        }
        catch
        {
        }
    }

    private async Task LoadWebcamSettings()
    {
        try
        {
            var localFolder = (await AppPaths.GetLocalFolderAsync());
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            if (!File.Exists(settingsFilePath)) return;

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrEmpty(json)) return;

            var settings = JObject.Parse(json);

            if (comboBox1.Items.Count == 0) return;

            var savedName = settings["WebcamSourceName"]?.Value<string>();
            if (!string.IsNullOrEmpty(savedName))
            {
                for (var i = 0; i < comboBox1.Items.Count; i++)
                {
                    if (string.Equals(comboBox1.Items[i]?.ToString(), savedName, StringComparison.Ordinal))
                    {
                        comboBox1.SelectedIndex = i;
                        return;
                    }
                }
            }

            var savedIndex = settings["WebcamSourceIndex"];
            if (savedIndex != null)
            {
                var index = savedIndex.Value<int>();
                if (index >= 0 && index < comboBox1.Items.Count)
                {
                    comboBox1.SelectedIndex = index;
                    return;
                }
            }

            if (comboBox1.SelectedIndex < 0)
                comboBox1.SelectedIndex = 0;
        }
        catch
        {
        }
    }

    private async void DirectShowSourceChanged(object sender, RoutedEventArgs e)
    {
        if (comboBox1.SelectedItem != null) DirectShowButton.IsEnabled = true;

        await StopCameraPreviewAsync();

        if (_useLegacyDirectShow)
        {
            if (comboBox1.Items.Count > 0 && comboBox1.SelectedItem != null)
            {
                comboBox2.Visibility = Visibility.Visible;
                DirectShowButton.Tapped -= InitializeMediaCaptureCam;
                DirectShowButton.Tapped -= CustomCameraCaptureUI;
                DirectShowButton.Tapped -= InitializeLegacyDirectShowCam;
                DirectShowButton.Tapped += InitializeLegacyDirectShowCam;
                PopulateLegacyDirectShowResolutions();
                Log("Camera source changed to " + comboBox1.SelectedItem.ToString());
            }

            _ = SaveWebcamSettings();
            return;
        }

        if (comboBox1.Items.Count > 0 && comboBox1.SelectedItem != null)
        {
            if (comboBox1.SelectedItem.ToString() == "Windows Camera app")
            {
                comboBox2.Visibility = Visibility.Collapsed;
                DirectShowButton.Tapped -= InitializeMediaCaptureCam;
                DirectShowButton.Tapped -= CustomCameraCaptureUI;
                DirectShowButton.Tapped += CustomCameraCaptureUI;
            }
            else
            {
                comboBox2.Visibility = Visibility.Visible;
                DirectShowButton.Tapped -= InitializeMediaCaptureCam;
                DirectShowButton.Tapped -= CustomCameraCaptureUI;
                DirectShowButton.Tapped += InitializeMediaCaptureCam;

                // Populate resolutions for selected camera
                await PopulateCameraResolutionsAsync();
            }

            Log("Camera source changed to " + comboBox1.SelectedItem.ToString());
        }

        _ = SaveWebcamSettings();
    }

    private void PopulateLegacyDirectShowResolutions()
    {
        comboBox2.Items.Clear();

        try
        {
            if (comboBox1.SelectedIndex < 0 || _directShowVideoDevices == null || comboBox1.SelectedIndex >= _directShowVideoDevices.Count)
                return;

            var selectedSource = new VideoCaptureDevice(_directShowVideoDevices[comboBox1.SelectedIndex].MonikerString);
            foreach (var capability in selectedSource.VideoCapabilities)
            {
                comboBox2.Items.Add($"{capability.FrameSize.Width} x {capability.FrameSize.Height}");
            }

            if (comboBox2.Items.Count > 0)
                comboBox2.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Log("Error populating legacy DirectShow resolutions: " + ex.Message);
        }
    }

    private async Task PopulateCameraResolutionsAsync()
    {
        comboBox2.Items.Clear();

        try
        {
            if (comboBox1.SelectedIndex < 0 || VideoCaptureDevices == null || comboBox1.SelectedIndex >= VideoCaptureDevices.Count)
                return;

            var selectedDevice = VideoCaptureDevices[comboBox1.SelectedIndex];
            var tempCapture = new MediaCapture();

            await tempCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = selectedDevice.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            var frameSource = tempCapture.FrameSources.FirstOrDefault(source =>
                source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview &&
                source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;

            if (frameSource == null)
            {
                frameSource = tempCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            }

            if (frameSource != null)
            {
                var addedResolutions = new HashSet<string>();
                foreach (var format in frameSource.SupportedFormats
                    .OrderByDescending(f => f.VideoFormat.Width * f.VideoFormat.Height))
                {
                    var resolution = $"{format.VideoFormat.Width} x {format.VideoFormat.Height}";
                    if (addedResolutions.Add(resolution))
                        comboBox2.Items.Add(resolution);
                }

                if (comboBox2.Items.Count > 0)
                    comboBox2.SelectedIndex = 0;
            }

            tempCapture.Dispose();
        }
        catch (Exception ex)
        {
            Log("Error populating resolutions: " + ex.Message);
        }
    }

    private async Task StopCameraPreviewAsync()
    {
        if (_useLegacyDirectShow)
        {
            await StopLegacyDirectShowPreviewAsync();
            return;
        }

        if (Interlocked.CompareExchange(ref _isStopping, 1, 0) != 0)
            return;

        Log("Stopping camera preview.");

        try
        {
            // Set early so FrameReader_FrameArrived exits immediately on any in-flight callbacks
            m_isPreviewing = false;
            _scanningCancellation?.Cancel();
            _scanningCancellation = null;

            if (m_frameReader != null)
            {
                // Unsubscribe before StopAsync so no new callbacks fire during teardown
                m_frameReader.FrameArrived -= FrameReader_FrameArrived;
                await m_frameReader.StopAsync();
                m_frameReader.Dispose();
                m_frameReader = null;
            }

            if (m_mediaPlayer != null)
            {
                // Detach from the MediaPlayerElement BEFORE disposing,
                // otherwise the element crashes trying to render a disposed player
                CameraPreview.SetMediaPlayer(null);
                m_mediaPlayer.Pause();
                m_mediaPlayer.Dispose();
                m_mediaPlayer = null;
            }

            if (m_mediaCapture != null)
            {
                m_mediaCapture.Dispose();
                m_mediaCapture = null;
            }

            m_frameSource = null;

            CameraPreview.Visibility = Visibility.Collapsed;
            BarcodeViewer.Visibility = Visibility.Visible;
            DirectShowButtonTextBlock.Text = "Webcam";
            BarcodeViewer.ClearValue(Image.SourceProperty);
        }
        catch (Exception ex)
        {
            Log("Error stopping camera: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _isStopping, 0);
        }
    }

    private async void StopVideoFeed(object sender, RoutedEventArgs e)
    {
        await StopCameraPreviewAsync();
    }

    public async void InitializeMediaCaptureCam(object sender, RoutedEventArgs e)
    {
        if (m_isPreviewing)
        {
            await StopCameraPreviewAsync();
            return;
        }

        try
        {
            frameCount = 0;
            detectedCode = null;
            _scanningCancellation = new CancellationTokenSource();

            if (comboBox1.SelectedIndex < 0 || VideoCaptureDevices == null || comboBox1.SelectedIndex >= VideoCaptureDevices.Count)
            {
                Log("Invalid camera selection");
                return;
            }

            var selectedDevice = VideoCaptureDevices[comboBox1.SelectedIndex];

            m_mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = selectedDevice.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            };

            await m_mediaCapture.InitializeAsync(settings);
            Log("MediaCapture initialized successfully");

            // Find a color video frame source (prefer preview, fall back to record)
            m_frameSource = m_mediaCapture.FrameSources.FirstOrDefault(source =>
                source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview &&
                source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;

            if (m_frameSource == null)
            {
                m_frameSource = m_mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
            }

            if (m_frameSource == null)
            {
                Log("No suitable frame source found");
                await StopCameraPreviewAsync();
                return;
            }

            // Set resolution if specified
            if (comboBox2.SelectedIndex >= 0 && comboBox2.SelectedItem != null)
                await SetCameraResolutionAsync();

            // Create MediaPlayer for preview display
            m_mediaPlayer = new MediaPlayer
            {
                RealTimePlayback = true,
                AutoPlay = false,
                Source = MediaSource.CreateFromMediaFrameSource(m_frameSource)
            };

            DispatcherQueue.TryEnqueue(() =>
            {
                CameraPreview.SetMediaPlayer(m_mediaPlayer);
                CameraPreview.Visibility = Visibility.Visible;
                BarcodeViewer.Visibility = Visibility.Collapsed;
                DirectShowButtonTextBlock.Text = "Stop Video";
            });

            m_mediaPlayer.Play();
            m_isPreviewing = true;

            // Create frame reader for barcode scanning
            m_frameReader = await m_mediaCapture.CreateFrameReaderAsync(m_frameSource);
            m_frameReader.FrameArrived += FrameReader_FrameArrived;
            await m_frameReader.StartAsync();

            Log("Camera preview started");
        }
        catch (Exception ex)
        {
            Log("Error initializing camera: " + ex.Message);
            await StopCameraPreviewAsync();
        }
    }

    private async Task SetCameraResolutionAsync()
    {
        try
        {
            if (m_frameSource == null || comboBox2.SelectedItem == null)
                return;

            var resolutionString = comboBox2.SelectedItem.ToString();
            if (resolutionString == null)
                return;

            var parts = resolutionString.Split('x');
            if (parts.Length != 2)
                return;

            if (!uint.TryParse(parts[0].Trim(), out var width) || !uint.TryParse(parts[1].Trim(), out var height))
                return;

            var targetFormat = m_frameSource.SupportedFormats.FirstOrDefault(format =>
                format.VideoFormat.Width == width &&
                format.VideoFormat.Height == height);

            if (targetFormat != null)
            {
                await m_frameSource.SetFormatAsync(targetFormat);
                Log($"Set resolution to {width}x{height}");
            }
        }
        catch (Exception ex)
        {
            Log("Error setting resolution: " + ex.Message);
        }
    }

    private int _isProcessingFrame; // 0 = idle, 1 = busy (interlocked guard)
    private int _isStopping; // 0 = idle, 1 = stopping (interlocked guard)

    private async void InitializeLegacyDirectShowCam(object sender, RoutedEventArgs e)
    {
        if (_directShowVideoDevice?.IsRunning == true)
        {
            BarcodeViewer.ClearValue(Image.SourceProperty);
            await StopLegacyDirectShowPreviewAsync();
            return;
        }

        try
        {
            frameCount = 0;
            detectedCode = null;

            if (comboBox1.SelectedIndex < 0 || _directShowVideoDevices == null || comboBox1.SelectedIndex >= _directShowVideoDevices.Count)
            {
                Log("Invalid legacy camera selection");
                return;
            }

            var selectedDevice = _directShowVideoDevices[comboBox1.SelectedIndex];
            _directShowVideoDevice = new VideoCaptureDevice(selectedDevice.MonikerString);

            if (_directShowVideoDevice.VideoCapabilities.Length > 0)
            {
                var resolutionIndex = comboBox2.SelectedIndex;
                if (resolutionIndex < 0 || resolutionIndex >= _directShowVideoDevice.VideoCapabilities.Length)
                    resolutionIndex = 0;

                _directShowVideoDevice.VideoResolution = _directShowVideoDevice.VideoCapabilities[resolutionIndex];
            }

            _directShowVideoDevice.NewFrame += DirectShowDevice_NewFrame;
            _directShowVideoDevice.Start();

            DirectShowButtonTextBlock.Text = "Stop Video";
            CameraPreview.Visibility = Visibility.Collapsed;
            BarcodeViewer.Visibility = Visibility.Visible;
            Log("Legacy DirectShow preview started");

            while (detectedCode == null && _directShowVideoDevice?.IsRunning == true)
                await Task.Delay(1000);

            if (detectedCode != null)
            {
                var result = DecodeBitmap(detectedCode);
                if (result != null)
                {
                    DidDecodeSucceed(0);
                    TxtActivityLog.Text = result;
                    BitmapToImageSource(detectedCode);
                    DirectShowButtonTextBlock.Text = "Webcam";
                    var isUri = await IsResultURI();
                    if (isUri) OpenTextWithButton.IsEnabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log("Error starting legacy DirectShow camera: " + ex.Message);
            await StopLegacyDirectShowPreviewAsync();
        }
    }

    private async Task StopLegacyDirectShowPreviewAsync()
    {
        try
        {
            if (_directShowVideoDevice != null)
            {
                _directShowVideoDevice.NewFrame -= DirectShowDevice_NewFrame;

                if (_directShowVideoDevice.IsRunning)
                {
                    await Task.Run(() =>
                    {
                        _directShowVideoDevice.SignalToStop();
                        _directShowVideoDevice.WaitForStop();
                    });
                }

                _directShowVideoDevice = null;
            }

            DirectShowButtonTextBlock.Text = "Webcam";
        }
        catch (Exception ex)
        {
            Log("Error stopping legacy DirectShow camera: " + ex.Message);
        }
    }

    private void DirectShowDevice_NewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        if (detectedCode != null)
            return;

        frameCount++;

        if (Interlocked.CompareExchange(ref _isProcessingDirectShowFrame, 1, 0) != 0)
            return;

        try
        {
            using (var video = (Bitmap)eventArgs.Frame.Clone())
            {
                var memory = new MemoryStream();
                video.Save(memory, ImageFormat.Bmp);
                memory.Position = 0;

                if (frameCount % 10 == 0)
                {
                    if (SilentDecodeBitmap(video))
                    {
                        detectedCode = (Bitmap)video.Clone();
                        DispatcherQueue.TryEnqueue(async () => { await StopLegacyDirectShowPreviewAsync(); });
                        return;
                    }
                }

                if (DispatcherQueue == null)
                {
                    _ = StopLegacyDirectShowPreviewAsync();
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.SetSource(memory.AsRandomAccessStream());
                    BarcodeViewer.SetValue(Image.SourceProperty, bitmapImage);
                });
            }
        }
        catch (Exception ex)
        {
            Log("Error processing legacy DirectShow frame: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessingDirectShowFrame, 0);
        }
    }

    private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!m_isPreviewing || detectedCode != null || _scanningCancellation?.IsCancellationRequested == true)
            return;

        frameCount++;

        // Process every 10th frame to reduce CPU usage (same cadence as original)
        if (frameCount % 10 != 0)
            return;

        // Prevent re-entrant processing — skip if a previous frame is still being decoded
        if (Interlocked.CompareExchange(ref _isProcessingFrame, 1, 0) != 0)
            return;

        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            if (frame?.VideoMediaFrame == null)
                return;

            SoftwareBitmap? softwareBitmap = null;
            bool ownsSoftwareBitmap = false;
            try
            {
                softwareBitmap = frame.VideoMediaFrame.SoftwareBitmap;
                if (softwareBitmap == null)
                {
                    if (frame.VideoMediaFrame.Direct3DSurface != null)
                    {
                        softwareBitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(
                            frame.VideoMediaFrame.Direct3DSurface).AsTask().GetAwaiter().GetResult();
                        ownsSoftwareBitmap = true;
                    }
                    else
                    {
                        return;
                    }
                }

                if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                    softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                {
                    var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    if (ownsSoftwareBitmap) softwareBitmap.Dispose();
                    softwareBitmap = converted;
                    ownsSoftwareBitmap = true;
                }

                var bitmap = ConvertSoftwareBitmapToBitmap(softwareBitmap);

                if (bitmap != null && SilentDecodeBitmap(bitmap))
                {
                    detectedCode = bitmap;
                    if (_isStopping == 0)
                    {
                        DispatcherQueue.TryEnqueue(async () =>
                        {
                            await StopCameraPreviewAsync();
                            ProcessDetectedBarcode();
                        });
                    }
                    else
                    {
                        bitmap.Dispose();
                        detectedCode = null;
                    }
                }
                else
                {
                    bitmap?.Dispose();
                }
            }
            finally
            {
                if (ownsSoftwareBitmap) softwareBitmap?.Dispose();
            }
        }
        catch (ObjectDisposedException)
        {
            // Frame reader or media capture was disposed while processing; this is expected during shutdown.
        }
        catch (Exception ex)
        {
            Log("Error processing frame: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessingFrame, 0);
        }
    }

    private Bitmap? ConvertSoftwareBitmapToBitmap(SoftwareBitmap softwareBitmap)
    {
        try
        {
            int width = softwareBitmap.PixelWidth;
            int height = softwareBitmap.PixelHeight;

            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                // Copy pixel data from SoftwareBitmap directly into the GDI+ Bitmap buffer
                byte[] pixelBuffer = new byte[4 * width * height];
                softwareBitmap.CopyToBuffer(pixelBuffer.AsBuffer());

                // Copy row by row to handle stride differences between SoftwareBitmap and GDI+
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixelBuffer, y * width * 4, bitmapData.Scan0 + y * bitmapData.Stride, width * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Log("Error converting bitmap: " + ex.Message);
            return null;
        }
    }

    private async void ProcessDetectedBarcode()
    {
        if (detectedCode == null)
            return;

        var result = DecodeBitmap(detectedCode);
        if (result != null)
        {
            DidDecodeSucceed(0);
            TxtActivityLog.Text = result;
            BitmapToImageSource(detectedCode);
            DirectShowButtonTextBlock.Text = "Webcam";
            var isUri = await IsResultURI();
            if (isUri) OpenTextWithButton.IsEnabled = true;
        }
    }

    private bool TryNormalizeUri(string input, out Uri uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim();

        // Already a valid absolute URI
        if (Uri.TryCreate(input, UriKind.Absolute, out var temp) &&
            (temp.Scheme == Uri.UriSchemeHttp || temp.Scheme == Uri.UriSchemeHttps))
        {
            uri = temp;
            return true;
        }

        // Add https:// if it looks like a URL (starts with "www." or contains a dot)
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Already has scheme but TryCreate above failed, so it's malformed
            return false;
        }

        if (input.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            (input.Contains('.') && !input.Contains(' ')))
        {
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out temp))
            {
                uri = temp;
                return true;
            }
        }

        // Try the original text as-is for custom URI schemes (e.g., "tel:", "mailto:")
        if (Uri.TryCreate(input, UriKind.Absolute, out temp))
        {
            uri = temp;
            return true;
        }

        return false;
    }

    private async Task<bool> IsResultURI()
    {
        try
        {
            if (!TryNormalizeUri(TxtActivityLog.Text, out var uri))
                return false;

            var status = await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri);

            if (status == LaunchQuerySupportStatus.Available)
            {
                Log("Result is a URI and at least 1 supporting app has been found.");
                return true;
            }

            return false;
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
                var localFolder = (await AppPaths.GetLocalFolderAsync());
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

        if (m_isPreviewing) await StopCameraPreviewAsync();
        if (_directShowVideoDevice?.IsRunning == true) await StopLegacyDirectShowPreviewAsync();

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
        Bitmap? startingBitmap = null;
        var startingClipboardContent = Clipboard.GetContent();

        if (startingClipboardContent != null)
            if (startingClipboardContent.Contains(StandardDataFormats.Bitmap))
            {
                Log("Bitmap found in clipboard before launch.");
                var data = await startingClipboardContent.GetBitmapAsync();
                var bit = await data.OpenReadAsync();
                startingBitmap = new Bitmap(bit.AsStreamForRead());
            }

        App.MainWindow.WindowState = WindowState.Minimized;
        var uri = new Uri("ms-screenclip:");
        Log("Launching Snipping Tool.");

        var launchResult = await Launcher.LaunchUriAsync(uri);
        if (launchResult == false)
        {
            Log("Failed to launch.");
            DidDecodeSucceed(2);
            App.MainWindow.WindowState = WindowState.Normal;
            return;
        }

        Log("Snipping Tool launched. Polling clipboard for new bitmap.");

        // Poll clipboard for up to 60 seconds for a new bitmap
        Bitmap? bitmap = null;
        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < timeout)
        {
            await Task.Delay(500);

            try
            {
                var clipboardContent = Clipboard.GetContent();
                if (clipboardContent == null || !clipboardContent.Contains(StandardDataFormats.Bitmap))
                    continue;

                var data = await clipboardContent.GetBitmapAsync();
                var bit = await data.OpenReadAsync();
                var candidate = new Bitmap(bit.AsStreamForRead());

                if (startingBitmap != null && CompareBitmaps(startingBitmap, candidate))
                    continue; // clipboard hasn't changed yet

                bitmap = candidate;
                break;
            }
            catch
            {
                // clipboard access can fail transiently; keep polling
            }
        }

        App.MainWindow.WindowState = WindowState.Normal;

        if (bitmap == null)
        {
            Log("No new bitmap detected in clipboard after Snipping Tool.");
            DidDecodeSucceed(3);
            return;
        }

        Log("New bitmap detected. Decoding.");
        var result = DecodeBitmap(bitmap);
        if (result != null)
        {
            TxtActivityLog.Text = result;
            BitmapToImageSource(bitmap);
            DidDecodeSucceed(0);
            var isUri = await IsResultURI();
            if (isUri) OpenTextWithButton.IsEnabled = true;
        }
        else
        {
            DidDecodeSucceed(1);
            BarcodeViewer.Source = null;
        }
    }

    public async void DecodeFromFile(string filepath)
    {
        //StorageFile file;
        //file = await StorageFile.GetFileFromPathAsync(filepath);
        var file = await StorageFile.GetFileFromPathAsync(filepath);
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

            BitmapToImageSource(bitmap);

            var isUri = await IsResultURI();
            if (isUri) OpenTextWithButton.IsEnabled = true;
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

    private void PasteKeyboardAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        DecodeFromClipboard(this, new RoutedEventArgs());
        args.Handled = true;
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
            var file = await (await AppPaths.GetLocalFolderAsync()).CreateFileAsync("temp.png",
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
        var tempCSVExists = await (await AppPaths.GetLocalFolderAsync()).TryGetItemAsync("temp.csv");

        if (path != null && tempCSVExists != null)
        {
            var csv = await (await AppPaths.GetLocalFolderAsync()).GetFileAsync("temp.csv");
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
            if (await IsResultURI() == false || !TryNormalizeUri(TxtActivityLog.Text, out var launchUri))
            {
                DidDecodeSucceed(6);
                OpenTextWithButton.IsEnabled = false;
                return;
            }

            await Launcher.LaunchUriAsync(launchUri, options);
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

        var csv = await (await AppPaths.GetLocalFolderAsync()).CreateFileAsync("temp.csv",
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


        var tempCSVExists = await (await AppPaths.GetLocalFolderAsync()).TryGetItemAsync("temp.csv");

        if (tempCSVExists != null)
        {
            var csv = await (await AppPaths.GetLocalFolderAsync()).GetFileAsync("temp.csv");
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
