using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SkillTree.Authoring.Runtime
{
    [ExecuteAlways]
    public sealed class SkillTreeRuntimeConnectionGraphic : MaskableGraphic
    {
        [SerializeField] private float thickness = 4f;
        [SerializeField] private int bezierSegments = 18;

        private readonly List<ConnectionRenderData> _connections = new();
        private bool _refreshPending;

        public int RenderedConnectionCount => _connections.Count;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnDisable()
        {
            _connections.Clear();
            _refreshPending = false;
            base.OnDisable();
        }

        public void Bind(
            SkillTreeGraphData graph,
            IReadOnlyDictionary<string, RectTransform> nodeRects)
        {
            _connections.Clear();

            if (graph != null && nodeRects != null)
            {
                foreach (var node in graph.nodes.Where(candidate => !string.IsNullOrWhiteSpace(candidate.parentId)))
                {
                    if (!nodeRects.TryGetValue(node.id, out var childRect) ||
                        !nodeRects.TryGetValue(node.parentId, out var parentRect))
                    {
                        continue;
                    }

                    _connections.Add(new ConnectionRenderData(parentRect, childRect, node.parentLineType));
                }
            }

            RefreshNow();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_connections.Count == 0)
            {
                return;
            }

            foreach (var connection in _connections)
            {
                if (connection.Parent == null || connection.Child == null)
                {
                    continue;
                }

                var start = ResolveCenter(connection.Parent);
                var end = ResolveCenter(connection.Child);
                if (connection.LineType == SkillTreeConnectionLineType.Straight)
                {
                    DrawStraight(vh, start, end);
                }
                else
                {
                    DrawBezier(vh, start, end);
                }
            }
        }

        private void LateUpdate()
        {
            if (!_refreshPending)
            {
                return;
            }

            _refreshPending = false;
            SetVerticesDirty();
        }

        public void RefreshNow()
        {
            _refreshPending = true;
            SetVerticesDirty();
        }

        private Vector2 ResolveCenter(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * 0.5f;
            return rectTransform.InverseTransformPoint(center);
        }

        private void DrawBezier(VertexHelper vh, Vector2 start, Vector2 end)
        {
            var segmentCount = Mathf.Max(2, bezierSegments);
            var middleX = (start.x + end.x) * 0.5f;
            var controlA = new Vector2(middleX, start.y);
            var controlB = new Vector2(middleX, end.y);
            var previous = start;

            for (var index = 1; index <= segmentCount; index += 1)
            {
                var t = index / (float)segmentCount;
                var current = EvaluateBezier(start, controlA, controlB, end, t);
                DrawSegment(vh, previous, current, color, thickness);
                previous = current;
            }
        }

        private void DrawStraight(VertexHelper vh, Vector2 start, Vector2 end)
        {
            DrawSegment(vh, start, end, color, thickness);
        }

        private static Vector2 EvaluateBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            var u = 1f - t;
            return (u * u * u * p0) +
                   (3f * u * u * t * p1) +
                   (3f * u * t * t * p2) +
                   (t * t * t * p3);
        }

        private static void DrawSegment(VertexHelper vh, Vector2 start, Vector2 end, Color color, float width)
        {
            var direction = (end - start).normalized;
            if (direction.sqrMagnitude <= float.Epsilon)
            {
                return;
            }

            var normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            var index = vh.currentVertCount;

            vh.AddVert(start - normal, color, Vector2.zero);
            vh.AddVert(start + normal, color, Vector2.zero);
            vh.AddVert(end + normal, color, Vector2.zero);
            vh.AddVert(end - normal, color, Vector2.zero);

            vh.AddTriangle(index + 0, index + 1, index + 2);
            vh.AddTriangle(index + 2, index + 3, index + 0);
        }

        private readonly struct ConnectionRenderData
        {
            public ConnectionRenderData(
                RectTransform parent,
                RectTransform child,
                SkillTreeConnectionLineType lineType)
            {
                Parent = parent;
                Child = child;
                LineType = lineType;
            }

            public RectTransform Parent { get; }
            public RectTransform Child { get; }
            public SkillTreeConnectionLineType LineType { get; }
        }
    }
}
