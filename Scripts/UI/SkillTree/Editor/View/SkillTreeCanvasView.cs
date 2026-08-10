using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SkillTree.Authoring.Editor
{
    public sealed class SkillTreeCanvasView : VisualElement
    {
        private const float DragStartThreshold = 6f;
        private const float ConnectionHitThreshold = 10f;
        private const int ConnectionHitSegments = 18;
        private readonly Dictionary<string, Vector2> _livePositions = new(StringComparer.Ordinal);
        private readonly List<ConnectionVisual> _connectionVisuals = new();
        private Vector2 _contentSize = new(1600f, 1000f);
        private SkillTreeGraphData _graph;
        private string _selectedNodeId;
        private string _selectedConnectionChildId;
        private string _pendingParentNodeId;
        private Vector2 _pendingParentPointerPosition;
        private string _draggingNodeId;
        private Vector2 _dragStartPointerPosition;
        private Vector2 _dragStartNodePosition;
        private bool _isDraggingNode;
        private Dictionary<string, SkillTreeValidationSeverity> _nodeSeverityById = new(StringComparer.Ordinal);
        private SkillNodeMetadataProviderAsset _metadataProvider;

        public event Action<string> NodeSelected;
        public event Action<string> ConnectionSelected;
        public event Action<string, Vector2> NodeMoved;
        public event Action<string> ParentLinkStarted;
        public event Action<string> ParentLinkCompleted;
        public event Action ParentLinkCancelled;

        public Vector2 ContentSize => _contentSize;

        public SkillTreeCanvasView()
        {
            style.position = Position.Relative;
            style.minWidth = 1600f;
            style.minHeight = 1000f;
            style.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<MouseDownEvent>(OnCanvasMouseDown);
            RegisterCallback<MouseMoveEvent>(OnCanvasMouseMove);
            RegisterCallback<MouseUpEvent>(OnCanvasMouseUp);
        }

        public void Render(
            SkillTreeGraphData graph,
            SkillNodeMetadataProviderAsset metadataProvider,
            string selectedNodeId,
            string selectedConnectionChildId,
            string pendingParentNodeId,
            IReadOnlyList<SkillTreeValidationIssue> issues)
        {
            _graph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            _metadataProvider = metadataProvider;
            _selectedNodeId = selectedNodeId;
            _selectedConnectionChildId = selectedConnectionChildId;
            _pendingParentNodeId = pendingParentNodeId;
            _livePositions.Clear();
            foreach (var node in _graph.nodes)
            {
                _livePositions[node.id] = node.position;
            }
            _nodeSeverityById = issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.nodeId))
                .GroupBy(issue => issue.nodeId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Max(issue => issue.severity),
                    StringComparer.Ordinal);

            Clear();
            _connectionVisuals.Clear();

            var maxX = 1600f;
            var maxY = 1000f;
            foreach (var node in _graph.nodes)
            {
                var severity = _nodeSeverityById.TryGetValue(node.id, out var value)
                    ? value
                    : SkillTreeValidationSeverity.Info;
                var hasError = severity == SkillTreeValidationSeverity.Error;
                var hasWarning = severity == SkillTreeValidationSeverity.Warning;
                var metadata = _metadataProvider != null && _metadataProvider.TryGetMetadata(node.id, out var resolved)
                    ? resolved
                    : null;

                var element = new SkillTreeNodeElement(
                    node,
                    metadata,
                    string.Equals(node.id, _selectedNodeId, StringComparison.Ordinal),
                    hasError,
                    hasWarning);
                Add(element);

                maxX = Mathf.Max(maxX, node.position.x + SkillTreeNodeElement.NodeWidth + 240f);
                maxY = Mathf.Max(maxY, node.position.y + SkillTreeNodeElement.NodeHeight + 240f);
            }

            RebuildConnectionVisuals();

            _contentSize = new Vector2(maxX, maxY);
            style.width = maxX;
            style.height = maxY;
            MarkDirtyRepaint();
        }

        public void UpdateSelection(string selectedNodeId, string selectedConnectionChildId)
        {
            _selectedNodeId = selectedNodeId;
            _selectedConnectionChildId = selectedConnectionChildId;
            foreach (var element in Children().OfType<SkillTreeNodeElement>())
            {
                var severity = _nodeSeverityById.TryGetValue(element.NodeId, out var value)
                    ? value
                    : SkillTreeValidationSeverity.Info;
                element.ApplyVisualState(
                    string.Equals(element.NodeId, _selectedNodeId, StringComparison.Ordinal),
                    severity == SkillTreeValidationSeverity.Error,
                    severity == SkillTreeValidationSeverity.Warning);
            }

            RebuildConnectionVisuals();
            MarkDirtyRepaint();
        }

        internal SkillTreeNodeElement GetNodeElementForTests(string nodeId)
        {
            return Children()
                .OfType<SkillTreeNodeElement>()
                .FirstOrDefault(element => string.Equals(element.NodeId, nodeId, StringComparison.Ordinal));
        }

        internal bool IsConnectionSelectedForTests(string childNodeId)
        {
            return string.Equals(_selectedConnectionChildId, childNodeId, StringComparison.Ordinal);
        }

        internal Vector2? GetConnectionPointForTests(string childNodeId)
        {
            var connection = _connectionVisuals.FirstOrDefault(candidate => string.Equals(candidate.ChildId, childNodeId, StringComparison.Ordinal));
            if (connection == null || connection.SampledPoints.Length == 0)
            {
                return null;
            }

            for (var index = connection.SampledPoints.Length / 2; index < connection.SampledPoints.Length; index += 1)
            {
                var worldPoint = this.ChangeCoordinatesTo(panel?.visualTree, connection.SampledPoints[index]);
                if (!IsPointInsideNode(worldPoint))
                {
                    return worldPoint;
                }
            }

            return this.ChangeCoordinatesTo(panel?.visualTree, connection.SampledPoints[connection.SampledPoints.Length / 2]);
        }

        internal string ResolveConnectionChildAtPanelPositionForTests(Vector2 panelPosition)
        {
            return ResolveConnectionAtPanelPosition(panelPosition)?.ChildId;
        }

        internal bool HasConnectionAtPanelPosition(Vector2 panelPosition)
        {
            return ResolveConnectionAtPanelPosition(panelPosition) != null;
        }

        internal void BeginNodeDragForTests(string nodeId, Vector2 pointerPosition)
        {
            NodeSelected?.Invoke(nodeId);
            _draggingNodeId = nodeId;
            _isDraggingNode = false;
            _dragStartPointerPosition = pointerPosition;
            _dragStartNodePosition = ResolveNodePosition(nodeId);
        }

        internal void UpdateNodeDragForTests(Vector2 pointerPosition)
        {
            HandleNodeDrag(pointerPosition, false);
        }

        internal void EndNodeDragForTests()
        {
            CompleteNodeDrag(false);
        }

        private void HandleDragStarted(string nodeId, Vector2 pointerPosition)
        {
            _draggingNodeId = nodeId;
            _isDraggingNode = false;
            _dragStartPointerPosition = PanelToCanvas(pointerPosition);
            _dragStartNodePosition = ResolveNodePosition(nodeId);
            this.CaptureMouse();
        }

        private void HandleParentLinkStarted(string nodeId)
        {
            ClearNodeDragState(false);
            _pendingParentNodeId = nodeId;
            _pendingParentPointerPosition = ResolveCenter(nodeId, ResolveNodePosition(nodeId));
            this.CaptureMouse();
            ParentLinkStarted?.Invoke(nodeId);
            MarkDirtyRepaint();
        }

        private void OnCanvasMouseDown(MouseDownEvent evt)
        {
            var targetNode = ResolveNodeAtPanelPosition(evt.mousePosition);
            if (targetNode == null)
            {
                if (evt.button == 0 && string.IsNullOrWhiteSpace(_pendingParentNodeId))
                {
                    var connection = ResolveConnectionAtPanelPosition(evt.mousePosition);
                    if (connection != null)
                    {
                        ConnectionSelected?.Invoke(connection.ChildId);
                        evt.StopPropagation();
                        return;
                    }

                    ConnectionSelected?.Invoke(null);
                    NodeSelected?.Invoke(null);
                }

                return;
            }

            NodeSelected?.Invoke(targetNode.NodeId);

            if (evt.button == 1)
            {
                HandleParentLinkStarted(targetNode.NodeId);
                evt.StopPropagation();
                return;
            }

            if (evt.button != 0)
            {
                return;
            }

            HandleDragStarted(targetNode.NodeId, evt.mousePosition);
            evt.StopPropagation();
        }

        private void OnCanvasMouseMove(MouseMoveEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(_pendingParentNodeId) && this.HasMouseCapture())
            {
                _pendingParentPointerPosition = PanelToCanvas(evt.mousePosition);
                MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            HandleNodeDrag(evt.mousePosition, true);
            evt.StopPropagation();
        }

        private void OnCanvasMouseUp(MouseUpEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(_pendingParentNodeId) && this.HasMouseCapture())
            {
                this.ReleaseMouse();

                var targetNode = ResolveNodeAtPanelPosition(evt.mousePosition);
                _pendingParentPointerPosition = Vector2.zero;

                if (targetNode == null)
                {
                    _pendingParentNodeId = null;
                    ParentLinkCancelled?.Invoke();
                    MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }

                _pendingParentNodeId = null;
                ParentLinkCompleted?.Invoke(targetNode.NodeId);
                MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }

            if (CompleteNodeDrag(true))
            {
                evt.StopPropagation();
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (_graph == null)
            {
                return;
            }

            var lookup = _graph.nodes.ToDictionary(node => node.id, node => node, StringComparer.Ordinal);
            var painter = context.painter2D;

            foreach (var connection in _connectionVisuals)
            {
                painter.strokeColor = connection.Color;
                painter.lineWidth = connection.LineWidth;
                painter.BeginPath();
                painter.MoveTo(connection.Start);

                if (connection.LineType == SkillTreeConnectionLineType.Straight)
                {
                    painter.LineTo(connection.End);
                }
                else
                {
                    painter.BezierCurveTo(connection.ControlA, connection.ControlB, connection.End);
                }

                painter.Stroke();
            }

            if (!string.IsNullOrWhiteSpace(_pendingParentNodeId))
            {
                if (!lookup.TryGetValue(_pendingParentNodeId, out var pendingParent))
                {
                    return;
                }

                var fromPos = ResolveCenter(_pendingParentNodeId, pendingParent.position);
                painter.strokeColor = new Color(0.35f, 0.72f, 1f, 0.9f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(fromPos);
                var mid = new Vector2((fromPos.x + _pendingParentPointerPosition.x) * 0.5f, (fromPos.y + _pendingParentPointerPosition.y) * 0.5f);
                painter.BezierCurveTo(
                    new Vector2(mid.x, fromPos.y),
                    new Vector2(mid.x, _pendingParentPointerPosition.y),
                    _pendingParentPointerPosition);
                painter.Stroke();
            }
        }

        private Vector2 ResolveCenter(string nodeId, Vector2 fallbackPosition)
        {
            var position = _livePositions.TryGetValue(nodeId, out var livePosition)
                ? livePosition
                : fallbackPosition;
            return new Vector2(
                position.x + SkillTreeNodeElement.NodeWidth * 0.5f,
                position.y + SkillTreeNodeElement.NodeHeight * 0.5f);
        }

        private Vector2 ResolveNodePosition(string nodeId)
        {
            if (_livePositions.TryGetValue(nodeId, out var livePosition))
            {
                return livePosition;
            }

            return _graph?.nodes.FirstOrDefault(node => string.Equals(node.id, nodeId, StringComparison.Ordinal))?.position ?? Vector2.zero;
        }

        private SkillTreeNodeElement ResolveNodeAtPanelPosition(Vector2 panelPosition)
        {
            var picked = panel?.Pick(panelPosition);
            while (picked != null)
            {
                if (picked is SkillTreeNodeElement nodeElement)
                {
                    return nodeElement;
                }

                picked = picked.parent;
            }

            return null;
        }

        private ConnectionVisual ResolveConnectionAtPanelPosition(Vector2 panelPosition)
        {
            var canvasPosition = PanelToCanvas(panelPosition);
            var bestDistanceSqr = ConnectionHitThreshold * ConnectionHitThreshold;
            ConnectionVisual bestVisual = null;

            foreach (var connection in _connectionVisuals)
            {
                var distanceSqr = ComputeConnectionDistanceSqr(connection, canvasPosition);
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                bestVisual = connection;
            }

            return bestVisual;
        }

        private bool IsPointInsideNode(Vector2 panelPosition)
        {
            return Children()
                .OfType<SkillTreeNodeElement>()
                .Any(element => element.worldBound.Contains(panelPosition));
        }

        private void HandleNodeDrag(Vector2 pointerPosition, bool requireCapture)
        {
            if (string.IsNullOrWhiteSpace(_draggingNodeId))
            {
                return;
            }

            if (requireCapture && !this.HasMouseCapture())
            {
                return;
            }

            var canvasPointerPosition = PanelToCanvas(pointerPosition);
            var delta = canvasPointerPosition - _dragStartPointerPosition;
            if (!_isDraggingNode)
            {
                if (delta.sqrMagnitude < DragStartThreshold * DragStartThreshold)
                {
                    return;
                }

                _isDraggingNode = true;
            }

            var nextPosition = new Vector2(
                Mathf.Max(20f, _dragStartNodePosition.x + delta.x),
                Mathf.Max(20f, _dragStartNodePosition.y + delta.y));

            _livePositions[_draggingNodeId] = nextPosition;
            ResolveNodeElement(_draggingNodeId)?.ApplyPosition(nextPosition);
            RebuildConnectionVisuals();
            MarkDirtyRepaint();
        }

        private bool CompleteNodeDrag(bool requireCapture)
        {
            if (string.IsNullOrWhiteSpace(_draggingNodeId))
            {
                return false;
            }

            if (requireCapture && !this.HasMouseCapture())
            {
                return false;
            }

            var movedNodeId = _draggingNodeId;
            var didDrag = _isDraggingNode;
            var finalPosition = ResolveNodePosition(movedNodeId);
            ClearNodeDragState(true);

            if (didDrag)
            {
                NodeMoved?.Invoke(movedNodeId, finalPosition);
            }
            return true;
        }

        private SkillTreeNodeElement ResolveNodeElement(string nodeId)
        {
            return Children()
                .OfType<SkillTreeNodeElement>()
                .FirstOrDefault(element => string.Equals(element.NodeId, nodeId, StringComparison.Ordinal));
        }

        private void ClearNodeDragState(bool releaseMouse)
        {
            if (releaseMouse && this.HasMouseCapture())
            {
                this.ReleaseMouse();
            }

            _draggingNodeId = null;
            _dragStartPointerPosition = Vector2.zero;
            _dragStartNodePosition = Vector2.zero;
            _isDraggingNode = false;
        }

        private Color ResolveEdgeColor(string childNodeId)
        {
            if (string.Equals(childNodeId, _selectedConnectionChildId, StringComparison.Ordinal))
            {
                return new Color(0.2f, 0.82f, 1f, 0.98f);
            }

            if (string.Equals(childNodeId, _selectedNodeId, StringComparison.Ordinal))
            {
                return new Color(0.35f, 0.72f, 1f);
            }

            if (_nodeSeverityById.TryGetValue(childNodeId, out var severity))
            {
                if (severity == SkillTreeValidationSeverity.Error)
                {
                    return new Color(0.92f, 0.32f, 0.28f, 0.9f);
                }

                if (severity == SkillTreeValidationSeverity.Warning)
                {
                    return new Color(1f, 0.68f, 0.22f, 0.85f);
                }
            }

            return new Color(0.43f, 0.52f, 0.62f, 0.75f);
        }

        private Vector2 PanelToCanvas(Vector2 panelPosition)
        {
            return panel?.visualTree?.ChangeCoordinatesTo(this, panelPosition) ?? panelPosition;
        }

        private void RebuildConnectionVisuals()
        {
            _connectionVisuals.Clear();
            if (_graph == null)
            {
                return;
            }

            var lookup = _graph.nodes.ToDictionary(node => node.id, node => node, StringComparer.Ordinal);
            foreach (var node in _graph.nodes.Where(candidate => !string.IsNullOrWhiteSpace(candidate.parentId)))
            {
                if (!lookup.TryGetValue(node.parentId, out var parent))
                {
                    continue;
                }

                var start = ResolveCenter(parent.id, parent.position);
                var end = ResolveCenter(node.id, node.position);
                var controlA = ResolveBezierControlA(start, end);
                var controlB = ResolveBezierControlB(start, end);
                var lineType = node.parentLineType;
                var sampledPoints = lineType == SkillTreeConnectionLineType.Straight
                    ? new[] { start, end }
                    : BuildBezierSamples(start, controlA, controlB, end, ConnectionHitSegments);

                _connectionVisuals.Add(new ConnectionVisual(
                    node.id,
                    start,
                    end,
                    controlA,
                    controlB,
                    lineType,
                    ResolveEdgeColor(node.id),
                    string.Equals(node.id, _selectedConnectionChildId, StringComparison.Ordinal) ? 5f : 3f,
                    sampledPoints));
            }
        }

        private static Vector2 ResolveBezierControlA(Vector2 start, Vector2 end)
        {
            var midX = (start.x + end.x) * 0.5f;
            return new Vector2(midX, start.y);
        }

        private static Vector2 ResolveBezierControlB(Vector2 start, Vector2 end)
        {
            var midX = (start.x + end.x) * 0.5f;
            return new Vector2(midX, end.y);
        }

        private static Vector2[] BuildBezierSamples(Vector2 start, Vector2 controlA, Vector2 controlB, Vector2 end, int segmentCount)
        {
            var safeSegmentCount = Mathf.Max(2, segmentCount);
            var points = new Vector2[safeSegmentCount + 1];
            for (var index = 0; index <= safeSegmentCount; index += 1)
            {
                var t = index / (float)safeSegmentCount;
                points[index] = EvaluateBezier(start, controlA, controlB, end, t);
            }

            return points;
        }

        private static float ComputeConnectionDistanceSqr(ConnectionVisual connection, Vector2 point)
        {
            var bestDistance = float.MaxValue;
            for (var index = 1; index < connection.SampledPoints.Length; index += 1)
            {
                var segmentDistance = DistanceToSegmentSqr(point, connection.SampledPoints[index - 1], connection.SampledPoints[index]);
                if (segmentDistance < bestDistance)
                {
                    bestDistance = segmentDistance;
                }
            }

            return bestDistance;
        }

        private static float DistanceToSegmentSqr(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= Mathf.Epsilon)
            {
                return (point - start).sqrMagnitude;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
            var projection = start + (segment * t);
            return (point - projection).sqrMagnitude;
        }

        private static Vector2 EvaluateBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            var u = 1f - t;
            return (u * u * u * p0) +
                   (3f * u * u * t * p1) +
                   (3f * u * t * t * p2) +
                   (t * t * t * p3);
        }

        private sealed class ConnectionVisual
        {
            public ConnectionVisual(
                string childId,
                Vector2 start,
                Vector2 end,
                Vector2 controlA,
                Vector2 controlB,
                SkillTreeConnectionLineType lineType,
                Color color,
                float lineWidth,
                Vector2[] sampledPoints)
            {
                ChildId = childId;
                Start = start;
                End = end;
                ControlA = controlA;
                ControlB = controlB;
                LineType = lineType;
                Color = color;
                LineWidth = lineWidth;
                SampledPoints = sampledPoints;
            }

            public string ChildId { get; }
            public Vector2 Start { get; }
            public Vector2 End { get; }
            public Vector2 ControlA { get; }
            public Vector2 ControlB { get; }
            public SkillTreeConnectionLineType LineType { get; }
            public Color Color { get; }
            public float LineWidth { get; }
            public Vector2[] SampledPoints { get; }
        }
    }
}
