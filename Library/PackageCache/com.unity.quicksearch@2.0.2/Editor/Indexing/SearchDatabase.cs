//#define DEBUG_INDEXING
#if UNITY_2020_1_OR_NEWER
#define ENABLE_ASYNC_INCREMENTAL_UPDATES
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    class SearchDatabase : ScriptableObject
    {
        // 1- First version
        // 2- Rename ADBIndex for SearchDatabase
        // 3- Add db name and type
        // 4- Add better ref: property indexing
        // 5- Fix asset has= property indexing.
        public const int version = (5 << 8) ^ SearchIndexEntry.version;

        public enum IndexType
        {
            asset,
            scene,
            prefab
        }

        [Serializable]
        public class Options
        {
            public bool disabled = false;           // Disables the index

            public bool files = true;               // Index file paths
            public bool directories = false;        // Index folder paths

            public bool types = true;               // Index type information about objects
            public bool properties = false;         // Index serialized properties of objects
            public bool dependencies = false;       // Index object dependencies (i.e. ref:<name>)
        }

        [Serializable]
        public class Settings
        {
            [NonSerialized] public string root;

            public string name;
            public string type = nameof(IndexType.asset);
            public string[] roots;
            public string[] includes;
            public string[] excludes;
            public int baseScore = 100;
            public Options options;
        }

        [SerializeField] public new string name;
        [SerializeField] public Settings settings;
        [SerializeField, HideInInspector] public byte[] bytes;

        public ObjectIndexer index { get; internal set; }

        internal static Dictionary<string, Type> indexerFactory = new Dictionary<string, Type>();
        internal static Dictionary<string, byte[]> incrementalIndexCache = new Dictionary<string, byte[]>();

        internal static event Action<SearchDatabase> indexLoaded;

        static SearchDatabase()
        {
            indexerFactory[nameof(IndexType.asset)] = typeof(AssetIndexer);
            indexerFactory[nameof(IndexType.scene)] = typeof(SceneIndexer);
            indexerFactory[nameof(IndexType.prefab)] = typeof(SceneIndexer);
        }

        [System.Diagnostics.Conditional("DEBUG_INDEXING")]
        internal void Log(string callName, params string[] args)
        {
            Debug.Log($"({GetInstanceID()}) SearchDatabase[<b>{name}</b>].<b>{callName}</b>[{string.Join(",", args)}]({bytes?.Length}, {index?.documentCount})");
        }

        internal ObjectIndexer CreateIndexer(Settings settings)
        {
            if (settings == null)
                return null;

            if (this && settings.root == null)
                settings.root = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(this)).Replace("\\", "/");

            if (!indexerFactory.TryGetValue(settings.type, out var indexerType))
                throw new ArgumentException($"{settings.type} indexer does not exist", nameof(settings.type));
            return (ObjectIndexer)Activator.CreateInstance(indexerType, new object[] {settings});
        }

        internal void OnEnable()
        {
            Log("OnEnable");

            index = CreateIndexer(settings);
            if (bytes == null)
                bytes = new byte[0];
            else
            {
                if (bytes.Length > 0)
                    Load();
            }
        }

        internal void OnDisable()
        {
            Log("OnDisable");
            AssetPostprocessorIndexer.contentRefreshed -= OnContentRefreshed;
        }

        private void Load()
        {
            Log("Load");
            index.LoadBytes(bytes, (success) => Setup());
        }

        private void Setup()
        {
            Log("Setup");
            AssetPostprocessorIndexer.contentRefreshed -= OnContentRefreshed;
            AssetPostprocessorIndexer.contentRefreshed += OnContentRefreshed;
            SendIndexLoaded(this);
        }

        private void OnContentRefreshed(string[] updated, string[] removed, string[] moved)
        {
            if (!this || settings.options.disabled)
                return;
            var changeset = new AssetIndexChangeSet(updated, removed, moved, p => HasDocumentChanged(p));
            if (!changeset.empty)
            {
                Log("OnContentRefreshed", changeset.all.ToArray());

                var it = IncrementalUpdate(-1, changeset);
                while (it.MoveNext())
                    ;
            }
        }

        private bool HasDocumentChanged(string path)
        {
            if (index.SkipEntry(path, true))
                return false;

            if (!index.TryGetHash(path, out var hash))
                return true;

            if (hash != index.GetDocumentHash(path))
                return true;

            return false;
        }

        internal void IncrementalUpdate()
        {
            var changeset = AssetPostprocessorIndexer.GetDiff(p => HasDocumentChanged(p));
            if (!changeset.empty)
            {
                Log($"IncrementalUpdate", changeset.all.ToArray());
                IncrementalUpdate(changeset);
            }
        }

        internal void IncrementalUpdate(AssetIndexChangeSet changeset)
        {
            var it = IncrementalUpdate(-1, changeset);
            while (it.MoveNext())
                ;
        }

        private IEnumerator IncrementalUpdate(int progressId, object userData)
        {
            var set = (AssetIndexChangeSet)userData;
            #if ENABLE_ASYNC_INCREMENTAL_UPDATES
            var pathIndex = 0;
            var pathCount = (float)set.updated.Length;
            #endif
            index.Start();
            foreach (var path in set.updated)
            {
                #if ENABLE_ASYNC_INCREMENTAL_UPDATES
                if (progressId != -1)
                {
                    var progressReport = pathIndex++ / pathCount;
                    Progress.Report(progressId, progressReport, path);
                }
                #endif
                index.IndexDocument(path, true);
                yield return null;
            }

            index.Finish((bytes) =>
            {
                if (!this)
                    return;

                var sourceAssetPath = AssetDatabase.GetAssetPath(this);
                if (!String.IsNullOrEmpty(sourceAssetPath))
                {
                    // Kick in an incremental import.
                    incrementalIndexCache[sourceAssetPath] = bytes;
                    AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.DontDownloadFromCacheServer);
                }
            }, set.removed, saveBytes: true);
        }

        internal static void SendIndexLoaded(SearchDatabase sb)
        {
            indexLoaded?.Invoke(sb);
        }

        public static IEnumerable<SearchDatabase> Enumerate(params string[] types)
        {
            const string k_SearchDataFindAssetQuery = "t:SearchDatabase a:all";
            return AssetDatabase.FindAssets(k_SearchDataFindAssetQuery).Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<SearchDatabase>(path))
                .Where(db => db != null && !db.settings.options.disabled && (types.Length == 0 || types.Contains(db.settings.type)))
                .Select(db => { db.Log("Enumerate"); return db; });
        }
    }
}
