using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring.Runtime;

namespace SkillTree.Authoring.Editor
{
    internal static class SkillTreeRuntimePrefabFactory
    {
        internal const string DefaultRuntimeNodePrefabPath = "Assets/Game/SkillTreeSamples/SkillTreeRuntimeNode.prefab";

        internal static SkillTreeRuntimeNodeView EnsureDefaultNodePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SkillTreeRuntimeNodeView>(DefaultRuntimeNodePrefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder("Assets/Game/SkillTreeSamples");

            var root = new GameObject("SkillTreeRuntimeNode", typeof(RectTransform), typeof(Image), typeof(Button));
            try
            {
                var rect = root.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(220f, 96f);

                var background = root.GetComponent<Image>();
                background.color = new Color(0.16f, 0.18f, 0.22f, 0.96f);
                background.raycastTarget = true;

                var button = root.GetComponent<Button>();
                button.targetGraphic = background;

                var selection = CreateImageChild("Selection", rect, new Color(0.32f, 0.65f, 1f, 0.25f));
                selection.rectTransform.anchorMin = Vector2.zero;
                selection.rectTransform.anchorMax = Vector2.one;
                selection.rectTransform.offsetMin = Vector2.zero;
                selection.rectTransform.offsetMax = Vector2.zero;
                selection.enabled = false;

                var icon = CreateImageChild("Icon", rect, Color.white);
                icon.rectTransform.anchorMin = new Vector2(0f, 1f);
                icon.rectTransform.anchorMax = new Vector2(0f, 1f);
                icon.rectTransform.pivot = new Vector2(0f, 1f);
                icon.rectTransform.sizeDelta = new Vector2(44f, 44f);
                icon.rectTransform.anchoredPosition = new Vector2(12f, -12f);
                icon.enabled = false;

                var name = CreateTextChild("Name", rect, 20f, FontStyles.Bold, TextAlignmentOptions.Left);
                name.rectTransform.anchorMin = new Vector2(0f, 1f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.pivot = new Vector2(0f, 1f);
                name.rectTransform.offsetMin = new Vector2(66f, -44f);
                name.rectTransform.offsetMax = new Vector2(-12f, -12f);
                name.text = "Skill Node";

                var cost = CreateTextChild("Cost", rect, 18f, FontStyles.Normal, TextAlignmentOptions.Right);
                cost.rectTransform.anchorMin = new Vector2(0f, 0f);
                cost.rectTransform.anchorMax = new Vector2(1f, 0f);
                cost.rectTransform.pivot = new Vector2(1f, 0f);
                cost.rectTransform.offsetMin = new Vector2(66f, 12f);
                cost.rectTransform.offsetMax = new Vector2(-12f, 36f);
                cost.text = "0";

                var nodeView = (SkillTreeRuntimeNodeView)root.AddComponent(ResolveSampleNodeViewType());
                AssignSerializedField(nodeView, "clickButton", button);
                AssignSerializedField(nodeView, "backgroundImage", background);
                AssignSerializedField(nodeView, "iconImage", icon);
                AssignSerializedField(nodeView, "nameText", name);
                AssignSerializedField(nodeView, "costText", cost);
                AssignSerializedField(nodeView, "selectedHighlight", selection);

                PrefabUtility.SaveAsPrefabAsset(root, DefaultRuntimeNodePrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<SkillTreeRuntimeNodeView>(DefaultRuntimeNodePrefabPath);
        }

        internal static SkillTreeRuntimeView CreateRuntimeViewPrefab(
            string assetPath,
            SkillTreeGraphData graph,
            SkillNodeMetadataProviderAsset metadataProvider,
            SkillTreeRuntimeNodeView nodePrefab)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("assetPath is required.", nameof(assetPath));
            }

            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (nodePrefab == null)
            {
                throw new ArgumentNullException(nameof(nodePrefab));
            }

            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace("\\", "/"));

            var root = new GameObject("SkillTreeRuntimeView", typeof(RectTransform), typeof(ScrollRect), typeof(SkillTreeRuntimeView));
            try
            {
                var rootRect = root.GetComponent<RectTransform>();
                rootRect.anchorMin = new Vector2(0.5f, 0.5f);
                rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.sizeDelta = new Vector2(1280f, 720f);

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
                viewport.transform.SetParent(root.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
                var viewportImage = viewport.GetComponent<Image>();
                viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
                viewportImage.raycastTarget = true;
                viewport.GetComponent<Mask>().showMaskGraphic = false;

                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(0f, 1f);
                contentRect.pivot = new Vector2(0f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(1600f, 1000f);

                var connections = new GameObject("Connections", typeof(RectTransform), typeof(CanvasRenderer), typeof(SkillTreeRuntimeConnectionGraphic));
                connections.transform.SetParent(content.transform, false);
                var connectionsRect = connections.GetComponent<RectTransform>();
                connectionsRect.anchorMin = new Vector2(0f, 1f);
                connectionsRect.anchorMax = new Vector2(0f, 1f);
                connectionsRect.pivot = new Vector2(0f, 1f);
                connectionsRect.sizeDelta = contentRect.sizeDelta;
                var connectionGraphic = connections.GetComponent<SkillTreeRuntimeConnectionGraphic>();
                connectionGraphic.color = new Color(0.43f, 0.52f, 0.62f, 0.85f);

                var nodeLayer = new GameObject("Nodes", typeof(RectTransform));
                nodeLayer.transform.SetParent(content.transform, false);
                var nodeLayerRect = nodeLayer.GetComponent<RectTransform>();
                nodeLayerRect.anchorMin = new Vector2(0f, 1f);
                nodeLayerRect.anchorMax = new Vector2(0f, 1f);
                nodeLayerRect.pivot = new Vector2(0f, 1f);
                nodeLayerRect.sizeDelta = contentRect.sizeDelta;

                var removedNodeLayer = new GameObject("RemovedNodes", typeof(RectTransform));
                removedNodeLayer.transform.SetParent(content.transform, false);
                var removedNodeLayerRect = removedNodeLayer.GetComponent<RectTransform>();
                removedNodeLayerRect.anchorMin = new Vector2(0f, 1f);
                removedNodeLayerRect.anchorMax = new Vector2(0f, 1f);
                removedNodeLayerRect.pivot = new Vector2(0f, 1f);
                removedNodeLayerRect.sizeDelta = contentRect.sizeDelta;
                removedNodeLayer.SetActive(false);

                var scrollRect = root.GetComponent<ScrollRect>();
                scrollRect.viewport = viewportRect;
                scrollRect.content = contentRect;
                scrollRect.horizontal = true;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 32f;

                var runtimeView = root.GetComponent<SkillTreeRuntimeView>();
                runtimeView.Configure(graph, metadataProvider, nodePrefab, contentRect, nodeLayerRect, connectionGraphic, removedNodeLayerRect);
                EnsureSourceBinding(root).Apply(graph.treeId, ResolveAssetGuid(metadataProvider));
                runtimeView.Build(InstantiateNodePrefab);

                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<SkillTreeRuntimeView>(assetPath);
        }

        internal static SkillTreeRuntimeNodeView InstantiateNodePrefab(
            SkillTreeRuntimeNodeView nodePrefab,
            Transform parent)
        {
            if (nodePrefab == null || parent == null)
            {
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(nodePrefab.gameObject, parent) as GameObject;
            return instance == null ? null : instance.GetComponent<SkillTreeRuntimeNodeView>();
        }

        private static Image CreateImageChild(string name, RectTransform parent, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var image = child.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateTextChild(
            string name,
            RectTransform parent,
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(parent, false);
            var text = child.GetComponent<TextMeshProUGUI>();
            text.font = ResolveDefaultFontAsset();
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_FontAsset ResolveDefaultFontAsset()
        {
            return TMP_Settings.instance == null ? null : TMP_Settings.defaultFontAsset;
        }

        private static void AssignSerializedField<TComponent>(UnityEngine.Object target, string fieldName, TComponent value)
            where TComponent : UnityEngine.Object
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Type ResolveSampleNodeViewType()
        {
            // 기본 프리팹은 샘플 구현을 사용하지만 Editor assembly가 Samples assembly를 정적 참조하지 않게 유지한다.
            var type = Type.GetType("SkillTree.Authoring.Samples.SampleSkillTreeRuntimeNodeView, SkillTree.Samples.Runtime");
            if (type == null || !typeof(SkillTreeRuntimeNodeView).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    "SampleSkillTreeRuntimeNodeView type was not found. Keep SkillTree.Samples.Runtime available or provide a custom runtime node prefab.");
            }

            return type;
        }

        private static SkillTreeRuntimeSourceBinding EnsureSourceBinding(GameObject root)
        {
            // RuntimeView Inspector를 오염시키지 않도록 source stamp는 숨김 컴포넌트에 저장한다.
            var binding = root.GetComponent<SkillTreeRuntimeSourceBinding>();
            if (binding == null)
            {
                binding = root.AddComponent<SkillTreeRuntimeSourceBinding>();
            }

            binding.hideFlags = HideFlags.HideInInspector;
            return binding;
        }

        private static string ResolveAssetGuid(UnityEngine.Object asset)
        {
            var assetPath = asset == null ? null : AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
            var folderName = Path.GetFileName(normalized);
            EnsureFolder(parent);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }
    }
}
