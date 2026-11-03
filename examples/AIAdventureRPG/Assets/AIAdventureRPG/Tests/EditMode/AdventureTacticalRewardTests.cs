using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureTacticalRewardTests
    {
        const string PreNormal=" pre_hp=65.00 pre_mp=65.00 max_mp=100.00";
        const string PreLowHP=" pre_hp=15.00 pre_mp=65.00 max_mp=100.00";
        const string PreLowMP=" pre_hp=65.00 pre_mp=12.00 max_mp=100.00";
        static float Score(string eventText,Vector4 delta)
            =>AdventureRewardPolicy.FromOutcome(delta,"source=Self action="+eventText);

        [Test]
        public void KillIsBestFightOutcomeAndMissIsBad()
        {
            float kill=Score("DefeatMonster"+PreNormal,Vector4.zero);
            float hit=Score("StrikeMonster"+PreNormal,Vector4.zero);
            float miss=Score("AttackMiss reason=no target"+PreNormal,Vector4.zero);
            Assert.That(kill,Is.GreaterThan(hit));
            Assert.That(hit,Is.GreaterThan(0f));
            Assert.That(miss,Is.LessThan(0f));
            Assert.That(Score("Ability SpecialAttack hits=1 kills=1 healed=0.0"+PreNormal,
                new Vector4(0,-12,0,0)),Is.GreaterThan(hit));
        }

        [Test]
        public void IncreasedHitIncentiveKeepsKillBonusDominant()
        {
            float ordinary=Score("StrikeMonster"+PreNormal,Vector4.zero);
            float special=Score("Ability SpecialAttack hits=1 kills=0 healed=0.0"+
                PreNormal,new Vector4(0,-12,0,0));
            float kill=Score("DefeatMonster"+PreNormal,Vector4.zero);
            Assert.That(ordinary,Is.EqualTo(.35f).Within(.001f));
            Assert.That(special,Is.EqualTo(.376f).Within(.001f));
            Assert.That(kill,Is.GreaterThan(ordinary));
            Assert.That(kill,Is.GreaterThan(special));
        }

        [Test]
        public void RealDodgeAndSeenThornEscapeAreGoodButContactIsBad()
        {
            Assert.That(Score("DodgeMonster Boar"+PreNormal,Vector4.zero),Is.GreaterThan(0f));
            Assert.That(Score("AvoidThorn Spike"+PreNormal,Vector4.zero),Is.GreaterThan(0f));
            Assert.That(Score("Collect Thorn"+PreNormal,new Vector4(-18,0,-2,0)),Is.LessThan(0f));
            Assert.That(Score("HitByMonster"+PreNormal,new Vector4(-12,0,-2,0)),Is.LessThan(0f));
        }

        [Test]
        public void DamageAtCriticalHPIsWorseAndDeathIsWorst()
        {
            float normal=Score("HitByMonster"+PreNormal,new Vector4(-10,0,-2,0));
            float low=Score("HitByMonster"+PreLowHP,new Vector4(-10,0,-2,0));
            float death=Score("HitByMonster"+PreLowHP+" death=true",new Vector4(-15,0,-2,0));
            Assert.That(low,Is.LessThan(normal));
            Assert.That(death,Is.EqualTo(-2f));
            Assert.That(death,Is.LessThan(low));
        }

        [Test]
        public void HerbRecoveryUnderTwentyPercentIsWorthMoreThanNormalRecovery()
        {
            float urgent=Score("Collect Herb"+PreLowHP,new Vector4(26,0,6,-9));
            float general=Score("Collect Herb"+PreNormal,new Vector4(26,0,6,-9));
            float wasted=Score("Collect Herb"+PreNormal,new Vector4(0,0,6,-9));
            Assert.That(urgent,Is.GreaterThan(general));
            Assert.That(general,Is.GreaterThan(0f));
            Assert.That(wasted,Is.LessThan(0f));
        }

        [Test]
        public void MPRefillUnderTwentyPercentIsWorthMoreThanNormalAndFullIsWaste()
        {
            float urgent=Score("Collect LoreStone"+PreLowMP,new Vector4(0,18,4,-9));
            float general=Score("Collect LoreStone"+PreNormal,new Vector4(0,18,4,-9));
            float wasted=Score("Collect LoreStone"+PreNormal,new Vector4(0,0,4,-9));
            Assert.That(urgent,Is.GreaterThan(general));
            Assert.That(general,Is.GreaterThan(0f));
            Assert.That(wasted,Is.LessThan(0f));
        }

        [Test]
        public void AbilityHitIsGoodButEmptyCastAndRejectedCastAreBad()
        {
            float hit=Score("Ability ArcPulse hits=1 kills=0 healed=0.0"+PreNormal,
                new Vector4(0,-20,0,0));
            float empty=Score("Ability ArcPulse hits=0 kills=0 healed=0.0"+PreNormal,
                new Vector4(0,-20,0,0));
            float rejected=Score("AbilityRejected SpecialAttack reason=no target"+PreNormal,
                Vector4.zero);
            Assert.That(hit,Is.GreaterThan(0f));
            Assert.That(empty,Is.LessThan(0f));
            Assert.That(rejected,Is.LessThan(0f));
        }

        [Test]
        public void SharedInteractButtonWithNoEnemyIsNotAnAttackMiss()
        {
            var go=new GameObject("Non-combat interaction test");
            try
            {
                var actor=go.AddComponent<AdventureAgent>();
                var stats=go.GetComponent<AdventureStats>();
                string before=stats.LastOutcome;
                Assert.That(actor.TryInteract(),Is.False);
                Assert.That(stats.LastOutcome,Is.EqualTo(before));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void ConfiguredLowHealthThresholdMatchesRewardAndActorState()
        {
            var go=new GameObject("Custom low health threshold");
            try
            {
                var agent=go.AddComponent<AdventureAgent>();
                var state=go.AddComponent<AdventureCombatState>();
                var stats=go.GetComponent<AdventureStats>();
                stats.health=25f;
                state.lowHealthPercent=30f;
                Assert.That(state.LowHP,Is.True);
                stats.TakeDamage("Test Boar",5f);
                float higherThreshold=AdventureRewardPolicy.FromOutcome(
                    stats.LastOutcomeDeltas,stats.LastOutcome);
                Assert.That(stats.LastOutcome,Does.Contain("hp_low_ratio=0.300"));
                state.lowHealthPercent=20f;
                stats.health=25f;
                Assert.That(state.LowHP,Is.False);
                stats.TakeDamage("Test Boar",5f);
                float lowerThreshold=AdventureRewardPolicy.FromOutcome(
                    stats.LastOutcomeDeltas,stats.LastOutcome);
                Assert.That(higherThreshold,Is.LessThan(lowerThreshold));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void ConfiguredLowManaThresholdMatchesRewardAndActorState()
        {
            var go=new GameObject("Custom low mana threshold");
            try
            {
                go.AddComponent<AdventureAgent>();
                var state=go.AddComponent<AdventureCombatState>();
                var stats=go.GetComponent<AdventureStats>();
                stats.magicPoints=25f;
                state.lowMagicPercent=30f;
                Assert.That(state.LowMP,Is.True);
                stats.PickUp(AdventureObjectKind.LoreStone,"LoreStone");
                float urgent=AdventureRewardPolicy.FromOutcome(
                    stats.LastOutcomeDeltas,stats.LastOutcome);
                Assert.That(stats.LastOutcome,Does.Contain("mp_low_ratio=0.300"));
                state.lowMagicPercent=20f;
                stats.magicPoints=25f;
                Assert.That(state.LowMP,Is.False);
                stats.PickUp(AdventureObjectKind.LoreStone,"LoreStone");
                float routine=AdventureRewardPolicy.FromOutcome(
                    stats.LastOutcomeDeltas,stats.LastOutcome);
                Assert.That(urgent,Is.GreaterThan(routine));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void OllamaRecentEventFilterExcludesOldButIncludesNewEvents()
        {
            var go=new GameObject("Fresh context boundary");
            try
            {
                var stats=go.AddComponent<AdventureStats>();
                stats.Record("old-event-before-reset");
                float resetAt=Time.time;
                stats.Record("new-event-after-reset");
                var field=typeof(AdventureStats).GetField("recentTimes",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                var times=(System.Collections.Generic.List<float>)field.GetValue(stats);
                times[0]=resetAt+.01f; // deterministic timestamp after reset
                string context=stats.ContextSummary(true,resetAt);
                Assert.That(context,Does.Contain("new-event-after-reset"));
                Assert.That(context,Does.Not.Contain("old-event-before-reset"));
                Assert.That(context,Does.Contain("HP="));
                Assert.That(context,Does.Contain("MP="));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void EarlierJournalEntriesCanBeExcludedFromResetOllamaContext()
        {
            var go=new GameObject("Prompt history cutoff test");
            try
            {
                var journal=go.AddComponent<AdventureEventJournal>();
                journal.Record(AdventureEventJournal.Kind.Seen,"old","before reset",Vector3.zero);
                Assert.That(journal.ReportSince(Time.time+1f),
                    Is.EqualTo("No observations since context reset"));
                Assert.That(journal.Report(2),Does.Contain("before reset"));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void StatusLowHPAndMPRemainIndependentOfCombat()
        {
            var go=new GameObject("Combat state test");
            try
            {
                go.AddComponent<AdventureAgent>();
                var state=go.GetComponent<AdventureCombatState>();
                if(state==null)state=go.AddComponent<AdventureCombatState>();
                var stats=go.GetComponent<AdventureStats>();
                Assert.That(state,Is.Not.Null);
                Assert.That(state.InCombat,Is.False);
                stats.health=19;
                stats.magicPoints=19;
                Assert.That(state.LowHP,Is.True);
                Assert.That(state.LowMP,Is.True);
                state.Engage();
                Assert.That(state.Label,Does.Contain("COMBAT"));
                Assert.That(state.Label,Does.Contain("LOW HP"));
                Assert.That(state.Label,Does.Contain("LOW MP"));
                stats.health=50;
                stats.magicPoints=50;
                Assert.That(state.Label,Does.Not.Contain("LOW"));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void OllamaContextResetClearsHistoryAndCancelsPendingWithoutChangingPlayerStats()
        {
            var temp=Path.Combine(Path.GetTempPath(),"ollama-context-reset-"+
                Guid.NewGuid().ToString("N")+".txt");
            var go=new GameObject("Ollama context reset test");
            try
            {
                go.AddComponent<AdventureAgent>();
                go.AddComponent<AdventureOllama>();
                var brain=go.AddComponent<AdventureBrain>();
                var field=typeof(AdventureBrain).GetField("memoryPath",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                field.SetValue(brain,temp);
                var stats=go.GetComponent<AdventureStats>();
                stats.health=29;stats.magicPoints=15;
                brain.Remember("old combat memory");
                Assert.That(brain.Memory.Count,Is.GreaterThan(0));
                brain.ResetContext();
                Assert.That(brain.Memory.Count,Is.Zero);
                Assert.That(File.ReadAllText(temp),Is.Empty);
                Assert.That(stats.health,Is.EqualTo(29));
                Assert.That(stats.magicPoints,Is.EqualTo(15));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if(File.Exists(temp))File.Delete(temp);
            }
        }
    }
}
