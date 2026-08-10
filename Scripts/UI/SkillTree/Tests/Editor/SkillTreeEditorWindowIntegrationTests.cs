using System.Collections;
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using SkillTree.Authoring.Editor;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeEditorWindowIntegrationTests
    {
        private SkillTreeEditorWindow _window;
        private string _treeId;
        private string _assetFolderPath;

        [SetUp]
        public void SetUp()
        {
            _window = EditorWindow.GetWindow<SkillTreeEditorWindow>();
            _window.position = new Rect(80f, 80f, 1800f, 1200f);
            _window.Focus();
            _treeId = $"integration_{Guid.NewGuid():N}";
            _assetFolderPath = $"Assets/Game/SkillTreeData/{_treeId}";
            _window.ControllerForTests.CreateNewGraph(_treeId);
            _window.ControllerForTests.AddNode(new Vector2(100f, 100f));
            _window.ControllerForTests.AddNode(new Vector2(360f, 180f));
            _window.ForceRefreshForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null)
            {
                return;
            }

            _window.Close();
            UnityEngine.Object.DestroyImmediate(_window);
            _window = null;

            if (AssetDatabase.IsValidFolder(_assetFolderPath))
            {
                AssetDatabase.DeleteAsset(_assetFolderPath);
            }
        }

        [UnityTest]
        public IEnumerator LeftClickDragMovesNodePosition()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var element = GetNodeElement(node.id);
            var originalPosition = node.position;
            var start = element.worldBound.center;
            var target = start + new Vector2(160f, 90f);

            DispatchMouseDown(_window.CanvasViewForTests, start, MouseButton.LeftMouse);
            DispatchMouseMove(_window.CanvasViewForTests, start, target, MouseButton.LeftMouse, 4);
            DispatchMouseUp(_window.CanvasViewForTests, target, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            var movedNode = _window.ControllerForTests.Graph.nodes.Single(item => item.id == node.id);
            Assert.That(movedNode.position, Is.Not.EqualTo(originalPosition));
            Assert.That(movedNode.position.x, Is.GreaterThan(originalPosition.x + 50f));
            Assert.That(movedNode.position.y, Is.GreaterThan(originalPosition.y + 40f));
        }

        [UnityTest]
        public IEnumerator ShortLeftClickKeepsNodePosition()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var element = GetNodeElement(node.id);
            var originalPosition = node.position;

            DispatchMouseDown(_window.CanvasViewForTests, element.worldBound.center, MouseButton.LeftMouse);
            DispatchMouseUp(_window.CanvasViewForTests, element.worldBound.center, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            var currentNode = _window.ControllerForTests.Graph.nodes.Single(item => item.id == node.id);
            Assert.That(_window.ControllerForTests.SelectedNodeId, Is.EqualTo(node.id));
            Assert.That(currentNode.position, Is.EqualTo(originalPosition));
        }

        [UnityTest]
        public IEnumerator RightClickOnChildAndMouseUpOnParentReparentsNode()
        {
            yield return WaitForFrames(2);

            var parentNode = _window.ControllerForTests.Graph.nodes[0];
            var childNode = _window.ControllerForTests.Graph.nodes[1];
            var parentElement = GetNodeElement(parentNode.id);
            var childElement = GetNodeElement(childNode.id);

            DispatchMouseDown(_window.CanvasViewForTests, childElement.worldBound.center, MouseButton.RightMouse);
            DispatchMouseUp(_window.CanvasViewForTests, parentElement.worldBound.center, MouseButton.RightMouse);

            yield return WaitForFrames(1);

            var updatedChild = _window.ControllerForTests.Graph.nodes.Single(item => item.id == childNode.id);
            Assert.That(updatedChild.parentId, Is.EqualTo(parentNode.id));
        }

        [UnityTest]
        public IEnumerator ConnectionClickThroughViewportSelectsLineAndShowsConnectionInspector()
        {
            yield return WaitForFrames(2);

            var parentNode = _window.ControllerForTests.Graph.nodes[0];
            var childNode = _window.ControllerForTests.Graph.nodes[1];
            _window.ControllerForTests.SelectNode(childNode.id);
            _window.ControllerForTests.SetSelectedParent(parentNode.id, out _);
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            var connectionPoint = _window.CanvasViewForTests.GetConnectionPointForTests(childNode.id);
            Assert.That(connectionPoint.HasValue, Is.True);
            DispatchMouseDown(_window.CanvasViewportForTests, connectionPoint.Value, MouseButton.LeftMouse);
            DispatchMouseUp(_window.CanvasViewportForTests, connectionPoint.Value, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            Assert.That(_window.ControllerForTests.SelectedNodeId, Is.Null);
            Assert.That(_window.ControllerForTests.SelectedConnectionChildId, Is.EqualTo(childNode.id));
            Assert.That(FindEnumField("Line Type"), Is.Not.Null);
            Assert.That(FindTextField("Child")?.value, Is.EqualTo(childNode.id));
            Assert.That(FindTextField("Parent")?.value, Is.EqualTo(parentNode.id));
        }

        [UnityTest]
        public IEnumerator LegacyJsonLoadConnectionClickThroughViewportSelectsLine()
        {
            yield return WaitForFrames(2);

            var directoryPath = Path.Combine(Application.dataPath, "Game/SkillTreeData", _treeId);
            Directory.CreateDirectory(directoryPath);
            var filePath = Path.Combine(directoryPath, $"{_treeId}.json");
            File.WriteAllText(filePath, @"{
  ""schemaVersion"": 2,
  ""treeId"": ""legacy_click"",
  ""nodes"": [
    {
      ""id"": ""node_001"",
      ""parentId"": """",
      ""position"": { ""x"": 120.0, ""y"": 120.0 }
    },
    {
      ""id"": ""node_002"",
      ""parentId"": ""node_001"",
      ""position"": { ""x"": 500.44647, ""y"": 359.99515 }
    }
  ]
}");

            _window.ControllerForTests.LoadFromFile(filePath);
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            var childNode = _window.ControllerForTests.Graph.nodes.Single(node => node.id == "node_002");
            var connectionPoint = _window.CanvasViewForTests.GetConnectionPointForTests(childNode.id);
            Assert.That(connectionPoint.HasValue, Is.True);
            DispatchMouseDown(_window.CanvasViewportForTests, connectionPoint.Value, MouseButton.LeftMouse);
            DispatchMouseUp(_window.CanvasViewportForTests, connectionPoint.Value, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            Assert.That(_window.ControllerForTests.SelectedNodeId, Is.Null);
            Assert.That(_window.ControllerForTests.SelectedConnectionChildId, Is.EqualTo(childNode.id));
        }

        [UnityTest]
        public IEnumerator CtrlWheelZoomIncreasesZoomScale()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var pointer = GetNodeElement(node.id).worldBound.center;
            var beforeZoom = _window.ZoomScaleForTests;

            DispatchWheel(_window.CanvasViewportForTests, pointer, -60f, EventModifiers.Control);

            yield return WaitForFrames(1);

            Assert.That(_window.ZoomScaleForTests, Is.GreaterThan(beforeZoom));
        }

        [UnityTest]
        public IEnumerator CmdWheelZoomOutDecreasesZoomScale()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var pointer = GetNodeElement(node.id).worldBound.center;
            DispatchWheel(_window.CanvasViewportForTests, pointer, -60f, EventModifiers.Command);

            yield return WaitForFrames(1);

            var zoomedIn = _window.ZoomScaleForTests;
            DispatchWheel(_window.CanvasViewportForTests, pointer, 60f, EventModifiers.Command);

            yield return WaitForFrames(1);

            Assert.That(_window.ZoomScaleForTests, Is.LessThanOrEqualTo(zoomedIn));
        }

        [UnityTest]
        public IEnumerator WheelWithoutModifierKeepsZoomAndScrollsViewport()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var pointer = GetNodeElement(node.id).worldBound.center;
            var beforeZoom = _window.ZoomScaleForTests;
            var beforeScroll = _window.CanvasScrollViewForTests.scrollOffset;

            DispatchWheel(_window.CanvasViewportForTests, pointer, 120f, EventModifiers.None);

            yield return WaitForFrames(1);

            Assert.That(_window.ZoomScaleForTests, Is.EqualTo(beforeZoom));
            Assert.That(_window.CanvasScrollViewForTests.scrollOffset, Is.Not.EqualTo(beforeScroll));
        }

        [UnityTest]
        public IEnumerator PointerAnchoredZoomKeepsNodeNearPointer()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var beforeCenter = GetNodeElement(node.id).worldBound.center;

            DispatchWheel(_window.CanvasViewportForTests, beforeCenter, -60f, EventModifiers.Control);

            yield return WaitForFrames(1);

            var afterCenter = GetNodeElement(node.id).worldBound.center;
            Assert.That(Vector2.Distance(beforeCenter, afterCenter), Is.LessThan(28f));
        }

        [UnityTest]
        public IEnumerator ZoomOutKeepsZoomRootAtLeastViewportSized()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var pointer = GetNodeElement(node.id).worldBound.center;

            DispatchWheel(_window.CanvasViewportForTests, pointer, 120f, EventModifiers.Control);
            DispatchWheel(_window.CanvasViewportForTests, pointer, 120f, EventModifiers.Control);
            DispatchWheel(_window.CanvasViewportForTests, pointer, 120f, EventModifiers.Control);

            yield return WaitForFrames(1);

            var viewportSize = _window.CanvasViewportForTests.layout.size;
            var zoomRootSize = _window.ZoomRootForTests.layout.size;

            Assert.That(zoomRootSize.x, Is.GreaterThanOrEqualTo(viewportSize.x));
            Assert.That(zoomRootSize.y, Is.GreaterThanOrEqualTo(viewportSize.y));
        }

        [UnityTest]
        public IEnumerator EmptySpaceDragPansViewport()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var pointer = GetNodeElement(node.id).worldBound.center;
            DispatchWheel(_window.CanvasViewportForTests, pointer, -60f, EventModifiers.Control);

            yield return WaitForFrames(1);

            var viewport = _window.CanvasViewportForTests;
            var start = viewport.worldBound.center + new Vector2(180f, 120f);
            var target = start + new Vector2(-120f, -80f);
            var beforeScroll = _window.CanvasScrollViewForTests.scrollOffset;

            DispatchMouseDown(viewport, start, MouseButton.LeftMouse);
            DispatchMouseMove(viewport, start, target, MouseButton.LeftMouse, 4);
            DispatchMouseUp(viewport, target, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            Assert.That(_window.CanvasScrollViewForTests.scrollOffset, Is.Not.EqualTo(beforeScroll));
        }

        [UnityTest]
        public IEnumerator CreateMetadataAssetsAttachesProviderAndRendersNodeMetadata()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            var firstNode = _window.ControllerForTests.Graph.nodes[0];
            var metadata = _window.ControllerForTests.GetMetadata(firstNode.id);
            var nodeElement = GetNodeElement(firstNode.id);

            Assert.That(_window.ControllerForTests.MetadataProvider, Is.Not.Null);
            Assert.That(_window.ControllerForTests.GetMetadataCatalog(), Is.Not.Null);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(nodeElement.TitleTextForTests, Is.EqualTo(firstNode.id));
            Assert.That(nodeElement.SubtitleTextForTests, Is.EqualTo("Cost 0  Lv 1"));
            Assert.That(nodeElement.BadgeTextForTests, Is.EqualTo("WARNING"));
        }

        [UnityTest]
        public IEnumerator CreateMetadataAssetsStoresProviderPathInJsonBindings()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(_window.ControllerForTests.Graph.editorBindings.metadataProviderAssetPath, Is.Not.Null.And.Not.Empty);
        }

        [UnityTest]
        public IEnumerator RematchMetadataAddsEntryForNewNode()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var newNode = _window.ControllerForTests.AddNode(new Vector2(620f, 220f));
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(_window.ControllerForTests.GetMetadata(newNode.id), Is.Null);

            _window.ControllerForTests.ReloadMetadata();
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(_window.ControllerForTests.GetMetadata(newNode.id), Is.Not.Null);
            Assert.That(_window.ControllerForTests.LastMetadataSyncReport, Is.Not.Null);
            Assert.That(_window.ControllerForTests.LastMetadataSyncReport.AddedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator InspectorHidesPositionAndMetadataFieldsAndShowsNodeInfoGroup()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var firstNode = _window.ControllerForTests.Graph.nodes[0];
            _window.ControllerForTests.SelectNode(firstNode.id);
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(FindFloatField("Position X"), Is.Null);
            Assert.That(FindFloatField("Position Y"), Is.Null);
            Assert.That(FindObjectField("Provider"), Is.Null);
            Assert.That(FindObjectField("Catalog"), Is.Null);
            Assert.That(HasLabelStartingWith("Match:"), Is.False);
            Assert.That(HasLabelText("Node Info"), Is.True);
            Assert.That(FindTextField("Display Name")?.value, Is.EqualTo(firstNode.id));
            Assert.That(FindTextField("Cost")?.value, Is.EqualTo("0"));
            Assert.That(FindTextField("Max Level")?.value, Is.EqualTo("1"));
            Assert.That(FindTextField("Description")?.value, Is.EqualTo(string.Empty));
            Assert.That(FindTextField("Effect"), Is.Null);
            Assert.That(HasLabelStartingWith("Effect:"), Is.False);
        }

        [UnityTest]
        public IEnumerator InspectorShowsMissingMetadataMessageInsideNodeInfoGroup()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var newNode = _window.ControllerForTests.AddNode(new Vector2(620f, 220f));
            _window.ControllerForTests.SelectNode(newNode.id);
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(HasLabelText("Node Info"), Is.True);
            Assert.That(HasLabelText("연결된 메타데이터가 없습니다."), Is.True);
            Assert.That(FindTextField("Display Name"), Is.Null);
            Assert.That(FindTextField("Cost"), Is.Null);
            Assert.That(FindTextField("Max Level"), Is.Null);
            Assert.That(FindTextField("Description"), Is.Null);
            Assert.That(HasLabelText("Validation"), Is.True);
            Assert.That(FindHelpBoxContaining("메타데이터를 찾을 수 없습니다"), Is.Not.Null);
            Assert.That(FindHelpBoxContaining("메타데이터 아이콘이 비어 있습니다."), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator LeftClickDragStillMovesNodeWhenZoomed()
        {
            yield return WaitForFrames(2);

            var node = _window.ControllerForTests.Graph.nodes[0];
            var element = GetNodeElement(node.id);
            DispatchWheel(_window.CanvasViewportForTests, element.worldBound.center, -60f, EventModifiers.Control);

            yield return WaitForFrames(1);

            element = GetNodeElement(node.id);
            var originalPosition = node.position;
            var start = element.worldBound.center;
            var target = start + new Vector2(180f, 100f);

            DispatchMouseDown(_window.CanvasViewForTests, start, MouseButton.LeftMouse);
            DispatchMouseMove(_window.CanvasViewForTests, start, target, MouseButton.LeftMouse, 4);
            DispatchMouseUp(_window.CanvasViewForTests, target, MouseButton.LeftMouse);

            yield return WaitForFrames(1);

            var movedNode = _window.ControllerForTests.Graph.nodes.Single(item => item.id == node.id);
            Assert.That(movedNode.position, Is.Not.EqualTo(originalPosition));
        }

        [UnityTest]
        public IEnumerator ApplyMetaButtonEnablesWhenRuntimePrefabIsSelected()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var runtimePrefab = CreateRuntimePrefabForWindow();
            _window.SetSelectedRuntimeViewForTests(runtimePrefab);
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            Assert.That(_window.ApplyRuntimeViewMetaButtonForTests.enabledSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator ApplyMetaCancelKeepsPrefabUnchanged()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var runtimePrefab = CreateRuntimePrefabForWindow();
            _window.SetSelectedRuntimeViewForTests(runtimePrefab);
            var addedNode = _window.ControllerForTests.AddNode(new Vector2(700f, 320f));
            _window.ConfirmDialogHandlerForTests = (_, _, _, _) => false;

            _window.ApplyRuntimeViewMetaForTests();
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            var prefabRoot = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(runtimePrefab));
            try
            {
                Assert.That(prefabRoot.transform.Find($"Viewport/Content/Nodes/{addedNode.id}_RuntimeNode"), Is.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [UnityTest]
        public IEnumerator ApplyMetaAcceptUpdatesRuntimePrefab()
        {
            yield return WaitForFrames(2);

            _window.ControllerForTests.CreateAndAttachMetadataAssets();
            var runtimePrefab = CreateRuntimePrefabForWindow();
            _window.SetSelectedRuntimeViewForTests(runtimePrefab);
            var addedNode = _window.ControllerForTests.AddNode(new Vector2(760f, 360f));
            _window.ConfirmDialogHandlerForTests = (_, _, _, _) => true;

            _window.ApplyRuntimeViewMetaForTests();
            _window.ForceRefreshForTests();

            yield return WaitForFrames(1);

            var prefabRoot = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(runtimePrefab));
            try
            {
                Assert.That(prefabRoot.transform.Find($"Viewport/Content/Nodes/{addedNode.id}_RuntimeNode"), Is.Not.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private SkillTreeNodeElement GetNodeElement(string nodeId)
        {
            var element = _window.CanvasViewForTests.GetNodeElementForTests(nodeId);
            Assert.That(element, Is.Not.Null, $"Node element not found for {nodeId}");
            return element;
        }

        private TextField FindTextField(string label)
        {
            return _window.InspectorScrollViewForTests.Query<TextField>().ToList().FirstOrDefault(field => field.label == label);
        }

        private FloatField FindFloatField(string label)
        {
            return _window.InspectorScrollViewForTests.Query<FloatField>().ToList().FirstOrDefault(field => field.label == label);
        }

        private ObjectField FindObjectField(string label)
        {
            return _window.InspectorScrollViewForTests.Query<ObjectField>().ToList().FirstOrDefault(field => field.label == label);
        }

        private EnumField FindEnumField(string label)
        {
            return _window.InspectorScrollViewForTests.Query<EnumField>().ToList().FirstOrDefault(field => field.label == label);
        }

        private bool HasLabelText(string text)
        {
            return _window.InspectorScrollViewForTests.Query<Label>().ToList().Any(label => string.Equals(label.text, text, StringComparison.Ordinal));
        }

        private bool HasLabelStartingWith(string prefix)
        {
            return _window.InspectorScrollViewForTests.Query<Label>().ToList().Any(label =>
                !string.IsNullOrEmpty(label.text) &&
                label.text.StartsWith(prefix, StringComparison.Ordinal));
        }

        private HelpBox FindHelpBoxContaining(string text)
        {
            return _window.InspectorScrollViewForTests.Query<HelpBox>().ToList().FirstOrDefault(box =>
                !string.IsNullOrEmpty(box.text) &&
                box.text.Contains(text, StringComparison.Ordinal));
        }

        private IEnumerator WaitForFrames(int frameCount)
        {
            for (var index = 0; index < frameCount; index++)
            {
                _window.ForceRefreshForTests();
                yield return null;
            }
        }

        private SkillTree.Authoring.Runtime.SkillTreeRuntimeView CreateRuntimePrefabForWindow()
        {
            var nodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            return SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(
                $"{_assetFolderPath}/{_treeId}_RuntimeView.prefab",
                _window.ControllerForTests.Graph,
                _window.ControllerForTests.MetadataProvider,
                nodePrefab);
        }

        private static void DispatchMouseMove(VisualElement target, Vector2 from, Vector2 to, MouseButton button, int steps)
        {
            var stepCount = Mathf.Max(1, steps);
            var previous = from;

            for (var index = 1; index <= stepCount; index++)
            {
                var ratio = index / (float)stepCount;
                var current = Vector2.Lerp(from, to, ratio);
                var imguiEvent = new Event
                {
                    type = EventType.MouseMove,
                    mousePosition = current,
                    delta = current - previous,
                    button = (int)button
                };

                using var mouseEvent = MouseMoveEvent.GetPooled(imguiEvent);
                target.SendEvent(mouseEvent);
                previous = current;
            }
        }

        private static void DispatchMouseUp(VisualElement target, Vector2 position, MouseButton button)
        {
            var imguiEvent = new Event
            {
                type = EventType.MouseUp,
                mousePosition = position,
                button = (int)button,
                clickCount = 1
            };

            using var mouseEvent = MouseUpEvent.GetPooled(imguiEvent);
            target.SendEvent(mouseEvent);
        }

        private static void DispatchMouseDown(VisualElement target, Vector2 position, MouseButton button)
        {
            var imguiEvent = new Event
            {
                type = EventType.MouseDown,
                mousePosition = position,
                button = (int)button,
                clickCount = 1
            };

            using var mouseEvent = MouseDownEvent.GetPooled(imguiEvent);
            target.SendEvent(mouseEvent);
        }

        private static void DispatchWheel(VisualElement target, Vector2 position, float deltaY, EventModifiers modifiers)
        {
            var imguiEvent = new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = position,
                delta = new Vector2(0f, deltaY),
                modifiers = modifiers
            };

            using var wheelEvent = WheelEvent.GetPooled(imguiEvent);
            target.SendEvent(wheelEvent);
        }
    }
}
