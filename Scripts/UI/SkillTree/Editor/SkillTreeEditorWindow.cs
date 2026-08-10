using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SkillTree.Authoring.Runtime;

namespace SkillTree.Authoring.Editor
{
    public sealed class SkillTreeEditorWindow : EditorWindow
    {
        private static readonly Color CanvasBackgroundColor = new(0.11f, 0.12f, 0.14f);
        private const float DefaultZoomScale = 1f;
        private const float MinZoomScale = 0.4f;
        private const float MaxZoomScale = 2f;
        private const float ZoomStepFactor = 1.15f;

        private SkillTreeEditorController _controller;
        private SkillTreeCanvasView _canvasView;
        private ScrollView _canvasScrollView;
        private VisualElement _canvasViewport;
        private VisualElement _zoomRoot;
        private ScrollView _inspectorScrollView;
        private ObjectField _providerField;
        private ObjectField _runtimeViewField;
        private Button _createMetadataButton;
        private Button _rematchMetadataButton;
        private Button _createDefaultRuntimeNodePrefabButton;
        private Button _createRuntimeViewPrefabButton;
        private Button _applyRuntimeViewMetaButton;
        private VisualElement _authoringToolbarRow;
        private VisualElement _runtimeToolbarRow;
        private Label _fileLabel;
        private Label _statusLabel;
        private Label _zoomLabel;
        [SerializeField] private bool _hasInitializedAuthoringGraph;
        [SerializeField] private SkillTreeRuntimeView _selectedRuntimeViewPrefab;
        private float _zoomScale = DefaultZoomScale;
        private bool _isCanvasWheelHandlerRegistered;
        private bool _isPanningViewport;
        private Vector2 _panStartPointerPosition;
        private Vector2 _panStartScrollOffset;

        internal SkillTreeEditorController ControllerForTests => _controller;
        internal SkillTreeCanvasView CanvasViewForTests => _canvasView;
        internal ScrollView CanvasScrollViewForTests => _canvasScrollView;
        internal VisualElement CanvasViewportForTests => _canvasViewport;
        internal VisualElement ZoomRootForTests => _zoomRoot;
        internal ScrollView InspectorScrollViewForTests => _inspectorScrollView;
        internal float ZoomScaleForTests => _zoomScale;
        internal Button ApplyRuntimeViewMetaButtonForTests => _applyRuntimeViewMetaButton;
        internal Func<string, string, string, string, bool> ConfirmDialogHandlerForTests { get; set; }
        internal Action<string, string, string> AlertDialogHandlerForTests { get; set; }

        [MenuItem("Window/SkillTree/Skill Tree Editor")]
        public static void Open()
        {
            var window = GetWindow<SkillTreeEditorWindow>();
            window.titleContent = new GUIContent("Skill Tree");
            window.minSize = new Vector2(1100f, 700f);
        }

        private void OnEnable()
        {
            _controller ??= new SkillTreeEditorController();
            _controller.StateChanged -= OnStateChanged;
            _controller.StateChanged += OnStateChanged;
            BuildUI();
            RefreshUI(SkillTreeEditorChangeKind.All);
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.StateChanged -= OnStateChanged;
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            rootVisualElement.Add(BuildToolbar());

            var splitView = new TwoPaneSplitView(0, 860, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;

            _canvasScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _canvasScrollView.style.flexGrow = 1f;
            _canvasScrollView.style.backgroundColor = CanvasBackgroundColor;
            _zoomRoot = new VisualElement();
            _zoomRoot.style.position = Position.Relative;
            _zoomRoot.style.backgroundColor = CanvasBackgroundColor;
            _canvasView = new SkillTreeCanvasView();
            _canvasView.style.transformOrigin = new TransformOrigin(
                new Length(0f, LengthUnit.Percent),
                new Length(0f, LengthUnit.Percent),
                0f);
            _canvasView.NodeSelected += id => _controller.SelectNode(id);
            _canvasView.ConnectionSelected += childId => _controller.SelectConnection(childId);
            _canvasView.NodeMoved += HandleNodeMoved;
            _canvasView.ParentLinkStarted += HandleParentLinkStarted;
            _canvasView.ParentLinkCompleted += HandleParentLinkCompleted;
            _canvasView.ParentLinkCancelled += HandleParentLinkCancelled;
            _zoomRoot.Add(_canvasView);
            _canvasScrollView.Add(_zoomRoot);
            EnsureCanvasViewport();
            ApplyZoomLayout(false);

            _inspectorScrollView = new ScrollView();
            _inspectorScrollView.style.minWidth = 320f;
            _inspectorScrollView.style.paddingLeft = 12f;
            _inspectorScrollView.style.paddingRight = 12f;
            _inspectorScrollView.style.paddingTop = 10f;

            splitView.Add(_canvasScrollView);
            splitView.Add(_inspectorScrollView);
            rootVisualElement.Add(splitView);
        }

        internal void ForceRefreshForTests()
        {
            RefreshUI(SkillTreeEditorChangeKind.All);
            _canvasView?.MarkDirtyRepaint();
            rootVisualElement.MarkDirtyRepaint();
            Repaint();
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Column;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            toolbar.style.paddingTop = 6f;
            toolbar.style.paddingBottom = 6f;
            toolbar.style.backgroundColor = new Color(0.16f, 0.17f, 0.19f);

            var primaryRow = BuildToolbarRow();
            primaryRow.Add(MakeButton("New", CreateNewGraph));
            primaryRow.Add(MakeButton("Load JSON", LoadJson));
            primaryRow.Add(MakeButton("Save", SaveCurrent));
            primaryRow.Add(MakeButton("Save As", SaveAs));

            var statusSpacer = new VisualElement();
            statusSpacer.style.flexGrow = 1f;
            primaryRow.Add(statusSpacer);

            _fileLabel = new Label();
            _fileLabel.style.minWidth = 180f;
            _fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _fileLabel.style.color = new Color(0.86f, 0.89f, 0.95f);
            primaryRow.Add(_fileLabel);

            _statusLabel = new Label();
            _statusLabel.style.minWidth = 260f;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            primaryRow.Add(_statusLabel);

            _zoomLabel = new Label();
            _zoomLabel.style.minWidth = 64f;
            _zoomLabel.style.marginLeft = 8f;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _zoomLabel.style.color = new Color(0.86f, 0.89f, 0.95f);
            primaryRow.Add(_zoomLabel);
            toolbar.Add(primaryRow);

            _authoringToolbarRow = BuildToolbarRow();
            _authoringToolbarRow.Add(MakeButton("Add Node", () => _controller.AddNode()));
            _authoringToolbarRow.Add(MakeButton("Delete Node", DeleteSelectedNode));
            _createMetadataButton = MakeButton("Create Metadata Assets", () => _controller.CreateAndAttachMetadataAssets());
            _authoringToolbarRow.Add(_createMetadataButton);
            _rematchMetadataButton = MakeButton("Refresh Metadata", () => _controller.ReloadMetadata());
            _authoringToolbarRow.Add(_rematchMetadataButton);

            _providerField = new ObjectField("Metadata")
            {
                objectType = typeof(SkillNodeMetadataProviderAsset),
                allowSceneObjects = false
            };
            _providerField.style.minWidth = 240f;
            _providerField.RegisterValueChangedCallback(evt =>
            {
                _controller.SetMetadataProvider(evt.newValue as SkillNodeMetadataProviderAsset);
            });
            _authoringToolbarRow.Add(_providerField);
            toolbar.Add(_authoringToolbarRow);

            _runtimeToolbarRow = BuildToolbarRow();
            _createDefaultRuntimeNodePrefabButton = MakeButton("Create Default Runtime Node", CreateDefaultRuntimeNodePrefab);
            _runtimeToolbarRow.Add(_createDefaultRuntimeNodePrefabButton);

            _createRuntimeViewPrefabButton = MakeButton("Create Runtime View Prefab", CreateRuntimeViewPrefab);
            _runtimeToolbarRow.Add(_createRuntimeViewPrefabButton);

            _runtimeViewField = new ObjectField("Runtime View")
            {
                objectType = typeof(SkillTreeRuntimeView),
                allowSceneObjects = false
            };
            _runtimeViewField.style.minWidth = 280f;
            _runtimeViewField.RegisterValueChangedCallback(evt =>
            {
                _selectedRuntimeViewPrefab = evt.newValue as SkillTreeRuntimeView;
                RefreshToolbar();
            });
            _runtimeToolbarRow.Add(_runtimeViewField);

            _applyRuntimeViewMetaButton = MakeButton("Apply Meta", ApplyRuntimeViewMeta);
            _runtimeToolbarRow.Add(_applyRuntimeViewMetaButton);
            toolbar.Add(_runtimeToolbarRow);

            return toolbar;
        }

        private void OnStateChanged(SkillTreeEditorChangeKind changeKind)
        {
            RefreshUI(changeKind);
        }

        private void RefreshUI(SkillTreeEditorChangeKind changeKind)
        {
            if ((changeKind & (SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.File | SkillTreeEditorChangeKind.Status | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.All)) != 0)
            {
                _providerField?.SetValueWithoutNotify(_controller.MetadataProvider);
                _runtimeViewField?.SetValueWithoutNotify(_selectedRuntimeViewPrefab);
                RefreshToolbar();
            }

            var requiresCanvasRender = (changeKind & (SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.All)) != 0;
            if (requiresCanvasRender)
            {
                _canvasView?.Render(
                    _controller.Graph,
                    _controller.MetadataProvider,
                    _controller.SelectedNodeId,
                    _controller.SelectedConnectionChildId,
                    _controller.PendingChildNodeId,
                    _controller.ValidationIssues);
                ApplyZoomLayout(false);
            }
            else if ((changeKind & SkillTreeEditorChangeKind.Selection) != 0)
            {
                _canvasView?.UpdateSelection(_controller.SelectedNodeId, _controller.SelectedConnectionChildId);
            }

            if ((changeKind & (SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.All)) != 0)
            {
                RebuildInspector();
            }
        }

        private void RefreshToolbar()
        {
            var fileName = string.IsNullOrWhiteSpace(_controller.CurrentFilePath)
                ? "Unsaved"
                : Path.GetFileName(_controller.CurrentFilePath);
            _fileLabel.text = $"File: {fileName}";
            if (_zoomLabel != null)
            {
                _zoomLabel.text = $"{Mathf.RoundToInt(_zoomScale * 100f)}%";
            }
            _createMetadataButton?.SetEnabled(_controller.MetadataProvider == null);
            _rematchMetadataButton?.SetEnabled(_controller.HasCatalogBackedMetadataProvider());
            _createDefaultRuntimeNodePrefabButton?.SetEnabled(true);
            _createRuntimeViewPrefabButton?.SetEnabled(true);
            _applyRuntimeViewMetaButton?.SetEnabled(
                _selectedRuntimeViewPrefab != null &&
                !string.IsNullOrWhiteSpace(_controller.MetadataProviderAssetGuid) &&
                AssetDatabase.Contains(_selectedRuntimeViewPrefab));
            if (_authoringToolbarRow != null)
            {
                _authoringToolbarRow.style.display = _hasInitializedAuthoringGraph
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            if (_runtimeToolbarRow != null)
            {
                _runtimeToolbarRow.style.display = _hasInitializedAuthoringGraph
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (!string.IsNullOrWhiteSpace(_controller.PendingChildNodeId))
            {
                _statusLabel.text = $"Parent Link: {_controller.PendingChildNodeId} -> ?";
                _statusLabel.style.color = new Color(0.35f, 0.72f, 1f);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_controller.StatusMessage))
            {
                _statusLabel.text = _controller.StatusMessage;
                _statusLabel.style.color = ResolveStatusColor(_controller.StatusType);
                return;
            }

            var errorCount = _controller.ValidationIssues.Count(issue => issue.severity == SkillTreeValidationSeverity.Error);
            var warningCount = _controller.ValidationIssues.Count(issue => issue.severity == SkillTreeValidationSeverity.Warning);
            _statusLabel.text = $"Errors {errorCount} / Warnings {warningCount}";
            _statusLabel.style.color = errorCount > 0
                ? new Color(0.92f, 0.35f, 0.28f)
                : warningCount > 0
                    ? new Color(1f, 0.7f, 0.25f)
                    : new Color(0.45f, 0.88f, 0.55f);
        }

        private static VisualElement BuildToolbarRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;
            return row;
        }

        private void RebuildInspector()
        {
            _inspectorScrollView.Clear();

            var title = new Label("Inspector");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16f;
            title.style.marginBottom = 10f;
            _inspectorScrollView.Add(title);

            var selectedConnection = _controller.GetSelectedConnectionNode();
            if (selectedConnection != null)
            {
                RebuildConnectionInspector(selectedConnection);
                return;
            }

            var selectedNode = _controller.GetSelectedNode();
            if (selectedNode == null)
            {
                _inspectorScrollView.Add(new HelpBox("노드를 선택하면 세부 정보를 편집할 수 있습니다.", HelpBoxMessageType.Info));
                AddValidationList(_inspectorScrollView, string.Empty);
                return;
            }

            AddTextField("ID", selectedNode.id, value =>
            {
                if (!_controller.RenameSelectedNode(value))
                {
                    _controller.SelectNode(selectedNode.id);
                }
            }, true);

            var parentOptions = _controller.Graph.nodes
                .Where(node => !string.Equals(node.id, selectedNode.id, StringComparison.Ordinal))
                .Select(node => node.id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            parentOptions.Insert(0, "<Root>");

            var currentParent = string.IsNullOrWhiteSpace(selectedNode.parentId) ? "<Root>" : selectedNode.parentId;
            if (!parentOptions.Contains(currentParent))
            {
                parentOptions.Add(currentParent);
            }
            var parentField = new PopupField<string>("Parent", parentOptions, currentParent);
            parentField.RegisterValueChangedCallback(evt =>
            {
                var targetParent = evt.newValue == "<Root>" ? null : evt.newValue;
                _controller.SetSelectedParent(targetParent, out _);
            });
            _inspectorScrollView.Add(parentField);

            var metadata = _controller.GetMetadata(selectedNode.id);
            var nodeInfoGroup = CreateInspectorGroup("Node Info");

            if (metadata == null)
            {
                var missingMetadataLabel = new Label("연결된 메타데이터가 없습니다.");
                missingMetadataLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                missingMetadataLabel.style.color = new Color(0.83f, 0.85f, 0.9f);
                nodeInfoGroup.Add(missingMetadataLabel);
            }
            else
            {
                AddReadOnlyTextField(nodeInfoGroup, "Display Name", metadata.displayName);
                AddReadOnlyTextField(nodeInfoGroup, "Cost", metadata.cost.ToString());
                AddReadOnlyTextField(nodeInfoGroup, "Max Level", metadata.maxLevel.ToString());
                AddReadOnlyTextField(nodeInfoGroup, "Description", metadata.description);
            }

            _inspectorScrollView.Add(nodeInfoGroup);

            AddValidationList(_inspectorScrollView, selectedNode.id);
        }

        private void RebuildConnectionInspector(SkillTreeNodeRecord selectedConnection)
        {
            var title = new Label("Connection");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15f;
            title.style.marginBottom = 10f;
            _inspectorScrollView.Add(title);

            var parentNode = string.IsNullOrWhiteSpace(selectedConnection.parentId)
                ? null
                : _controller.Graph.nodes.FirstOrDefault(node => string.Equals(node.id, selectedConnection.parentId, StringComparison.Ordinal));

            AddReadOnlyTextField("Child", selectedConnection.id);
            AddReadOnlyTextField("Parent", parentNode?.id ?? "<Root>");

            var lineTypeField = new EnumField("Line Type", selectedConnection.parentLineType);
            lineTypeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillTreeConnectionLineType lineType)
                {
                    _controller.SetSelectedConnectionLineType(lineType);
                }
            });
            _inspectorScrollView.Add(lineTypeField);

            AddValidationList(_inspectorScrollView, selectedConnection.id);
        }

        private void AddValidationList(VisualElement container, string nodeId)
        {
            var issues = string.IsNullOrWhiteSpace(nodeId)
                ? _controller.ValidationIssues.Where(issue => string.IsNullOrWhiteSpace(issue.nodeId)).ToList()
                : _controller.GetIssuesForNode(nodeId);
            if (issues.Count == 0)
            {
                return;
            }

            var header = new Label("Validation");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 14f;
            container.Add(header);

            foreach (var issue in issues)
            {
                container.Add(new HelpBox(issue.message, ToHelpBoxType(issue.severity)));
            }
        }

        private void AddTextField(string label, string value, Action<string> onChanged, bool delayed = true)
        {
            var field = new TextField(label)
            {
                value = value ?? string.Empty,
                isDelayed = delayed
            };
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            _inspectorScrollView.Add(field);
        }

        private static VisualElement CreateInspectorGroup(string title)
        {
            var group = new Box();
            group.style.marginTop = 14f;
            group.style.paddingTop = 8f;
            group.style.paddingBottom = 8f;
            group.style.paddingLeft = 8f;
            group.style.paddingRight = 8f;

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 6f;
            group.Add(header);
            return group;
        }

        private void AddReadOnlyObjectField(string label, Type objectType, UnityEngine.Object value)
        {
            var field = new ObjectField(label)
            {
                objectType = objectType,
                allowSceneObjects = false,
                value = value
            };
            field.SetEnabled(false);
            _inspectorScrollView.Add(field);
        }

        private void AddReadOnlyTextField(string label, string value)
        {
            AddReadOnlyTextField(_inspectorScrollView, label, value);
        }

        private static void AddReadOnlyTextField(VisualElement container, string label, string value)
        {
            var field = new TextField(label)
            {
                value = value ?? string.Empty,
                isReadOnly = true
            };
            field.SetEnabled(false);
            container.Add(field);
        }

        private void HandleNodeMoved(string nodeId, Vector2 position)
        {
            _controller.MoveNode(nodeId, position);
        }

        private void HandleParentLinkStarted(string parentNodeId)
        {
            _controller.BeginParentLink(parentNodeId);
        }

        private void HandleParentLinkCompleted(string childNodeId)
        {
            _controller.CompleteParentLink(childNodeId, out _);
        }

        private void HandleParentLinkCancelled()
        {
            _controller.CancelParentLink();
        }

        private void LoadJson()
        {
            var path = EditorUtility.OpenFilePanel("Load Skill Tree JSON", Application.dataPath, "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            _hasInitializedAuthoringGraph = true;
            _controller.LoadFromFile(path);
        }

        private void CreateNewGraph()
        {
            _hasInitializedAuthoringGraph = true;
            _controller.CreateNewGraph();
        }

        private void SaveCurrent()
        {
            if (_controller.SaveToCurrentFile(out var errorMessage))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_controller.CurrentFilePath))
            {
                SaveAs();
                return;
            }

            ShowAlertDialog("Save Failed", errorMessage, "OK");
        }

        private void SaveAs()
        {
            var defaultName = string.IsNullOrWhiteSpace(_controller.CurrentFilePath)
                ? $"{_controller.Graph.treeId}.json"
                : Path.GetFileName(_controller.CurrentFilePath);
            var path = EditorUtility.SaveFilePanel("Save Skill Tree JSON", Application.dataPath, defaultName, "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!_controller.SaveToPath(path, out var errorMessage))
            {
                ShowAlertDialog("Save Failed", errorMessage, "OK");
            }
        }

        private void CreateDefaultRuntimeNodePrefab()
        {
            var prefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            _controller.ReportStatus(
                prefab == null
                    ? "기본 런타임 노드 프리팹을 만들지 못했습니다."
                    : $"기본 런타임 노드 프리팹을 준비했습니다. ({SkillTreeRuntimePrefabFactory.DefaultRuntimeNodePrefabPath})",
                prefab == null ? SkillTreeEditorStatusType.Error : SkillTreeEditorStatusType.Info);
            RefreshToolbar();
        }

        private void CreateRuntimeViewPrefab()
        {
            _controller.ReloadMetadata();
            if (_controller.HasBlockingErrors())
            {
                _controller.ReportStatus("런타임 프리팹 생성 전에 그래프 오류를 해결해야 합니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            var treeId = string.IsNullOrWhiteSpace(_controller.Graph?.treeId)
                ? "skill_tree"
                : _controller.Graph.treeId.Trim();
            var defaultDirectory = $"Assets/Game/SkillTreeData/{treeId}";
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Runtime View Prefab",
                $"{treeId}_RuntimeView.prefab",
                "prefab",
                "런타임용 SkillTree 뷰 프리팹을 저장할 경로를 선택하세요.",
                defaultDirectory);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var runtimeNodePrefab = SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
            if (runtimeNodePrefab == null)
            {
                _controller.ReportStatus("기본 Runtime Node 프리팹을 준비하지 못했습니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            var prefab = SkillTreeRuntimePrefabFactory.CreateRuntimeViewPrefab(
                path,
                _controller.Graph,
                _controller.MetadataProvider,
                runtimeNodePrefab);
            if (prefab == null)
            {
                _controller.ReportStatus("런타임 프리팹 생성에 실패했습니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            _controller.ReportStatus($"런타임 프리팹을 생성했습니다. ({path})", SkillTreeEditorStatusType.Info);
            _selectedRuntimeViewPrefab = prefab;
            _runtimeViewField?.SetValueWithoutNotify(prefab);
            RefreshToolbar();
            EditorGUIUtility.PingObject(prefab);
        }

        private void ApplyRuntimeViewMeta()
        {
            _controller.ReloadMetadata();
            if (_controller.HasBlockingErrors())
            {
                _controller.ReportStatus("메타 적용 전에 그래프 오류를 해결해야 합니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            if (_selectedRuntimeViewPrefab == null)
            {
                _controller.ReportStatus("메타를 적용할 RuntimeView 프리팹을 선택하세요.", SkillTreeEditorStatusType.Error);
                return;
            }

            if (_controller.MetadataProvider == null || string.IsNullOrWhiteSpace(_controller.MetadataProviderAssetGuid))
            {
                _controller.ReportStatus("메타 공급자 GUID를 확인할 수 없어 적용할 수 없습니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            var prefabPath = AssetDatabase.GetAssetPath(_selectedRuntimeViewPrefab);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                _controller.ReportStatus("선택한 RuntimeView 프리팹의 경로를 확인할 수 없습니다.", SkillTreeEditorStatusType.Error);
                return;
            }

            using var session = SkillTreeRuntimePrefabSyncService.OpenSession(
                prefabPath,
                _controller.Graph,
                _controller.MetadataProvider,
                _controller.MetadataProviderAssetGuid,
                _selectedRuntimeViewPrefab.NodePrefab);

            if (session.Status == SkillTreeRuntimePrefabSyncSessionStatus.Error)
            {
                _controller.ReportStatus(session.ErrorMessage, SkillTreeEditorStatusType.Error);
                ShowAlertDialog("Apply Meta Failed", session.ErrorMessage, "OK");
                return;
            }

            if (session.Status == SkillTreeRuntimePrefabSyncSessionStatus.BindingMismatch)
            {
                var mismatchMessage =
                    "선택한 RuntimeView 프리팹이 다른 메타에 연결되어 있어 적용을 중단했습니다.\n\n" +
                    $"Stored Tree ID: {session.StoredTreeId}\n" +
                    $"Stored Provider GUID: {session.StoredMetadataProviderGuid}\n" +
                    $"Current Tree ID: {_controller.Graph.treeId}\n" +
                    $"Current Provider GUID: {_controller.MetadataProviderAssetGuid}";
                _controller.ReportStatus("다른 목표 메타에 연결된 RuntimeView 프리팹이라 적용할 수 없습니다.", SkillTreeEditorStatusType.Error);
                ShowAlertDialog("Apply Meta Blocked", mismatchMessage, "OK");
                return;
            }

            if (session.RequiresInitialBindingConfirmation &&
                !ShowConfirmDialog(
                    "Apply Meta",
                    "선택한 RuntimeView 프리팹에 저장된 메타 연결 정보가 없습니다.\n초기 연결을 진행하면서 현재 tree/provider 식별값을 저장할까요?",
                    "Apply",
                    "Cancel"))
            {
                _controller.ReportStatus("RuntimeView 메타 적용을 취소했습니다.", SkillTreeEditorStatusType.Info);
                return;
            }

            var report = session.BuildReport;
            var confirmMessage =
                "RuntimeView 프리팹에 메타 동기화를 적용할까요?\n\n" +
                $"Added: {report.AddedCount}\n" +
                $"Moved: {report.MovedCount}\n" +
                $"Revived: {report.RevivedCount}\n" +
                $"Deleted: {report.DeletedCount}\n" +
                $"Legacy Warnings: {report.UntouchedLegacyObjectCount}";
            if (!ShowConfirmDialog("Apply Meta", confirmMessage, "Apply", "Cancel"))
            {
                _controller.ReportStatus("RuntimeView 메타 적용을 취소했습니다.", SkillTreeEditorStatusType.Info);
                return;
            }

            session.Save();
            _selectedRuntimeViewPrefab = AssetDatabase.LoadAssetAtPath<SkillTreeRuntimeView>(prefabPath);
            _runtimeViewField?.SetValueWithoutNotify(_selectedRuntimeViewPrefab);
            _controller.ReportStatus(
                $"RuntimeView 메타를 적용했습니다. Added {report.AddedCount} / Moved {report.MovedCount} / Revived {report.RevivedCount} / Deleted {report.DeletedCount}",
                SkillTreeEditorStatusType.Info);
            RefreshToolbar();
            EditorGUIUtility.PingObject(_selectedRuntimeViewPrefab);
        }

        private void DeleteSelectedNode()
        {
            if (_controller.GetSelectedNode() == null)
            {
                return;
            }

            if (!ShowConfirmDialog("Delete Node", "선택한 노드를 삭제하고 자식 노드를 루트로 승격할까요?", "Delete", "Cancel"))
            {
                return;
            }

            _controller.DeleteSelectedNode();
        }

        private void OnCanvasWheel(WheelEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey)
            {
                return;
            }

            if (_canvasViewport == null || _canvasView == null || _zoomRoot == null)
            {
                EnsureCanvasViewport();
            }

            if (_canvasViewport == null || _canvasView == null || _zoomRoot == null)
            {
                return;
            }

            var direction = evt.delta.y > 0f ? -1 : 1;
            if (direction == 0)
            {
                return;
            }

            var currentOffset = _canvasScrollView.scrollOffset;
            var pointerInViewport = evt.localMousePosition;
            var pointerInCanvas = new Vector2(
                (currentOffset.x + pointerInViewport.x) / _zoomScale,
                (currentOffset.y + pointerInViewport.y) / _zoomScale);
            var nextZoomScale = direction > 0
                ? Mathf.Min(MaxZoomScale, _zoomScale * ZoomStepFactor)
                : Mathf.Max(MinZoomScale, _zoomScale / ZoomStepFactor);

            if (Mathf.Approximately(nextZoomScale, _zoomScale))
            {
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }

            _zoomScale = nextZoomScale;
            ApplyZoomLayout(false);

            var nextOffset = new Vector2(
                pointerInCanvas.x * _zoomScale - pointerInViewport.x,
                pointerInCanvas.y * _zoomScale - pointerInViewport.y);
            var clampedOffset = ClampScrollOffset(nextOffset);
            _canvasScrollView.scrollOffset = clampedOffset;
            _canvasScrollView.schedule.Execute(() =>
            {
                if (_canvasScrollView == null)
                {
                    return;
                }

                _canvasScrollView.scrollOffset = ClampScrollOffset(clampedOffset);
            });
            RefreshToolbar();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void ApplyZoomLayout(bool preserveScrollOffset)
        {
            EnsureCanvasViewport();
            if (_canvasView == null || _zoomRoot == null)
            {
                return;
            }

            var previousOffset = _canvasScrollView?.scrollOffset ?? Vector2.zero;
            var contentSize = _canvasView.ContentSize;
            var viewportSize = _canvasViewport?.layout.size ?? Vector2.zero;
            var scaledContentSize = contentSize * _zoomScale;
            _zoomRoot.style.width = Mathf.Max(scaledContentSize.x, viewportSize.x);
            _zoomRoot.style.height = Mathf.Max(scaledContentSize.y, viewportSize.y);
            _canvasView.transform.position = Vector3.zero;
            _canvasView.transform.scale = new Vector3(_zoomScale, _zoomScale, 1f);

            if (preserveScrollOffset && _canvasScrollView != null)
            {
                _canvasScrollView.scrollOffset = ClampScrollOffset(previousOffset);
            }
        }

        private Vector2 ClampScrollOffset(Vector2 value)
        {
            EnsureCanvasViewport();
            if (_canvasViewport == null || _zoomRoot == null)
            {
                return value;
            }

            var contentSize = _canvasView.ContentSize * _zoomScale;
            var viewportSize = _canvasViewport.layout.size;
            var maxX = Mathf.Max(0f, contentSize.x - viewportSize.x);
            var maxY = Mathf.Max(0f, contentSize.y - viewportSize.y);
            return new Vector2(
                Mathf.Clamp(value.x, 0f, maxX),
                Mathf.Clamp(value.y, 0f, maxY));
        }

        private void EnsureCanvasViewport()
        {
            _canvasViewport ??= _canvasScrollView?.Q<VisualElement>(className: ScrollView.viewportUssClassName);
            if (_canvasViewport != null && !_isCanvasWheelHandlerRegistered)
            {
                _canvasViewport.style.backgroundColor = CanvasBackgroundColor;
                _canvasViewport.RegisterCallback<WheelEvent>(OnCanvasWheel, TrickleDown.TrickleDown);
                _canvasViewport.RegisterCallback<MouseDownEvent>(OnCanvasViewportMouseDown, TrickleDown.TrickleDown);
                _canvasViewport.RegisterCallback<MouseMoveEvent>(OnCanvasViewportMouseMove, TrickleDown.TrickleDown);
                _canvasViewport.RegisterCallback<MouseUpEvent>(OnCanvasViewportMouseUp, TrickleDown.TrickleDown);
                _isCanvasWheelHandlerRegistered = true;
            }
        }

        private void OnCanvasViewportMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || _canvasViewport == null)
            {
                return;
            }

            if (ResolveNodeElementAtPanelPosition(evt.mousePosition) != null)
            {
                return;
            }

            if (_canvasView != null && _canvasView.HasConnectionAtPanelPosition(evt.mousePosition))
            {
                return;
            }

            _controller?.SelectNode(null);
            _isPanningViewport = true;
            _panStartPointerPosition = evt.mousePosition;
            _panStartScrollOffset = _canvasScrollView.scrollOffset;
            _canvasViewport.CaptureMouse();
            evt.StopImmediatePropagation();
        }

        private void OnCanvasViewportMouseMove(MouseMoveEvent evt)
        {
            if (!_isPanningViewport || _canvasViewport == null || !_canvasViewport.HasMouseCapture())
            {
                return;
            }

            var delta = evt.mousePosition - _panStartPointerPosition;
            var nextOffset = new Vector2(
                _panStartScrollOffset.x - delta.x,
                _panStartScrollOffset.y - delta.y);
            _canvasScrollView.scrollOffset = ClampScrollOffset(nextOffset);
            evt.StopImmediatePropagation();
        }

        private void OnCanvasViewportMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 0 || !_isPanningViewport)
            {
                return;
            }

            EndViewportPan();
            evt.StopImmediatePropagation();
        }

        private void EndViewportPan()
        {
            if (_canvasViewport != null && _canvasViewport.HasMouseCapture())
            {
                _canvasViewport.ReleaseMouse();
            }

            _isPanningViewport = false;
            _panStartPointerPosition = Vector2.zero;
            _panStartScrollOffset = Vector2.zero;
        }

        private SkillTreeNodeElement ResolveNodeElementAtPanelPosition(Vector2 panelPosition)
        {
            var picked = rootVisualElement.panel?.Pick(panelPosition);
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

        private static Button MakeButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.minWidth = 82f;
            button.style.marginRight = 6f;
            return button;
        }

        internal void SetSelectedRuntimeViewForTests(SkillTreeRuntimeView runtimeView)
        {
            _selectedRuntimeViewPrefab = runtimeView;
            _runtimeViewField?.SetValueWithoutNotify(runtimeView);
            RefreshToolbar();
        }

        internal void ApplyRuntimeViewMetaForTests()
        {
            ApplyRuntimeViewMeta();
        }

        private bool ShowConfirmDialog(string title, string message, string ok, string cancel)
        {
            return ConfirmDialogHandlerForTests != null
                ? ConfirmDialogHandlerForTests(title, message, ok, cancel)
                : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        private void ShowAlertDialog(string title, string message, string ok)
        {
            if (AlertDialogHandlerForTests != null)
            {
                AlertDialogHandlerForTests(title, message, ok);
                return;
            }

            EditorUtility.DisplayDialog(title, message, ok);
        }

        private static HelpBoxMessageType ToHelpBoxType(SkillTreeValidationSeverity severity)
        {
            return severity switch
            {
                SkillTreeValidationSeverity.Error => HelpBoxMessageType.Error,
                SkillTreeValidationSeverity.Warning => HelpBoxMessageType.Warning,
                _ => HelpBoxMessageType.Info
            };
        }

        private static Color ResolveStatusColor(SkillTreeEditorStatusType statusType)
        {
            return statusType switch
            {
                SkillTreeEditorStatusType.Error => new Color(0.92f, 0.35f, 0.28f),
                SkillTreeEditorStatusType.Warning => new Color(1f, 0.7f, 0.25f),
                _ => new Color(0.45f, 0.88f, 0.55f)
            };
        }
    }
}
