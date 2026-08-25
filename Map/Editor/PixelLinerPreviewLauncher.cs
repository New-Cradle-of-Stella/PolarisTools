using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PolarisTools.Map.Editor;

internal static class PixelLinerPreviewLauncher
{
    internal static string Open(string extractionResult)
    {
        string[] fields = (extractionResult ?? "").Split('|');
        if (fields.Length < 2 || !File.Exists(fields[0]) || !Directory.Exists(fields[1]))
            throw new InvalidDataException("The game returned an invalid preview extraction result.");

        string executable = FindExecutable();
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "\"" + fields[0].Replace("\"", "\\\"") + "\"",
            WorkingDirectory = fields[1],
            UseShellExecute = true,
        });
        string pxls = fields.Length > 2 ? fields[2] : "?";
        string textures = fields.Length > 3 ? fields[3] : "?";
        return "PixelLiner opened · " + pxls + " PXLS · " + textures + " textures · temporary preview only";
    }

    static string FindExecutable()
    {
        string[] registryKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{19E44BBD-50EE-DD56-6A0C-D91FF0D003E6}",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{19E44BBD-50EE-DD56-6A0C-D91FF0D003E6}",
        };
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string keyName in registryKeys)
            {
                using (RegistryKey? key = root.OpenSubKey(keyName))
                {
                    string? location = key?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        string candidate = Path.Combine(location, "PixelLiner.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
        }

        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "PixelLiner", "PixelLiner.exe");
        if (File.Exists(fallback)) return fallback;
        throw new FileNotFoundException("PixelLiner is not installed or its installation could not be located.");
    }
}
