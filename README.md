# SkillTreeMaker

Unity에서 JSON 기반 스킬 트리를 편집하고 런타임 UI 프리팹으로 생성하기 위한 도구입니다.

## 프로젝트 연결

Unity 프로젝트의 `Assets` 아래에 서브모듈로 추가합니다.

```bash
git submodule add https://github.com/jenemia/SkillTree.git Assets/Game/SkilTreeMaker
git submodule update --init --recursive
```

## 핵심 데이터

- 그래프 JSON: 노드 식별자, 부모 관계, 에디터 좌표
- `SkillNodeMetadataCatalog`: 이름, 설명, 비용, 최대 레벨, `Sprite` 아이콘
- 런타임 스냅샷: 노드별 강화 레벨과 보유 재화

노드 상태는 `Locked`, `Open`, `Purchased`, `Maxed`로 계산됩니다. 부모 노드의 저장 레벨이 1 이상이어야 자식 노드가 `Open` 상태가 됩니다.

## 검증

Unity Test Framework의 `SkillTree.Editor.Tests` EditMode 테스트를 실행합니다.
