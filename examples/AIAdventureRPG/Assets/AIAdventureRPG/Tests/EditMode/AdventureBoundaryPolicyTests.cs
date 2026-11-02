using AIAdventureRPG;
using NUnit.Framework;
using UnityEngine;

namespace AIAdventureRPG.Tests
{
    public sealed class AdventureBoundaryPolicyTests
    {
        [Test]
        public void CornerAllowsOnlyDirectionsThatLeadBackIntoWorld()
        {
            bool[] legal=AdventureActionAvailability.ForPosition(
                new Vector3(62,12,2),64,64,2,true);
            Assert.That(legal.Length,Is.EqualTo(9));
            Assert.That(legal[0],Is.False); // cannot idle when stuck at a wall
            Assert.That(legal[1],Is.True); // north
            Assert.That(legal[2],Is.False); // south
            Assert.That(legal[3],Is.True); // west
            Assert.That(legal[4],Is.False); // east
            Assert.That(legal[5],Is.True); // north-west
            Assert.That(legal[6],Is.False);
            Assert.That(legal[7],Is.False);
            Assert.That(legal[8],Is.False);
            Assert.That(AdventureActionAvailability.LegalCount(legal),Is.EqualTo(3));
            Assert.That(AdventureActionAvailability.IsLegal(-.7071f,.7071f,legal),Is.True);
            Assert.That(AdventureActionAvailability.IsLegal(.7071f,-.7071f,legal),Is.False);
        }
        [Test]
        public void AwayFromBoundaryAllNineActionsRemainAvailable()
        {
            var legal=AdventureActionAvailability.ForPosition(
                new Vector3(32,10,32),64,64,2,true);
            Assert.That(AdventureActionAvailability.LegalCount(legal),Is.EqualTo(9));
        }
        [Test]
        public void EscapeCorridorDoesNotAllowImmediateReturnToCorner()
        {
            var trapped=AdventureActionAvailability.ForPosition(
                new Vector3(62,10,2),64,64,2,true,-1,1);
            Assert.That(AdventureActionAvailability.LegalCount(trapped),Is.EqualTo(1));
            Assert.That(trapped[5],Is.True); // only northwest
            var stillEscaping=AdventureActionAvailability.ForPosition(
                new Vector3(57,10,6),64,64,2,true,-1,1);
            Assert.That(AdventureActionAvailability.LegalCount(stillEscaping),Is.EqualTo(1));
            Assert.That(stillEscaping[5],Is.True);
            Assert.That(stillEscaping[8],Is.False); // never return southeast yet
        }

        [Test]
        public void EdgeOnlyEscapeKeepsThreeInwardDirectionsAvailable()
        {
            var bottom=AdventureActionAvailability.ForPosition(
                new Vector3(32,10,2),64,64,2,true,0,1);
            Assert.That(AdventureActionAvailability.LegalCount(bottom),Is.EqualTo(3));
            Assert.That(bottom[1],Is.True);
            Assert.That(bottom[5],Is.True);
            Assert.That(bottom[6],Is.True);
            Assert.That(bottom[2],Is.False);
        }

        [Test]
        public void EvaluateMayIdleButCannotRequestOutwardMovement()
        {
            var legal=AdventureActionAvailability.ForPosition(new Vector3(62,10,2),64,64);
            Assert.That(legal[0],Is.True);
            Assert.That(legal[2],Is.False);
            Assert.That(legal[4],Is.False);
            Assert.That(legal[8],Is.False);
        }
        [Test]
        public void LegalMaskShiftsNearBorderBeforeClampingPosition()
        {
            var legal=AdventureActionAvailability.ForPosition(
                new Vector3(60.5f,10,3.2f),64,64,2,true);
            Assert.That(legal[4],Is.False); // east exceeds 62
            Assert.That(legal[2],Is.False); // south drops below 2
            Assert.That(legal[1],Is.True);
            Assert.That(legal[3],Is.True);
        }
    }
}
