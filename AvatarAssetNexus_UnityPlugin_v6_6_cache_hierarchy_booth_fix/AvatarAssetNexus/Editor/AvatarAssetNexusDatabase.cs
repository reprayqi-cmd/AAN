using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace AvatarAssetNexus
{
    [Serializable]
    public class AvatarPointerCache
    {
        public string avatarKey;
        public string avatarDisplayName;
        public string avatarAssetPath;
        public string avatarSourceKind;
        public long avatarFileSize;
        public long avatarModifiedUtcTicks;
        public string analyzedAt;
        public string unusedAnalyzedAt;
        public string autoCategoryId;
        public List<PrefabPointerEntry> prefabs = new List<PrefabPointerEntry>();
        public List<PrefabPointerEntry> unusedPrefabs = new List<PrefabPointerEntry>();
        public List<AvatarPartUsageEntry> partUsages = new List<AvatarPartUsageEntry>();
    }

    [Serializable]
    public class AvatarPartUsageEntry
    {
        public string objectPath;
        public string status;
        public int confidence;
        public string reason;
        public string matchedPrefabPath;
        public string meshPath;
        public List<string> materialPaths = new List<string>();
    }

    [Serializable]
    public class PrefabPointerEntry
    {
        public string prefabPath;
        public string prefabGuid;
        public string displayName;
        public long fileSize;
        public long modifiedUtcTicks;
        public List<string> dependencyPaths = new List<string>();
    }

    [Serializable]
    public class CustomCategoryRecord
    {
        public string id;
        public string name;
        public List<string> assetPaths = new List<string>();
    }

    [Serializable]
    public class AssetExtraMetadata
    {
        public string assetPath;
        public string boothUrl;
        public string boothId;
        public string boothTitle;
        public string boothAuthor;
        public string boothPrice;
        public string boothThumbnailUrl;
        public string notes;
        public string localCoverAssetPath;
        public List<string> tags = new List<string>();
    }

    public class AvatarAssetNexusDatabase : ScriptableObject
    {
        public const string DataFolder = "Assets/AvatarAssetNexusData";
        public const string DataAssetPath = DataFolder + "/AvatarAssetNexusDatabase.asset";

        public List<AvatarPointerCache> avatarCaches = new List<AvatarPointerCache>();
        public List<CustomCategoryRecord> customCategories = new List<CustomCategoryRecord>();
        public List<AssetExtraMetadata> assetMetadata = new List<AssetExtraMetadata>();
        public List<string> recentImportedAssetPaths = new List<string>();
        public string recentImportedAt;

        public static AvatarAssetNexusDatabase LoadOrCreate()
        {
            var db = AssetDatabase.LoadAssetAtPath<AvatarAssetNexusDatabase>(DataAssetPath);
            if (db != null)
            {
                db.EnsureDefaults();
                return db;
            }

            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                AssetDatabase.CreateFolder("Assets", "AvatarAssetNexusData");
            }

            db = CreateInstance<AvatarAssetNexusDatabase>();
            db.EnsureDefaults();
            AssetDatabase.CreateAsset(db, DataAssetPath);
            AssetDatabase.SaveAssets();
            return db;
        }

        public void EnsureDefaults()
        {
            if (avatarCaches == null) avatarCaches = new List<AvatarPointerCache>();
            if (customCategories == null) customCategories = new List<CustomCategoryRecord>();
            if (assetMetadata == null) assetMetadata = new List<AssetExtraMetadata>();
            if (recentImportedAssetPaths == null) recentImportedAssetPaths = new List<string>();
            foreach (var cache in avatarCaches)
            {
                if (cache.prefabs == null) cache.prefabs = new List<PrefabPointerEntry>();
                if (cache.unusedPrefabs == null) cache.unusedPrefabs = new List<PrefabPointerEntry>();
                if (cache.partUsages == null) cache.partUsages = new List<AvatarPartUsageEntry>();
            }
            foreach (var category in customCategories)
            {
                if (category.assetPaths == null) category.assetPaths = new List<string>();
            }
            foreach (var metadata in assetMetadata)
            {
                if (metadata.tags == null) metadata.tags = new List<string>();
            }

            if (customCategories.Count == 0)
            {
                customCategories.Add(new CustomCategoryRecord
                {
                    id = Guid.NewGuid().ToString("N"),
                    name = "Favorites",
                    assetPaths = new List<string>()
                });
            }
        }

        public AvatarPointerCache FindAvatarCache(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return null;
            return avatarCaches.FirstOrDefault(x => x.avatarKey == avatarKey);
        }

        public AvatarPointerCache GetOrCreateAvatarCache(string avatarKey)
        {
            var cache = FindAvatarCache(avatarKey);
            if (cache != null) return cache;

            cache = new AvatarPointerCache
            {
                avatarKey = avatarKey,
                prefabs = new List<PrefabPointerEntry>(),
                unusedPrefabs = new List<PrefabPointerEntry>()
            };
            avatarCaches.Add(cache);
            return cache;
        }

        public CustomCategoryRecord GetCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return customCategories.FirstOrDefault(x => x.id == id);
        }

        public CustomCategoryRecord CreateCategory(string baseName = "New Category")
        {
            var safeName = string.IsNullOrEmpty(baseName) ? "New Category" : baseName;
            var name = safeName;
            var index = 2;
            while (customCategories.Any(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = safeName + " " + index;
                index++;
            }

            var category = new CustomCategoryRecord
            {
                id = Guid.NewGuid().ToString("N"),
                name = name,
                assetPaths = new List<string>()
            };
            customCategories.Add(category);
            return category;
        }

        public AssetExtraMetadata GetMetadata(string assetPath, bool create)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var meta = assetMetadata.FirstOrDefault(x => x.assetPath == assetPath);
            if (meta != null || !create) return meta;

            meta = new AssetExtraMetadata
            {
                assetPath = assetPath,
                tags = new List<string>()
            };
            assetMetadata.Add(meta);
            return meta;
        }

        public void AddAssetToCategory(string categoryId, string assetPath)
        {
            if (string.IsNullOrEmpty(categoryId) || string.IsNullOrEmpty(assetPath)) return;
            var category = GetCategory(categoryId);
            if (category == null) return;
            if (category.assetPaths == null) category.assetPaths = new List<string>();
            if (!category.assetPaths.Contains(assetPath)) category.assetPaths.Add(assetPath);
        }

        public void AddAssetsToCategory(string categoryId, IEnumerable<string> assetPaths)
        {
            if (assetPaths == null) return;
            foreach (var path in assetPaths)
            {
                AddAssetToCategory(categoryId, path);
            }
        }

        public void RemoveAssetFromCategory(string categoryId, string assetPath)
        {
            var category = GetCategory(categoryId);
            if (category == null || category.assetPaths == null) return;
            category.assetPaths.Remove(assetPath);
        }

        public void ReplaceCategoryAssets(string categoryId, IEnumerable<string> assetPaths)
        {
            var category = GetCategory(categoryId);
            if (category == null) return;
            category.assetPaths = assetPaths == null
                ? new List<string>()
                : assetPaths.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        }

        public void MoveCategory(string sourceId, string targetId, bool placeAfter)
        {
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId) || sourceId == targetId) return;
            var sourceIndex = customCategories.FindIndex(x => x.id == sourceId);
            var targetIndex = customCategories.FindIndex(x => x.id == targetId);
            if (sourceIndex < 0 || targetIndex < 0) return;

            var category = customCategories[sourceIndex];
            customCategories.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex) targetIndex--;
            var insertIndex = placeAfter ? targetIndex + 1 : targetIndex;
            insertIndex = Mathf.Clamp(insertIndex, 0, customCategories.Count);
            customCategories.Insert(insertIndex, category);
        }

        public void SetRecentImportedAssets(IEnumerable<string> paths)
        {
            if (recentImportedAssetPaths == null) recentImportedAssetPaths = new List<string>();
            recentImportedAssetPaths = paths == null
                ? new List<string>()
                : paths.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            recentImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void ClearRecentImportedAssets()
        {
            if (recentImportedAssetPaths == null) recentImportedAssetPaths = new List<string>();
            recentImportedAssetPaths.Clear();
            recentImportedAt = string.Empty;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
