using System.IO;

using Windows.Storage;

namespace barcodrod.io.Helpers;

/// <summary>
/// Resolves the per-user local data folder in a way that works both when the app runs
/// packaged (MSIX, where <see cref="ApplicationData"/> is available) and unpackaged
/// (classic MSI / portable, where <see cref="ApplicationData.Current"/> throws because
/// the process has no package identity).
/// </summary>
public static class AppPaths
{
    private static StorageFolder? _localFolder;

    /// <summary>
    /// Absolute path to the folder where the app stores its settings and logs.
    /// The folder is created if it does not yet exist.
    /// </summary>
    public static string LocalDataFolder
    {
        get
        {
            if (RuntimeHelper.IsMSIX)
            {
                return ApplicationData.Current.LocalFolder.Path;
            }

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "barcodrod.io");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// Returns the local data folder as a <see cref="StorageFolder"/>, working in both packaged
    /// and unpackaged modes. Use this anywhere the code previously used
    /// <c>ApplicationData.Current.LocalFolder</c> so it keeps functioning when the app is
    /// installed via the classic (unpackaged) MSI.
    /// </summary>
    public static async Task<StorageFolder> GetLocalFolderAsync()
    {
        if (RuntimeHelper.IsMSIX)
        {
            return ApplicationData.Current.LocalFolder;
        }

        return _localFolder ??= await StorageFolder.GetFolderFromPathAsync(LocalDataFolder);
    }
}
