using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureMagicTests
    {
        [Test]
        public void MagicPointsSpendExactlyAndCannotGoNegative()
        {
            var go=new GameObject("MP test");
            try
            {
                var stats=go.AddComponent<AdventureStats>();
                stats.magicPoints=40f;
                Assert.That(stats.HasMP(12f),Is.True);
                Assert.That(stats.TrySpendMP(12f),Is.True);
                Assert.That(stats.magicPoints,Is.EqualTo(28f));
                Assert.That(stats.TrySpendMP(30f),Is.False);
                Assert.That(stats.magicPoints,Is.EqualTo(28f));
                Assert.That(stats.TrySpendMP(-5f),Is.False);
                Assert.That(stats.TrySpendMP(float.NaN),Is.False);
                stats.RestoreMP(1000f);
                Assert.That(stats.magicPoints,Is.EqualTo(stats.maxMagicPoints));
                Assert.That(stats.TrySpendMP(100f),Is.True);
                Assert.That(stats.magicPoints,Is.Zero);
                Assert.That(stats.TrySpendMP(12f),Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void NormalAttackIsLowerThanSpecialAndSpecialCostsMP()
        {
            var go=new GameObject("Magic agent test");
            try
            {
                go.AddComponent<Rigidbody>();
                var stats=go.AddComponent<AdventureStats>();
                var actor=go.AddComponent<AdventureAgent>();
                Assert.That(actor.attackDamage,Is.EqualTo(14f));
                Assert.That(actor.specialAttackDamage,Is.EqualTo(32f));
                Assert.That(actor.specialAttackDamage,Is.GreaterThan(actor.attackDamage));
                Assert.That(actor.specialAttackMpCost,Is.EqualTo(12f));
                var before=stats.magicPoints;
                Assert.That(actor.TryUseAbility(AdventureAbility.SpecialAttack),Is.False);
                Assert.That(stats.magicPoints,Is.EqualTo(before),
                    "A miss/invalid target must not spend MP");
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void AbilityOutcomeRecordsActualMPCostAndIsNotASecondNormalAttack()
        {
            var go=new GameObject("Magic outcome test");
            try
            {
                var stats=go.AddComponent<AdventureStats>();
                stats.magicPoints=40f;
                Assert.That(stats.TrySpendMP(12f),Is.True);
                stats.ResolveAbility("SpecialAttack",40f,1,0);
                Assert.That(stats.LastOutcome,Does.Contain("MP 40->28"));
                Assert.That(stats.LastOutcome,Does.Contain("action=Ability SpecialAttack"));
                Assert.That(stats.monsterHits,Is.EqualTo(1));
                Assert.That(stats.LastOutcomeDeltas.y,Is.EqualTo(-12f));
                Assert.That(AdventureRewardPolicy.FromOutcome(stats.LastOutcomeDeltas,stats.LastOutcome),
                    Is.GreaterThan(-.5f));
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test]
        public void StationaryAICastTurnsTowardTheCommandedFacingBeforeAttack()
        {
            var go=new GameObject("Stationary AI face test");
            try
            {
                go.AddComponent<AdventureAgent>().mode=ControlMode.Tiny;
                go.transform.rotation=Quaternion.identity;
                go.GetComponent<AdventureAgent>().Apply(new AdventureAction(0f,0f,
                    1f,0f,ability:AdventureAbility.SpecialAttack));
                Assert.That(Vector3.Dot(go.transform.forward,Vector3.right),
                    Is.GreaterThan(.999f));
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void InvalidSpellGivesNegativeTrainingSignalWithNoMPOrHealthChange()
        {
            var go=new GameObject("Invalid spell test");
            try
            {
                var stats=go.AddComponent<AdventureStats>();
                float mp=stats.magicPoints,health=stats.health;
                stats.RejectAbility("SpecialAttack","no nearby monster");
                Assert.That(stats.magicPoints,Is.EqualTo(mp));
                Assert.That(stats.health,Is.EqualTo(health));
                Assert.That(stats.LastOutcome,Does.Contain("action=AbilityRejected SpecialAttack"));
                Assert.That(stats.LastOutcomeDeltas,Is.EqualTo(Vector4.zero));
                Assert.That(AdventureRewardPolicy.FromOutcome(stats.LastOutcomeDeltas,stats.LastOutcome),
                    Is.EqualTo(-.13f).Within(.00001f));
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void AgentAutomaticallyHasExactlyOneStatsComponent()
        {
            var go=new GameObject("Agent dependency test");
            try
            {
                go.AddComponent<AdventureAgent>();
                Assert.That(go.GetComponents<AdventureStats>().Length,Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void IdleAbilityCommandRemainsIndependentOfMovementAndInteraction()
        {
            var action=new AdventureAction(0,0,0,1,ability:AdventureAbility.ArcPulse);
            Assert.That(action.ability,Is.EqualTo(AdventureAbility.ArcPulse));
            Assert.That(action.interact,Is.False);
            Assert.That(action.moveDistance,Is.Zero);
        }
    }
}
