using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;
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

        [MenuItem("Window/Analysis/Memory Profiler/Export/Graphics (Estimated) to CSV", false, 101)]
        static void ExportGraphics()
        {
             if (!ExportValidate())
                return;
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            if (window.m_SnapshotDataService.Base != null)
                TableExportUtility.ExportGraphicsToCsv(window.m_SnapshotDataService.Base);
        }

        [MenuItem("Window/Analysis/Memory Profiler/Export/Unity Objects to CSV", false, 102)]
        static void ExportUnityObjects()
        {
             if (!ExportValidate())
                return;
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            if (window.m_SnapshotDataService.Base != null)
                TableExportUtility.ExportUnityObjectsToCsv(window.m_SnapshotDataService.Base);
        }

        public static void ExportAllManagedObjectsToCsv(CachedSnapshot snapshot)
        {
            var path = EditorUtility.SaveFilePanel("Export All Managed Objects to CSV", MemoryProfilerSettings.LastImportPath, "AllManagedObjects", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();

            
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
        
        public static void ExportGraphicsToCsv(CachedSnapshot snapshot)
        {
            // Use the last import path for consistency with other exports
            var path = EditorUtility.SaveFilePanel("Export Graphics (Estimated) to CSV", MemoryProfilerSettings.LastImportPath, "Graphics", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            // Build the same data model used by All Of Memory -> Graphics (Estimated)
            var builder = new Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModelBuilder();
            var args = new Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModelBuilder.BuildArgs(
                searchFilter: null,
                nameFilter: null,
                pathFilter: null,
                excludeAll: false,
                breakdownNativeReserved: false,
                disambiguateUnityObjects: false,
                breakdownGfxResources: true,       // ensure we list individual graphics resources
                selectionProcessor: null,
                allocationRootNamesToSplitIntoSuballocations: null
            );

            var model = builder.Build(snapshot, args);

            // Find the Graphics (Estimated) root in the tree
            var graphicsRoot = model.RootNodes.FirstOrDefault(n => n.data.Name == Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModelBuilder.GraphicsGroupName);
            if (graphicsRoot.Equals(default(UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData>)))
            {
                // No graphics group present, export empty with header
                var empty = new StringBuilder();
                empty.AppendLine("NameOfObject,Type,Allocated(MB)");
                File.WriteAllText(path, empty.ToString(), Encoding.UTF8);
                EditorUtility.RevealInFinder(path);
                return;
            }

            // Collect all leaves under the Graphics group, using the immediate child of Graphics as the "Type" (e.g., Texture2D, Mesh, Reserved, etc.)
            var rows = new List<(string Name, string Type, long Bytes)>();

            void CollectLeaves(UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData> node, string currentType)
            {
                var children = node.children;
                if (children == null || !children.Any())
                {
                    // Leaf item
                    var name = node.data.Name ?? string.Empty;
                    var sizeBytes = (long)node.data.Size.Committed;
                    if (sizeBytes > 0)
                        rows.Add((name, currentType ?? string.Empty, sizeBytes));
                    return;
                }

                foreach (var child in children)
                {
                    // If we're directly under the Graphics root, treat this child as the "Type" group.
                    var nextType = currentType;
                    if (node.id == graphicsRoot.id)
                        nextType = child.data.Name ?? string.Empty;

                    CollectLeaves(child, nextType);
                }
            }

            CollectLeaves(graphicsRoot, currentType: null);

            // Order by allocated size descending
            var ordered = rows.OrderByDescending(r => r.Bytes);

            // Write CSV
            var sb = new StringBuilder();
            sb.AppendLine("NameOfObject,Type,Allocated(MB)");

            foreach (var row in ordered)
            {
                // CSV escape quotes
                var safeName = (row.Name ?? string.Empty).Replace("\"", "\"\"");
                var safeType = (row.Type ?? string.Empty).Replace("\"", "\"\"");
                sb.AppendLine($"\"{safeName}\",\"{safeType}\",{FormatBytes(row.Bytes)}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }

        public static void ExportUnityObjectsToCsv(CachedSnapshot snapshot)
        {
            // Use the last import path for consistency with other exports
            var path = EditorUtility.SaveFilePanel("Export Unity Objects to CSV", MemoryProfilerSettings.LastImportPath, "UnityObjects", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            // Build the same data that drives the Unity Objects table, then flatten it to actual objects.
            var builder = new Unity.MemoryProfiler.Editor.UI.UnityObjectsModelBuilder();
            var args = new Unity.MemoryProfiler.Editor.UI.UnityObjectsModelBuilder.BuildArgs(
                searchStringFilter: null,
                unityObjectNameFilter: null,
                unityObjectTypeNameFilter: null,
                unityObjectInstanceIDFilter: null,
                flattenHierarchy: true,               // export leaves (actual objects)
                potentialDuplicatesFilter: false,     // keep all, not just duplicates
                disambiguateByInstanceId: false,
                selectionProcessor: null
            );

            var model = builder.Build(snapshot, args);
            var leaves = model.RootNodes; // already flattened to leaves

            var sb = new StringBuilder();
            // Keep original requested header, but format Size as MB. Add Allocated/Resident for clarity.
            sb.AppendLine("NameOfObject,Type,Allocated(MB),Resident(MB)");

            // Order by total allocated (Committed) size, desc
            foreach (var node in leaves.OrderByDescending(n => n.data.TotalSize.Committed))
            {
                var data = node.data;

                // Expect leaves to reference a NativeObject. Skip anything else defensively.
                if (data.Source.Id != CachedSnapshot.SourceIndex.SourceId.NativeObject)
                    continue;

                var nativeIndex = data.Source.Index;
                // Resolve type and name like the UI does
                var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeIndex];
                var typeName = snapshot.NativeTypes.TypeName[typeIndex] ?? string.Empty;

                // Prefer item name from model (it can include disambiguation), fallback to snapshot name
                var name = string.IsNullOrEmpty(data.Name)
                    ? snapshot.NativeObjects.ObjectName[nativeIndex] ?? "unknown"
                    : data.Name;

                // CSV escape
                var safeName = name.Replace("\"", "\"\"");
                var safeType = typeName.Replace("\"", "\"\"");

                var allocatedMb = FormatBytes((long)data.TotalSize.Committed);
                var residentMb = FormatBytes((long)data.TotalSize.Resident);

                sb.AppendLine($"\"{safeName}\",\"{safeType}\",{allocatedMb},{residentMb}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }
    }
}
