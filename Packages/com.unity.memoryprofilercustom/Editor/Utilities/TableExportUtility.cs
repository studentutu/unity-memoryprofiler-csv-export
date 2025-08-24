using UnityEditor;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor;

namespace Unity.MemoryProfiler.Editor
{
    internal static class TableExportUtility
    {

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
            sb.AppendLine("Type,Size, RefCount");

            // Use crawled managed objects from the current snapshot API
            var objects = snapshot.CrawledData.ManagedObjects;
            var typeNames = snapshot.TypeDescriptions.TypeDescriptionName;

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                // Skip invalid entries
                if (obj.PtrObject == 0 || obj.ITypeDescription < 0)
                    continue;

                var typeName = typeNames[obj.ITypeDescription] ?? string.Empty;
                // CSV-safe quoting
                var safeType = typeName.Replace("\"", "\"\"");
                var size = obj.Size;
                var numberOfReferences = obj.RefCount;
                
                // TODO: Add total allocated size per type (requires aggregation)

                sb.AppendLine($"\"{safeType}\",{size},{numberOfReferences}");
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
