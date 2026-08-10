using System;
using UnityEngine;

namespace SkillTree.Authoring.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SkillTreeRuntimeSourceBinding : MonoBehaviour
    {
        [SerializeField] private string sourceTreeId;
        [SerializeField] private string sourceMetadataProviderGuid;

        public string SourceTreeId => sourceTreeId;
        public string SourceMetadataProviderGuid => sourceMetadataProviderGuid;
        public bool HasSourceBinding => !string.IsNullOrWhiteSpace(sourceTreeId) &&
                                        !string.IsNullOrWhiteSpace(sourceMetadataProviderGuid);

        private void OnEnable()
        {
            // 프리팹 동기화용 메타데이터이므로 일반 Inspector에서는 감춘다.
            hideFlags = HideFlags.HideInInspector;
        }

        private void OnValidate()
        {
            // 에디터에서 컴포넌트가 다시 로드될 때도 숨김 상태를 유지한다.
            hideFlags = HideFlags.HideInInspector;
        }

        public void Apply(string treeId, string metadataProviderGuid)
        {
            // 프리팹이 어떤 graph/provider 조합으로 생성됐는지 최소 정보만 저장한다.
            sourceTreeId = string.IsNullOrWhiteSpace(treeId) ? null : treeId.Trim();
            sourceMetadataProviderGuid = string.IsNullOrWhiteSpace(metadataProviderGuid)
                ? null
                : metadataProviderGuid.Trim();
        }

        public void Clear()
        {
            // 초기 바인딩 확인 테스트와 수동 재설정을 위해 저장된 stamp를 비운다.
            sourceTreeId = null;
            sourceMetadataProviderGuid = null;
        }

        public bool Matches(string treeId, string metadataProviderGuid)
        {
            if (!HasSourceBinding)
            {
                return false;
            }

            return string.Equals(sourceTreeId, treeId?.Trim(), StringComparison.Ordinal) &&
                   string.Equals(sourceMetadataProviderGuid, metadataProviderGuid?.Trim(), StringComparison.Ordinal);
        }
    }
}
