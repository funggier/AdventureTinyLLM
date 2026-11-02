using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureBoredomTests
    {
        GameObject host;
        AdventureStats stats;

        [SetUp]
        public void SetUp()
        {
            host=new GameObject("TEMP - Happiness Test");
            stats=host.AddComponent<AdventureStats>();
            stats.health=50;stats.magicPoints=50;stats.happiness=50;
            stats.development=10;stats.boredom=100;
        }
        [TearDown]
        public void TearDown()
        {
            if(host!=null)Object.DestroyImmediate(host);
        }

        [Test]
        public void FullBoredomDrainsHealthKnowledgeAndDevelopmentAndStagnationDrainsHappiness()
        {
            stats.Tick(60f);
            Assert.That(stats.health,Is.EqualTo(47f).Within(.001f));
            Assert.That(stats.magicPoints,Is.EqualTo(48f).Within(.001f));
            // 12-second grace, then 48 seconds at -3 happiness per minute.
            Assert.That(stats.happiness,Is.EqualTo(47.6f).Within(.001f));
            Assert.That(stats.development,Is.EqualTo(8));
            Assert.That(stats.IsFullyBored,Is.True);
            Assert.That(stats.IsStagnating,Is.True);
        }

        [Test]
        public void MovingALittleDoesNotResetNoProgressTimerOrHappinessDecay()
        {
            stats.boredom=20f;
            for(int i=0;i<30;i++)stats.Tick(1f); // keeps walking
            // Walking does not relieve boredom; residence is still below 35 seconds.
            Assert.That(stats.boredom,Is.EqualTo(20f));
            Assert.That(stats.happiness,Is.EqualTo(49.1f).Within(.01f));
            Assert.That(stats.SecondsWithoutProgress,Is.EqualTo(30).Within(.01f));
            Assert.That(stats.IsStagnating,Is.True);
            Assert.That(stats.health,Is.EqualTo(50f));
        }

        [Test]
        public void TalkingAndEatingAndKillResetStagnationAndIncreaseHappiness()
        {
            stats.boredom=0;
            stats.Tick(20f);
            int initial=stats.MeaningfulProgressCount;
            stats.Meet("Mira","herbs");
            Assert.That(stats.happiness,Is.GreaterThan(50));
            Assert.That(stats.SecondsWithoutProgress,Is.EqualTo(0));
            Assert.That(stats.MeaningfulProgressCount,Is.EqualTo(initial+1));
            float afterTalking=stats.happiness;
            stats.Tick(10f);
            stats.PickUp(AdventureObjectKind.Herb,"Herb");
            Assert.That(stats.happiness,Is.GreaterThan(afterTalking));
            Assert.That(stats.SecondsWithoutProgress,Is.Zero);
            stats.Tick(10f);
            stats.StrikeMonster("Boar",30,true);
            Assert.That(stats.happiness,Is.GreaterThan(afterTalking));
            Assert.That(stats.SecondsWithoutProgress,Is.Zero);
            Assert.That(stats.monsterKills,Is.EqualTo(1));
        }

        [Test]
        public void MonsterHitCountsAsProgressButDoesNotGrantEarlyVictoryHappiness()
        {
            stats.boredom=0;
            stats.Tick(20f);
            var h=stats.happiness;
            int before=stats.MeaningfulProgressCount;
            stats.StrikeMonster("Boar",24,false);
            Assert.That(stats.happiness,Is.EqualTo(h));
            Assert.That(stats.MeaningfulProgressCount,Is.EqualTo(before+1));
            Assert.That(stats.SecondsWithoutProgress,Is.Zero);
        }

        [Test]
        public void PoisonAndHazardsDoNotCountAsGoodProgress()
        {
            int before=stats.MeaningfulProgressCount;
            float h=stats.happiness;
            stats.PickUp(AdventureObjectKind.PoisonBerry,"Duskberry",true);
            Assert.That(stats.MeaningfulProgressCount,Is.EqualTo(before));
            Assert.That(stats.happiness,Is.LessThan(h));
            stats.TakeDamage("Boar",4);
            Assert.That(stats.MeaningfulProgressCount,Is.EqualTo(before));
        }

        [Test]
        public void PenaltiesClampAtZero()
        {
            stats.health=.1f;stats.magicPoints=.1f;stats.happiness=.1f;
            stats.development=1;
            stats.Tick(240f);
            Assert.That(stats.health,Is.Zero);
            Assert.That(stats.magicPoints,Is.Zero);
            Assert.That(stats.happiness,Is.Zero);
            Assert.That(stats.development,Is.Zero);
        }

        [Test]
        public void StepSizeDoesNotMateriallyChangeDecay()
        {
            var go=new GameObject("TEMP - Happiness Step Test");
            try
            {
                var small=go.AddComponent<AdventureStats>();
                small.health=50;small.magicPoints=50;small.happiness=50;
                small.development=10;small.boredom=100;
                for(int i=0;i<120;i++)small.Tick(.5f);
                stats.Tick(60f);
                Assert.That(small.health,Is.EqualTo(stats.health).Within(.001f));
                Assert.That(small.magicPoints,Is.EqualTo(stats.magicPoints).Within(.001f));
                Assert.That(small.happiness,Is.EqualTo(stats.happiness).Within(.001f));
                Assert.That(small.development,Is.EqualTo(stats.development));
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void OutcomeSeparatesPriorPassiveDecayFromHerbHappiness()
        {
            stats.Tick(60f);
            float beforeHealth=stats.health,beforeHappiness=stats.happiness;
            stats.PickUp(AdventureObjectKind.Herb,"Test herb");
            Assert.That(stats.LastOutcomeDeltas.x,Is.EqualTo(stats.health-beforeHealth).Within(.001f));
            Assert.That(stats.LastOutcomeDeltas.y,Is.Zero);
            Assert.That(stats.LastOutcomeDeltas.z,
                Is.EqualTo(stats.happiness-beforeHappiness).Within(.001f));
            Assert.That(stats.LastOutcomeDeltas.z,Is.EqualTo(6f));
            Assert.That(stats.LastOutcomeDeltas.w,Is.EqualTo(-9f));
        }
    }
}