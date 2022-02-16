using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;

namespace Unity.QuickSearch
{
    readonly struct AssetIndexChangeSet
    {
        public readonly string[] updated;
        public readonly string[] removed;

        public AssetIndexChangeSet(IEnumerable<string> updated, IEnumerable<string> removed, IEnumerable<string> moved, Func<string, bool> predicate)
        {
            this.removed = removed.Where(predicate).ToArray();
            this.updated = updated.Concat(moved).Distinct().Where(predicate).ToArray();
        }

        public bool empty => updated?.Length == 0 && removed?.Length == 0;
        public IEnumerable<string> all => updated.Concat(removed).Distinct();
    }

    class AssetPostprocessorIndexer : AssetPostprocessor
    {
        private static bool s_Enabled;
        private static double s_BatchStartTime;

        private static HashSet<string> s_UpdatedItems = new HashSet<string>();
        private static HashSet<string> s_RemovedItems = new HashSet<string>();
        private static HashSet<string> s_MovedItems = new HashSet<string>();

        private static HashSet<string> s_AllUpdatedItems = new HashSet<string>();
        private static HashSet<string> s_AllRemovedItems = new HashSet<string>();
        private static HashSet<string> s_AllMovedItems = new HashSet<string>();

        private static object s_ContentRefreshedLock = new object();
        private static event Action<string[], string[], string[]> s_ContentRefreshed;
        public static event Action<string[], string[], string[]> contentRefreshed
        {
            add
            {
                lock (s_ContentRefreshedLock)
                {
                    Enable();
                    s_ContentRefreshed -= value;
                    s_ContentRefreshed += value;
                }
            }

            remove
            {
                lock (s_ContentRefreshedLock)
                {
                    s_ContentRefreshed -= value;
                    if (s_ContentRefreshed == null || s_ContentRefreshed.GetInvocationList().Length == 0)
                        Disable();
                }
            }
        }

        static AssetPostprocessorIndexer()
        {
            EditorApplication.quitting += OnQuitting;
        }

        public static void Enable()
        {
            s_Enabled = true;
        }

        public static void Disable()
        {
            s_Enabled = false;
        }

        public static AssetIndexChangeSet GetDiff(Func<string, bool> predicate)
        {
            return new AssetIndexChangeSet(s_AllUpdatedItems, s_AllRemovedItems, s_AllMovedItems, predicate);
        }

        private static void OnQuitting()
        {
            s_Enabled = false;
        }

        [UsedImplicitly]
        internal static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            RaiseContentRefreshed(imported, deleted.Concat(movedFrom).Distinct().ToArray(), movedTo);
        }

        private static void RaiseContentRefreshed(IEnumerable<string> updated, IEnumerable<string> removed, IEnumerable<string> moved)
        {
            s_AllUpdatedItems.UnionWith(updated);
            s_AllRemovedItems.UnionWith(removed);
            s_AllMovedItems.UnionWith(moved);

            if (!s_Enabled)
                return;

            s_UpdatedItems.UnionWith(updated);
            s_RemovedItems.UnionWith(removed);
            s_MovedItems.UnionWith(moved);

            if (s_UpdatedItems.Count > 0 || s_RemovedItems.Count > 0 || s_MovedItems.Count > 0)
                RaiseContentRefreshed();
        }

        private static void RaiseContentRefreshed()
        {
            EditorApplication.delayCall -= RaiseContentRefreshed;

            var currentTime = EditorApplication.timeSinceStartup;
            if (s_BatchStartTime != 0 && currentTime - s_BatchStartTime > 0.5)
            {
                if (s_UpdatedItems.Count > 0 || s_RemovedItems.Count > 0 || s_MovedItems.Count > 0)
                    s_ContentRefreshed?.Invoke(s_UpdatedItems.ToArray(), s_RemovedItems.ToArray(), s_MovedItems.ToArray());
                s_UpdatedItems.Clear();
                s_RemovedItems.Clear();
                s_MovedItems.Clear();
                s_BatchStartTime = 0;
            }
            else
            {
                if (s_BatchStartTime == 0)
                    s_BatchStartTime = currentTime;
                EditorApplication.delayCall += RaiseContentRefreshed;
            }
        }
    }
}
