using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeMetadataSyncTests
    {
        private string _treeId;
        private string _assetFolderPath;
        private string _legacyJsonPath;

        [SetUp]
        public void SetUp()
        {
            _treeId = $"sync_{Guid.NewGuid():N}";
            _assetFolderPath = $"Assets/Game/SkillTreeData/{_treeId}";
            _legacyJsonPath = Path.Combine(Path.GetTempPath(), $"{_treeId}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_assetFolderPath))
            {
                AssetDatabase.DeleteAsset(_assetFolderPath);
            }

            if (File.Exists(_legacyJsonPath))
            {
                File.Delete(_legacyJsonPath);
            }
        }

        [Test]
        public void CreateOrAttachAssetsCreatesEntriesForEveryNode()
        {
            var graph = CreateGraph(_treeId, "root", "child");

            var report = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);

            Assert.That(report.Provider, Is.Not.Null);
            Assert.That(report.Catalog, Is.Not.Null);
            Assert.That(report.AddedCount, Is.EqualTo(2));
            Assert.That(report.MatchedCount, Is.EqualTo(2));
            Assert.That(report.Catalog.Entries.Select(entry => entry.nodeId), Is.EquivalentTo(new[] { "child", "root" }));
            Assert.That(report.Catalog.Entries.All(entry => entry.displayName == entry.nodeId), Is.True);
        }

        [Test]
        public void SyncExistingProviderPreservesManualMetadataValues()
        {
            var graph = CreateGraph(_treeId, "root", "child");
            var createdReport = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var rootEntry = createdReport.Catalog.Entries.Single(entry => entry.nodeId == "root");
            rootEntry.displayName = "Manual Root";
            rootEntry.description = "manual description";
            rootEntry.effectSummary = "manual effect";
            rootEntry.cost = 7;
            rootEntry.maxLevel = 4;
            EditorUtility.SetDirty(createdReport.Catalog);
            AssetDatabase.SaveAssets();

            var syncReport = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.SyncExistingProvider(graph, createdReport.Provider);

            var syncedRootEntry = syncReport.Catalog.Entries.Single(entry => entry.nodeId == "root");
            Assert.That(syncReport.AddedCount, Is.EqualTo(0));
            Assert.That(syncedRootEntry.displayName, Is.EqualTo("Manual Root"));
            Assert.That(syncedRootEntry.description, Is.EqualTo("manual description"));
            Assert.That(syncedRootEntry.effectSummary, Is.EqualTo("manual effect"));
            Assert.That(syncedRootEntry.cost, Is.EqualTo(7));
            Assert.That(syncedRootEntry.maxLevel, Is.EqualTo(4));
        }

        [Test]
        public void SyncExistingProviderSeedsLegacyDisplayNameAndIconOnlyWhenMetadataIsBlank()
        {
            var graph = CreateGraph(_treeId, "root");
            File.WriteAllText(_legacyJsonPath, @"{
  ""schemaVersion"": 1,
  ""treeId"": ""legacy"",
  ""nodes"": [
    {
      ""id"": ""root"",
      ""displayName"": ""Legacy Root"",
      ""iconKey"": ""Folder Icon"",
      ""position"": { ""x"": 100.0, ""y"": 120.0 }
    }
  ]
}");

            var report = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph, _legacyJsonPath);
            var rootEntry = report.Catalog.Entries.Single(entry => entry.nodeId == "root");

            Assert.That(rootEntry.displayName, Is.EqualTo("Legacy Root"));
            Assert.That(rootEntry.icon, Is.Null);
        }

        [Test]
        public void SyncExistingProviderDoesNotOverwriteExistingDisplayNameOrIconWithLegacyValues()
        {
            var graph = CreateGraph(_treeId, "root");
            var createdReport = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var rootEntry = createdReport.Catalog.Entries.Single(entry => entry.nodeId == "root");
            rootEntry.displayName = "Existing Root";
            rootEntry.icon = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            EditorUtility.SetDirty(createdReport.Catalog);
            AssetDatabase.SaveAssets();

            File.WriteAllText(_legacyJsonPath, @"{
  ""schemaVersion"": 1,
  ""treeId"": ""legacy"",
  ""nodes"": [
    {
      ""id"": ""root"",
      ""displayName"": ""Legacy Root"",
      ""iconKey"": ""Folder Icon"",
      ""position"": { ""x"": 100.0, ""y"": 120.0 }
    }
  ]
}");

            var syncReport = SkillTree.Authoring.Editor.SkillTreeMetadataAssetSyncService.SyncExistingProvider(graph, createdReport.Provider, _legacyJsonPath);
            var syncedRootEntry = syncReport.Catalog.Entries.Single(entry => entry.nodeId == "root");

            Assert.That(syncedRootEntry.displayName, Is.EqualTo("Existing Root"));
            Assert.That(syncedRootEntry.icon, Is.EqualTo(rootEntry.icon));
        }

        private static SkillTreeGraphData CreateGraph(string treeId, params string[] nodeIds)
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
            for (var index = 0; index < nodeIds.Length; index += 1)
            {
                graph.nodes.Add(new SkillTreeNodeRecord
                {
                    id = nodeIds[index],
                    position = new Vector2(100f + (index * 120f), 120f)
                });
            }

            return graph;
        }
    }
}
