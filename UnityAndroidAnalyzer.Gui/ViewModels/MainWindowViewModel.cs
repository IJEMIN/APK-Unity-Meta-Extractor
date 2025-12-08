using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UnityAndroidAnalyzer.Core;

namespace UnityAndroidAnalyzer.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IUnityAnalyzer _analyzer;

    [ObservableProperty]
    private string? apkPath;

    [ObservableProperty]
    private string markdown = "";

    public MainWindowViewModel(IUnityAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public async Task AnalyzeLocalAsync()
    {
        if (string.IsNullOrWhiteSpace(ApkPath))
            return;

        var result = await _analyzer.AnalyzeLocalAsync(ApkPath, Array.Empty<string>());
        Markdown = result.Markdown;
    }
}