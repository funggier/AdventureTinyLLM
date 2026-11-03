using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventurePerceptionMemoryTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;

        static AdventureVision.VisibleThing Sight(int identity,string type,string name,
            float x,float z)
            =>new AdventureVision.VisibleThing{
                Identity=identity,Type=type,Name=name,
                WorldPosition=new Vector3(x,0,z),Distance=Mathf.Sqrt(x*x+z*z),
                Bearing=new Vector3(x,0,z).normalized
            };

        static AdventurePerceptionMemory Build(out GameObject go)
        {
            go=new GameObject("Perception FIFO test");
            go.transform.position=Vector3.zero;
            var actor=go.AddComponent<AdventureAgent>();
            // EditMode AddComponent does not invoke the agent's PlayMode Awake.
            var memory=go.AddComponent<AdventurePerceptionMemory>();
            typeof(AdventureAgent).GetField("<PerceptionMemory>k__BackingField",Flags)
                .SetValue(actor,memory);
            Assert.That(memory,Is.Not.Null);
            // Avoid touching the user's persistent memory file. These EditMode
            // unit tests isolate only the in-memory observation algorithm.
            typeof(AdventurePerceptionMemory).GetField("loaded",Flags)
                .SetValue(memory,true);
            return memory;
        }

        [Test]
        public void WindingAttackTriggersExactlyOncePerWindupWithoutDiscoveryCooldown()
        {
            GameObject go;var memory=Build(out go);
            var monsterObject=new GameObject("Windup alert test monster");
            try
            {
                var monster=monsterObject.AddComponent<AdventureMonster>();
                monster.displayName="Threat test";
                monsterObject.transform.position=new Vector3(2,0,0);
                typeof(AdventureMonster).GetProperty("IsAlive",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureMonster).GetProperty("IsWindingUp",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                var windup=typeof(AdventureMonster).GetProperty("WindupEndsAt",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                int callbacks=0;
                AdventurePerceptionMemory.PerceptionAlert latest=null;
                memory.ImportantChange+=alert=>{callbacks++;latest=alert;};
                windup.SetValue(monster,Time.time+1f,null);

                memory.ObserveAttackWindup(monster,2f);
                memory.ObserveAttackWindup(monster,2f);
                Assert.That(callbacks,Is.EqualTo(1));
                Assert.That(memory.UrgentThreatAlerts,Is.EqualTo(1));
                Assert.That(latest.urgent,Is.True);
                Assert.That(latest.reason,Is.EqualTo("AttackWindup"));

                // No ordinary .65-second discovery cooldown may block the
                // next real attack when the monster never leaves the 9 ring.
                windup.SetValue(monster,Time.time+2f,null);
                memory.ObserveAttackWindup(monster,2f);
                Assert.That(callbacks,Is.EqualTo(2));

                windup.SetValue(monster,Time.time+3f,null);
                memory.ObserveAttackWindup(monster,19f);
                Assert.That(callbacks,Is.EqualTo(2),
                    "A hidden/out-of-range monster cannot trigger an urgent alert.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(monsterObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WindingAttackReplansTinyWithoutRewardingOrTrainingForObservation()
        {
            GameObject go;var memory=Build(out go);
            var monsterObject=new GameObject("Reactive attacker");
            try
            {
                var actor=go.GetComponent<AdventureAgent>();
                var stats=go.GetComponent<AdventureStats>();
                var tiny=go.AddComponent<AdventureTinyController>();
                typeof(AdventureTinyController).GetField("actor",Flags).SetValue(tiny,actor);
                var callback=typeof(AdventureAgent).GetMethod("OnPerceptionAlert",Flags);
                memory.ImportantChange+=(AdventurePerceptionMemory.PerceptionAlert alert)=>
                    callback.Invoke(actor,new object[]{alert});
                actor.mode=ControlMode.Tiny;

                var monster=monsterObject.AddComponent<AdventureMonster>();
                monsterObject.transform.position=new Vector3(3,0,0);
                typeof(AdventureMonster).GetProperty("IsAlive",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureMonster).GetProperty("IsWindingUp",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureMonster).GetProperty("WindupEndsAt",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,Time.time+1f,null);
                var actionId=typeof(AdventureTinyController).GetField(
                    "<LastActionId>k__BackingField",Flags);
                actionId.SetValue(tiny,"old-one-time-credit-id");
                string previous=stats.LastOutcome;
                memory.ObserveAttackWindup(monster,3f);
                Assert.That(tiny.ReactiveReplans,Is.EqualTo(1));
                Assert.That(tiny.Status,Does.Contain("URGENT"));
                Assert.That(tiny.LastActionId,Is.Empty);
                Assert.That(stats.LastOutcome,Is.EqualTo(previous));
                Assert.That(tiny.AcceptedOutcomes,Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(monsterObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FiveSnapshotsPerCoordinateAreFIFO()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                for(int n=1;n<=7;n++)
                    memory.AcceptScan(new [] {Sight(n,"Herb","Plant "+n,4,0)});
                var history=memory.HistoryAt(new Vector3(4,0,0));
                Assert.That(history.Count,Is.EqualTo(5));
                Assert.That(history[0].name,Is.EqualTo("Plant 3"));
                Assert.That(history[4].name,Is.EqualTo("Plant 7"));
                Assert.That(memory.DescribeHistoryAt(new Vector3(4,0,0)),
                    Does.Contain("Plant 7"));
                Assert.That(memory.DescribeHistoryAt(new Vector3(4,0,0)),
                    Does.Not.Contain("Plant 1"));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void StationaryHerbScanDoesNotFloodCoordinateHistory()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                for(int n=0;n<30;n++)
                    memory.AcceptScan(new [] {Sight(100,"Herb","Same herb",4,1)});
                Assert.That(memory.HistoryAt(new Vector3(4,0,1)).Count,Is.EqualTo(1));
                Assert.That(memory.ChangeCountAt(new Vector3(4,0,1)),Is.Zero);
                Assert.That(memory.HasMovedObject(100),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void MonsterMovingLeavesEvidenceAtBothOldAndNewCoordinates()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                memory.AcceptScan(new [] {Sight(101,"Monster","Boar",2,0)});
                memory.AcceptScan(new [] {Sight(101,"Monster","Boar",6,0)});
                Assert.That(memory.HasMovedObject(101),Is.True);
                Assert.That(memory.DescribeHistoryAt(new Vector3(2,0,0)),
                    Does.Contain("MovedAway"));
                Assert.That(memory.DescribeHistoryAt(new Vector3(6,0,0)),
                    Does.Contain("Moved"));
                Assert.That(memory.ChangeCountAt(new Vector3(2,0,0)),
                    Is.GreaterThanOrEqualTo(1));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void NineEighteenHysteresisPreventsEdgeChatter()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                memory.alertRadius=9f;memory.rearmRadius=18f;
                int triggered=0;
                memory.ImportantChange+=_=>triggered++;
                memory.AcceptScan(new [] {Sight(111,"Herb","Border herb",8.9f,0)});
                typeof(AdventurePerceptionMemory).GetMethod("Update",Flags)
                    .Invoke(memory,null);
                Assert.That(triggered,Is.EqualTo(1));

                // Merely crossing the INNER boundary never re-arms this ID.
                memory.AcceptScan(new [] {Sight(111,"Herb","Border herb",9.2f,0)});
                memory.AcceptScan(new [] {Sight(111,"Herb","Border herb",8.8f,0)});
                typeof(AdventurePerceptionMemory).GetMethod("Update",Flags).Invoke(memory,null);
                Assert.That(triggered,Is.EqualTo(1));

                memory.AcceptScan(new [] {Sight(111,"Herb","Border herb",19f,0)});
                memory.AcceptScan(new [] {Sight(111,"Herb","Border herb",8.2f,0)});
                typeof(AdventurePerceptionMemory).GetField("lastDispatch",Flags)
                    .SetValue(memory,-999f);
                typeof(AdventurePerceptionMemory).GetMethod("Update",Flags).Invoke(memory,null);
                Assert.That(triggered,Is.EqualTo(2),
                    "Only an observed >18-unit separation should re-arm");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void DifferentObjectAtSameCoordinateIsNewHistory()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                memory.AcceptScan(new [] {Sight(201,"Herb","Herb A",7,1)});
                memory.AcceptScan(new [] {Sight(202,"Thorn","New thorn",7,1)});
                var history=memory.HistoryAt(new Vector3(7,0,1));
                Assert.That(history.Count,Is.EqualTo(2));
                Assert.That(history[0].kind,Is.EqualTo("Herb"));
                Assert.That(history[1].kind,Is.EqualTo("Thorn"));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void ReactiveDiscoveryReplansTinyWithoutFakeReward()
        {
            GameObject go;var memory=Build(out go);
            try
            {
                var agent=go.GetComponent<AdventureAgent>();
                var stats=go.GetComponent<AdventureStats>();
                var tiny=go.AddComponent<AdventureTinyController>();
                typeof(AdventureTinyController).GetField("actor",Flags)
                    .SetValue(tiny,agent);
                var callback=typeof(AdventureAgent).GetMethod("OnPerceptionAlert",Flags);
                memory.ImportantChange+=(AdventurePerceptionMemory.PerceptionAlert alert)=>
                    callback.Invoke(agent,new object[]{alert});
                agent.mode=ControlMode.Tiny;
                string previous=stats.LastOutcome;
                memory.AcceptScan(new [] {Sight(501,"Herb","New healing herb",3,0)});
                typeof(AdventurePerceptionMemory).GetMethod("Update",Flags).Invoke(memory,null);
                Assert.That(tiny.ReactiveReplans,Is.EqualTo(1));
                Assert.That(stats.LastOutcome,Is.EqualTo(previous),
                    "Seeing an object must not automatically create a reward");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public void WindingUpMonsterBehindAvatarWinsDecisionCoordinateFocus()
        {
            GameObject go;var memory=Build(out go);
            var monsterObject=new GameObject("Rear boar test");
            var worldObject=new GameObject("Rear boar world test");
            try
            {
                var actor=go.GetComponent<AdventureAgent>();
                var vision=go.AddComponent<AdventureVision>();
                var combat=go.AddComponent<AdventureCombatState>();
                monsterObject.transform.position=new Vector3(6,0,0);
                var monster=monsterObject.AddComponent<AdventureMonster>();
                monster.displayName="Rear boar";
                typeof(AdventureMonster).GetProperty("IsAlive",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureMonster).GetProperty("IsWindingUp",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureCombatState).GetProperty("NearestMonster",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(combat,monster,null);
                typeof(AdventureCombatState).GetProperty("NearestMonsterDistance",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(combat,6f,null);

                var herb=Sight(711,"Herb","Front herb",2,0);
                var visible=new List<AdventureVision.VisibleThing>{herb};
                int monsterId=monsterObject.transform.GetEntityId().GetHashCode();
                memory.AcceptScan(new[]{Sight(monsterId,"Monster","Rear boar",4,0)});
                memory.AcceptScan(new[]{Sight(monsterId,"Monster","Rear boar",6,0)});
                memory.AcceptScan(visible);
                Vector3 coordinate;int id;
                Assert.That(memory.TryDecisionFocus(visible,combat,out coordinate,out id),Is.True);
                Assert.That(coordinate.x,Is.EqualTo(6f));
                Assert.That(id,Is.EqualTo(monsterId));
                Assert.That(memory.HasMovedObject(id),Is.True);
                Assert.That(memory.DecisionMapReport(visible,2,combat),
                    Does.Contain("Rear boar"),
                    "A hostile known by combat proximity must enter the Ollama map report.");
                Assert.That(memory.DecisionMapReport(visible,2,combat),
                    Does.Contain("Front herb"),
                    "Ordinary visible resources must remain available as secondary context.");

                // Tiny must consult this same priority coordinate during the
                // actual numeric Encode(), not just in an Ollama text helper.
                var world=worldObject.AddComponent<AdventureWorld>();
                actor.Initialize(world);
                var liveVisible=(List<AdventureVision.VisibleThing>)
                    typeof(AdventureVision).GetField("visible",Flags).GetValue(vision);
                liveVisible.Add(herb);
                var tiny=go.AddComponent<AdventureTinyController>();
                typeof(AdventureTinyController).GetField("actor",Flags).SetValue(tiny,actor);
                typeof(AdventureTinyController).GetField("stats",Flags).SetValue(tiny,actor.Stats);
                float[] observation=tiny.Encode();
                Assert.That(observation.Length,Is.EqualTo(56));
                Assert.That(observation[55],Is.EqualTo(1f),
                    "Tiny must read the moving rear boar's coordinate history, not the static front herb.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(monsterObject);
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MonsterOutsideDetectionDoesNotRevealHiddenCoordinate()
        {
            GameObject go;var memory=Build(out go);
            var monsterObject=new GameObject("Out of ring");
            try
            {
                var combat=go.AddComponent<AdventureCombatState>();
                monsterObject.transform.position=new Vector3(28,0,0);
                var monster=monsterObject.AddComponent<AdventureMonster>();
                typeof(AdventureMonster).GetProperty("IsAlive",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(monster,true,null);
                typeof(AdventureCombatState).GetProperty("NearestMonster",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(combat,monster,null);
                typeof(AdventureCombatState).GetProperty("NearestMonsterDistance",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .SetValue(combat,28f,null);
                Vector3 focus;int identity;
                Assert.That(memory.TryDecisionFocus(null,combat,out focus,out identity),
                    Is.False);
                Assert.That(memory.DecisionMapReport(null,4,combat),
                    Does.Not.Contain("Out of ring"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(monsterObject);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TinyDecisionReadsFiveSnapshotsWithoutChangingOriginalActions()
        {
            GameObject go;var memory=Build(out go);
            GameObject worldObject=new GameObject("Test world");
            try
            {
                var agent=go.GetComponent<AdventureAgent>();
                go.AddComponent<AdventureVision>();
                var world=worldObject.AddComponent<AdventureWorld>();
                agent.Initialize(world);
                var tiny=go.AddComponent<AdventureTinyController>();
                typeof(AdventureTinyController).GetField("actor",Flags)
                    .SetValue(tiny,agent);
                typeof(AdventureTinyController).GetField("stats",Flags)
                    .SetValue(tiny,agent.Stats);
                var seen=new List<AdventureVision.VisibleThing>{
                    Sight(601,"Herb","Wild herb",4,0)
                };
                var field=typeof(AdventureVision).GetField("visible",Flags);
                var actual=(List<AdventureVision.VisibleThing>)field.GetValue(agent.Vision);
                actual.AddRange(seen);
                memory.AcceptScan(seen);
                float[] obs=tiny.Encode();
                Assert.That(obs.Length,Is.EqualTo(56));
                Assert.That(obs[52],Is.GreaterThan(0f),
                    "Current coordinate history must enter the policy state");
                Assert.That(obs[55],Is.Zero,
                    "Static objects must not appear to be moving");
                memory.AcceptScan(new [] {Sight(601,"Herb","Wild herb",8,0)});
                seen[0].WorldPosition=new Vector3(8,0,0);
                obs=tiny.Encode();
                Assert.That(obs[55],Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(worldObject);
            }
        }
    }
}
