namespace UnityProjectAnalyzer.Core;

// UnityProjectAnalyzer.Core

public class AnalysisResult
{
    public string Title { get; set; } = "";
    public string UnityVersion { get; set; } = "";
    public string RenderPipeline { get; set; } = "";
    public bool EntitiesUsed { get; set; }
    public bool NguiUsed { get; set; }
    public bool AddressablesUsed { get; set; }
    public bool HavokUsed { get; set; }
    public bool EntitiesPhysicsUsed { get; set; }
    public bool UiToolkitUsed { get; set; }
    public List<(string Script, int Count)> MajorScriptInsights { get; set; } = new();
    
    // 파일 경로 저장을 위한 필드
    public string? MetadataPath { get; set; }
    public string? ScriptingAssembliesPath { get; set; }
}

public interface IUnityAnalyzer
{
    string DownloadRootPath { get; set; }
    Task<AnalysisResult> AnalyzeLocalAsync(string path, IEnumerable<string> extraPaths);
    Task<AnalysisResult> AnalyzeDeviceAsync(string deviceSerial, string packageName);
}
