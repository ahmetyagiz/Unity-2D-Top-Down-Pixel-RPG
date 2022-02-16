//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch
{
    class AssetIndexer : ObjectIndexer
    {
        public AssetIndexer(SearchDatabase.Settings settings)
            : base(String.IsNullOrEmpty(settings.name) ? "assets" : settings.name, settings)
        {
        }

        protected override System.Collections.IEnumerator BuildAsync(int progressId, object userData = null)
        {
            var paths = GetDependencies();
            var pathIndex = 0;
            var pathCount = (float)paths.Count;

            Start(clear: true);

            EditorApplication.LockReloadAssemblies();
            lock (this)
            {
                foreach (var path in paths)
                {
                    var progressReport = pathIndex++ / pathCount;
                    ReportProgress(progressId, path, progressReport, false);
                    IndexDocument(path, false);
                    yield return null;
                }
            }
            EditorApplication.UnlockReloadAssemblies();

            Finish();
            while (!IsReady())
                yield return null;

            ReportProgress(progressId, $"Indexing Completed (Documents: {documentCount}, Indexes: {indexCount:n0})", 1f, true);
            yield return null;
        }

        public override IEnumerable<string> GetRoots()
        {
            if (settings.roots == null)
                settings.roots = new string[0];
            var roots = settings.roots.Where(r => Directory.Exists(r)).ToArray();
            if (roots.Length == 0)
                roots = new string[] { settings.root };
            return roots.Select(r => r.Replace("\\", "/"));
        }

        public override List<string> GetDependencies()
        {
            return GetRoots().SelectMany(root =>
            {
                return Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                    .Where(path => !SkipEntry(path))
                    .Where(path => !String.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    .Select(path => path.Replace("\\", "/"));
            }).ToList();
        }

        public override Hash128 GetDocumentHash(string path)
        {
            return AssetDatabase.GetAssetDependencyHash(path);
        }

        public override void IndexDocument(string path, bool checkIfDocumentExists)
        {
            var documentIndex = AddDocument(path, checkIfDocumentExists);
            AddDocumentHash(path, GetDocumentHash(path));
            if (documentIndex < 0)
                return;

            IndexWordComponents(documentIndex, path);

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                IndexWord(documentIndex, fileName, fileName.Length, true);
                IndexProperty(documentIndex, "name", fileName, saveKeyword: false);

                IndexWord(documentIndex, path, path.Length, exact: true);
                IndexProperty(documentIndex, "id", path, saveKeyword: false, exact: true);

                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                    IndexProperty(documentIndex, "a", "packages", saveKeyword: true, exact: true);
                else
                    IndexProperty(documentIndex, "a", "assets", saveKeyword: true, exact: true);

                if (!String.IsNullOrEmpty(name))
                    IndexProperty(documentIndex, "a", name, saveKeyword: true, exact: true);

                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    IndexNumber(documentIndex, "size", (double)fi.Length);
                    IndexProperty(documentIndex, "ext", fi.Extension.Replace(".", "").ToLowerInvariant(), saveKeyword: false);
                    IndexNumber(documentIndex, "age", (DateTime.Now - fi.LastWriteTime).TotalDays);
                    IndexProperty(documentIndex, "dir", fi.Directory.Name.ToLowerInvariant(), saveKeyword: false);
                    IndexProperty(documentIndex, "t", "file", saveKeyword: false, exact: true);
                }
                else if (Directory.Exists(path))
                {
                    IndexProperty(documentIndex, "t", "folder", saveKeyword: false, exact: true);
                }

                var at = AssetDatabase.GetMainAssetTypeAtPath(path);
                var hasCustomIndexers = HasCustomIndexers(at);

                if (settings.options.types && at != null)
                {
                    IndexWord(documentIndex, at.Name);
                    while (at != null && at != typeof(Object) && at != typeof(GameObject))
                    {
                        IndexProperty(documentIndex, "t", at.Name, saveKeyword: true);
                        at = at.BaseType;
                    }
                }
                else if (at != null)
                {
                    IndexProperty(documentIndex, "t", at.Name, saveKeyword: true);
                }

                if (settings.options.properties || hasCustomIndexers)
                {
                    bool wasLoaded = AssetDatabase.IsMainAssetAtPathLoaded(path);
                    var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (!mainAsset)
                        return;

                    #if !UNITY_2020_2_OR_NEWER
                    var labels = AssetDatabase.GetLabels(mainAsset);
                    #else
                    var guid = AssetDatabase.GUIDFromAssetPath(path);
                    var labels = AssetDatabase.GetLabels(guid);
                    #endif
                    foreach (var label in labels)
                        IndexProperty(documentIndex, "l", label, saveKeyword: true);

                    if (hasCustomIndexers)
                        IndexCustomProperties(path, documentIndex, mainAsset);

                    if (settings.options.properties)
                    {
                        if (!String.IsNullOrEmpty(mainAsset.name))
                            IndexWord(documentIndex, mainAsset.name, true);

                        var prefabType = PrefabUtility.GetPrefabAssetType(mainAsset);
                        if (prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant)
                            IndexProperty(documentIndex, "t", "prefab", saveKeyword: true);

                        if (settings.options.properties)
                            IndexObject(documentIndex, mainAsset);

                        if (mainAsset is GameObject go)
                        {
                            foreach (var v in go.GetComponents(typeof(Component)))
                            {
                                if (!v || v.GetType() == typeof(Transform))
                                    continue;
                                IndexPropertyComponents(documentIndex, "t", v.GetType().Name);
                                IndexPropertyComponents(documentIndex, "has", v.GetType().Name);

                                if (settings.options.properties)
                                    IndexObject(documentIndex, v, dependencies: settings.options.dependencies);
                            }
                        }
                    }

                    if (!wasLoaded)
                    {
                        if (mainAsset && !mainAsset.hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset) &&
                            !(mainAsset is GameObject) &&
                            !(mainAsset is Component) &&
                            !(mainAsset is AssetBundle))
                        {
                            Resources.UnloadAsset(mainAsset);
                        }
                    }
                }

                if (settings.options.dependencies)
                {
                    foreach (var depPath in AssetDatabase.GetDependencies(path, true))
                    {
                        if (path == depPath)
                            continue;
                        var depName = Path.GetFileNameWithoutExtension(depPath);
                        IndexProperty(documentIndex, "ref", depName, saveKeyword: false);
                        IndexProperty(documentIndex, "ref", depPath, saveKeyword: false, exact: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
