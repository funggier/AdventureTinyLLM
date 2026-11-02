using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureControlAndDeathTests
    {
        GameObject host;
        AdventureStats stats;
        AdventureAgent actor;

        [SetUp]
        public void Setup()
        {
            host=new GameObject("TEMP - Adventure Mode and Death Tests");
            host.AddComponent<Rigidbody>();
            stats=host.AddComponent<AdventureStats>();
            actor=host.AddComponent<AdventureAgent>();
            host.AddComponent<AdventureTinyController>();
        }

        [TearDown]
        public void Teardown()
        {
            if(host!=null)Object.DestroyImmediate(host);
        }

        [Test]
        public void TrainedInferenceCannotBeSelectedWithUntrainedOrUnavailableTinyAPI()
        {
            Assert.That(actor.ActivePlayMode,Is.EqualTo(AdventurePlayMode.Manual));
            Assert.That(actor.TrySelectPlayMode(AdventurePlayMode.TrainedTiny),Is.False);
            Assert.That(actor.TrySelectPlayMode(AdventurePlayMode.TrainTiny),Is.False);
            Assert.That(actor.mode,Is.EqualTo(ControlMode.Manual));
        }

        [Test]
        public void OllamaAndManualAreIndependentlySelectable()
        {
            Assert.That(actor.TrySelectPlayMode(AdventurePlayMode.Ollama),Is.True);
            Assert.That(actor.ActivePlayMode,Is.EqualTo(AdventurePlayMode.Ollama));
            Assert.That(actor.TrySelectPlayMode(AdventurePlayMode.Manual),Is.True);
            Assert.That(actor.ActivePlayMode,Is.EqualTo(AdventurePlayMode.Manual));
        }

        [Test]
        public void LethalMonsterDamageHasMaximumDeathPenaltyAndDoesNotDoubleReport()
        {
            stats.health=11f;
            int outcomes=0;
            string outcome="";
            stats.OutcomeObserved+=s=>{outcomes++;outcome=s;};
            stats.TakeDamage("Boar",18f);
            Assert.That(stats.health,Is.Zero);
            Assert.That(outcome,Does.Contain(" death=true"));
            Assert.That(AdventureRewardPolicy.FromOutcome(stats.LastOutcomeDeltas,outcome),
                Is.EqualTo(-2f));
            stats.ReportDeath();
            Assert.That(stats.deaths,Is.EqualTo(1));
            Assert.That(outcomes,Is.EqualTo(1),
                "Death after a lethal hit must not train the same action twice");
            stats.ResetLife();
            Assert.That(stats.health,Is.EqualTo(72f));
        }

        [Test]
        public void PassiveDeathProducesOneExplicitHeavyPenalty()
        {
            stats.health=0;
            int outcomes=0;
            string outcome="";
            stats.OutcomeObserved+=s=>{outcomes++;outcome=s;};
            stats.ReportDeath();
            Assert.That(outcomes,Is.EqualTo(1));
            Assert.That(outcome,Does.Contain("source=Self action=Death"));
            Assert.That(outcome,Does.Contain(" death=true"));
            Assert.That(AdventureRewardPolicy.FromOutcome(stats.LastOutcomeDeltas,outcome),
                Is.EqualTo(-2f));
        }

        [Test]
        public void OrdinaryDamageIsLessPunishingThanDeath()
        {
            var change=new Vector4(-18f,0f,-2f,0f);
            float reward=AdventureRewardPolicy.FromOutcome(change,
                "source=Boar action=HitByMonster");
            Assert.That(reward,Is.GreaterThan(-2f));
        }
    }

    public sealed class AdventureHUDLayoutTests
    {
        [TestCase(1600f,900f,1f)]
        [TestCase(1920f,1080f,1.2f)]
        [TestCase(1280f,720f,.8f)]
        [TestCase(1024f,768f,.64f)]
        public void HUDScalesProportionallyAndPanelsRemainInViewport(
            float width,float height,float expectedScale)
        {
            var layout=AdventureHUDLayout.ForScreen(width,height);
            Assert.That(layout.scale,Is.EqualTo(expectedScale).Within(.0001f));
            var panels=new[]{layout.stats,layout.selector,layout.log,layout.controller,
                layout.dialogue,layout.sign,layout.hints};
            foreach(var rect in panels)
            {
                Assert.That(rect.xMin,Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.yMin,Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax,Is.LessThanOrEqualTo(layout.width+.001f));
                Assert.That(rect.yMax,Is.LessThanOrEqualTo(layout.height+.001f));
            }
            Assert.That(layout.selector.xMin,Is.GreaterThanOrEqualTo(layout.stats.xMax));
            Assert.That(layout.selector.xMax,Is.LessThanOrEqualTo(layout.log.xMin));
        }
    }
}
