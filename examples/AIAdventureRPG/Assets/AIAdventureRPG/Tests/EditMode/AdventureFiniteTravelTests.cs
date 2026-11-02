using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureFiniteTravelTests
    {
        [Test]
        public void TravelEndsAtActualRequestedDistanceWithoutOvershooting()
        {
            var budget=new AdventureMoveBudget();
            budget.Begin(new Vector2(1f,1f),4f);
            Assert.That(budget.Direction.magnitude,Is.EqualTo(1f).Within(.00001f));
            Assert.That(budget.Allow(3f),Is.EqualTo(3f));
            Assert.That(budget.Advance(3f),Is.False);
            Assert.That(budget.Allow(4f),Is.EqualTo(1f));
            Assert.That(budget.Advance(1f),Is.True);
            Assert.That(budget.Travelled,Is.EqualTo(4f));
            budget.Cancel();
            Assert.That(budget.Active,Is.False);
            Assert.That(budget.Allow(2f),Is.Zero);
        }

        [Test]
        public void BlockedMotionDoesNotConsumeRemainingDistance()
        {
            var budget=new AdventureMoveBudget();
            budget.Begin(Vector2.right,6f);
            Assert.That(budget.Advance(0f),Is.False);
            Assert.That(budget.Remaining,Is.EqualTo(6f));
            Assert.That(budget.Advance(2f),Is.False);
            Assert.That(budget.Remaining,Is.EqualTo(4f));
        }

        [Test]
        public void TravelDistanceIsBoundedAndIdleDoesNotMove()
        {
            var budget=new AdventureMoveBudget();
            budget.Begin(Vector2.right,999f);
            Assert.That(budget.Requested,Is.EqualTo(12f));
            budget.Begin(Vector2.zero,5f);
            Assert.That(budget.Active,Is.False);
            budget.Begin(Vector2.right,float.NaN);
            Assert.That(budget.Active,Is.False);
        }

        [Test]
        public void AICommandIsFiniteButManualMovementSemanticsAreUnchanged()
        {
            var go=new GameObject("Temporary agent finite move");
            try
            {
                go.AddComponent<Rigidbody>();
                go.AddComponent<AdventureStats>();
                var agent=go.AddComponent<AdventureAgent>();
                agent.mode=ControlMode.Tiny;
                agent.Apply(new AdventureAction(1f,0f,distance:2f));
                Assert.That(agent.AIMoveActive,Is.True);
                Assert.That(agent.AIMoveRemaining,Is.EqualTo(2f));
                agent.StopAIMovement("cancelled");
                Assert.That(agent.AIMoveActive,Is.False);
                agent.mode=ControlMode.Manual;
                agent.Apply(new AdventureAction(1f,0f));
                Assert.That(agent.AIMoveActive,Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void AIWalkingFollowsRealDirectionRatherThanUnrelatedFaceCommand()
        {
            var go=new GameObject("Temporary facing movement");
            try
            {
                go.AddComponent<Rigidbody>();
                go.AddComponent<AdventureStats>();
                var agent=go.AddComponent<AdventureAgent>();
                agent.mode=ControlMode.Tiny;
                // Requested movement east, but the model's optional face is west.
                // Do not turn west while the measured movement motor is active.
                go.transform.rotation=Quaternion.identity;
                agent.Apply(new AdventureAction(1f,0f,-1f,0f,distance:4f));
                Assert.That(Quaternion.Angle(go.transform.rotation,Quaternion.identity),
                    Is.LessThan(.01f));
                agent.StopAIMovement();
                // Stationary commands can still turn independently to interact.
                agent.Apply(new AdventureAction(0f,0f,1f,0f));
                // EditMode has Time.deltaTime=0: validate the stationary
                // facing command is retained; Play Mode applies the rotation.
                Assert.That(agent.CurrentAction.faceX,Is.EqualTo(1f));
            }
            finally {Object.DestroyImmediate(go);}
        }

        [Test]
        public void ActualTravelDirectionOverridesOriginalHeadingAndStaysUpright()
        {
            var prior=Quaternion.LookRotation(Vector3.forward);
            var east=AdventureFacing.ForTravel(prior,
                new Vector3(2f,3f,0f),.02f,100f);
            Assert.That(Vector3.Angle(east*Vector3.forward,Vector3.right),Is.LessThan(.01f));
            Assert.That(Vector3.Angle(east*Vector3.up,Vector3.up),Is.LessThan(.01f));
            // The navigator has rerouted us north even though the original
            // command was east. The visual must face north after moving north.
            var detour=AdventureFacing.ForTravel(east,
                new Vector3(0f,-5f,1f),.02f,100f);
            Assert.That(Vector3.Angle(detour*Vector3.forward,Vector3.forward),
                Is.LessThan(.01f));
        }

        [Test]
        public void IdleAndBlockedMotionDoNotRotateAndWalkingTurnsSmoothly()
        {
            var prior=Quaternion.LookRotation(Vector3.forward);
            var noMotion=AdventureFacing.ForTravel(prior,Vector3.up*3f,.02f,9f);
            Assert.That(Quaternion.Angle(prior,noMotion),Is.LessThan(.01f));
            var first=AdventureFacing.ForTravel(prior,Vector3.right,.02f,9f);
            Assert.That(Quaternion.Angle(prior,first),Is.GreaterThan(1f));
            Assert.That(Quaternion.Angle(prior,first),Is.LessThan(89f));
            Assert.That(Quaternion.Angle(first,Quaternion.LookRotation(Vector3.right)),
                Is.LessThan(90f));
        }

        [Test]
        public void EncounterLogRetainsSeenCoordinatesAndActualTravelAndStopsDuplicates()
        {
            var go=new GameObject("Temporary journal");
            try
            {
                var journal=go.AddComponent<AdventureEventJournal>();
                journal.Seen(33,"Herb","Green herb",new Vector3(12f,0f,25f));
                journal.Seen(33,"Herb","Green herb",new Vector3(12f,0f,25f));
                Assert.That(journal.Count,Is.EqualTo(1));
                journal.Moved("blocked",6f,2.5f,new Vector3(14.5f,0f,25f),"rock wall");
                Assert.That(journal.LastMovementStatus,Is.EqualTo(-1f));
                Assert.That(journal.LastTravelledDistance,Is.EqualTo(2.5f));
                Assert.That(journal.Report(),Does.Contain("rock wall"));
                Assert.That(journal.Report(),Does.Contain("x=12.0,z=25.0"));
                Assert.That(journal.Report(),Does.Contain("x=14.5,z=25.0"));
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
