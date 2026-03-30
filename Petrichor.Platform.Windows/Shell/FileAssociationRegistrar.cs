using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Petrichor.Platform.Windows.Shell;

[SupportedOSPlatform("windows")]
public static class FileAssociationRegistrar
{
    private const string ProgId = "Petrichor.AudioFile";
    private static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".alac"
    ];

    public static void EnsureOpenWithRegistration(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        var openCommand = $"\"{executablePath}\" \"%1\"";

        using (var appCommandKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Petrichor.App.exe\shell\open\command"))
        {
            appCommandKey?.SetValue(string.Empty, openCommand);
        }

        using (var appTypesKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Petrichor.App.exe\SupportedTypes"))
        {
            foreach (var extension in SupportedExtensions)
            {
                appTypesKey?.SetValue(extension, string.Empty);
            }
        }

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey?.SetValue(string.Empty, "Petrichor Audio File");
        }

        using (var progIdCommandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
        {
            progIdCommandKey?.SetValue(string.Empty, openCommand);
        }

        foreach (var extension in SupportedExtensions)
        {
            using var openWithKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWithKey?.SetValue(ProgId, string.Empty, RegistryValueKind.String);
        }

        RefreshExplorerFileAssociations();
    }

    private static void RefreshExplorerFileAssociations()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
