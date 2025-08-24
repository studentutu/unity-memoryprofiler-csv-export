using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor;

namespace Unity.MemoryProfiler.Editor
{
    internal static class TableExportUtility
    {
        static float BytesToMegabytes(long bytes)
        {
            return bytes / (1024f * 1024f);
        }
        
        static string FormatBytes(long bytes)
        { 
            return $"{BytesToMegabytes(bytes):F3} MB";
        } 

        static bool ExportValidate()
        {
            // The menu items are only available in case a snapshot is loaded and selected
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            return window != null && window.m_SnapshotDataService != null && window.m_SnapshotDataService.Base != null;
        }

        [MenuItem("Window/Analysis/Memory Profiler/Export/All Managed Objects to CSV", false, 100)]
        static void ExportAllManagedObjects()
        {
            if (!ExportValidate())
                return;
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            if (window.m_SnapshotDataService.Base != null)
                TableExportUtility.ExportAllManagedObjectsToCsv(window.m_SnapshotDataService.Base);
        }

        // [MenuItem("Window/Analysis/Memory Profiler/Export/Graphics (Estimated) to CSV", false, 101)]
        // static void ExportGraphics()
        // {
        //      if (!ExportValidate())
        //         return;
        //     var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
        //     if (window.m_SnapshotDataService.Base != null)
        //         TableExportUtility.ExportGraphicsToCsv(window.m_SnapshotDataService.Base);
        // }

        // [MenuItem("Window/Analysis/Memory Profiler/Export/Unity Objects to CSV", false, 102)]
        // static void ExportUnityObjects()
        // {
        //      if (!ExportValidate())
        //         return;
        //     var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
        //     if (window.m_SnapshotDataService.Base != null)
        //         TableExportUtility.ExportUnityObjectsToCsv(window.m_SnapshotDataService.Base);
        // }

        public static void ExportAllManagedObjectsToCsv(CachedSnapshot snapshot)
        {
            var path = EditorUtility.SaveFilePanel("Export All Managed Objects to CSV", MemoryProfilerSettings.LastImportPath, "AllManagedObjects", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();

            // Use crawled managed objects from the current snapshot API
            var objects = snapshot.CrawledData.ManagedObjects;
            var typeNames = snapshot.TypeDescriptions.TypeDescriptionName;

            // 1) Aggregate totals per managed type
            var totals = new Dictionary<int, (long totalSize, int count)>();
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                // Skip invalid entries
                if (obj.PtrObject == 0 || obj.ITypeDescription < 0)
                    continue;

                var typeIdx = obj.ITypeDescription;
                var size = (long)obj.Size;

                if (totals.TryGetValue(typeIdx, out var t))
                    totals[typeIdx] = (t.totalSize + size, t.count + 1);
                else
                    totals[typeIdx] = (size, 1);
            }

            // Section A: Per-Type Totals
            sb.AppendLine("Managed Types Totals");
            sb.AppendLine("Type,Count,TotalSize(Mb)");

            foreach (var kv in totals.OrderByDescending(kv => kv.Value.totalSize))
            {
                var typeIdx = kv.Key;
                var (totalSize, count) = kv.Value;

                var typeName = (typeIdx >= 0 && typeIdx < typeNames.Length) ? (typeNames[typeIdx] ?? string.Empty) : string.Empty;
                var safeType = typeName.Replace("\"", "\"\"");

                // Print average as whole bytes to avoid decimals in CSV
                sb.AppendLine($"\"{safeType}\",{count},{FormatBytes(totalSize)}");
            }

            File.WriteAllText(path, sb.ToString());
            EditorUtility.RevealInFinder(path);
        }
        
        // public static void ExportGraphicsToCsv(CachedSnapshot snapshot)
        // {
        //     var path = EditorUtility.SaveFilePanel("Export Graphics (Estimated) to CSV", "", "Graphics", "csv");
        //     if (string.IsNullOrEmpty(path))
        //         return;

        //     var sb = new StringBuilder();
        //     sb.AppendLine("Id,Owner,TotalSize");

        //     for (int i = 0; i < snapshot.GfxResources.Count; ++i)
        //     {
        //         var id = snapshot.GfxResources.InstanceId[i];
        //         var owner = snapshot.GetOwningObjectForGfxResource(i);
        //         var size = snapshot.GfxResources.TotalSize[i];
        //         sb.AppendLine($"{id},{owner},{size}");
        //     }

        //     File.WriteAllText(path, sb.ToString());
        // }

        // public static void ExportUnityObjectsToCsv(CachedSnapshot snapshot)
        // {
        //     var path = EditorUtility.SaveFilePanel("Export Unity Objects to CSV", "", "UnityObjects", "csv");
        //     if (string.IsNullOrEmpty(path))
        //         return;

        //     var sb = new StringBuilder();
        //     sb.AppendLine("InstanceId,Name,Type,Size");

        //     for (int i = 0; i < snapshot.NativeObjects.Count; ++i)
        //     {
        //         var id = snapshot.NativeObjects.InstanceId[i];
        //         var name = snapshot.NativeObjects.Name[i];
        //         var type = snapshot.NativeTypes.Name[snapshot.NativeObjects.TypeIndex[i]];
        //         var size = snapshot.NativeObjects.Size[i];
        //         sb.AppendLine($"{id},{name},{type},{size}");
        //     }
        //     File.WriteAllText(path, sb.ToString());
        // }
    }
}
