using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureSurvivalTests
    {
        [Test]
        public void PoisonFoodHarmsAndSafeFoodHeals()
        {
            Assert.That(AdventureRules.HealthAfter(50,AdventureObjectKind.SafeBerry),Is.EqualTo(62));
            Assert.That(AdventureRules.HealthAfter(50,AdventureObjectKind.PoisonBerry),Is.EqualTo(18));
            Assert.That(AdventureRules.HealthAfter(90,AdventureObjectKind.SafeBerry),Is.EqualTo(100));
        }

        [Test]
        public void LosingHealthIsFarWorseThanBoredomAndReadingGivesZero()
        {
            float hurt=AdventureRewardPolicy.FromOutcome(new Vector4(-18,0,0,0),
                "source=Wild Boar action=HitByMonster");
            float bored=AdventureRewardPolicy.FromOutcome(new Vector4(0,0,0,25),
                "source=Idle action=Wait");
            float heal=AdventureRewardPolicy.FromOutcome(new Vector4(18,0,0,0),
                "source=Amberberry action=Collect SafeBerry");
            Assert.That(hurt,Is.LessThan(-.6f));
            Assert.That(Mathf.Abs(bored),Is.LessThan(.02f));
            Assert.That(heal,Is.GreaterThan(0));
            Assert.That(AdventureRewardPolicy.FromOutcome(new Vector4(10,0,0,0),
                "source=Camp board action=ReadSign"),Is.EqualTo(0));
        }

        [Test]
        public void DefeatingMonsterIsRewardedButNotMoreThanMajorHealthLoss()
        {
            float kill=AdventureRewardPolicy.FromOutcome(Vector4.zero,
                "source=Wild Boar action=DefeatMonster");
            float attack=AdventureRewardPolicy.FromOutcome(Vector4.zero,
                "source=Wild Boar action=StrikeMonster");
            Assert.That(kill,Is.GreaterThan(attack));
            Assert.That(kill,Is.EqualTo(1.45f).Within(.001f));
            Assert.That(AdventureRewardPolicy.FromOutcome(
                new Vector4(-50,0,0,0),"source=Wild Boar action=HitByMonster"),
                Is.LessThan(-kill));
        }

        [Test]
        public void BerriesRequireDeliberateEatingAndUseInteractOutcome()
        {
            var host=new GameObject("Berry test actor");
            var berryObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                host.AddComponent<Rigidbody>().isKinematic=true;
                var stats=host.AddComponent<AdventureStats>();
                var agent=host.AddComponent<AdventureAgent>();
                berryObject.transform.position=Vector3.forward*1.5f;
                var berry=berryObject.AddComponent<AdventurePickup>();
                berry.kind=AdventureObjectKind.PoisonBerry;
                berry.displayName="Duskberry";
                stats.health=80f;
                Assert.That(berry.RequiresInteraction,Is.True);
                Assert.That(berry.CanConsume(agent),Is.True);
                Assert.That(stats.health,Is.EqualTo(80f)); // proximity alone is harmless
                berryObject.transform.position=host.transform.position;
                Assert.That(berry.CanConsume(agent),Is.True); // overlapping the berry is usable
                Assert.That(berry.TryConsume(agent),Is.True);
                Assert.That(stats.health,Is.EqualTo(48f));
                Assert.That(stats.LastOutcome,Does.Contain("action=Eat PoisonBerry"));
                Assert.That(berry.Available,Is.False);
                Assert.That(berry.TryConsume(agent),Is.False);
            }
            finally
            {
                Object.DestroyImmediate(berryObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NoProgressPenaltyAppliesEvenWhenAvatarWiggles()
        {
            // Physical displacement is intentionally not an argument.
            float penalty=AdventureRewardPolicy.PassiveNoProgressPenalty(1f,35f,0,12f);
            Assert.That(penalty,Is.LessThan(0f));
            Assert.That(penalty,Is.GreaterThan(-.02f));
            float hurt=AdventureRewardPolicy.FromOutcome(
                new Vector4(-10f,0,0,0),"source=Boar action=HitByMonster");
            Assert.That(Mathf.Abs(hurt),Is.GreaterThan(Mathf.Abs(penalty)*10f));
        }

        [Test]
        public void ProgressOrSignOrGracePeriodPreventFalsePenalties()
        {
            Assert.That(AdventureRewardPolicy.PassiveNoProgressPenalty(1f,7f,0,12f),Is.Zero);
            Assert.That(AdventureRewardPolicy.PassiveNoProgressPenalty(1f,20f,1,12f),Is.Zero);
            Assert.That(AdventureRewardPolicy.PassiveNoProgressPenalty(1f,20f,0,12f,true),Is.Zero);
            Assert.That(AdventureRewardPolicy.PassiveNoProgressPenalty(0f,40f,0,12f),Is.Zero);
            Assert.That(AdventureRewardPolicy.PassiveNoProgressPenalty(4f,40f,0,12f),Is.Zero);
        }

        [Test]
        public void NoProgressPenaltyUsesGameSecondsAndIsSmall()
        {
            float normal=AdventureRewardPolicy.PassiveNoProgressPenalty(1f,32f,0,12f);
            float fast=AdventureRewardPolicy.PassiveNoProgressPenalty(1f,32f,0,12f);
            float longer=AdventureRewardPolicy.PassiveNoProgressPenalty(2f,32f,0,12f);
            Assert.That(normal,Is.EqualTo(fast));
            Assert.That(longer,Is.LessThan(normal));
            Assert.That(Mathf.Abs(longer),Is.LessThan(.035f));
        }

        [Test]
        public void SignReadNeverReportsAnOutcomeOrChangesStats()
        {
            var host=new GameObject("Sign test agent");
            var signObject=new GameObject("Sign");
            try
            {
                var agent=host.AddComponent<AdventureAgent>();
                var stats=host.GetComponent<AdventureStats>();
                stats.health=70;stats.magicPoints=12;stats.boredom=37;
                bool signRead=false,gotReward=false;
                agent.SignRead+=(name,text)=>signRead=true;
                stats.OutcomeObserved+=text=>gotReward=true;
                var sign=signObject.AddComponent<AdventureSign>();
                sign.displayName="Notice";sign.signText="Duskberry is poisonous";
                agent.ReadSign(sign);
                Assert.That(signRead,Is.True);
                Assert.That(gotReward,Is.False);
                Assert.That(stats.health,Is.EqualTo(70));
                Assert.That(stats.magicPoints,Is.EqualTo(12));
                Assert.That(stats.boredom,Is.EqualTo(37));
            }
            finally
            {
                Object.DestroyImmediate(signObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MonsterDamageAndKillCreateDistinctGameOutcomes()
        {
            var host=new GameObject("Combat stats");
            try
            {
                var stats=host.AddComponent<AdventureStats>();
                stats.health=60;
                int events=0;
                stats.OutcomeObserved+=text=>events++;
                stats.TakeDamage("Boar",14);
                Assert.That(stats.health,Is.EqualTo(46));
                Assert.That(stats.LastOutcomeDeltas.x,Is.EqualTo(-14));
                stats.StrikeMonster("Boar",24,false);
                stats.StrikeMonster("Boar",18,true);
                Assert.That(stats.monsterKills,Is.EqualTo(1));
                Assert.That(stats.monsterHits,Is.EqualTo(2));
                Assert.That(events,Is.EqualTo(3));
                Assert.That(stats.LastOutcome,Does.Contain("DefeatMonster"));
            }
            finally {Object.DestroyImmediate(host);}
        }
    }
}