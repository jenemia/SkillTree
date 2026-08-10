using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SkillTree.Authoring.Editor
{
    public static class SkillTreeEditorSmokeBridge
    {
        public static string RunLeftDragSmokeTest()
        {
            var window = EditorWindow.GetWindow<SkillTreeEditorWindow>();
            window.position = new Rect(80f, 80f, 1800f, 1200f);
            window.Focus();

            var controller = window.ControllerForTests;
            controller.CreateNewGraph("smoke");
            controller.AddNode(new Vector2(100f, 100f));
            controller.AddNode(new Vector2(360f, 180f));
            window.ForceRefreshForTests();

            var node = controller.Graph.nodes[0];
            var element = window.CanvasViewForTests.GetNodeElementForTests(node.id);
            if (element == null)
            {
                return "fail: node element missing";
            }

            var before = node.position;
            var start = new Vector2(
                node.position.x + SkillTreeNodeElement.NodeWidth * 0.5f,
                node.position.y + SkillTreeNodeElement.NodeHeight * 0.5f);
            var target = start + new Vector2(160f, 90f);
            var beforeSelected = controller.SelectedNodeId;
            var panelState = $"canvasPanel={(window.CanvasViewForTests.panel != null)}; nodePanel={(element.panel != null)}; world={element.worldBound.x:0.##},{element.worldBound.y:0.##},{element.worldBound.width:0.##},{element.worldBound.height:0.##}";

            window.CanvasViewForTests.BeginNodeDragForTests(node.id, start);
            window.CanvasViewForTests.UpdateNodeDragForTests(target);
            window.CanvasViewForTests.EndNodeDragForTests();

            window.ForceRefreshForTests();

            var after = controller.Graph.nodes.First(item => item.id == node.id).position;
            return $"before={before.x:0.##},{before.y:0.##}; after={after.x:0.##},{after.y:0.##}; moved={(after != before)}; beforeSelected={beforeSelected}; selected={controller.SelectedNodeId}; {panelState}";
        }
    }
}
