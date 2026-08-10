using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SkillTree.Authoring.Editor
{
    internal sealed class SkillTreeMetadataSyncReport
    {
        public SkillNodeMetadataProviderAsset Provider { get; set; }
        public SkillNodeMetadataCatalog Catalog { get; set; }
        public int AddedCount { get; set; }
        public int MatchedCount { get; set; }
        public int StaleCount { get; set; }
        public bool CreatedProviderAsset { get; set; }
        public bool CreatedCatalogAsset { get; set; }

        public string Summary => $"Added {AddedCount} / Matched {MatchedCount} / Stale {StaleCount}";
    }

    internal static class SkillTreeMetadataAssetSyncService
    {
        private const string RootFolder = "Assets/Game/SkillTreeData";

        public static SkillTreeMetadataSyncReport CreateOrAttachAssets(SkillTreeGraphData graph, string legacyJsonPath = null)
        {
            graph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            var legacySeeds = LoadLegacySeeds(legacyJsonPath);
            var safeTreeId = SanitizeAssetName(graph.treeId);
            var treeFolder = EnsureFolder($"{RootFolder}/{safeTreeId}");
            var catalogPath = $"{treeFolder}/{safeTreeId}_SkillNodeMetadataCatalog.asset";
            var providerPath = $"{treeFolder}/{safeTreeId}_SkillNodeMetadataProvider.asset";

            var catalog = AssetDatabase.LoadAssetAtPath<SkillNodeMetadataCatalog>(catalogPath);
            var createdCatalogAsset = false;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SkillNodeMetadataCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
                createdCatalogAsset = true;
            }

            var provider = AssetDatabase.LoadAssetAtPath<ScriptableObjectSkillNodeMetadataProvider>(providerPath);
            var createdProviderAsset = false;
            if (provider == null)
            {
                provider = ScriptableObject.CreateInstance<ScriptableObjectSkillNodeMetadataProvider>();
                provider.BindCatalog(catalog);
                AssetDatabase.CreateAsset(provider, providerPath);
                createdProviderAsset = true;
            }

            return SyncCatalog(graph, provider, catalog, legacySeeds, createdProviderAsset, createdCatalogAsset);
        }

        public static SkillTreeMetadataSyncReport SyncExistingProvider(
            SkillTreeGraphData graph,
            SkillNodeMetadataProviderAsset provider,
            string legacyJsonPath = null)
        {
            graph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            if (provider is not ScriptableObjectSkillNodeMetadataProvider scriptableProvider ||
                scriptableProvider.Catalog == null)
            {
                return null;
            }

            return SyncCatalog(graph, scriptableProvider, scriptableProvider.Catalog, LoadLegacySeeds(legacyJsonPath), false, false);
        }

        public static ScriptableObjectSkillNodeMetadataProvider FindExistingProvider(SkillTreeGraphData graph)
        {
            if (graph == null)
            {
                return null;
            }

            var safeTreeId = SanitizeAssetName(graph.treeId);
            var providerPath = $"{RootFolder}/{safeTreeId}/{safeTreeId}_SkillNodeMetadataProvider.asset";
            return AssetDatabase.LoadAssetAtPath<ScriptableObjectSkillNodeMetadataProvider>(providerPath);
        }

        private static SkillTreeMetadataSyncReport SyncCatalog(
            SkillTreeGraphData graph,
            ScriptableObjectSkillNodeMetadataProvider provider,
            SkillNodeMetadataCatalog catalog,
            IReadOnlyDictionary<string, LegacyNodeSeed> legacySeeds,
            bool createdProviderAsset,
            bool createdCatalogAsset)
        {
            var providerUpdated = false;
            var catalogUpdated = false;
            var graphNodeIds = graph.nodes
                .Where(node => !string.IsNullOrWhiteSpace(node.id))
                .Select(node => node.id.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .ToList();

            var addedCount = 0;
            foreach (var nodeId in graphNodeIds)
            {
                var isNewEntry = false;
                if (!catalog.TryGetEntry(nodeId, out var entry))
                {
                    entry = catalog.AddEntry(nodeId);
                    addedCount += 1;
                    catalogUpdated = true;
                    isNewEntry = true;
                }

                if (!legacySeeds.TryGetValue(nodeId, out var seed))
                {
                    continue;
                }

                if ((isNewEntry || string.IsNullOrWhiteSpace(entry.displayName)) && !string.IsNullOrWhiteSpace(seed.DisplayName))
                {
                    entry.displayName = seed.DisplayName;
                    catalogUpdated = true;
                }

                // Legacy editor iconKey는 Texture2D이므로 Sprite 계약으로 자동 이전하지 않는다.
            }

            if (catalogUpdated)
            {
                catalog.SortEntries();
                EditorUtility.SetDirty(catalog);
            }

            if (provider.Catalog != catalog)
            {
                provider.BindCatalog(catalog);
                EditorUtility.SetDirty(provider);
                providerUpdated = true;
            }

            if (catalogUpdated || createdProviderAsset || createdCatalogAsset || providerUpdated)
            {
                AssetDatabase.SaveAssets();
            }

            var knownNodeIds = new HashSet<string>(catalog.GetKnownNodeIds(), StringComparer.Ordinal);
            var staleCount = knownNodeIds.Count(nodeId => !graphNodeIds.Contains(nodeId));

            return new SkillTreeMetadataSyncReport
            {
                Provider = provider,
                Catalog = catalog,
                AddedCount = addedCount,
                MatchedCount = graphNodeIds.Count,
                StaleCount = staleCount,
                CreatedProviderAsset = createdProviderAsset,
                CreatedCatalogAsset = createdCatalogAsset
            };
        }

        private static string EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return folderPath;
            }

            var parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(parent) || parent == "." || parent == "/")
            {
                return folderPath;
            }

            EnsureFolder(parent);

            var folderName = Path.GetFileName(folderPath);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }

            return folderPath;
        }

        private static string SanitizeAssetName(string value)
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? "skill_tree" : value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitizedChars = candidate
                .Select(character => invalidChars.Contains(character) || character == '/'
                    ? '_'
                    : character)
                .ToArray();
            var sanitized = new string(sanitizedChars).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "skill_tree" : sanitized;
        }

        private static IReadOnlyDictionary<string, LegacyNodeSeed> LoadLegacySeeds(string legacyJsonPath)
        {
            if (string.IsNullOrWhiteSpace(legacyJsonPath) || !File.Exists(legacyJsonPath))
            {
                return new Dictionary<string, LegacyNodeSeed>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(legacyJsonPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, LegacyNodeSeed>(StringComparer.Ordinal);
            }

            var legacyGraph = JsonUtility.FromJson<LegacySkillTreeGraphData>(json);
            var seeds = new Dictionary<string, LegacyNodeSeed>(StringComparer.Ordinal);
            if (legacyGraph?.nodes == null)
            {
                return seeds;
            }

            foreach (var legacyNode in legacyGraph.nodes)
            {
                if (legacyNode == null || string.IsNullOrWhiteSpace(legacyNode.id))
                {
                    continue;
                }

                seeds[legacyNode.id.Trim()] = new LegacyNodeSeed(
                    string.IsNullOrWhiteSpace(legacyNode.displayName) ? null : legacyNode.displayName.Trim(),
                    string.IsNullOrWhiteSpace(legacyNode.iconKey) ? null : legacyNode.iconKey.Trim());
            }

            return seeds;
        }

        [Serializable]
        private sealed class LegacySkillTreeGraphData
        {
            public List<LegacySkillTreeNodeRecord> nodes;
        }

        [Serializable]
        private sealed class LegacySkillTreeNodeRecord
        {
            public string id;
            public string displayName;
            public string iconKey;
        }

        private struct LegacyNodeSeed
        {
            public readonly string DisplayName;
            public readonly string IconKey;

            public LegacyNodeSeed(string displayName, string iconKey)
            {
                DisplayName = displayName;
                IconKey = iconKey;
            }
        }
    }
}
