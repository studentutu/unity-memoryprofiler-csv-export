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

        [MenuItem("Window/Analysis/Memory Profiler Export/Managed Objects to CSV", false, 100)]
        static void ExportAllManagedObjects()
        {
            if (!ExportValidate())
                return;
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            if (window.m_SnapshotDataService.Base != null)
                TableExportUtility.ExportAllManagedObjectsToCsv(window.m_SnapshotDataService.Base);
        }

        [MenuItem("Window/Analysis/Memory Profiler Export/Graphics (Estimated) to CSV", false, 101)]
        static void ExportGraphics()
        {
             if (!ExportValidate())
                return;
            var window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            if (window.m_SnapshotDataService.Base != null)
                TableExportUtility.ExportGraphicsToCsv(window.m_SnapshotDataService.Base);
        }

        [MenuItem("Window/Analysis/Memory Profiler Export/Unity Objects to CSV", false, 102)]
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
            // Ask for a destination path
            var path = EditorUtility.SaveFilePanel("Export All Managed Objects to CSV", MemoryProfilerSettings.LastImportPath, "AllManagedObjects", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            // Build the same data model used by All Of Memory -> Managed -> Managed Objects
            var builder = new Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModelBuilder();
            var args = new Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModelBuilder.BuildArgs(
                searchFilter: null,
                nameFilter: null,
                pathFilter: null,
                excludeAll: false,
                breakdownNativeReserved: false,
                disambiguateUnityObjects: false,   // match default table view
                breakdownGfxResources: true,
                selectionProcessor: null,
                allocationRootNamesToSplitIntoSuballocations: null
            );

            var model = builder.Build(snapshot, args);

            // Find "Managed" root
            var managedRoot = model.RootNodes.FirstOrDefault(n => string.Equals(n.data.Name, "Managed", System.StringComparison.Ordinal));
            if (managedRoot.Equals(default(UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData>)))
            {
                // No managed data, export empty with header
                var empty = new StringBuilder();
                empty.AppendLine("Type,Count,Allocated(MB),Resident(MB)");
                File.WriteAllText(path, empty.ToString(), Encoding.UTF8);
                EditorUtility.RevealInFinder(path);
                return;
            }

            // Find "Managed Objects" node under Managed
            var managedObjectsNode = managedRoot.children?.FirstOrDefault(c => string.Equals(c.data.Name, "Managed Objects", System.StringComparison.Ordinal))
                                     ?? default;

            var sb = new StringBuilder();
            sb.AppendLine("Type,Count,Allocated(MB),Resident(MB)");

            if (!managedObjectsNode.Equals(default(UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData>)))
            {
                // Aggregate per managed type: Count, total Committed (Allocated) and total Resident
                var aggregates = new Dictionary<string, (long committed, long resident, int count)>(System.StringComparer.Ordinal);

                foreach (var typeNode in managedObjectsNode.children ?? Enumerable.Empty<UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData>>())
                {
                    var typeName = typeNode.data.Name ?? string.Empty;

                    long typeCommitted = 0;
                    long typeResident = 0;
                    int typeCount = 0;

                    // Leaves should be individual managed objects
                    foreach (var objNode in typeNode.children ?? Enumerable.Empty<UnityEngine.UIElements.TreeViewItemData<Unity.MemoryProfiler.Editor.UI.AllTrackedMemoryModel.ItemData>>())
                    {
                        var committed = (long)objNode.data.Size.Committed;
                        var resident = (long)objNode.data.Size.Resident;
                        typeCommitted += committed;
                        typeResident += resident;
                        typeCount++;
                    }

                    if (typeCount == 0 && (typeNode.data.Size.Committed > 0 || typeNode.data.Size.Resident > 0))
                    {
                        // Fallback in case the model provides size only at the type node level
                        typeCommitted += (long)typeNode.data.Size.Committed;
                        typeResident += (long)typeNode.data.Size.Resident;
                    }

                    if (aggregates.TryGetValue(typeName, out var acc))
                        aggregates[typeName] = (acc.committed + typeCommitted, acc.resident + typeResident, acc.count + typeCount);
                    else
                        aggregates[typeName] = (typeCommitted, typeResident, typeCount);
                }

                foreach (var kvp in aggregates.OrderByDescending(k => k.Value.committed))
                {
                    var safeType = (kvp.Key ?? string.Empty).Replace("\"", "\"\"");
                    sb.AppendLine($"\"{safeType}\",{kvp.Value.count},{FormatBytes(kvp.Value.committed)},{FormatBytes(kvp.Value.resident)}");
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
