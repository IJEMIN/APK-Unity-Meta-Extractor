using System.IO.Compression;
using System.Text;

namespace UnityProjectAnalyzer.Core;

public class UnityAnalyzer : IUnityAnalyzer
{
    public string DownloadRootPath { get; set; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityProjectAnalyzer");

    public async Task<AnalysisResult> AnalyzeLocalAsync(string path, IEnumerable<string> extraPaths)
    {
        if (Directory.Exists(path))
        {
            return await Task.Run(() => PerformDirectoryAnalysis(path));
        }

        var containerPaths = new List<string> { path };
        containerPaths.AddRange(extraPaths);

        var zipArchives = new List<ZipArchive>();
        try
        {
            foreach (var containerPath in containerPaths)
            {
                if (File.Exists(containerPath))
                {
                    zipArchives.Add(ZipFile.OpenRead(containerPath));
                }
            }

            if (zipArchives.Count == 0)
            {
                throw new FileNotFoundException("No valid APK/OBB containers found.");
            }

            return await Task.Run(() => PerformAnalysis(Path.GetFileName(path), zipArchives));
        }
        finally
        {
            foreach (var z in zipArchives)
            {
                z.Dispose();
            }
        }
    }

    public async Task<AnalysisResult> AnalyzeDeviceAsync(string deviceSerial, string packageName)
    {
        var adb = new AdbHelper();
        adb.SetSerial(deviceSerial);

        var tempDir = Path.Combine(DownloadRootPath, packageName);
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
        Directory.CreateDirectory(tempDir);

        try
        {
            var apkPaths = adb.GetApkPaths(packageName);
            var obbPaths = adb.GetObbPaths(packageName);

            var localApkPaths = new List<string>();
            var localObbPaths = new List<string>();

            for (int i = 0; i < apkPaths.Count; i++)
            {
                var localPath = Path.Combine(tempDir, $"base_{i}.apk");
                adb.Pull(apkPaths[i], localPath);
                localApkPaths.Add(localPath);
            }

            for (int i = 0; i < obbPaths.Count; i++)
            {
                var localPath = Path.Combine(tempDir, Path.GetFileName(obbPaths[i]));
                adb.Pull(obbPaths[i], localPath);
                localObbPaths.Add(localPath);
            }

            if (localApkPaths.Count == 0)
            {
                throw new Exception("Failed to pull APK from device.");
            }

            // 분석 수행
            var result = await AnalyzeLocalAsync(localApkPaths[0], localApkPaths.Skip(1).Concat(localObbPaths));
            
            // 임시 파일 경로 저장 (GUI에서 열기 기능을 위해)
            var metadataPath = Path.Combine(tempDir, "global-metadata.dat");
            var scriptingAssembliesPath = Path.Combine(tempDir, "ScriptingAssemblies.json");
            
            return result;
        }
        finally
        {
        }
    }

    private AnalysisResult PerformDirectoryAnalysis(string rootPath)
    {
        // For directories (.app or Windows folder), we search for files recursively
        var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
        
        var scriptingAssembliesJson = "";
        var scriptingPath = allFiles.FirstOrDefault(f => f.EndsWith("ScriptingAssemblies.json", StringComparison.OrdinalIgnoreCase));
        if (scriptingPath != null) scriptingAssembliesJson = File.ReadAllText(scriptingPath);
        
        var runtimeInitJson = "";
        var runtimeInitPath = allFiles.FirstOrDefault(f => f.EndsWith("RuntimeInitializeOnLoads.json", StringComparison.OrdinalIgnoreCase));
        if (runtimeInitPath != null) runtimeInitJson = File.ReadAllText(runtimeInitPath);
        
        var unityVersion = Analyzer.DetectUnityVersionFromDirectory(rootPath) ?? "Unknown";
        
        byte[]? metadataBytes = null;
        var metadataFile = allFiles.FirstOrDefault(f => f.EndsWith("global-metadata.dat", StringComparison.OrdinalIgnoreCase));
        if (metadataFile != null) metadataBytes = File.ReadAllBytes(metadataFile);

        // 유니티 데이터 분석 수행
        var parsingData = Analyzer.AnalyzeDirectoryUnity3D(rootPath);

        var rp = Analyzer.DetectRenderPipeline(metadataBytes, scriptingAssembliesJson, allFiles);
        var entities = Analyzer.DetectEntities(scriptingAssembliesJson, runtimeInitJson, parsingData);
        var ngui = Analyzer.DetectNgui(parsingData);
        var addr = Analyzer.DetectAddressablesFromDirectory(rootPath);
        var insights = Analyzer.GetMajorScriptInsights(parsingData);
        var havok = Analyzer.DetectHavokPhysics(scriptingAssembliesJson, runtimeInitJson, metadataBytes);
        var entPhys = Analyzer.DetectEntitiesPhysics(scriptingAssembliesJson);
        var uitk = Analyzer.DetectUiToolkitFromDirectory(rootPath, parsingData);

        return new AnalysisResult
        {
            Title = Path.GetFileName(rootPath),
            UnityVersion = unityVersion,
            RenderPipeline = rp,
            EntitiesUsed = entities,
            EntitiesPhysicsUsed = entPhys,
            NguiUsed = ngui,
            AddressablesUsed = addr,
            HavokUsed = havok,
            UiToolkitUsed = uitk,
            MajorScriptInsights = insights,
            MetadataPath = metadataFile,
            ScriptingAssembliesPath = scriptingPath
        };
    }

    private AnalysisResult PerformAnalysis(string title, List<ZipArchive> zipArchives)
    {
        var scriptingAssembliesJson = Analyzer.ExtractJsonTextFromContainers(zipArchives, "assets/bin/Data/ScriptingAssemblies.json");
        var runtimeInitJson = Analyzer.ExtractJsonTextFromContainers(zipArchives, "assets/bin/Data/RuntimeInitializeOnLoads.json");
        var unityVersion = Analyzer.DetectUnityVersionFromContainers(zipArchives) ?? "Unknown";
        var metadataBytes = Analyzer.ExtractMetadataFromContainers(zipArchives);

        // 유니티 데이터 분석 수행 (2단계 분석 전략)
        var parsingData = Analyzer.AnalyzeDataUnity3D(zipArchives);

        var allEntryNames = zipArchives.SelectMany(z => z.Entries.Select(e => e.FullName));
        var rp = Analyzer.DetectRenderPipeline(metadataBytes, scriptingAssembliesJson, allEntryNames);
        var entities = Analyzer.DetectEntities(scriptingAssembliesJson, runtimeInitJson, parsingData);
        var ngui = Analyzer.DetectNgui(parsingData);
        var addr = Analyzer.DetectAddressables(zipArchives);
        var insights = Analyzer.GetMajorScriptInsights(parsingData);
        var havok = Analyzer.DetectHavokPhysics(scriptingAssembliesJson, runtimeInitJson, metadataBytes);
        var entPhys = Analyzer.DetectEntitiesPhysics(scriptingAssembliesJson);
        var uitk = Analyzer.DetectUiToolkit(zipArchives, parsingData);

        // 추출된 파일들을 임시 디렉토리에 저장하여 나중에 열어볼 수 있게 함
        string? savedMetadataPath = null;
        string? savedScriptingPath = null;
        try
        {
            var tempDir = Path.Combine(DownloadRootPath, "LastAnalysis");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            if (metadataBytes != null)
            {
                savedMetadataPath = Path.Combine(tempDir, "global-metadata.dat");
                File.WriteAllBytes(savedMetadataPath, metadataBytes);
            }

            if (!string.IsNullOrEmpty(scriptingAssembliesJson))
            {
                savedScriptingPath = Path.Combine(tempDir, "ScriptingAssemblies.json");
                File.WriteAllText(savedScriptingPath, scriptingAssembliesJson);
            }
        }
        catch { /* Ignore */ }

        return new AnalysisResult
        {
            Title = title,
            UnityVersion = unityVersion,
            RenderPipeline = rp,
            EntitiesUsed = entities,
            EntitiesPhysicsUsed = entPhys,
            NguiUsed = ngui,
            AddressablesUsed = addr,
            HavokUsed = havok,
            UiToolkitUsed = uitk,
            MajorScriptInsights = insights,
            MetadataPath = savedMetadataPath,
            ScriptingAssembliesPath = savedScriptingPath
        };
    }
}
