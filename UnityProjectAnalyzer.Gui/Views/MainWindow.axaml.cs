using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UnityProjectAnalyzer.Gui.ViewModels;

namespace UnityProjectAnalyzer.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SelectFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (OperatingSystem.IsMacOS())
        {
            var path = await OpenMacNativeFilePickerAsync();
            if (!string.IsNullOrEmpty(path))
            {
                vm.ApkPath = path;
                return;
            }
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                },
                new FilePickerFileType("Supported Files")
                {
                    Patterns = new[] { "*.apk" }
                }
            }
        });

        if (files is { Count: > 0 })
        {
            vm.ApkPath = files[0].Path.LocalPath;
        }
    }

    private async Task<string?> OpenMacNativeFilePickerAsync()
    {
        try
        {
            // AppleScript: 'choose file' allows selecting .app bundles as files by default.
            // Using multiple -e to avoid escaping issues with single/double quotes in a single string.
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = "-e \"try\" " +
                            "-e \"set theFile to choose file with prompt \\\"Select File\\\"\" " +
                            "-e \"return POSIX path of theFile\" " +
                            "-e \"on error\" " +
                            "-e \"return \\\"\\\"\" " +
                            "-e \"end try\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var result = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var path = result.Trim();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private async void SelectFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Directory",
            AllowMultiple = false
        });

        if (folders is { Count: > 0 })
        {
            vm.ApkPath = folders[0].Path.LocalPath;
        }
    }

    private async void Analyze_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        await vm.AnalyzeLocalAsync();
    }

    private async void ChangeDownloadPath_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Download Root Path",
            AllowMultiple = false
        });

        if (folders is { Count: > 0 })
        {
            vm.DownloadRootPath = folders[0].Path.LocalPath;
        }
    }
}