using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring.Runtime;
using SkillTree.Authoring.Samples;

namespace SkillTree.Authoring.Tests
{
    public sealed class SampleScriptableObjectIntegrationTests
    {
        private const string SampleRoot = "Assets/Game/SkilTreeMaker/SkillTreeSamples/ScriptableObjectCatalog";
        private const string SampleCatalogPath = SampleRoot + "/ScriptableObjectSkillCatalog.asset";
        private const string SampleProviderPath = SampleRoot + "/ScriptableObjectSkillCatalogProvider.asset";
        private const string SamplePrefabPath = SampleRoot + "/ScriptableObjectSkillTreeSample.prefab";

        [Test]
        public void SampleProviderResolvesAllSampleEntries()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SampleSkillCatalogAsset>(SampleCatalogPath);
            var provider = AssetDatabase.LoadAssetAtPath<SampleSkillCatalogProviderAsset>(SampleProviderPath);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.Catalog, Is.EqualTo(catalog));
            Assert.That(provider.GetKnownSkillIds(), Is.EquivalentTo(catalog.GetKnownSkillIds()));

            foreach (var skill in catalog.Skills)
            {
                Assert.That(provider.TryGetDefinition(skill.skillId, out var definition), Is.True, skill.skillId);
                Assert.That(provider.TryGetMetadata(skill.skillId, out var metadata), Is.True, skill.skillId);
                Assert.That(definition.displayName, Is.EqualTo(skill.displayName));
                Assert.That(metadata.displayName, Is.EqualTo(skill.displayName));
            }
        }

        [Test]
        public void SampleSkillDataContainsOnlyStaticDefinitionFields()
        {
            var fieldNames = typeof(SampleSkillData)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fieldNames, Is.EqualTo(new[]
            {
                "cost",
                "description",
                "displayName",
                "effectSummary",
                "icon",
                "maxLevel",
                "skillId"
            }));
        }

        [Test]
        public void SampleProviderFailsWhenTreeIdMismatches()
        {
            var catalog = ScriptableObject.CreateInstance<SampleSkillCatalogAsset>();
            var provider = ScriptableObject.CreateInstance<SampleSkillCatalogProviderAsset>();
            catalog.Configure("sample_tree", new[]
            {
                CreateSkill("root", "Root", 1, 1)
            });
            provider.BindCatalog(catalog);

            try
            {
                var graph = SkillTreeJsonService.CreateDefaultGraph("other_tree");
                graph.nodes.Add(new SkillTreeNodeRecord { id = "root", position = new Vector2(100f, 100f) });

                Assert.That(
                    () => provider.ValidateGraphOrThrow(graph),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("treeId"));
            }
            finally
            {
                Cleanup(catalog, provider);
            }
        }

        [Test]
        public void SampleProviderFailsWhenGraphContainsUnknownNode()
        {
            var catalog = ScriptableObject.CreateInstance<SampleSkillCatalogAsset>();
            var provider = ScriptableObject.CreateInstance<SampleSkillCatalogProviderAsset>();
            catalog.Configure("sample_tree", new[]
            {
                CreateSkill("root", "Root", 1, 1)
            });
            provider.BindCatalog(catalog);

            try
            {
                var graph = SkillTreeJsonService.CreateDefaultGraph("sample_tree");
                graph.nodes.Add(new SkillTreeNodeRecord { id = "root", position = new Vector2(100f, 100f) });
                graph.nodes.Add(new SkillTreeNodeRecord { id = "childA", parentId = "root", position = new Vector2(300f, 100f) });

                Assert.That(
                    () => provider.ValidateGraphOrThrow(graph),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("childA"));
            }
            finally
            {
                Cleanup(catalog, provider);
            }
        }

        [Test]
        public void BootstrapLoadsDefaultSnapshotWhenSnapshotAssetIsMissing()
        {
            var graph = CreateSampleGraph();
            var catalog = ScriptableObject.CreateInstance<SampleSkillCatalogAsset>();
            var provider = ScriptableObject.CreateInstance<SampleSkillCatalogProviderAsset>();
            catalog.Configure(graph.treeId, CreateSkills().ToArray());
            provider.BindCatalog(catalog);
            var bootstrapRoot = new GameObject("Bootstrap", typeof(SampleSkillTreeBootstrap));
            var bootstrap = bootstrapRoot.GetComponent<SampleSkillTreeBootstrap>();

            try
            {
                AssignReference(bootstrap, "provider", provider);
                var snapshot = bootstrap.LoadSnapshot(graph);

                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.treeId, Is.EqualTo(graph.treeId));
                Assert.That(snapshot.userSkills.Count, Is.EqualTo(graph.nodes.Count));
                Assert.That(snapshot.selectedSkillId, Is.EqualTo("root"));
            }
            finally
            {
                Cleanup(bootstrapRoot, catalog, provider);
            }
        }

        [Test]
        public void BootstrapPreviewBuildsRuntimeViewWithoutController()
        {
            var graph = CreateSampleGraph();
            var graphAsset = new TextAsset(SkillTreeJsonService.Serialize(graph));
            var catalog = ScriptableObject.CreateInstance<SampleSkillCatalogAsset>();
            var provider = ScriptableObject.CreateInstance<SampleSkillCatalogProviderAsset>();
            catalog.Configure(graph.treeId, CreateSkills().ToArray());
            provider.BindCatalog(catalog);
            var nodeTemplate = CreateNodeTemplate();
            var runtimeRoot = CreateRuntimeViewRoot(nodeTemplate, out var runtimeView);
            var canvasRoot = WrapInCanvas(runtimeRoot);
            var previewBridge = runtimeRoot.AddComponent<SampleResolvedSkillTreePreviewBridge>();
            var bootstrap = runtimeRoot.AddComponent<SampleSkillTreeBootstrap>();

            try
            {
                AssignReference(bootstrap, "runtimeView", runtimeView);
                AssignReference(bootstrap, "graphJson", graphAsset);
                AssignReference(bootstrap, "provider", provider);
                AssignReference(bootstrap, "runtimeBridge", previewBridge);

                Assert.That(bootstrap.BuildPreviewNow(), Is.True);

                Assert.That(runtimeView.RenderedNodeCount, Is.EqualTo(graph.nodes.Count));
                Assert.That(bootstrap.InitializationCount, Is.EqualTo(0));
                Assert.That(bootstrap.IsInitialized, Is.False);
                Assert.That(previewBridge.ApplyCount, Is.EqualTo(0));
                Assert.That(bootstrap.CurrentResolvedData, Is.Null);
                Assert.That(() => bootstrap.ApplyResolvedDataNow(), Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                Cleanup(canvasRoot, graphAsset, catalog, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void SamplePrefabIsPrewiredForBootstrapOwnedInitialization()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SamplePrefabPath);

            Assert.That(prefab, Is.Not.Null);

            var runtimeView = prefab.GetComponent<SkillTreeRuntimeView>();
            var bootstrap = prefab.GetComponent<SampleSkillTreeBootstrap>();
            var previewBridge = prefab.GetComponent<SampleResolvedSkillTreePreviewBridge>();

            Assert.That(runtimeView, Is.Not.Null);
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(previewBridge, Is.Not.Null);
            Assert.That(bootstrap.RuntimeView, Is.EqualTo(runtimeView));
            Assert.That(bootstrap.GraphJson, Is.Not.Null);
            Assert.That(bootstrap.SnapshotJson, Is.Not.Null);
            Assert.That(bootstrap.Provider, Is.Not.Null);
            Assert.That(bootstrap.BuildPreviewNow(), Is.True);
            Assert.That(runtimeView.RenderedNodeCount, Is.EqualTo(4));
        }

        private static SkillTreeGraphData CreateSampleGraph()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("scriptableobject_catalog_sample");
            graph.nodes.Add(new SkillTreeNodeRecord { id = "root", position = new Vector2(120f, 120f) });
            graph.nodes.Add(new SkillTreeNodeRecord { id = "childA", parentId = "root", position = new Vector2(440f, 120f) });
            graph.nodes.Add(new SkillTreeNodeRecord { id = "childB", parentId = "root", position = new Vector2(440f, 340f), parentLineType = SkillTreeConnectionLineType.Straight });
            graph.nodes.Add(new SkillTreeNodeRecord { id = "grandchild", parentId = "childA", position = new Vector2(760f, 120f) });
            return graph;
        }

        private static IEnumerable<SampleSkillData> CreateSkills()
        {
            yield return CreateSkill("root", "Root Focus", 3, 1);
            yield return CreateSkill("childA", "Branch Alpha", 5, 2);
            yield return CreateSkill("childB", "Branch Beta", 4, 2);
            yield return CreateSkill("grandchild", "Deep Specialization", 8, 1);
        }

        private static SampleSkillData CreateSkill(string skillId, string displayName, int cost, int maxLevel)
        {
            return new SampleSkillData
            {
                skillId = skillId,
                displayName = displayName,
                description = $"{displayName} description",
                effectSummary = $"{displayName} effect",
                cost = cost,
                maxLevel = maxLevel
            };
        }

        private static SkillTreeRuntimeNodeView CreateNodeTemplate()
        {
            var root = new GameObject("RuntimeNodeTemplate", typeof(RectTransform), typeof(Image), typeof(Button), typeof(SampleSkillTreeRuntimeNodeView));
            root.SetActive(false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 96f);
            var background = root.GetComponent<Image>();
            var button = root.GetComponent<Button>();
            button.targetGraphic = background;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(root.transform, false);
            var name = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            name.transform.SetParent(root.transform, false);
            name.font = TMP_Settings.instance == null ? null : TMP_Settings.defaultFontAsset;
            var cost = new GameObject("Cost", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            cost.transform.SetParent(root.transform, false);
            cost.font = TMP_Settings.instance == null ? null : TMP_Settings.defaultFontAsset;
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
            SkillTreeRuntimeNodeView nodeTemplate,
            out SkillTreeRuntimeView runtimeView)
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

            runtimeView = root.GetComponent<SkillTreeRuntimeView>();
            AssignReference(runtimeView, "nodePrefab", nodeTemplate);
            AssignReference(runtimeView, "contentRoot", content.GetComponent<RectTransform>());
            AssignReference(runtimeView, "nodeLayer", nodes.GetComponent<RectTransform>());
            AssignReference(runtimeView, "removedNodeLayer", removedNodes.GetComponent<RectTransform>());
            AssignReference(runtimeView, "connectionGraphic", connections.GetComponent<SkillTreeRuntimeConnectionGraphic>());
            return root;
        }

        private static GameObject WrapInCanvas(GameObject runtimeRoot)
        {
            var canvasRoot = new GameObject("CanvasRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeRoot.transform.SetParent(canvasRoot.transform, false);
            return canvasRoot;
        }

        private static void AssignReference<TValue>(UnityEngine.Object target, string fieldName, TValue value)
            where TValue : UnityEngine.Object
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
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
    }
}
