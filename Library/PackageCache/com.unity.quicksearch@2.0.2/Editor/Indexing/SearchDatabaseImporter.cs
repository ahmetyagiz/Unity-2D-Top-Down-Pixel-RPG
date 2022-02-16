//#define DEBUG_INDEXING

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
using UnityEditor.Experimental.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace Unity.QuickSearch
{
    [ExcludeFromPreset, ScriptedImporter(version: SearchDatabase.version, importQueueOffset: int.MaxValue, ext: "index")]
    class SearchDatabaseImporter : ScriptedImporter
    {
        private SearchDatabase db { get; set; }

        /// This boolean state is used to delay the importation of indexes
        /// that depends on assets that get imported to late such as prefabs.
        private static bool s_DelayImport = true;

        static SearchDatabaseImporter()
        {
            EditorApplication.delayCall += () => s_DelayImport = false;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var filePath = ctx.assetPath;
            var jsonText = System.IO.File.ReadAllText(filePath);
            var settings = JsonUtility.FromJson<SearchDatabase.Settings>(jsonText);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

            hideFlags |= HideFlags.HideInInspector;

            #if DEBUG_INDEXING
            using (new DebugTimer($"Importing index {fileName}"))
            #endif
            {
                db = ScriptableObject.CreateInstance<SearchDatabase>();
                db.name = fileName;
                db.hideFlags = HideFlags.NotEditable;
                db.settings = settings;
                db.settings.root = Path.GetDirectoryName(filePath).Replace("\\", "/");
                if (String.IsNullOrEmpty(db.settings.name))
                    db.settings.name = fileName;
                db.index = db.CreateIndexer(settings);

                if (!Reimport(filePath))
                {
                    if (ShouldDelayImport())
                    {
                        db.Log("Delayed Import");
                        EditorApplication.delayCall += () => AssetDatabase.ImportAsset(filePath);
                    }
                    else
                    {
                        Build();
                    }
                }

                ctx.AddObjectToAsset(fileName, db);
                ctx.SetMainObject(db);
            }
        }

        private bool ShouldDelayImport()
        {
            if (db.settings.type == nameof(SearchDatabase.IndexType.asset))
                return false;
            return s_DelayImport;
        }

        private bool Reimport(string assetPath)
        {
            if (!SearchDatabase.incrementalIndexCache.TryGetValue(assetPath, out var cachedIndexBytes))
                return false;
            SearchDatabase.incrementalIndexCache.Remove(assetPath);
            db.bytes = cachedIndexBytes;
            return db.index.LoadBytes(cachedIndexBytes, (loaded) =>
                {
                    db.Log($"Reimport.{loaded}");
                    SearchDatabase.SendIndexLoaded(db);
                });
        }

        private void Build()
        {
            try
            {
                db.index.reportProgress += ReportProgress;
                db.index.Build();
                db.bytes = db.index.SaveBytes();
                db.Log("Build");
            }
            finally
            {
                db.index.reportProgress -= ReportProgress;
            }
        }

        private void ReportProgress(int progressId, string description, float progress, bool finished)
        {
            EditorUtility.DisplayProgressBar($"Building {db.name} index...", description, progress);
            if (finished)
                EditorUtility.ClearProgressBar();
        }

        public static void CreateTemplateIndex(string templateFilename, string path)
        {
            var dirPath = path;
            var templatePath = $"{Utils.packageFolderName}/Templates/{templateFilename}.index.template";

            if (!File.Exists(templatePath))
                return;

            var templateContent = File.ReadAllText(templatePath);

            if (File.Exists(path))
            {
                dirPath = Path.GetDirectoryName(path);
                if (Selection.assetGUIDs.Length > 1)
                    path = dirPath;
                var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Select(p => $"\"{p}\"");
                templateContent = templateContent.Replace("\"roots\": []", $"\"roots\": [\r\n    {String.Join(",\r\n    ", paths)}\r\n  ]");
            }

            var indexPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dirPath, $"{Path.GetFileNameWithoutExtension(path)}.index")).Replace("\\", "/");
            File.WriteAllText(indexPath, templateContent);
            AssetDatabase.ImportAsset(indexPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"Generated {templateFilename} index at {indexPath}");
        }

        private static bool ValidateTemplateIndexCreation<T>() where T : UnityEngine.Object
        {
            var asset = Selection.activeObject as T;
            if (asset)
                return true;
            return CreateIndexProjectValidation();
        }

        [MenuItem("Assets/Create/Quick Search/Project Index")]
        internal static void CreateIndexProject()
        {
            var folderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Assets", folderPath);
        }

        [MenuItem("Assets/Create/Quick Search/Project Index", validate = true)]
        internal static bool CreateIndexProjectValidation()
        {
            var folder = Selection.activeObject as DefaultAsset;
            if (!folder)
                return false;
            return Directory.Exists(AssetDatabase.GetAssetPath(folder));
        }

        [MenuItem("Assets/Create/Quick Search/Prefab Index")]
        internal static void CreateIndexPrefab()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Prefabs", assetPath);
        }

        [MenuItem("Assets/Create/Quick Search/Prefab Index", validate = true)]
        internal static bool CreateIndexPrefabValidation()
        {
            return ValidateTemplateIndexCreation<GameObject>();
        }

        [MenuItem("Assets/Create/Quick Search/Scene Index")]
        internal static void CreateIndexScene()
        {
            var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            CreateTemplateIndex("Scenes", assetPath);
        }

        [MenuItem("Assets/Create/Quick Search/Scene Index", validate = true)]
        internal static bool CreateIndexSceneValidation()
        {
            return ValidateTemplateIndexCreation<SceneAsset>();
        }
    }
}
