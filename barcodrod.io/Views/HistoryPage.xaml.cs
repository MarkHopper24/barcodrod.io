using barcodrod.io.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Text.Core;

namespace barcodrod.io.Views;

public sealed partial class HistoryPage : Page
{



    public HistoryPage()
    {
        App.GetService<HistoryViewModel>();
        InitializeComponent();
        RefreshCounters();
        HistoryList.SelectionChanged += (s, e) =>
        {
            RefreshCounters();
        };
    }



    private async void SaveFromHistory(object sender, RoutedEventArgs e)
    {
        var selectedItems = HistoryList.SelectedItems;

        if (selectedItems.Count == 0)
        {
            return;
        }


        var timestamp = DateTime.Now.ToString("MM.dd.yy.HHmm");
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;


        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var savePath = await picker.PickSingleFolderAsync();
        if (savePath == null)
        {
            return;
        }
        //create two folders, one for the images and one for the text files
        var imageFolder = await savePath.CreateFolderAsync("Barcodes" + "." + timestamp);
        var textFolder = await savePath.CreateFolderAsync("Text" + "." + timestamp);

        var localFolder = ApplicationData.Current.LocalFolder;
        var historyFolder = await localFolder.GetFolderAsync("History");
        if (historyFolder != null)
        {
            var files = await historyFolder.GetFilesAsync();

            if (files.Count > 0)
            {

                var imageFiles = Directory.GetFiles(historyFolder.Path, "*.png");
                var textFiles = Directory.GetFiles(historyFolder.Path, "*.txt");
                //remove the files extensions from the file paths in imageFiles list
                for (int i = 0; i < imageFiles.Length; i++)
                {
                    imageFiles[i] = Path.GetFileNameWithoutExtension(imageFiles[i]);
                }
                for (int i = 0; i < textFiles.Length; i++)
                {
                    textFiles[i] = Path.GetFileNameWithoutExtension(textFiles[i]);
                }


                //var sharedItemsList = imageFiles.Intersect(textFiles);

                var filesToSave = new List<string>();

                int selectedItemCount = selectedItems.Count();
                for (int i = 0; i < selectedItemCount; i++)
                {
                    var item = selectedItems[i] as ListViewItem;

                    if (item == null)
                    {
                        continue;
                    }
                    var grid = item.Content as Grid;
                    var Panel2 = grid.Children[0] as StackPanel;
                    var pathTextBlock = Panel2.Children[0] as TextBlock;
                    var path = pathTextBlock.Text;
                    var imageFilePath = Path.Combine(historyFolder.Path, path);
                    if (File.Exists(imageFilePath))
                    {
                        //copy the image file to the image folder
                        File.Copy(imageFilePath, Path.Combine(imageFolder.Path, path));
                    }
                    //remove the file extension from the imageFilePath and replace it with .txt
                    var textFilePath = Path.ChangeExtension(imageFilePath, ".txt");

                    if (File.Exists(textFilePath))
                    {
                        //copy the text file to the text folder
                        File.Copy(textFilePath, Path.Combine(textFolder.Path, Path.GetFileName(textFilePath)));
                    }
                }
                //if folder exists, open it
                if (Directory.Exists(imageFolder.Path))
                {
                    await Launcher.LaunchFolderAsync(imageFolder);
                }

                if (Directory.Exists(textFolder.Path))
                {
                    await Launcher.LaunchFolderAsync(textFolder);
                }
            }
        }
    }

    private async void SaveImagesFromHistory(object sender, RoutedEventArgs e)
    {
        var selectedItems = HistoryList.SelectedItems;

        if (selectedItems.Count == 0)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("MM.dd.yy.HHmm");
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;


        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var savePath = await picker.PickSingleFolderAsync();
        if (savePath == null)
        {
            return;
        }
        //create two folders, one for the images and one for the text files
        var imageFolder = await savePath.CreateFolderAsync("Barcodes" + "." + timestamp);


        var localFolder = ApplicationData.Current.LocalFolder;
        var historyFolder = await localFolder.GetFolderAsync("History");
        if (historyFolder != null)
        {
            var files = await historyFolder.GetFilesAsync();

            if (files.Count > 0)
            {

                var imageFiles = Directory.GetFiles(historyFolder.Path, "*.png");
                //remove the files extensions from the file paths in imageFiles list
                for (int i = 0; i < imageFiles.Length; i++)
                {
                    imageFiles[i] = Path.GetFileNameWithoutExtension(imageFiles[i]);
                }

                //var sharedItemsList = imageFiles.Intersect(textFiles);

                var filesToSave = new List<string>();

                int selectedItemCount = selectedItems.Count();
                for (int i = 0; i < selectedItemCount; i++)
                {
                    var item = selectedItems[i] as ListViewItem;

                    if (item == null)
                    {
                        continue;
                    }
                    var grid = item.Content as Grid;
                    var Panel2 = grid.Children[0] as StackPanel;
                    var pathTextBlock = Panel2.Children[0] as TextBlock;
                    var path = pathTextBlock.Text;
                    var imageFilePath = Path.Combine(historyFolder.Path, path);
                    if (File.Exists(imageFilePath))
                    {
                        //copy the image file to the image folder
                        File.Copy(imageFilePath, Path.Combine(imageFolder.Path, path));
                    }
                }
                //if folder exists, open it
                if (Directory.Exists(imageFolder.Path))
                {
                    await Launcher.LaunchFolderAsync(imageFolder);
                }
            }
        }
    }

    private async void SaveTextFromHistory(object sender, RoutedEventArgs e)
    {
        var selectedItems = HistoryList.SelectedItems;

        if (selectedItems.Count == 0)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("MM.dd.yy.HHmm");
        var window = new Microsoft.UI.Xaml.Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;


        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var savePath = await picker.PickSingleFolderAsync();
        if (savePath == null)
        {
            return;
        }
        //create two folders, one for the images and one for the text files
        var textFolder = await savePath.CreateFolderAsync("Text" + "." + timestamp);

        var localFolder = ApplicationData.Current.LocalFolder;
        var historyFolder = await localFolder.GetFolderAsync("History");
        if (historyFolder != null)
        {
            var files = await historyFolder.GetFilesAsync();

            if (files.Count > 0)
            {

                var textFiles = Directory.GetFiles(historyFolder.Path, "*.txt");

                for (int i = 0; i < textFiles.Length; i++)
                {
                    textFiles[i] = Path.GetFileNameWithoutExtension(textFiles[i]);
                }


                //var sharedItemsList = imageFiles.Intersect(textFiles);

                var filesToSave = new List<string>();

                int selectedItemCount = selectedItems.Count();
                for (int i = 0; i < selectedItemCount; i++)
                {
                    var item = selectedItems[i] as ListViewItem;

                    if (item == null)
                    {
                        continue;
                    }
                    var grid = item.Content as Grid;
                    var Panel2 = grid.Children[0] as StackPanel;
                    var pathTextBlock = Panel2.Children[0] as TextBlock;
                    var path = pathTextBlock.Text;
                    //remove the file extension from the imageFilePath and replace it with .txt
                    var textPath = Path.ChangeExtension(path, ".txt");
                    var textFilePath = Path.Combine(historyFolder.Path, textPath);

                    //remove the file extension from the imageFilePath and replace it with .txt

                    if (File.Exists(textFilePath))
                    {
                        //copy the text file to the text folder
                        File.Copy(textFilePath, Path.Combine(textFolder.Path, Path.GetFileName(textFilePath)));
                    }
                }

                //if folder exists, open it
                if (Directory.Exists(textFolder.Path))
                {
                    await Launcher.LaunchFolderAsync(textFolder);
                }
            }
        }
    }

    private async void SelectAllHistory(object sender, RoutedEventArgs e)
    {
        //check if all items are selected, if so, deselect them all
        if (HistoryList.SelectedItems.Count == HistoryList.Items.Count)
        {
            HistoryList.SelectedItems.Clear();
        }

        else
        {
            HistoryList.SelectAll();

        }
        RefreshCounters();
    }

    private async void DeleteFromHistory(object sender, RoutedEventArgs e)
    {
        var selectedItems = HistoryList.SelectedItems;

        if (selectedItems.Count == 0)
        {
            return;
        }
        var localFolder = ApplicationData.Current.LocalFolder;
        var historyFolder = await localFolder.GetFolderAsync("History");
        if (historyFolder != null)
        {
            var files = await historyFolder.GetFilesAsync();

            if (files.Count > 0)
            {
                //prompt the user to confirm the deletion of the history
                ContentDialog deleteHistoryDialog = new ContentDialog
                {
                    Title = "Delete History?",
                    Content = "Are you sure you want to delete " + selectedItems.Count() + " barcode(s)? This action cannot be undone.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel"
                };
                deleteHistoryDialog.XamlRoot = this.XamlRoot;

                ContentDialogResult result = await deleteHistoryDialog.ShowAsync();
                //if the user clicks the delete button, delete the history
                if (result == ContentDialogResult.Primary)
                {
                    var imageFiles = Directory.GetFiles(historyFolder.Path, "*.png");
                    var textFiles = Directory.GetFiles(historyFolder.Path, "*.txt");
                    //remove the files extensions from the file paths in imageFiles list
                    for (int i = 0; i < imageFiles.Length; i++)
                    {
                        imageFiles[i] = Path.GetFileNameWithoutExtension(imageFiles[i]);
                    }
                    for (int i = 0; i < textFiles.Length; i++)
                    {
                        textFiles[i] = Path.GetFileNameWithoutExtension(textFiles[i]);
                    }


                    //var sharedItemsList = imageFiles.Intersect(textFiles);

                    var filesToDelete = new List<string>();

                    int selectedItemCount = selectedItems.Count();

                    for (int i = 0; i < selectedItemCount; i++)
                    {
                        var item = selectedItems[i] as ListViewItem;

                        if (item == null)
                        {
                            continue;
                        }

                        var grid = item.Content as Grid;
                        var Panel2 = grid.Children[0] as StackPanel;
                        var pathTextBlock = Panel2.Children[0] as TextBlock;
                        var path = pathTextBlock.Text;
                        var imageFilePath = Path.Combine(historyFolder.Path, path);
                        var textFilePath = Path.ChangeExtension(imageFilePath, ".txt");

                        if (File.Exists(imageFilePath))
                        {
                            //delete the image file
                            filesToDelete.Add(imageFilePath);
                            filesToDelete.Add(textFilePath);
                        }

                    }

                    foreach (var file in filesToDelete)
                    {
                        File.Delete(file);
                    }
                    while (HistoryList.SelectedItems.Count > 0)
                    {
                        HistoryList.Items.Remove(HistoryList.SelectedItem);
                    }
                    RefreshCounters();

                }

                RefreshCounters();

            }
        }
    }

    private async void LoadHistory(object sender, RoutedEventArgs e)
    {
        var isHistoryEnabled = await IsHistoryEnabled();
        if (isHistoryEnabled == false)
        {
            ContentDialog enableHistoryDialog = new ContentDialog
            {
                Title = "History Disabled",
                Content = "History is currently disabled. It can be enabled within Settings.",
                PrimaryButtonText = "Open Settings",
                CloseButtonText = "Go Back"
            };
            enableHistoryDialog.XamlRoot = this.XamlRoot;

            ContentDialogResult result = await enableHistoryDialog.ShowAsync();
            //if the user clicks the delete button, delete the history
            if (result == ContentDialogResult.Primary)
            {
                Frame.Navigate(typeof(SettingsPage));

            }
            else
            {
                Frame.GoBack();
            }
        }

        if (isHistoryEnabled == true)
        {

            var localFolder = ApplicationData.Current.LocalFolder;
            var historyFolder = await localFolder.GetFolderAsync("History");

            if (historyFolder != null)
            {
                var unorderedFiles = await historyFolder.GetFilesAsync();
                var files = unorderedFiles.ToList();

                if (files.Count > 0)
                {
                    string[] imageFiles = Directory.GetFiles(historyFolder.Path, "*.png");
                    Array.Sort(imageFiles, new Comparison<string>((x, y) => DateTime.Compare(File.GetCreationTime(y), File.GetCreationTime(x))));
                    int imageCount = imageFiles.Length;

                    int i = 0;
                    while (i < imageCount)
                    {

                        StorageFile imageFile = await StorageFile.GetFileFromPathAsync(imageFiles[i]);
                        if (imageFile == null)
                        {
                            i++;
                            continue;
                        }
                        string imageFileName = imageFile.DisplayName;

                        string imageFileNameNoExtension = Path.GetFileNameWithoutExtension(imageFileName);

                        if (File.Exists(historyFolder.Path + "\\" + imageFileNameNoExtension + ".txt") == false)
                        {
                            i++;
                            continue;
                        }

                        StorageFile textFile = await StorageFile.GetFileFromPathAsync(historyFolder.Path + "\\" + imageFileNameNoExtension + ".txt");
                        string textFileName = textFile.DisplayName;
                        string textFileNameNoExtension = Path.GetFileNameWithoutExtension(textFileName);

                        if (imageFileNameNoExtension == textFileNameNoExtension)
                        {
                            RefreshCounters();

                            Grid grid = new Grid();
                            grid.ColumnDefinitions.Add(new ColumnDefinition());
                            grid.ColumnDefinitions.Add(new ColumnDefinition());
                            grid.ColumnDefinitions[0].Width = new GridLength(200);
                            grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                            StackPanel stackPanel = new StackPanel();
                            stackPanel.Orientation = Orientation.Vertical;

                            TextBlock pathTextBlock = new TextBlock();
                            pathTextBlock.Text = imageFileName;
                            pathTextBlock.FontSize = 12;
                            pathTextBlock.VerticalAlignment = VerticalAlignment.Top;
                            pathTextBlock.Margin = new Thickness(0, 10, 0, 0);
                            stackPanel.Children.Add(pathTextBlock);


                            Image image = new Image();
                            BitmapImage bitmapimage = new BitmapImage();
                            await bitmapimage.SetSourceAsync(await imageFile.OpenAsync(FileAccessMode.Read));
                            image.Source = bitmapimage;
                            image.VerticalAlignment = VerticalAlignment.Center;
                            image.Width = 150;
                            image.Height = 150;
                            image.Margin = new Thickness(0, 10, 0, 10);

                            MenuFlyout ImageRightClickCommandBar = ImageRightClickMenuBar;
                            //add copy image to command bar



                            image.RightTapped += (s, e) =>
                            {
                                ImageRightClickCommandBar.ShowAt(image, e.GetPosition(image));
                                CopyImageMenuButton.Click += async (s, e) =>
                                {
                                    var dataPackage = new DataPackage();
                                    dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(imageFile));
                                    Clipboard.SetContentWithOptions(dataPackage, new ClipboardContentOptions() { IsAllowedInHistory = true, IsRoamable = true });
                                    //close the command bar
                                    ImageRightClickCommandBar.Hide();
                                };

                                //SaveImageMenuButton.Click += async (s, e) =>
                                //{
                                //    String timestamp = DateTime.Now.ToString("MMddyy.HHmm");
                                //    Window window = new Microsoft.UI.Xaml.Window();
                                //    IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                                //    FileSavePicker picker = new Windows.Storage.Pickers.FileSavePicker();
                                //    picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                                //    picker.FileTypeChoices.Add("PNG", new List<string>() { ".png" });


                                //    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                                //    StorageFile savePath = await picker.PickSaveFileAsync();
                                //    if (savePath == null)
                                //    {
                                //        return;
                                //    }
                                //    else
                                //    {
                                //        await imageFile.CopyAndReplaceAsync(savePath);
                                //    }
                                //};

                                OpenImageMenuButton.Click += async (s, e) =>
                                {
                                    var options = new Windows.System.LauncherOptions();
                                    options.DisplayApplicationPicker = true;
                                    await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(imageFile.Path), options);
                                };

                            };




                            stackPanel.Children.Add(image);
                            stackPanel.Margin = new Thickness(0, 0, 0, 0);
                            stackPanel.HorizontalAlignment = HorizontalAlignment.Center;
                            stackPanel.VerticalAlignment = VerticalAlignment.Center;


                            grid.Children.Add(stackPanel);
                            Grid.SetColumn(stackPanel, 0);




                            TextBox textBox = new TextBox();
                            textBox.TextWrapping = TextWrapping.Wrap;


                            textBox.Text = await FileIO.ReadTextAsync(textFile);
                            textBox.Margin = new Thickness(0, 10, 0, 10);
                            textBox.Background = new SolidColorBrush(Colors.Transparent);

                            grid.Children.Add(textBox);
                            Grid.SetColumn(textBox, 1);
                            Grid.SetRow(textBox, 0);
                            Grid.SetRowSpan(textBox, 2);
                            textBox.IsReadOnly = true;






                            ListViewItem listViewItem = new ListViewItem();
                            listViewItem.Margin = new Thickness(0, 0, 10, 0);


                            listViewItem.Content = grid;

                            HistoryList.Items.Add(listViewItem);
                            i++;
                            // RefreshCounters();

                        }

                        //Make sure the history list is is scrolled to the bottom
                        //HistoryList.ScrollIntoView(HistoryList.Items[HistoryList.Items.Count - 1]);
                        RefreshCounters();

                    }







                }

            }
        }
    }

    private void RefreshCounters()
    {
        HistoryCountTextBlock.Text = "Count: " + HistoryList.Items.Count;
        HistoryCountSelectedTextBlock.Text = "Selected: " + HistoryList.SelectedItems.Count;

    }

    private async Task<bool> IsHistoryEnabled()
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        String settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //get the history folder
        var historyFolder = await localFolder.CreateFolderAsync("history", Windows.Storage.CreationCollisionOption.OpenIfExists);

        //read HistoryEnabled from settings.json
        string jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        JObject settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
        {

            return currentHistorySetting.Value<bool>();
        }
        else
        {
            return false;
        }
    }

}
