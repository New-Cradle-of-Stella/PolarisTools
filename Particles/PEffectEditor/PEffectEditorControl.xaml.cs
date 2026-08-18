using Polaris.Particles.Debugging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PolarisTools.Particles.PEffectEditor;

public partial class PEffectEditorControl : UserControl
{
    private string? _filePath;
    private bool _loading;

    internal bool IsDirty { get; private set; }

    public PEffectEditorControl()
    {
        InitializeComponent();
    }

    internal void LoadFile(string path)
    {
        _loading = true;
        try
        {
            _filePath = path;
            SourceEditor.Text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            IsDirty = false;
            StatusText.Text = path;
        }
        finally
        {
            _loading = false;
        }
    }

    internal void SaveFile(string? path = null)
    {
        string target = string.IsNullOrWhiteSpace(path) ? _filePath ?? string.Empty : path!;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("The .peffect document has no file path.");

        File.WriteAllText(target, SourceEditor.Text, new UTF8Encoding(false));
        _filePath = target;
        IsDirty = false;
        StatusText.Text = "Saved " + target;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveFile();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PEffect Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "WPF routed event handlers must return void; all exceptions are handled in this method.")]
    private async void Debug_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveFile();
            IReadOnlyList<PEffectDebugWireFile> files = BuildSnapshot();
            StatusText.Text = $"Pushing {files.Count} .peffect file(s)…";
            (bool ok, string message) = await PEffectDebugClient.SendAsync(files, TimeSpan.FromSeconds(4));
            StatusText.Text = message;
            if (!ok)
                MessageBox.Show(message, "PEffect Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "PEffect Debug", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<PEffectDebugWireFile> BuildSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
            throw new InvalidOperationException("Save this .peffect before debugging it.");

        string root = FindProjectRoot(Path.GetDirectoryName(_filePath)!);
        var files = new List<PEffectDebugWireFile>();
        foreach (string path in Directory.EnumerateFiles(root, "*.peffect", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(root, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string text = string.Equals(path, _filePath, StringComparison.OrdinalIgnoreCase)
                ? SourceEditor.Text
                : File.ReadAllText(path);
            files.Add(new PEffectDebugWireFile(
                Path.GetFileNameWithoutExtension(path),
                MakeRelativePath(root, path),
                text));
        }

        if (files.Count == 0)
            throw new InvalidOperationException("No .peffect files were found in the project.");
        return files;
    }

    private static string FindProjectRoot(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current != null)
        {
            if (current.EnumerateFiles("*.csproj", SearchOption.TopDirectoryOnly).Any())
                return current.FullName;
            current = current.Parent;
        }
        return directory;
    }

    private static bool IsBuildOutput(string root, string path)
    {
        string relative = MakeRelativePath(root, path);
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("bin", StringComparison.OrdinalIgnoreCase)
               || first.Equals("obj", StringComparison.OrdinalIgnoreCase)
               || first.Equals(".git", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeRelativePath(string root, string path)
    {
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(prefix.Length)
            : path;
    }

    private void SourceEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
            IsDirty = true;
    }
}
