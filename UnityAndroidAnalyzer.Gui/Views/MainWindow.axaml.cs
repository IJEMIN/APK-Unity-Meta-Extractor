using Avalonia.Controls;
using Avalonia.Interactivity;
using UnityAndroidAnalyzer.Gui.ViewModels;

namespace UnityAndroidAnalyzer.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SelectApk_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var dlg = new OpenFileDialog
        {
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "APK", Extensions = { "apk" } } }
        };

        var result = await dlg.ShowAsync(this);
        if (result is { Length: > 0 })
            vm.ApkPath = result[0];
    }

    private async void Analyze_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        await vm.AnalyzeLocalAsync();
    }
}