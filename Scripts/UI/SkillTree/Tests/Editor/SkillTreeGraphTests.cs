using System;
using System.Linq;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SkillTree.Authoring.Editor;

namespace SkillTree.Authoring.Tests
{
    public class SkillTreeGraphTests
    {
        [Test]
        public void JsonRoundTripPreservesNodeIdentityAndPosition()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("demo");
            graph.editorBindings.metadataProviderAssetPath = "Assets/Game/SkillTreeData/demo/demo_SkillNodeMetadataProvider.asset";
            graph.editorBindings.metadataProviderAssetGuid = "demo-guid";
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(120f, 180f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                parentLineType = SkillTreeConnectionLineType.Straight,
                position = new Vector2(420f, 180f)
            });

            var json = SkillTreeJsonService.Serialize(graph);
            var reloaded = SkillTreeJsonService.Deserialize(json);

            Assert.That(reloaded.treeId, Is.EqualTo("demo"));
            Assert.That(reloaded.editorBindings, Is.Not.Null);
            Assert.That(reloaded.editorBindings.metadataProviderAssetPath, Is.EqualTo(graph.editorBindings.metadataProviderAssetPath));
            Assert.That(reloaded.editorBindings.metadataProviderAssetGuid, Is.EqualTo(graph.editorBindings.metadataProviderAssetGuid));
            Assert.That(reloaded.nodes.Count, Is.EqualTo(2));
            Assert.That(reloaded.nodes.Single(node => node.id == "child").parentId, Is.EqualTo("root"));
            Assert.That(reloaded.nodes.Single(node => node.id == "child").parentLineType, Is.EqualTo(SkillTreeConnectionLineType.Straight));
            Assert.That(reloaded.nodes.Single(node => node.id == "child").position, Is.EqualTo(new Vector2(420f, 180f)));
            Assert.That(json, Does.Not.Contain("displayName"));
            Assert.That(json, Does.Not.Contain("iconKey"));
            Assert.That(json, Does.Not.Contain("visualKey"));
            Assert.That(json, Does.Not.Contain("tabId"));
            Assert.That(json, Does.Not.Contain("branchId"));
        }

        [Test]
        public void DeserializeIgnoresLegacyGraphFields()
        {
            const string json = @"{
  ""schemaVersion"": 1,
  ""treeId"": ""legacy"",
  ""nodes"": [
    {
      ""id"": ""root"",
      ""displayName"": ""Legacy Root"",
      ""iconKey"": ""Folder Icon"",
      ""visualKey"": ""root"",
      ""tabId"": ""core"",
      ""branchId"": ""main"",
      ""position"": {
        ""x"": 120.0,
        ""y"": 140.0
      }
    }
  ]
}";

            var graph = SkillTreeJsonService.Deserialize(json);

            Assert.That(graph.schemaVersion, Is.EqualTo(SkillTreeGraphData.CurrentSchemaVersion));
            Assert.That(graph.treeId, Is.EqualTo("legacy"));
            Assert.That(graph.editorBindings, Is.Not.Null);
            Assert.That(graph.editorBindings.metadataProviderAssetPath, Is.Null);
            Assert.That(graph.editorBindings.metadataProviderAssetGuid, Is.Null);
            Assert.That(graph.nodes.Count, Is.EqualTo(1));
            Assert.That(graph.nodes[0].id, Is.EqualTo("root"));
            Assert.That(graph.nodes[0].parentLineType, Is.EqualTo(SkillTreeConnectionLineType.Curved));
            Assert.That(graph.nodes[0].position, Is.EqualTo(new Vector2(120f, 140f)));
        }

        [Test]
        public void ControllerLoadFromFileRestoresEditorBindingsFromJson()
        {
            var treeId = $"restore_{System.Guid.NewGuid():N}";
            var assetFolderPath = $"Assets/Game/SkillTreeData/{treeId}";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId));
            AssetDatabase.Refresh();

            var controller = new SkillTreeEditorController();
            try
            {
                var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
                graph.nodes.Add(new SkillTreeNodeRecord
                {
                    id = "root",
                    position = new Vector2(120f, 120f)
                });

                var metadataReport = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
                var jsonPath = Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId, $"{treeId}.json");

                graph.editorBindings.metadataProviderAssetPath = AssetDatabase.GetAssetPath(metadataReport.Provider);
                graph.editorBindings.metadataProviderAssetGuid = AssetDatabase.AssetPathToGUID(graph.editorBindings.metadataProviderAssetPath);
                SkillTreeJsonService.SaveToFile(jsonPath, graph);

                controller.LoadFromFile(jsonPath);

                Assert.That(controller.MetadataProvider, Is.EqualTo(metadataReport.Provider));
                Assert.That(controller.MetadataProviderAssetGuid, Is.EqualTo(graph.editorBindings.metadataProviderAssetGuid));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(assetFolderPath))
                {
                    AssetDatabase.DeleteAsset(assetFolderPath);
                }
            }
        }

        [Test]
        public void ControllerLoadFromFileKeepsBrokenBindingPathsAndReportsWarning()
        {
            var treeId = $"broken_{System.Guid.NewGuid():N}";
            var assetFolderPath = $"Assets/Game/SkillTreeData/{treeId}";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId));
            AssetDatabase.Refresh();

            var controller = new SkillTreeEditorController();
            try
            {
                var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
                graph.nodes.Add(new SkillTreeNodeRecord
                {
                    id = "root",
                    position = new Vector2(120f, 120f)
                });
                graph.editorBindings.metadataProviderAssetPath = "Assets/Game/SkillTreeData/missing/missing_provider.asset";

                var jsonPath = Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId, $"{treeId}.json");
                SkillTreeJsonService.SaveToFile(jsonPath, graph);

                controller.LoadFromFile(jsonPath);

                Assert.That(controller.MetadataProvider, Is.Null);
                Assert.That(controller.Graph.editorBindings.metadataProviderAssetPath, Is.EqualTo(graph.editorBindings.metadataProviderAssetPath));
                Assert.That(controller.StatusType, Is.EqualTo(SkillTreeEditorStatusType.Warning));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(assetFolderPath))
                {
                    AssetDatabase.DeleteAsset(assetFolderPath);
                }
            }
        }

        [Test]
        public void ControllerLoadFromFileBackfillsProviderGuidForLegacyJson()
        {
            var treeId = $"legacyguid_{Guid.NewGuid():N}";
            var assetFolderPath = $"Assets/Game/SkillTreeData/{treeId}";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId));
            AssetDatabase.Refresh();

            var controller = new SkillTreeEditorController();
            try
            {
                var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
                graph.nodes.Add(new SkillTreeNodeRecord
                {
                    id = "root",
                    position = new Vector2(120f, 120f)
                });

                var metadataReport = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
                graph.editorBindings.metadataProviderAssetPath = AssetDatabase.GetAssetPath(metadataReport.Provider);
                graph.editorBindings.metadataProviderAssetGuid = null;

                var jsonPath = Path.Combine(Application.dataPath, "Game/SkillTreeData", treeId, $"{treeId}.json");
                SkillTreeJsonService.SaveToFile(jsonPath, graph);

                controller.LoadFromFile(jsonPath);

                Assert.That(controller.MetadataProvider, Is.EqualTo(metadataReport.Provider));
                Assert.That(controller.MetadataProviderAssetGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(graph.editorBindings.metadataProviderAssetPath)));
                Assert.That(controller.Graph.editorBindings.metadataProviderAssetGuid, Is.EqualTo(controller.MetadataProviderAssetGuid));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(assetFolderPath))
                {
                    AssetDatabase.DeleteAsset(assetFolderPath);
                }
            }
        }

        [Test]
        public void DeleteNodePromotesChildrenToRoot()
        {
            var graph = CreateSampleGraph();

            var deleted = SkillTreeGraphMutator.DeleteNode(graph, "root");

            Assert.That(deleted, Is.True);
            Assert.That(graph.nodes.Any(node => node.id == "root"), Is.False);
            Assert.That(graph.nodes.Single(node => node.id == "child").parentId, Is.Null);
        }

        [Test]
        public void ReparentRejectsCycles()
        {
            var graph = CreateSampleGraph();
            SkillTreeGraphMutator.AddNode(graph, new Vector2(700f, 100f)).id = "leaf";
            SkillTreeGraphMutator.SetParent(graph, "leaf", "child", out _);

            var allowed = SkillTreeGraphValidator.CanAssignParent(graph, "root", "leaf", out var errorMessage);

            Assert.That(allowed, Is.False);
            Assert.That(errorMessage, Does.Contain("순환"));
        }

        [Test]
        public void DuplicateIdsProduceBlockingErrors()
        {
            var graph = CreateSampleGraph();
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                position = new Vector2(500f, 240f)
            });

            var issues = SkillTreeGraphValidator.Validate(graph);

            Assert.That(issues.Any(issue =>
                issue.severity == SkillTreeValidationSeverity.Error &&
                issue.code == "DuplicateNodeId"), Is.True);
        }

        [Test]
        public void MissingMetadataAndIconProduceWarnings()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph();
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "orphan",
                position = new Vector2(100f, 100f)
            });

            var provider = ScriptableObject.CreateInstance<EmptyMetadataProvider>();
            var issues = SkillTreeGraphValidator.Validate(graph, provider);

            Assert.That(issues.Any(issue => issue.code == "MissingMetadata"), Is.True);
            Assert.That(issues.Any(issue => issue.code == "MissingIcon"), Is.True);
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void UnusedMetadataProducesWarning()
        {
            var graph = CreateSampleGraph();
            var provider = ScriptableObject.CreateInstance<TestMetadataProvider>();
            provider.knownNodeIds.Add("root");
            provider.knownNodeIds.Add("ghost_node");
            provider.metadataById["root"] = new SkillNodeMetadata
            {
                nodeId = "root"
            };

            var issues = SkillTreeGraphValidator.Validate(graph, provider);

            Assert.That(issues.Any(issue =>
                issue.severity == SkillTreeValidationSeverity.Warning &&
                issue.code == "UnusedMetadata" &&
                issue.nodeId == "ghost_node"), Is.True);
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void RenameNodeUpdatesChildParentReference()
        {
            var graph = CreateSampleGraph();

            var renamed = SkillTreeGraphMutator.RenameNode(graph, "root", "root_main");

            Assert.That(renamed, Is.True);
            Assert.That(graph.nodes.Single(node => node.id == "child").parentId, Is.EqualTo("root_main"));
        }

        [Test]
        public void ControllerParentLinkConnectsStartedChildToTargetParent()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("controller");
            controller.AddNode(new Vector2(100f, 100f));
            controller.AddNode(new Vector2(300f, 100f));

            var parent = controller.Graph.nodes[0].id;
            var child = controller.Graph.nodes[1].id;

            controller.BeginParentLink(child);
            var success = controller.CompleteParentLink(parent, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(controller.Graph.nodes.Single(node => node.id == child).parentId, Is.EqualTo(parent));
            Assert.That(controller.PendingChildNodeId, Is.Null);
        }

        [Test]
        public void SelectingConnectionClearsNodeSelectionAndStoresChildId()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("connection_select");
            var parent = controller.AddNode(new Vector2(100f, 100f)).id;
            var child = controller.AddNode(new Vector2(320f, 180f)).id;
            controller.SetSelectedParent(parent, out _);

            controller.SelectNode(parent);
            controller.SelectConnection(child);

            Assert.That(controller.SelectedNodeId, Is.Null);
            Assert.That(controller.SelectedConnectionChildId, Is.EqualTo(child));
        }

        [Test]
        public void SelectedConnectionLineTypeUpdatesChildRecord()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("connection_style");
            var parent = controller.AddNode(new Vector2(100f, 100f)).id;
            var child = controller.AddNode(new Vector2(320f, 180f)).id;
            controller.SetSelectedParent(parent, out _);
            controller.SelectConnection(child);

            var changed = controller.SetSelectedConnectionLineType(SkillTreeConnectionLineType.Straight);

            Assert.That(changed, Is.True);
            Assert.That(controller.Graph.nodes.Single(node => node.id == child).parentLineType, Is.EqualTo(SkillTreeConnectionLineType.Straight));
        }

        [Test]
        public void SelectingNodeClearsSelectedConnection()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("connection_clear");
            var parent = controller.AddNode(new Vector2(100f, 100f)).id;
            var child = controller.AddNode(new Vector2(320f, 180f)).id;
            controller.SelectNode(child);
            controller.SetSelectedParent(parent, out _);
            controller.SelectConnection(child);
            controller.SelectNode(parent);

            Assert.That(controller.SelectedConnectionChildId, Is.Null);
        }

        [Test]
        public void OneParentCanKeepMultipleChildren()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("siblings");
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "A",
                position = new Vector2(100f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "B1",
                position = new Vector2(320f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "B2",
                position = new Vector2(320f, 240f)
            });

            var firstAssigned = SkillTreeGraphMutator.SetParent(graph, "B1", "A", out var firstError);
            var secondAssigned = SkillTreeGraphMutator.SetParent(graph, "B2", "A", out var secondError);

            Assert.That(firstAssigned, Is.True, firstError);
            Assert.That(secondAssigned, Is.True, secondError);
            Assert.That(graph.nodes.Single(node => node.id == "B1").parentId, Is.EqualTo("A"));
            Assert.That(graph.nodes.Single(node => node.id == "B2").parentId, Is.EqualTo("A"));
        }

        [Test]
        public void ControllerParentLinkKeepsExistingSiblingWhenAddingAnotherChild()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("controller_siblings");
            var parent = controller.AddNode(new Vector2(100f, 100f)).id;
            var firstChild = controller.AddNode(new Vector2(320f, 100f)).id;
            var secondChild = controller.AddNode(new Vector2(320f, 240f)).id;

            controller.SelectNode(firstChild);
            var firstLinked = controller.SetSelectedParent(parent, out var firstError);
            Assert.That(firstLinked, Is.True, firstError);
            Assert.That(controller.Graph.nodes.Single(node => node.id == firstChild).parentId, Is.EqualTo(parent));

            controller.SelectNode(secondChild);
            var secondLinked = controller.SetSelectedParent(parent, out var secondError);

            Assert.That(secondLinked, Is.True, secondError);
            Assert.That(controller.Graph.nodes.Single(node => node.id == firstChild).parentId, Is.EqualTo(parent));
            Assert.That(controller.Graph.nodes.Single(node => node.id == secondChild).parentId, Is.EqualTo(parent));
        }

        [Test]
        public void ControllerParentLinkCancelsOnEmptyTarget()
        {
            var controller = new SkillTreeEditorController();
            controller.CreateNewGraph("controller");
            controller.AddNode(new Vector2(100f, 100f));

            var first = controller.Graph.nodes[0].id;
            controller.BeginParentLink(first);
            var success = controller.CompleteParentLink(null, out _);

            Assert.That(success, Is.False);
            Assert.That(controller.PendingChildNodeId, Is.Null);
        }

        private static SkillTreeGraphData CreateSampleGraph()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("sample");
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(100f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                position = new Vector2(340f, 120f)
            });
            return graph;
        }

        private sealed class EmptyMetadataProvider : SkillNodeMetadataProviderAsset
        {
            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                metadata = null;
                return false;
            }
        }

        private sealed class TestMetadataProvider : SkillNodeMetadataProviderAsset
        {
            public readonly System.Collections.Generic.Dictionary<string, SkillNodeMetadata> metadataById = new(System.StringComparer.Ordinal);
            public readonly System.Collections.Generic.List<string> knownNodeIds = new();

            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                return metadataById.TryGetValue(nodeId, out metadata);
            }

            public override System.Collections.Generic.IReadOnlyList<string> GetKnownNodeIds()
            {
                return knownNodeIds;
            }
        }
    }
}
