using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring.Runtime;
using SkillTree.Authoring.Samples;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeRuntimeRefreshTests
    {
        [Test]
        public void RefreshUpdatesStatusTextAndSelectionWithoutRebuildingNodes()
        {
            // Refresh는 기존 노드 인스턴스를 재사용하면서 상태만 갱신해야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
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
                    connectionGraphic,
                    FindRect(root, "Viewport/Content/RemovedNodes"));
                runtimeView.Build();

                runtimeView.TryGetNodeView("child", out var childViewBeforeRefresh);
                var resolved = SkillTreeProgressionService.Resolve(
                    graph,
                    provider,
                    new SkillTreeSnapshot
                    {
                        currencyBalance = 20,
                        selectedSkillId = "child",
                        userSkills = new List<UserSkillState>
                        {
                            new() { skillId = "root", level = 1, isUnlocked = true }
                        }
                    });

                runtimeView.Refresh(resolved);

                runtimeView.TryGetNodeView("child", out var childViewAfterRefresh);
                Assert.That(childViewAfterRefresh, Is.SameAs(childViewBeforeRefresh));
                Assert.That(((SampleSkillTreeRuntimeNodeView)childViewAfterRefresh).CostLabel, Is.EqualTo("Open · Lv 0/1 · Cost 7"));
                Assert.That(childViewAfterRefresh.IsSelected, Is.True);
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void RefreshShowsMaxStatusAfterSkillIsUpgraded()
        {
            // 동일한 뷰에서 해금 이후 최대 레벨 상태까지 연속 반영할 수 있어야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
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
                    connectionGraphic,
                    FindRect(root, "Viewport/Content/RemovedNodes"));
                runtimeView.Build();

                runtimeView.Refresh(SkillTreeProgressionService.Resolve(
                    graph,
                    provider,
                    new SkillTreeSnapshot
                    {
                        currencyBalance = 20,
                        selectedSkillId = "root",
                        userSkills = new List<UserSkillState>
                        {
                            new() { skillId = "root", level = 1, isUnlocked = true }
                        }
                    }));

                runtimeView.TryGetNodeView("root", out var rootView);
                Assert.That(((SampleSkillTreeRuntimeNodeView)rootView).CostLabel, Is.EqualTo("Lv 1/1 · Max"));
                Assert.That(rootView.IsSelected, Is.True);
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        // 테스트용 최소 그래프를 만든다.
        private static SkillTreeGraphData CreateGraph()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("runtime_refresh_test");
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(100f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                position = new Vector2(420f, 280f)
            });
            return graph;
        }

        // 정의 provider는 기존 메타데이터 asset 계약을 테스트 더블로 대체한다.
        private static TestMetadataProvider CreateProvider()
        {
            var provider = ScriptableObject.CreateInstance<TestMetadataProvider>();
            provider.metadataById["root"] = new SkillNodeMetadata
            {
                nodeId = "root",
                displayName = "Root Skill",
                cost = 3,
                maxLevel = 1
            };
            provider.metadataById["child"] = new SkillNodeMetadata
            {
                nodeId = "child",
                displayName = "Child Skill",
                cost = 7,
                maxLevel = 1
            };
            return provider;
        }

        // 런타임 뷰와 동일한 형태의 노드 템플릿을 만든다.
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

        // Refresh 테스트용 런타임 뷰 루트를 구성한다.
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

        // 캔버스가 있어야 TMP와 UGUI 갱신이 정상 동작한다.
        private static GameObject WrapInCanvas(GameObject runtimeRoot)
        {
            var canvasRoot = new GameObject("CanvasRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeRoot.transform.SetParent(canvasRoot.transform, false);
            return canvasRoot;
        }

        // 루트에서 특정 RectTransform을 찾는다.
        private static RectTransform FindRect(GameObject root, string path)
        {
            return root.transform.Find(path)?.GetComponent<RectTransform>();
        }

        // 직렬화 필드를 테스트 코드에서 채워 넣는다.
        private static void AssignReference<TValue>(UnityEngine.Object target, string fieldName, TValue value)
            where TValue : UnityEngine.Object
        {
            var serializedObject = new UnityEditor.SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        // 테스트 오브젝트를 정리한다.
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

        private sealed class TestMetadataProvider : SkillNodeMetadataProviderAsset
        {
            public readonly Dictionary<string, SkillNodeMetadata> metadataById = new(StringComparer.Ordinal);

            // authoring metadata provider를 테스트 더블로 제공한다.
            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                return metadataById.TryGetValue(nodeId, out metadata);
            }
        }
    }
}
