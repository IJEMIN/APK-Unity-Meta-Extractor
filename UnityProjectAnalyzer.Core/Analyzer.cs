using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace UnityProjectAnalyzer.Core;

public class Analyzer
{
    // ---- Config / Patterns ----
    const string VersionPattern =
        @"((20[0-9]{2}|[5-9][0-9]{3})\.[0-9]+\.[0-9]+[fpab][0-9]*)";
    private const string MetadataPath = "assets/bin/Data/Managed/Metadata/global-metadata.dat";
    
    
    public static bool DetectHavokPhysics(
        string scriptingAssembliesJson,
        string runtimeInitJson,
        byte[]? metadataBytes)
    {
        // 1. ScriptingAssemblies.json 확인 (가장 확실)
        if (!string.IsNullOrEmpty(scriptingAssembliesJson))
        {
            var s = scriptingAssembliesJson;
            if (s.IndexOf("Havok.Physics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("com.havok.physics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(runtimeInitJson))
        {
            var s = runtimeInitJson;
            if (s.IndexOf("Havok.Physics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        if (metadataBytes != null && metadataBytes.Length > 0)
        {
            var s = ExtractPrintableAscii(metadataBytes);
            if (s.IndexOf("Havok.Physics", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
    
    public static string DetectRenderPipeline(byte[]? metadataBytes, string scriptingAssembliesJson = "", IEnumerable<string>? fileNames = null)
    {
        var s = "";
        if (metadataBytes != null && metadataBytes.Length > 0)
        {
            s = ExtractPrintableAscii(metadataBytes).ToLowerInvariant();
        }
        
        if (!string.IsNullOrEmpty(scriptingAssembliesJson))
        {
            s += "\n" + scriptingAssembliesJson.ToLowerInvariant();
        }
        
        if (fileNames != null)
        {
            foreach (var f in fileNames)
            {
                s += "\n" + Path.GetFileName(f).ToLowerInvariant();
            }
        }

        if (string.IsNullOrEmpty(s))
            return "Unknown";

        if (s.Contains("com.unity.render-pipelines.universal") || // package
            s.Contains("unityengine.rendering.universal") || // namespace
            s.Contains("universalrenderpipeline") ||
            s.Contains("forwardrenderer") ||
            s.Contains("renderer2d") ||
            s.Contains("unity.renderpipelines.universal.runtime"))
            return "URP";

        if (s.Contains("com.unity.render-pipelines.high-definition") || // package
            s.Contains("unityengine.rendering.highdefinition") || // namespace
            s.Contains("hdrenderpipeline") ||
            s.Contains("unity.renderpipelines.highdefinition.runtime"))
            return "HDRP";
        
        // Scriptable Render Pipeline (SRP) without URP/HDRP 
        if (s.Contains("com.unity.render-pipelines.core") || 
            s.Contains("unity.renderpipelines.core.runtime")) 
            return "SRP";

        // If we have metadata or scripting assemblies, and none of the above matched, it's likely Built-in
        // But if we have absolutely nothing, it should remain Unknown
        return "Built-in";
    }

    public static string ExtractPrintableAscii(byte[] bytes, int minLen = 4)
    {
        var sb = new StringBuilder(bytes.Length);
        var current = new StringBuilder();

        foreach (var b in bytes)
        {
            if (b >= 32 && b <= 126)
            {
                current.Append((char)b);
            }
            else
            {
                if (current.Length >= minLen)
                {
                    sb.AppendLine(current.ToString());
                }
                current.Clear();
            }
        }

        if (current.Length >= minLen)
        {
            sb.AppendLine(current.ToString());
        }

        return sb.ToString();
    }

    public static byte[]? ExtractMetadataFromContainers(List<ZipArchive> zips)
    {
        foreach (var z in zips)
        {
            var bytes = ExtractEntryBytes(z, MetadataPath);
            if (bytes != null && bytes.Length > 0) return bytes;
        }
        return null;
    }

    public static string? DetectUnityVersionFromDirectory(string rootPath)
    {
        var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
        
        // 1) globalgamemanagers
        var ggm = allFiles.FirstOrDefault(f => Path.GetFileName(f) == "globalgamemanagers");
        if (ggm != null)
        {
            var v = FindVersionInFile(ggm);
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // 2) data.unity3d
        var data3d = allFiles.FirstOrDefault(f => Path.GetFileName(f) == "data.unity3d");
        if (data3d != null)
        {
            var v = FindVersionInFile(data3d);
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // 3) UnityPlayer.dll (Windows) or UnityPlayer (Mac) or UnityFramework (Mac)
        var players = allFiles.Where(f => 
            f.EndsWith("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase) || 
            f.EndsWith("UnityPlayer", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith("UnityFramework", StringComparison.OrdinalIgnoreCase));
        
        foreach (var p in players)
        {
            var v = FindVersionInFile(p);
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // 4) fallback: metadata
        var metadataFile = allFiles.FirstOrDefault(f => f.EndsWith("global-metadata.dat", StringComparison.OrdinalIgnoreCase));
        if (metadataFile != null)
        {
            var bytes = File.ReadAllBytes(metadataFile);
            var s = ExtractPrintableAscii(bytes);
            var m = Regex.Match(s, VersionPattern);
            if (m.Success) return m.Value;
        }

        return null;
    }

    private static string? FindVersionInFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Read only first 10MB to avoid large files impact
            var length = Math.Min(fs.Length, 10 * 1024 * 1024);
            var buffer = new byte[length];
            fs.Read(buffer, 0, (int)length);
            
            var s = ExtractPrintableAscii(buffer);
            var m = Regex.Match(s, VersionPattern);
            return m.Success ? m.Value : null;
        }
        catch { return null; }
    }

    public static string? DetectUnityVersionFromContainers(List<ZipArchive> zips)
    {
        // 1) globalgamemanagers
        foreach (var z in zips)
        {
            var v = FindVersionInEntry(z, "assets/bin/Data/globalgamemanagers");
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // 2) data.unity3d
        foreach (var z in zips)
        {
            var v = FindVersionInEntry(z, "assets/bin/Data/data.unity3d");
            if (!string.IsNullOrEmpty(v)) return v;
        }

        // 3) libunity.so (arm64, v7a)
        foreach (var entryName in new[] { "lib/arm64-v8a/libunity.so", "lib/armeabi-v7a/libunity.so" })
        {
            foreach (var z in zips)
            {
                var v = FindVersionInEntry(z, entryName);
                if (!string.IsNullOrEmpty(v))  return v;
            }
        }

        // 4) fallback: metadata
        var metadataBytes = ExtractMetadataFromContainers(zips);
        if (metadataBytes != null)
        {
            var s = Analyzer.ExtractPrintableAscii(metadataBytes);
            var m = Regex.Match(s, VersionPattern);
            if (m.Success)
                return m.Value;
        }

        return null;
    }

    private static string? FindVersionInEntry(ZipArchive zip, string entryName)
    {
        var bytes = ExtractEntryBytes(zip, entryName);
        if (bytes == null || bytes.Length == 0)
            return null;

        var s = ExtractPrintableAscii(bytes);
        var m = Regex.Match(s, VersionPattern);
        return m.Success ? m.Value : null;
    }

    public static bool DetectEntities(string scriptingAssembliesJson, string runtimeInitJson, UnityParsingData? parsingData)
    {
        // 1. Scene에서 SubScene 컴포넌트 사용 여부 확인 (가장 확실한 증거)
        if (parsingData != null && parsingData.SceneComponents.Contains("SubScene"))
        {
            return true;
        }

        // 기준 2: ScriptingAssemblies.json에 Entities 관련 어셈블리 있는지
        if (!string.IsNullOrEmpty(scriptingAssembliesJson))
        {
            var s = scriptingAssembliesJson;
            if (s.IndexOf("Unity.Entities", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Unity.Entities.Hybrid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        // 기준 3: RuntimeInitializeOnLoads.json에 Entities 관련 타입/어셈블리 초기화 항목이 있는지
        if (!string.IsNullOrEmpty(runtimeInitJson))
        {
            var s = runtimeInitJson;
            if (s.IndexOf("Unity.Entities", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Unity.Entities.Hybrid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        
        return false;
    }

    public static bool DetectNgui(UnityParsingData? parsingData)
    {
        if (parsingData != null)
        {
            foreach (var script in parsingData.AllMonoScripts)
            {
                // NGUI use UIOrthoCamera component 
                if (script.Contains("UIOrthoCamera", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool DetectAddressables(List<ZipArchive> zips)
    {
        foreach (var z in zips)
        {
            foreach (var entry in z.Entries)
            {
                var name = entry.FullName.Replace("\\", "/").ToLowerInvariant();
                if (name.Contains("aa/") ||
                    name.Contains("addressables") ||
                    Regex.IsMatch(name, @"catalog.*\.json", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(name, @"catalog.*\.hash", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool DetectAddressablesFromDirectory(string rootPath)
    {
        var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
        foreach (var file in allFiles)
        {
            var name = file.Replace("\\", "/").ToLowerInvariant();
            if (name.Contains("/aa/") ||
                name.Contains("addressables") ||
                Regex.IsMatch(Path.GetFileName(name), @"catalog.*\.json", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(Path.GetFileName(name), @"catalog.*\.hash", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
    
    public static string ExtractJsonTextFromContainers(List<ZipArchive> zips, string entryPath)
    {
        foreach (var zip in zips)
        {
            var bytes = ExtractEntryBytes(zip, entryPath);
            if (bytes == null || bytes.Length <= 0) continue;
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // fallback: try default encoding
                return Encoding.Default.GetString(bytes);
            }
        }

        return string.Empty;
    }
    
    public static List<(string Script, int Count)> GetMajorScriptInsights(UnityParsingData? parsingData)
    {
        var result = new List<(string, int)>();
        if (parsingData == null) return result;

        var counts = new Dictionary<string, int>();
        foreach (var script in parsingData.AllMonoScripts)
        {
            // UnityEngine, UnityEditor 등은 너무 광범위하므로 한 단계 더 깊게 본다
            var parts = script.Split('.');
            string key;
            if (parts.Length > 1)
            {
                if ((parts[0] == "UnityEngine" || parts[0] == "Unity" || parts[0] == "UnityEditor") && parts.Length > 2)
                {
                    key = parts[0] + "." + parts[1];
                }
                else
                {
                    key = parts[0];
                }
            }
            else
            {
                key = "(no namespace)";
            }
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(30))
        {
            result.Add((kv.Key, kv.Value));
        }

        return result;
    }

    private static byte[]? ExtractEntryBytes(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry? entry = null;
        foreach (var e in zip.Entries)
        {
            var name = e.FullName.Replace("\\", "/");
            if (string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase))
            {
                entry = e;
                break;
            }
        }

        if (entry == null)
            return null;

        using var ms = new MemoryStream();
        using (var s = entry.Open())
            s.CopyTo(ms);

        return ms.ToArray();
    }

    public static UnityParsingData AnalyzeDataUnity3D(List<ZipArchive> zips)
    {
        UnityAssetsReader.ClearCache();

        // Pass 1: Collect MonoScripts from all assets
        Console.WriteLine("[Analyzer] Pass 1: Collecting MonoScripts...");
        ProcessAllAssets(zips, true);

        // Pass 2: Analyze GameObjects
        Console.WriteLine("[Analyzer] Pass 2: Analyzing GameObjects...");
        ProcessAllAssets(zips, false);

        return UnityAssetsReader.ParsingData;
    }

    public static UnityParsingData AnalyzeDirectoryUnity3D(string rootPath)
    {
        UnityAssetsReader.ClearCache();

        // Pass 1: Collect MonoScripts
        Console.WriteLine("[Analyzer] Pass 1 (Dir): Collecting MonoScripts...");
        ProcessDirectoryAssets(rootPath, true);

        // Pass 2: Analyze GameObjects
        Console.WriteLine("[Analyzer] Pass 2 (Dir): Analyzing GameObjects...");
        ProcessDirectoryAssets(rootPath, false);

        return UnityAssetsReader.ParsingData;
    }

    private static void ProcessDirectoryAssets(string rootPath, bool scriptsOnly)
    {
        var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
        foreach (var file in allFiles)
        {
            var name = Path.GetFileName(file);
            
            // data.unity3d
            if (name == "data.unity3d")
            {
                if (!scriptsOnly) Console.WriteLine("[Analyzer] Found data.unity3d in folder.");
                using var fs = File.OpenRead(file);
                var reader = new UnityFsReader(fs);
                reader.Read(scriptsOnly);
                continue;
            }

            // Other assets
            if (name.EndsWith(".resS") || name.EndsWith(".resource") || name.EndsWith(".resourceBatch") || name.EndsWith(".bundle")) continue;

            bool isPotentialAsset = name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) || 
                                    name.EndsWith(".sharedassets", StringComparison.OrdinalIgnoreCase) || 
                                    name.Contains("globalgamemanagers", StringComparison.OrdinalIgnoreCase) || 
                                    name.Contains("level", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("unity_builtin_extra", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("unity default resources", StringComparison.OrdinalIgnoreCase);
            
            if (isPotentialAsset)
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    if (fs.Length > 20)
                    {
                        var reader = new UnityAssetsReader(fs);
                        reader.Read(name, scriptsOnly);
                    }
                }
                catch { }
            }
        }
    }

    private static void ProcessAllAssets(List<ZipArchive> zips, bool scriptsOnly)
    {
        foreach (var zip in zips)
        {
            // 1. Process data.unity3d
            var data = ExtractEntryBytes(zip, "assets/bin/Data/data.unity3d");
            if (data != null)
            {
                if (!scriptsOnly)
                    Console.WriteLine("[Analyzer] Found data.unity3d. Size: " + data.Length);
                using var ms = new MemoryStream(data);
                var reader = new UnityFsReader(ms);
                reader.Read(scriptsOnly);
            }

            // 2. Process other potential assets in APK
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace("\\", "/");
                if (!name.StartsWith("assets/bin/Data/")) continue;
                
                // data.unity3d already handled
                if (name == "assets/bin/Data/data.unity3d") continue;
                
                // Skip known non-asset extensions
                if (name.EndsWith(".resS") || name.EndsWith(".resource") || name.EndsWith(".resourceBatch") || name.EndsWith(".bundle")) continue;

                // Typical asset files
                bool isPotentialAsset = name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) || 
                                        name.EndsWith(".sharedassets", StringComparison.OrdinalIgnoreCase) || 
                                        name.Contains("globalgamemanagers", StringComparison.OrdinalIgnoreCase) || 
                                        name.Contains("level", StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains("unity_builtin_extra", StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains("unity default resources", StringComparison.OrdinalIgnoreCase);
                
                if (isPotentialAsset)
                {
                    var entryData = ExtractEntryBytes(zip, name);
                    if (entryData != null && entryData.Length > 20) // Minimum header size
                    {
                        if (!scriptsOnly)
                            Console.WriteLine($"[Analyzer] Found potential asset file in APK: {name}. Size: {entryData.Length}");
                        try
                        {
                            using var ms = new MemoryStream(entryData);
                            var reader = new UnityAssetsReader(ms);
                            reader.Read(Path.GetFileName(name), scriptsOnly);
                        }
                        catch (Exception)
                        {
                            // Some might not be valid assets files despite name
                        }
                    }
                }
            }
        }
    }

    public static bool DetectUiToolkit(List<ZipArchive> zips, UnityParsingData? parsingData)
    {
        // 1. Scene에서 UIDocument 컴포넌트 사용 여부 확인 (가장 확실한 증거)
        if (parsingData != null && parsingData.SceneComponents.Any(c => c.Contains("UIDocument")))
        {
            return true;
        }

        return false;
    }

    public static bool DetectUiToolkitFromDirectory(string rootPath, UnityParsingData? parsingData)
    {
        // Directory based analysis mostly relies on parsingData which we already populated
        if (parsingData != null && parsingData.SceneComponents.Any(c => c.Contains("UIDocument")))
        {
            return true;
        }
        
        // Also check for .uxml files in directory
        try
        {
            if (Directory.GetFiles(rootPath, "*.uxml", SearchOption.AllDirectories).Any())
                return true;
        }
        catch { }

        return false;
    }

    public static bool DetectEntitiesPhysics(string scriptingAssembliesJson)
    {
        if (!string.IsNullOrEmpty(scriptingAssembliesJson))
        {
            if (scriptingAssembliesJson.Contains("Unity.Physics", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}