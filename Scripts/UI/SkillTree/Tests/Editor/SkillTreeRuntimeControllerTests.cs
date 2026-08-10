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
    public sealed class SkillTreeRuntimeControllerTests
    {
        [Test]
        public void FirstClickOnlyChangesSelection()
        {
            // 첫 클릭은 선택만 바꾸고 레벨이나 재화를 건드리지 않아야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            var nodeTemplate = CreateNodeTemplate();
            var runtimeRoot = CreateRuntimeViewRoot(nodeTemplate, out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(runtimeRoot);
            var snapshot = new SkillTreeSnapshot
            {
                currencyBalance = 20,
                selectedSkillId = "root",
                userSkills = new List<UserSkillState>
                {
                    new() { skillId = "root", level = 1, isUnlocked = true }
                }
            };
            var controller = new SkillTreeRuntimeController(runtimeView, graph, provider, snapshot);

            try
            {
                controller.Initialize();
                runtimeView.TryGetNodeView("child", out var childView);

                childView.ClickButton.onClick.Invoke();

                Assert.That(controller.CurrentSnapshot.selectedSkillId, Is.EqualTo("child"));
                Assert.That(controller.CurrentSnapshot.userSkills.Single(state => state.skillId == "child").level, Is.EqualTo(0));
                Assert.That(controller.CurrentSnapshot.currencyBalance, Is.EqualTo(20));
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void ClickingSelectedNodeAttemptsUpgrade()
        {
            // 이미 선택된 노드를 다시 클릭하면 업그레이드 시도가 실행되어야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            var nodeTemplate = CreateNodeTemplate();
            var runtimeRoot = CreateRuntimeViewRoot(nodeTemplate, out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(runtimeRoot);
            var snapshot = new SkillTreeSnapshot
            {
                currencyBalance = 20,
                selectedSkillId = "child",
                userSkills = new List<UserSkillState>
                {
                    new() { skillId = "root", level = 1, isUnlocked = true }
                }
            };
            var controller = new SkillTreeRuntimeController(runtimeView, graph, provider, snapshot);

            try
            {
                controller.Initialize();
                runtimeView.TryGetNodeView("child", out var childView);

                childView.ClickButton.onClick.Invoke();

                Assert.That(controller.CurrentSnapshot.userSkills.Single(state => state.skillId == "child").level, Is.EqualTo(1));
                Assert.That(controller.CurrentSnapshot.currencyBalance, Is.EqualTo(13));
                Assert.That(controller.CurrentResolvedData.skillStatuses.Single(status => status.skillId == "child").isMaxed, Is.True);
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        [Test]
        public void ApplyResolvedDataDoesNotRunUntilExplicitlyRequested()
        {
            // 브리지는 자동 실행되지 않고 명시적 호출 때만 적용되어야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            var nodeTemplate = CreateNodeTemplate();
            var runtimeRoot = CreateRuntimeViewRoot(nodeTemplate, out var runtimeView, out var connectionGraphic);
            var canvasRoot = WrapInCanvas(runtimeRoot);
            var bridge = new TestRuntimeBridge();
            var controller = new SkillTreeRuntimeController(
                runtimeView,
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = 20, selectedSkillId = "root" },
                bridge);

            try
            {
                controller.Initialize();
                Assert.That(bridge.ApplyCount, Is.EqualTo(0));

                runtimeView.TryGetNodeView("root", out var rootView);
                rootView.ClickButton.onClick.Invoke();
                Assert.That(bridge.ApplyCount, Is.EqualTo(0));

                controller.ApplyResolvedData();
                Assert.That(bridge.ApplyCount, Is.EqualTo(1));
                Assert.That(bridge.LastResolvedData, Is.Not.Null);
            }
            finally
            {
                Cleanup(canvasRoot, provider, nodeTemplate.gameObject);
            }
        }

        // 테스트용 최소 그래프를 구성한다.
        private static SkillTreeGraphData CreateGraph()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("runtime_controller_test");
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(120f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                position = new Vector2(420f, 260f)
            });
            return graph;
        }

        // 컨트롤러 테스트도 기존 메타데이터 provider 기반 authoring 경로를 재사용한다.
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

        // 런타임 프리팹 없이도 노드 표현 테스트가 가능하도록 템플릿을 만든다.
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

        // 실제 런타임 계층 구조와 비슷한 테스트 루트를 구성한다.
        private static GameObject CreateRuntimeViewRoot(
            SkillTreeRuntimeNodeView nodeTemplate,
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

            runtimeView = root.GetComponent<SkillTreeRuntimeView>();
            connectionGraphic = connections.GetComponent<SkillTreeRuntimeConnectionGraphic>();
            AssignReference(runtimeView, "nodePrefab", nodeTemplate);
            AssignReference(runtimeView, "contentRoot", content.GetComponent<RectTransform>());
            AssignReference(runtimeView, "nodeLayer", nodes.GetComponent<RectTransform>());
            AssignReference(runtimeView, "removedNodeLayer", removedNodes.GetComponent<RectTransform>());
            AssignReference(runtimeView, "connectionGraphic", connectionGraphic);
            return root;
        }

        // 캔버스 컨텍스트를 만들어 UI 컴포넌트 갱신이 가능하게 한다.
        private static GameObject WrapInCanvas(GameObject runtimeRoot)
        {
            var canvasRoot = new GameObject("CanvasRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeRoot.transform.SetParent(canvasRoot.transform, false);
            return canvasRoot;
        }

        // 직렬화 필드 할당을 테스트 코드에서 안전하게 처리한다.
        private static void AssignReference<TValue>(UnityEngine.Object target, string fieldName, TValue value)
            where TValue : UnityEngine.Object
        {
            var serializedObject = new UnityEditor.SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        // 테스트 오브젝트를 누수 없이 정리한다.
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

            // 기존 authoring 자산 경로를 흉내 내는 메타데이터 provider다.
            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                return metadataById.TryGetValue(nodeId, out metadata);
            }
        }

        private sealed class TestRuntimeBridge : ISkillTreeRuntimeBridge<ResolvedSkillTreeData>
        {
            public int ApplyCount { get; private set; }

            public ResolvedSkillTreeData LastResolvedData { get; private set; }

            // 명시적 브리지 적용 여부를 추적한다.
            public void Apply(ResolvedSkillTreeData resolved)
            {
                ApplyCount += 1;
                LastResolvedData = resolved;
            }
        }
    }
}
