using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring.Editor;
using SkillTree.Authoring.Runtime;
using SkillTree.Authoring.Samples;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeRuntimePrefabSyncServiceTests
    {
        private string _treeId;
        private string _assetFolderPath;
        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            _treeId = $"prefabsync_{Guid.NewGuid():N}";
            _assetFolderPath = $"Assets/Game/SkillTreeData/{_treeId}";
            _assetPath = $"{_assetFolderPath}/{_treeId}_RuntimeView.prefab";
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(_assetFolderPath))
            {
                AssetDatabase.DeleteAsset(_assetFolderPath);
            }
        }

        [Test]
        public void OpenSessionBlocksWhenStoredBindingDoesNotMatch()
        {
            var graph = CreateGraph(_treeId, includeChild: true);
            var metadataReport = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(_assetPath, graph, metadataReport.Provider, nodePrefab);

            var prefabRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var sourceBinding = prefabRoot.GetComponent<SkillTreeRuntimeSourceBinding>();
                sourceBinding.Apply("other_tree", "other-guid");
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, _assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            using var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                _assetPath,
                graph,
                metadataReport.Provider,
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(metadataReport.Provider)));

            Assert.That(session.Status, Is.EqualTo(SkillTreeRuntimePrefabSyncSessionStatus.BindingMismatch));
            Assert.That(session.StoredTreeId, Is.EqualTo("other_tree"));
            Assert.That(session.StoredMetadataProviderGuid, Is.EqualTo("other-guid"));
        }

        [Test]
        public void OpenSessionTreatsBlankStampAsInitialBindingAndSavesSourceStamp()
        {
            var graph = CreateGraph(_treeId, includeChild: true);
            var metadataReport = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(_assetPath, graph, metadataReport.Provider, nodePrefab);

            var prefabRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var sourceBinding = prefabRoot.GetComponent<SkillTreeRuntimeSourceBinding>();
                sourceBinding.Clear();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, _assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            var providerGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(metadataReport.Provider));
            using (var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                       _assetPath,
                       graph,
                       metadataReport.Provider,
                       providerGuid))
            {
                Assert.That(session.Status, Is.EqualTo(SkillTreeRuntimePrefabSyncSessionStatus.Ready));
                Assert.That(session.RequiresInitialBindingConfirmation, Is.True);
                session.Save();
            }

            var reloadedRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var sourceBinding = reloadedRoot.GetComponent<SkillTreeRuntimeSourceBinding>();
                Assert.That(sourceBinding.SourceTreeId, Is.EqualTo(graph.treeId));
                Assert.That(sourceBinding.SourceMetadataProviderGuid, Is.EqualTo(providerGuid));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(reloadedRoot);
            }
        }

        [Test]
        public void OpenSessionPreservesCustomizationsAcrossAddDeleteAndRevive()
        {
            var initialGraph = CreateGraph(_treeId, includeChild: true);
            var metadataReport = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(initialGraph);
            var providerGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(metadataReport.Provider));
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(_assetPath, initialGraph, metadataReport.Provider, nodePrefab);

            var prefabRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var runtimeView = prefabRoot.GetComponent<SkillTreeRuntimeView>();
                runtimeView.Build(initialGraph, metadataReport.Provider);
                Assert.That(runtimeView.TryGetNodeView("child", out var childView), Is.True);
                childView.GetComponent<Image>().color = new Color(0.84f, 0.21f, 0.32f, 1f);
                var canvasGroup = childView.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0.42f;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, _assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            var expandedGraph = CreateGraph(_treeId, includeChild: true);
            expandedGraph.nodes.Single(node => node.id == "child").position = new Vector2(620f, 240f);
            expandedGraph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "new_leaf",
                parentId = "child",
                position = new Vector2(860f, 420f)
            });

            using (var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                       _assetPath,
                       expandedGraph,
                       metadataReport.Provider,
                       providerGuid))
            {
                Assert.That(session.Status, Is.EqualTo(SkillTreeRuntimePrefabSyncSessionStatus.Ready));
                Assert.That(session.BuildReport.MovedCount, Is.EqualTo(1), "MovedCount");
                Assert.That(session.BuildReport.AddedCount, Is.EqualTo(1), "AddedCount");
                session.Save();
            }

            var expandedRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var runtimeView = expandedRoot.GetComponent<SkillTreeRuntimeView>();
                runtimeView.Build(expandedGraph, metadataReport.Provider);
                Assert.That(runtimeView.TryGetNodeView("child", out var childView), Is.True);
                Assert.That(childView.GetComponent<Image>().color, Is.EqualTo(new Color(0.84f, 0.21f, 0.32f, 1f)));
                Assert.That(childView.GetComponent<CanvasGroup>(), Is.Not.Null);
                Assert.That(childView.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.42f));
                Assert.That(runtimeView.TryGetNodeView("new_leaf", out var newLeafView), Is.True);
                Assert.That(newLeafView.SerializedNodeId, Is.EqualTo("new_leaf"));
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(newLeafView), Is.True);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(newLeafView), Is.EqualTo(nodePrefab));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(expandedRoot);
            }

            var deletedGraph = CreateGraph(_treeId, includeChild: false);
            using (var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                       _assetPath,
                       deletedGraph,
                       metadataReport.Provider,
                       providerGuid))
            {
                Assert.That(session.BuildReport.DeletedCount, Is.EqualTo(2), "DeletedCount");
                session.Save();
            }

            var deletedRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var runtimeView = deletedRoot.GetComponent<SkillTreeRuntimeView>();
                runtimeView.Build(deletedGraph, metadataReport.Provider);
                Assert.That(runtimeView.TryGetRemovedNodeView("child", out var removedChildView), Is.True);
                Assert.That(((SampleSkillTreeRuntimeNodeView)removedChildView).DisplayName, Does.StartWith("[Deleted] "));
                Assert.That(removedChildView.GetComponent<CanvasGroup>(), Is.Not.Null);
                Assert.That(removedChildView.GetComponent<Image>().color, Is.EqualTo(new Color(0.84f, 0.21f, 0.32f, 1f)));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(deletedRoot);
            }

            using (var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                       _assetPath,
                       expandedGraph,
                       metadataReport.Provider,
                       providerGuid))
            {
                Assert.That(session.BuildReport.RevivedCount, Is.EqualTo(2), "RevivedCount");
                session.Save();
            }

            var revivedRoot = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                var runtimeView = revivedRoot.GetComponent<SkillTreeRuntimeView>();
                runtimeView.Build(expandedGraph, metadataReport.Provider);
                Assert.That(runtimeView.TryGetNodeView("child", out var revivedChildView), Is.True);
                Assert.That(((SampleSkillTreeRuntimeNodeView)revivedChildView).DisplayName, Does.Not.StartWith("[Deleted] "));
                Assert.That(revivedChildView.GetComponent<CanvasGroup>(), Is.Not.Null);
                Assert.That(revivedChildView.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.42f));
                Assert.That(revivedChildView.GetComponent<Image>().color, Is.EqualTo(new Color(0.84f, 0.21f, 0.32f, 1f)));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(revivedRoot);
            }
        }

        private static SkillTreeGraphData CreateGraph(string treeId, bool includeChild)
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(120f, 120f)
            });

            if (includeChild)
            {
                graph.nodes.Add(new SkillTreeNodeRecord
                {
                    id = "child",
                    parentId = "root",
                    position = new Vector2(420f, 220f)
                });
            }

            return graph;
        }

    }
}
