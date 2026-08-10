using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SkillTree.Authoring.Editor
{
    public sealed class SkillTreeNodeElement : VisualElement
    {
        public const float NodeWidth = 190f;
        public const float NodeHeight = 96f;
        private readonly Label _titleLabel;
        private readonly Label _subLabel;
        private readonly Label _badgeLabel;
        private readonly Image _iconImage;
        private Vector2 _currentPosition;

        public string NodeId { get; }
        internal Vector2 CurrentPositionForTests => _currentPosition;
        internal string TitleTextForTests => _titleLabel.text;
        internal string SubtitleTextForTests => _subLabel.text;
        internal string BadgeTextForTests => _badgeLabel.text;
        internal UnityEngine.Object IconForTests => _iconImage.sprite != null ? _iconImage.sprite : _iconImage.image;

        public SkillTreeNodeElement(
            SkillTreeNodeRecord node,
            SkillNodeMetadata metadata,
            bool isSelected,
            bool hasError,
            bool hasWarning)
        {
            NodeId = node.id;
            _currentPosition = node.position;

            style.position = Position.Absolute;
            style.width = NodeWidth;
            style.height = NodeHeight;
            style.left = node.position.x;
            style.top = node.position.y;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 8f;
            style.paddingBottom = 8f;
            style.borderTopWidth = 2f;
            style.borderBottomWidth = 2f;
            style.borderLeftWidth = 2f;
            style.borderRightWidth = 2f;
            style.borderTopColor = ResolveBorderColor(isSelected, hasError, hasWarning);
            style.borderBottomColor = ResolveBorderColor(isSelected, hasError, hasWarning);
            style.borderLeftColor = ResolveBorderColor(isSelected, hasError, hasWarning);
            style.borderRightColor = ResolveBorderColor(isSelected, hasError, hasWarning);
            style.backgroundColor = ResolveBackgroundColor();
            style.borderTopLeftRadius = 10f;
            style.borderTopRightRadius = 10f;
            style.borderBottomLeftRadius = 10f;
            style.borderBottomRightRadius = 10f;
            style.flexDirection = FlexDirection.Row;

            var fallbackIcon = metadata?.icon == null
                ? EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image
                : null;
            _iconImage = new Image { scaleMode = ScaleMode.ScaleToFit };
            if (metadata?.icon != null)
            {
                _iconImage.sprite = metadata.icon;
            }
            else
            {
                _iconImage.image = fallbackIcon;
            }
            _iconImage.style.width = 36f;
            _iconImage.style.height = 36f;
            _iconImage.style.marginRight = 8f;
            _iconImage.style.unityBackgroundImageTintColor = metadata?.icon == null && fallbackIcon == null
                ? new Color(1f, 1f, 1f, 0.2f)
                : Color.white;

            var textColumn = new VisualElement();
            textColumn.style.flexGrow = 1f;
            textColumn.style.flexDirection = FlexDirection.Column;

            _titleLabel = new Label(string.IsNullOrWhiteSpace(metadata?.displayName) ? node.id : metadata.displayName);
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.color = Color.white;
            _titleLabel.style.whiteSpace = WhiteSpace.Normal;

            _subLabel = new Label(BuildSubtitle(node, metadata));
            _subLabel.style.color = new Color(1f, 1f, 1f, 0.78f);
            _subLabel.style.whiteSpace = WhiteSpace.Normal;
            _subLabel.style.fontSize = 10f;

            _badgeLabel = new Label(BuildBadge(node, metadata, hasError, hasWarning));
            _badgeLabel.style.color = new Color(1f, 1f, 1f, 0.92f);
            _badgeLabel.style.fontSize = 10f;
            _badgeLabel.style.marginTop = 6f;

            textColumn.Add(_titleLabel);
            textColumn.Add(_subLabel);
            textColumn.Add(_badgeLabel);

            Add(_iconImage);
            Add(textColumn);
        }

        public void ApplyPosition(Vector2 position)
        {
            _currentPosition = position;
            style.left = position.x;
            style.top = position.y;
        }

        public void ApplyVisualState(bool isSelected, bool hasError, bool hasWarning)
        {
            var borderColor = ResolveBorderColor(isSelected, hasError, hasWarning);
            style.borderTopColor = borderColor;
            style.borderBottomColor = borderColor;
            style.borderLeftColor = borderColor;
            style.borderRightColor = borderColor;
        }

        private static string BuildSubtitle(SkillTreeNodeRecord node, SkillNodeMetadata metadata)
        {
            if (metadata != null)
            {
                return $"Cost {metadata.cost}  Lv {metadata.maxLevel}";
            }

            return string.Empty;
        }

        private static string BuildBadge(
            SkillTreeNodeRecord node,
            SkillNodeMetadata metadata,
            bool hasError,
            bool hasWarning)
        {
            if (hasError)
            {
                return "ERROR";
            }

            if (hasWarning)
            {
                return "WARNING";
            }

            if (metadata != null)
            {
                return "Ready";
            }

            return "Missing Metadata";
        }

        private static Color ResolveBorderColor(bool isSelected, bool hasError, bool hasWarning)
        {
            if (hasError)
            {
                return new Color(0.92f, 0.32f, 0.28f);
            }

            if (isSelected)
            {
                return new Color(0.35f, 0.72f, 1f);
            }

            if (hasWarning)
            {
                return new Color(1f, 0.68f, 0.22f);
            }

            return new Color(0.25f, 0.32f, 0.4f);
        }

        private static Color ResolveBackgroundColor()
        {
            return new Color(0.16f, 0.18f, 0.22f);
        }
    }
}
