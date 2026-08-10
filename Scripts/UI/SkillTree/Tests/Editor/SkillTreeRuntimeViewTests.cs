using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using SkillTree.Authoring.Editor;
using SkillTree.Authoring.Runtime;
using SkillTree.Authoring.Samples;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeRuntimeViewTests
    {
        private string _treeId;
        private string _assetFolderPath;

        [SetUp]
        public void SetUp()
        {
            _treeId = $"runtime_{Guid.NewGuid():N}";
            _assetFolderPath = $"Assets/Game/SkillTreeData/{_treeId}";
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
        public void RuntimeViewBuildCreatesNodesAndConnectionsFromGraph()
        {
            var graph = CreateGraph(_treeId);
            var provider = CreateMetadataProvider();
            var nodeTemplate = CreateNodeTemplate();
            var root = CreateRuntimeViewRoot(out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(root);

            try
            {
                runtimeView.Configure(
                    graph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic);
                runtimeView.Build();
                Canvas.ForceUpdateCanvases();

                Assert.That(runtimeView.RenderedNodeCount, Is.EqualTo(2));
                Assert.That(connectionGraphic.RenderedConnectionCount, Is.EqualTo(1));
                Assert.That(GetGeneratedVertexCount(connectionGraphic), Is.GreaterThan(0));

                Assert.That(runtimeView.TryGetNodeView("root", out var rootView), Is.True);
                Assert.That(runtimeView.TryGetNodeView("child", out var childView), Is.True);
                Assert.That(rootView.IsSelected, Is.True);
                var childSampleView = (SampleSkillTreeRuntimeNodeView)childView;
                Assert.That(childSampleView.DisplayName, Is.EqualTo("Child Node"));
                Assert.That(childSampleView.CostLabel, Is.EqualTo("7"));
                Assert.That(childSampleView.IconSprite, Is.Not.Null);
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void RuntimeViewBuildCreatesVerticesForStraightConnections()
        {
            var graph = CreateGraph(_treeId);
            graph.nodes.Single(node => node.id == "child").parentLineType = SkillTreeConnectionLineType.Straight;
            var provider = CreateMetadataProvider();
            var nodeTemplate = CreateNodeTemplate();
            var root = CreateRuntimeViewRoot(out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(root);

            try
            {
                runtimeView.Configure(
                    graph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic);
                runtimeView.Build();
                Canvas.ForceUpdateCanvases();

                Assert.That(connectionGraphic.RenderedConnectionCount, Is.EqualTo(1));
                Assert.That(GetGeneratedVertexCount(connectionGraphic), Is.GreaterThan(0));
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void RuntimeViewNodeClickSelectsNodeAndRaisesEvent()
        {
            var graph = CreateGraph(_treeId);
            var provider = CreateMetadataProvider();
            var nodeTemplate = CreateNodeTemplate();
            var root = CreateRuntimeViewRoot(out var runtimeView, out var connectionGraphic);

            try
            {
                runtimeView.Configure(
                    graph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic);
                runtimeView.Build();

                string selectedNodeId = null;
                runtimeView.OnNodeSelected += nodeId => selectedNodeId = nodeId;

                Assert.That(runtimeView.TryGetNodeView("child", out var childView), Is.True);
                childView.ClickButton.onClick.Invoke();

                Assert.That(runtimeView.SelectedNodeId, Is.EqualTo("child"));
                Assert.That(selectedNodeId, Is.EqualTo("child"));
                Assert.That(childView.IsSelected, Is.True);
            }
            finally
            {
                Cleanup(root, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void RuntimeNodeBaseSupportsCustomSubclassWithoutSampleFields()
        {
            var root = new GameObject("BareNode", typeof(RectTransform), typeof(Button), typeof(BareRuntimeNodeView));
            var restoredRoot = new GameObject("restored_RuntimeNode", typeof(RectTransform), typeof(BareRuntimeNodeView));

            try
            {
                var view = root.GetComponent<BareRuntimeNodeView>();
                AssignReference(view, "clickButton", root.GetComponent<Button>());

                var definition = new SkillDefinition
                {
                    skillId = "custom",
                    displayName = "Custom Skill",
                    cost = 9
                };
                view.BindDefinition("custom", definition);

                Assert.That(view.NodeId, Is.EqualTo("custom"));
                Assert.That(view.SerializedNodeId, Is.EqualTo("custom"));
                Assert.That(view.BoundNodeId, Is.EqualTo("custom"));
                Assert.That(view.LastDefinition, Is.SameAs(definition));

                view.ApplyStatus(
                    new UserSkillData { definition = definition, state = new UserSkillState { skillId = "custom" } },
                    new SkillStatusData { skillId = "custom", isAffordable = true },
                    true);

                Assert.That(view.LastStatus.skillId, Is.EqualTo("custom"));
                Assert.That(view.IsSelected, Is.True);
                Assert.That(view.LastSelectionValue, Is.True);

                string clickedNodeId = null;
                view.SetClickHandler(nodeId => clickedNodeId = nodeId);
                view.ClickButton.onClick.Invoke();
                Assert.That(clickedNodeId, Is.EqualTo("custom"));

                clickedNodeId = null;
                view.MarkAsDeleted();
                view.ClickButton.onClick.Invoke();
                Assert.That(view.SyncState, Is.EqualTo(SkillTreeRuntimeNodeSyncState.DeletedFromGraph));
                Assert.That(clickedNodeId, Is.Null);

                view.MarkAsActive("custom");
                view.ClickButton.onClick.Invoke();
                Assert.That(view.SyncState, Is.EqualTo(SkillTreeRuntimeNodeSyncState.Active));
                Assert.That(clickedNodeId, Is.EqualTo("custom"));

                var restoredView = restoredRoot.GetComponent<BareRuntimeNodeView>();
                Assert.That(restoredView.TryRestoreSerializedNodeIdFromName(), Is.True);
                Assert.That(restoredView.NodeId, Is.EqualTo("restored"));
            }
            finally
            {
                Cleanup(root, restoredRoot);
            }
        }

        [Test]
        public void RuntimePrefabFactoryCreatesPrefabWithExpectedHierarchy()
        {
            var graph = CreateGraph(_treeId);
            var report = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            var assetPath = $"{_assetFolderPath}/{_treeId}_RuntimeView.prefab";

            Assert.That(AssetDatabase.GetAssetPath(nodePrefab), Is.EqualTo(SkillTreeRuntimePrefabFactory.DefaultRuntimeNodePrefabPath));

            var runtimePrefab = SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(
                assetPath,
                graph,
                report.Provider,
                nodePrefab);

            Assert.That(runtimePrefab, Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), Is.Not.Null);

            var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                Assert.That(prefabRoot.transform.Find("Viewport"), Is.Not.Null);
                Assert.That(prefabRoot.transform.Find("Viewport/Content"), Is.Not.Null);
                Assert.That(prefabRoot.transform.Find("Viewport/Content/Connections"), Is.Not.Null);
                Assert.That(prefabRoot.transform.Find("Viewport/Content/Nodes"), Is.Not.Null);
                Assert.That(prefabRoot.transform.Find("Viewport/Content/RemovedNodes"), Is.Not.Null);

                var runtimeView = prefabRoot.GetComponent<SkillTreeRuntimeView>();
                Assert.That(runtimeView, Is.Not.Null);
                Assert.That(runtimeView.NodePrefab, Is.EqualTo(nodePrefab));
                Assert.That(nodePrefab, Is.TypeOf<SampleSkillTreeRuntimeNodeView>());
                Assert.That(runtimeView.NodeLayer.childCount, Is.EqualTo(graph.nodes.Count));
                foreach (Transform child in runtimeView.NodeLayer)
                {
                    var childNodeView = child.GetComponent<SkillTreeRuntimeNodeView>();
                    Assert.That(childNodeView, Is.Not.Null);
                    Assert.That(PrefabUtility.IsPartOfPrefabInstance(childNodeView), Is.True);
                    Assert.That(
                        PrefabUtility.GetCorrespondingObjectFromSource(childNodeView),
                        Is.EqualTo(nodePrefab));
                }

                var sourceBinding = prefabRoot.GetComponent<SkillTreeRuntimeSourceBinding>();
                Assert.That(sourceBinding, Is.Not.Null);
                Assert.That(sourceBinding.SourceTreeId, Is.EqualTo(graph.treeId));
                Assert.That(sourceBinding.SourceMetadataProviderGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(report.Provider))));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [Test]
        public void RuntimeViewInspectorDoesNotExposeSourceData()
        {
            var root = CreateRuntimeViewRoot(out var runtimeView, out _);
            var serializedObject = new SerializedObject(runtimeView);

            try
            {
                Assert.That(serializedObject.FindProperty("graphSnapshot"), Is.Null);
                Assert.That(serializedObject.FindProperty("metadataProvider"), Is.Null);
                Assert.That(serializedObject.FindProperty("sourceTreeId"), Is.Null);
                Assert.That(serializedObject.FindProperty("sourceMetadataProviderGuid"), Is.Null);
                Assert.That(serializedObject.FindProperty("rebuildOnStart"), Is.Null);
            }
            finally
            {
                Cleanup(root);
            }
        }

        [Test]
        public void BuildReusesMatchedNodeViewAndPreservesCustomizations()
        {
            var graph = CreateGraph(_treeId);
            var provider = CreateMetadataProvider();
            var nodeTemplate = CreateNodeTemplate();
            var root = CreateRuntimeViewRoot(out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(root);

            try
            {
                runtimeView.Configure(
                    graph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic);
                runtimeView.Build();

                Assert.That(runtimeView.TryGetNodeView("child", out var originalChildView), Is.True);
                var customBackground = new Color(0.91f, 0.35f, 0.22f, 1f);
                originalChildView.GetComponent<Image>().color = customBackground;
                originalChildView.gameObject.AddComponent<TestRuntimeMarker>();

                var updatedGraph = CreateGraph(_treeId);
                updatedGraph.nodes.Single(node => node.id == "child").position = new Vector2(560f, 420f);
                runtimeView.Configure(
                    updatedGraph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic,
                    FindRect(root, "Viewport/Content/RemovedNodes"));
                var report = runtimeView.Build();

                Assert.That(report.MovedCount, Is.EqualTo(1));
                Assert.That(runtimeView.TryGetNodeView("child", out var updatedChildView), Is.True);
                Assert.That(updatedChildView, Is.SameAs(originalChildView));
                Assert.That(updatedChildView.GetComponent<Image>().color, Is.EqualTo(customBackground));
                Assert.That(updatedChildView.GetComponent<TestRuntimeMarker>(), Is.Not.Null);
                Assert.That(updatedChildView.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(560f, -420f)));
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void BuildMovesDeletedNodesToRemovedLayerAndRevivesThem()
        {
            var graph = CreateGraph(_treeId);
            var provider = CreateMetadataProvider();
            var nodeTemplate = CreateNodeTemplate();
            var root = CreateRuntimeViewRoot(out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(root);

            try
            {
                runtimeView.Configure(
                    graph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic);
                runtimeView.Build();

                Assert.That(runtimeView.TryGetNodeView("child", out var originalChildView), Is.True);

                var deletedGraph = CreateGraph(_treeId);
                deletedGraph.nodes.RemoveAll(node => node.id == "child");
                runtimeView.Configure(
                    deletedGraph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic,
                    FindRect(root, "Viewport/Content/RemovedNodes"));
                var deleteReport = runtimeView.Build();

                Assert.That(deleteReport.DeletedCount, Is.EqualTo(1));
                Assert.That(runtimeView.TryGetNodeView("child", out _), Is.False);
                Assert.That(runtimeView.TryGetRemovedNodeView("child", out var removedChildView), Is.True);
                Assert.That(removedChildView, Is.SameAs(originalChildView));
                Assert.That(removedChildView.SyncState, Is.EqualTo(SkillTreeRuntimeNodeSyncState.DeletedFromGraph));
                Assert.That(((SampleSkillTreeRuntimeNodeView)removedChildView).DisplayName, Does.StartWith("[Deleted] "));
                Assert.That(connectionGraphic.RenderedConnectionCount, Is.EqualTo(0));

                var revivedGraph = CreateGraph(_treeId);
                revivedGraph.nodes.Single(node => node.id == "child").position = new Vector2(620f, 260f);
                runtimeView.Configure(
                    revivedGraph,
                    provider,
                    nodeTemplate,
                    FindRect(root, "Viewport/Content"),
                    FindRect(root, "Viewport/Content/Nodes"),
                    connectionGraphic,
                    FindRect(root, "Viewport/Content/RemovedNodes"));
                var reviveReport = runtimeView.Build();

                Assert.That(reviveReport.RevivedCount, Is.EqualTo(1));
                Assert.That(runtimeView.TryGetRemovedNodeView("child", out _), Is.False);
                Assert.That(runtimeView.TryGetNodeView("child", out var revivedChildView), Is.True);
                Assert.That(revivedChildView, Is.SameAs(originalChildView));
                Assert.That(revivedChildView.SyncState, Is.EqualTo(SkillTreeRuntimeNodeSyncState.Active));
                Assert.That(((SampleSkillTreeRuntimeNodeView)revivedChildView).DisplayName, Does.Not.StartWith("[Deleted] "));
                Assert.That(revivedChildView.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(620f, -260f)));
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator RuntimePrefabBuildsConnectionsWithExternalGraph()
        {
            var graph = CreateGraph(_treeId);
            var report = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(graph);
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            var assetPath = $"{_assetFolderPath}/{_treeId}_RuntimeView.prefab";

            SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(
                assetPath,
                graph,
                report.Provider,
                nodePrefab);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            var canvasRoot = WrapInCanvas(instance);

            try
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;

                var runtimeView = instance.GetComponent<SkillTreeRuntimeView>();
                var connectionGraphic = instance.transform.Find("Viewport/Content/Connections")
                    .GetComponent<SkillTreeRuntimeConnectionGraphic>();
                runtimeView.Build(graph, report.Provider);
                Canvas.ForceUpdateCanvases();

                Assert.That(runtimeView.RenderedNodeCount, Is.EqualTo(graph.nodes.Count));
                Assert.That(connectionGraphic.RenderedConnectionCount, Is.EqualTo(1));
                Assert.That(GetGeneratedVertexCount(connectionGraphic), Is.GreaterThan(0));
            }
            finally
            {
                Cleanup(canvasRoot);
            }
        }

        private static SkillTreeGraphData CreateGraph(string treeId)
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(100f, 120f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                position = new Vector2(420f, 320f)
            });
            return graph;
        }

        private static TestMetadataProvider CreateMetadataProvider()
        {
            var provider = ScriptableObject.CreateInstance<TestMetadataProvider>();
            provider.metadataById["root"] = new SkillNodeMetadata
            {
                nodeId = "root",
                displayName = "Root Node",
                cost = 3
            };
            provider.metadataById["child"] = new SkillNodeMetadata
            {
                nodeId = "child",
                displayName = "Child Node",
                cost = 7,
                icon = CreateIconSprite()
            };
            return provider;
        }

        private static SkillTreeRuntimeNodeView CreateNodeTemplate()
        {
            var root = new GameObject("RuntimeNodeTemplate", typeof(RectTransform), typeof(Image), typeof(Button), typeof(SampleSkillTreeRuntimeNodeView));
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 96f);
            var background = root.GetComponent<Image>();
            var button = root.GetComponent<Button>();
            button.targetGraphic = background;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(root.transform, false);
            var name = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            name.transform.SetParent(root.transform, false);
            name.font = ResolveDefaultFontAsset();
            var cost = new GameObject("Cost", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            cost.transform.SetParent(root.transform, false);
            cost.font = ResolveDefaultFontAsset();
            var highlight = new GameObject("Highlight", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            highlight.transform.SetParent(root.transform, false);
            highlight.enabled = false;

            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "clickButton", button);
            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "backgroundImage", background);
            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "iconImage", icon);
            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "nameText", name);
            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "costText", cost);
            AssignReference(root.GetComponent<SampleSkillTreeRuntimeNodeView>(), "selectedHighlight", highlight);
            root.SetActive(true);
            return root.GetComponent<SkillTreeRuntimeNodeView>();
        }

        private static GameObject CreateRuntimeViewRoot(
            out SkillTreeRuntimeView runtimeView,
            out SkillTreeRuntimeConnectionGraphic connectionGraphic)
        {
            var root = new GameObject("RuntimeViewRoot", typeof(RectTransform), typeof(SkillTreeRuntimeView));
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var connections = new GameObject("Connections", typeof(RectTransform), typeof(CanvasRenderer), typeof(SkillTreeRuntimeConnectionGraphic));
            connections.transform.SetParent(content.transform, false);
            var nodes = new GameObject("Nodes", typeof(RectTransform));
            nodes.transform.SetParent(content.transform, false);
            var removedNodes = new GameObject("RemovedNodes", typeof(RectTransform));
            removedNodes.transform.SetParent(content.transform, false);
            removedNodes.SetActive(false);

            runtimeView = root.GetComponent<SkillTreeRuntimeView>();
            connectionGraphic = connections.GetComponent<SkillTreeRuntimeConnectionGraphic>();
            return root;
        }

        private static GameObject WrapInCanvas(GameObject runtimeRoot)
        {
            var canvasRoot = new GameObject("CanvasRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            runtimeRoot.transform.SetParent(canvasRoot.transform, false);
            var runtimeRect = runtimeRoot.GetComponent<RectTransform>();
            runtimeRect.anchorMin = new Vector2(0.5f, 0.5f);
            runtimeRect.anchorMax = new Vector2(0.5f, 0.5f);
            runtimeRect.pivot = new Vector2(0.5f, 0.5f);
            runtimeRect.sizeDelta = new Vector2(1280f, 720f);
            return canvasRoot;
        }

        private static RectTransform FindRect(GameObject root, string path)
        {
            return root.transform.Find(path)?.GetComponent<RectTransform>();
        }

        private static void AssignReference<TValue>(UnityEngine.Object target, string fieldName, TValue value)
            where TValue : UnityEngine.Object
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite CreateIconSprite()
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = texture.GetPixels32();
            for (var index = 0; index < pixels.Length; index += 1)
            {
                pixels[index] = new Color32(255, 200, 40, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private static TMP_FontAsset ResolveDefaultFontAsset()
        {
            return TMP_Settings.instance == null ? null : TMP_Settings.defaultFontAsset;
        }

        private static void Cleanup(params UnityEngine.Object[] objects)
        {
            foreach (var item in objects)
            {
                if (item == null)
                {
                    continue;
                }

                if (item is GameObject gameObject)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(item);
            }
        }

        private static int GetGeneratedVertexCount(SkillTreeRuntimeConnectionGraphic graphic)
        {
            var vertexHelper = new VertexHelper();
            var populateMethod = typeof(SkillTreeRuntimeConnectionGraphic)
                .GetMethod(
                    "OnPopulateMesh",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(VertexHelper) },
                    null);
            populateMethod.Invoke(graphic, new object[] { vertexHelper });
            return vertexHelper.currentVertCount;
        }

        private sealed class TestMetadataProvider : SkillNodeMetadataProviderAsset
        {
            public readonly System.Collections.Generic.Dictionary<string, SkillNodeMetadata> metadataById = new(StringComparer.Ordinal);

            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                return metadataById.TryGetValue(nodeId, out metadata);
            }
        }

        private sealed class TestRuntimeMarker : MonoBehaviour
        {
        }
    }

    public sealed class BareRuntimeNodeView : SkillTreeRuntimeNodeView
    {
        public string BoundNodeId { get; private set; }
        public SkillDefinition LastDefinition { get; private set; }
        public SkillStatusData LastStatus { get; private set; }
        public bool LastSelectionValue { get; private set; }

        protected override void OnDefinitionBound(string nodeId, SkillDefinition definition)
        {
            BoundNodeId = nodeId;
            LastDefinition = definition;
        }

        protected override void OnStatusBound(UserSkillData userSkill, SkillStatusData status)
        {
            LastStatus = status;
        }

        protected override void OnSelectionChanged(bool isSelected)
        {
            LastSelectionValue = isSelected;
        }
    }
}
