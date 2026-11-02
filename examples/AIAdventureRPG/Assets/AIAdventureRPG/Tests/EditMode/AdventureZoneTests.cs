using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureZoneTests
    {
        GameObject host;
        AdventureStats stats;
        [SetUp]
        public void Setup()
        {
            host=new GameObject("TEMP - Adventure Zone Test");
            host.transform.position=new Vector3(1f,0f,1f);
            stats=host.AddComponent<AdventureStats>();
            stats.health=80;stats.happiness=60;stats.boredom=0;
        }
        [TearDown]
        public void Teardown()
        {
            if(host!=null)Object.DestroyImmediate(host);
        }

        [Test]
        public void SixtyFourUnitMapIsFourByFourBroadZones()
        {
            Assert.That(stats.zoneCellSize,Is.EqualTo(16));
            Assert.That(AdventureStats.ZoneOf(new Vector3(0,3,0),16),Is.EqualTo(new Vector2Int(0,0)));
            Assert.That(AdventureStats.ZoneOf(new Vector3(15.99f,0,15.99f),16),Is.EqualTo(new Vector2Int(0,0)));
            Assert.That(AdventureStats.ZoneOf(new Vector3(16f,0,63f),16),Is.EqualTo(new Vector2Int(1,3)));
            Assert.That(AdventureStats.ZoneOf(new Vector3(63.9f,0,63.9f),16),Is.EqualTo(new Vector2Int(3,3)));
        }

        [Test]
        public void StandingStillBeforeZoneGraceDoesNotIncreaseBoredom()
        {
            stats.Tick(0f);
            stats.Tick(20f);
            Assert.That(stats.boredom,Is.Zero);
            Assert.That(stats.CurrentZoneFatigueSeconds,Is.EqualTo(20f).Within(.001f));
        }

        [Test]
        public void StayingStillPastZoneGraceRaisesOnlyZoneBasedBoredom()
        {
            stats.Tick(35f);
            Assert.That(stats.boredom,Is.Zero);
            stats.Tick(5f);
            Assert.That(stats.boredom,Is.EqualTo(1.25f).Within(.001f));
            Assert.That(stats.ZoneBoredomLastTick,Is.EqualTo(1.25f).Within(.001f));
        }

        [Test]
        public void LongResidenceInZoneRaisesBoredomEvenWhenMoving()
        {
            stats.Tick(35f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 1:1"));
            Assert.That(stats.boredom,Is.Zero);
            stats.Tick(5f);
            Assert.That(stats.IsZoneOverstayed,Is.True);
            Assert.That(stats.boredom,Is.EqualTo(1.25f).Within(.001f));
            Assert.That(stats.SecondsWithoutProgress,Is.EqualTo(40f).Within(.001f));
            Assert.That(stats.CurrentZoneFatigueSeconds,Is.EqualTo(40f).Within(.001f));
        }

        [Test]
        public void VisitingFreshSectorStopsFatigueAfterConfirmation()
        {
            stats.Tick(40f);
            host.transform.position=new Vector3(18f,0,1f);
            stats.Tick(1f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 1:1"));
            stats.Tick(1f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 2:1"));
            Assert.That(stats.IsZoneOverstayed,Is.False);
            Assert.That(stats.ZonesVisited,Is.EqualTo(2));
            Assert.That(stats.CurrentZoneFatigueSeconds,Is.LessThan(3f));
        }

        [Test]
        public void BoundaryPingPongDoesNotInstantlyClearOldZoneFatigue()
        {
            stats.Tick(40f);
            host.transform.position=new Vector3(17f,0,1f);
            stats.Tick(2f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 2:1"));
            host.transform.position=new Vector3(1f,0,1f);
            stats.Tick(2f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 1:1"));
            Assert.That(stats.IsZoneOverstayed,Is.True);
            Assert.That(stats.CurrentZoneFatigueSeconds,Is.GreaterThan(35f));
        }

        [Test]
        public void FormerZoneEventuallyRecoversWhenExploringElsewhere()
        {
            stats.Tick(40f);
            host.transform.position=new Vector3(18f,0,1f);
            stats.Tick(90f);
            host.transform.position=new Vector3(1f,0,1f);
            stats.Tick(2f);
            Assert.That(stats.CurrentZoneLabel,Is.EqualTo("Zone 1:1"));
            Assert.That(stats.IsZoneOverstayed,Is.False);
        }

        [Test]
        public void ZoneOverstayNeverGivesFreeRewardForMovingAcrossBoundary()
        {
            int events=0;
            stats.OutcomeObserved+=text=>events++;
            stats.Tick(40f);
            host.transform.position=new Vector3(18f,0,1f);
            stats.Tick(2f);
            Assert.That(events,Is.Zero);
        }

        [Test]
        public void ZonePenaltyIsTinyAndPreventedByRealProgressOrReadingSign()
        {
            float reward=AdventureRewardPolicy.ZoneStayPenalty(1f,true,0);
            Assert.That(reward,Is.EqualTo(-.002f).Within(.00001f));
            Assert.That(AdventureRewardPolicy.ZoneStayPenalty(1f,false,0),Is.Zero);
            Assert.That(AdventureRewardPolicy.ZoneStayPenalty(1f,true,1),Is.Zero);
            Assert.That(AdventureRewardPolicy.ZoneStayPenalty(1f,true,0,true),Is.Zero);
            Assert.That(AdventureRewardPolicy.ZoneStayPenalty(4f,true,0),Is.Zero);
            float danger=AdventureRewardPolicy.FromOutcome(new Vector4(-10f,0,0,0),
                "source=Boar action=HitByMonster");
            Assert.That(Mathf.Abs(danger),Is.GreaterThan(Mathf.Abs(reward)*100f));
        }
    }
}
