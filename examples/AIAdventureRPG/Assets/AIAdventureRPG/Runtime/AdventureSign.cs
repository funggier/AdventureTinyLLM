using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>Physical in-world text; reading it grants no stats or reward.</summary>
    public sealed class AdventureSign : MonoBehaviour
    {
        [TextArea(2,5)] public string signText = "Read the local survival notes.";
        [Min(1f)] public float readDistance = 4.5f;
        [Range(0,1)] public float facingThreshold = .55f;
        public string displayName = "Trail sign";
        [Min(0f)] public float repeatReadDelay=10f;
        float nextReadTime;
        public bool CanRead(AdventureAgent actor)
        {
            if(actor==null || actor.Stats==null)return false;
            Vector3 direction=transform.position-actor.transform.position;
            direction.y=0f;
            return direction.sqrMagnitude>.001f &&
                direction.sqrMagnitude<=readDistance*readDistance &&
                Vector3.Dot(actor.transform.forward,direction.normalized)>=facingThreshold &&
                actor.Vision!=null && actor.Vision.CanSeeSign(this);
        }
        public bool TryRead(AdventureAgent actor)
        {
            if(Time.time<nextReadTime||!CanRead(actor))return false;
            nextReadTime=Time.time+repeatReadDelay;
            // Sign data is an observation, not an evaluation of correctness.
            actor.ReadSign(this);
            return true;
        }
    }
}