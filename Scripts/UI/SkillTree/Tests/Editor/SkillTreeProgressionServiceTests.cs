using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SkillTree.Authoring.Tests
{
    public sealed class SkillTreeProgressionServiceTests
    {
        [Test]
        public void NormalizeSnapshotAddsMissingStatesAndRepairsSelection()
        {
            // 누락된 유저 상태와 잘못된 선택 노드를 그래프 기준으로 보정한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            var snapshot = new SkillTreeSnapshot
            {
                treeId = "wrong_tree",
                selectedSkillId = "missing",
                userSkills = new List<UserSkillState>
                {
                    new() { skillId = "root", level = 1, isUnlocked = true },
                    new() { skillId = "removed", level = 4, isUnlocked = true }
                }
            };

            var normalized = SkillTreeProgressionService.NormalizeSnapshot(graph, provider, snapshot);

            Assert.That(normalized.treeId, Is.EqualTo(graph.treeId));
            Assert.That(normalized.userSkills.Select(state => state.skillId), Is.EqualTo(new[] { "root", "child" }));
            Assert.That(normalized.selectedSkillId, Is.EqualTo("root"));
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void ResolveKeepsChildLockedUntilParentUnlocked()
        {
            // 부모가 해금되기 전까지 자식은 잠금 상태여야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();

            var initialResolved = SkillTreeProgressionService.Resolve(
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = 20 });

            var childInitial = initialResolved.skillStatuses.Single(status => status.skillId == "child");
            Assert.That(childInitial.isLocked, Is.True);
            Assert.That(childInitial.progressState, Is.EqualTo(SkillNodeProgressState.Locked));
            Assert.That(childInitial.canUpgrade, Is.False);

            var unlockedResolved = SkillTreeProgressionService.Resolve(
                graph,
                provider,
                new SkillTreeSnapshot
                {
                    currencyBalance = 20,
                    userSkills = new List<UserSkillState>
                    {
                        new() { skillId = "root", level = 1, isUnlocked = false }
                    }
                });

            var childUnlocked = unlockedResolved.skillStatuses.Single(status => status.skillId == "child");
            Assert.That(childUnlocked.isLocked, Is.False);
            Assert.That(childUnlocked.progressState, Is.EqualTo(SkillNodeProgressState.Open));
            Assert.That(childUnlocked.canUpgrade, Is.True);
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void ResolveMarksMaxedSkillsAsNonUpgradeable()
        {
            // 최대 레벨 도달 시 더 이상 업그레이드할 수 없어야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            var resolved = SkillTreeProgressionService.Resolve(
                graph,
                provider,
                new SkillTreeSnapshot
                {
                    currencyBalance = 20,
                    userSkills = new List<UserSkillState>
                    {
                        new() { skillId = "root", level = 1, isUnlocked = true }
                    }
                });

            var rootStatus = resolved.skillStatuses.Single(status => status.skillId == "root");
            Assert.That(rootStatus.isMaxed, Is.True);
            Assert.That(rootStatus.progressState, Is.EqualTo(SkillNodeProgressState.Maxed));
            Assert.That(rootStatus.canUpgrade, Is.False);
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void TryUpgradeReturnsLockedFailureForUnavailableChild()
        {
            // 잠금 스킬 업그레이드 시도는 명시적인 실패 이유를 돌려줘야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();

            var result = SkillTreeProgressionService.TryUpgrade(
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = 20 },
                "child");

            Assert.That(result.status, Is.EqualTo(SkillUpgradeResultStatus.Failed));
            Assert.That(result.failureReason, Is.EqualTo(SkillUpgradeFailureReason.Locked));
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void TryUpgradeReturnsInsufficientCurrencyFailure()
        {
            // 재화 부족은 잠금과 구분되는 실패 이유로 처리한다.
            var graph = CreateGraph();
            var provider = CreateProvider();

            var result = SkillTreeProgressionService.TryUpgrade(
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = 2 },
                "root");

            Assert.That(result.status, Is.EqualTo(SkillUpgradeResultStatus.Failed));
            Assert.That(result.failureReason, Is.EqualTo(SkillUpgradeFailureReason.InsufficientCurrency));
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void TryUpgradeConsumesCurrencyAndIncreasesLevel()
        {
            // 정상 업그레이드는 레벨 증가와 재화 차감을 함께 반영해야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();

            var result = SkillTreeProgressionService.TryUpgrade(
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = 10, selectedSkillId = "root" },
                "root");

            var rootState = result.updatedSnapshot.userSkills.Single(state => state.skillId == "root");
            var rootStatus = result.resolvedData.skillStatuses.Single(status => status.skillId == "root");
            var childStatus = result.resolvedData.skillStatuses.Single(status => status.skillId == "child");

            Assert.That(result.status, Is.EqualTo(SkillUpgradeResultStatus.Success));
            Assert.That(result.failureReason, Is.EqualTo(SkillUpgradeFailureReason.None));
            Assert.That(result.updatedSnapshot.currencyBalance, Is.EqualTo(7));
            Assert.That(rootState.level, Is.EqualTo(1));
            Assert.That(rootState.isUnlocked, Is.True);
            Assert.That(rootStatus.isMaxed, Is.True);
            Assert.That(childStatus.isLocked, Is.False);
            Assert.That(childStatus.progressState, Is.EqualTo(SkillNodeProgressState.Open));
            UnityEngine.Object.DestroyImmediate(provider);
        }

        [Test]
        public void TryUpgradePreservesUnsignedBalancesAboveIntMaxValue()
        {
            // 큰 재화도 int 범위로 잘리지 않고 정확히 차감되어야 한다.
            var graph = CreateGraph();
            var provider = CreateProvider();
            const uint balance = (uint)int.MaxValue + 100u;

            var result = SkillTreeProgressionService.TryUpgrade(
                graph,
                provider,
                new SkillTreeSnapshot { currencyBalance = balance },
                "root");

            Assert.That(result.status, Is.EqualTo(SkillUpgradeResultStatus.Success));
            Assert.That(result.updatedSnapshot.currencyBalance, Is.EqualTo(balance - 3u));
            UnityEngine.Object.DestroyImmediate(provider);
        }

        // 테스트용 최소 그래프를 만든다.
        private static SkillTreeGraphData CreateGraph()
        {
            var graph = SkillTreeJsonService.CreateDefaultGraph("progression_test");
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "root",
                position = new Vector2(100f, 100f)
            });
            graph.nodes.Add(new SkillTreeNodeRecord
            {
                id = "child",
                parentId = "root",
                position = new Vector2(360f, 220f)
            });
            return graph;
        }

        // 메타데이터 기반 provider를 그대로 사용해 새 정의 어댑터 경로도 함께 검증한다.
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
                cost = 5,
                maxLevel = 1
            };
            return provider;
        }

        private sealed class TestMetadataProvider : SkillNodeMetadataProviderAsset
        {
            public readonly Dictionary<string, SkillNodeMetadata> metadataById = new(StringComparer.Ordinal);

            // 기존 메타데이터 provider 계약을 테스트 더블로 제공한다.
            public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
            {
                return metadataById.TryGetValue(nodeId, out metadata);
            }

            // 검증기는 이 목록을 사용하므로 테스트에서도 함께 제공한다.
            public override IReadOnlyList<string> GetKnownNodeIds()
            {
                return metadataById.Keys.ToList();
            }
        }
    }
}
