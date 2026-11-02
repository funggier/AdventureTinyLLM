using UnityEngine;

namespace AIAdventureRPG
{
    // Terrain and obstacle guard. Only uses geometry available to the avatar.
    // Intervention is exposed to the trainer to avoid rewarding another policy.
    [RequireComponent(typeof(AdventureAgent))]
    public sealed class AdventureNavigation : MonoBehaviour
    {
        public bool enableForAI=true;
        public float lookAhead=1.9f;
        public float maxStepUp=1.2f;
        public float maxDrop=2.2f;
        public float radius=.50f;
        public bool LastIntervened{get;private set;}
        public bool LastBlocked{get;private set;}
        public int AvoidanceCount{get;private set;}
        public string LastReason{get;private set;}="clear";
        public string LastObstacleName{get;private set;}="";
        public Vector3 LastObstaclePosition{get;private set;}
        AdventureAgent actor;
        readonly RaycastHit[] hits=new RaycastHit[16];
        float nextProbe;
        Vector3 lastHeading,lastSteer;
        bool cachedIntervened,cachedBlocked;

        void Awake(){actor=GetComponent<AdventureAgent>();}

        public Vector3 Steer(Vector3 requested)
        {
            if(actor==null)actor=GetComponent<AdventureAgent>();
            LastIntervened=false;LastBlocked=false;
            requested.y=0;
            if(!enableForAI||actor==null||actor.World==null||requested.sqrMagnitude<.0001f)
                return requested;
            var heading=requested.normalized;
            if(Time.time<nextProbe && Vector3.Dot(heading,lastHeading)>.985f)
            {
                LastIntervened=cachedIntervened;
                LastBlocked=cachedBlocked;
                return lastSteer*requested.magnitude;
            }
            nextProbe=Time.time+.13f;
            lastHeading=heading;
            if(IsPassable(heading))
            {
                LastReason="clear";
                lastSteer=heading;cachedIntervened=false;cachedBlocked=false;
                return requested;
            }
            string obstruction=LastReason;
            float[] candidates={40f,-40f,80f,-80f,130f,-130f,180f};
            foreach(float angle in candidates)
            {
                var turn=Quaternion.Euler(0,angle,0)*heading;
                if(!IsPassable(turn))continue;
                LastIntervened=true;
                AvoidanceCount++;
                LastReason="detour around "+obstruction;
                lastSteer=turn;cachedIntervened=true;cachedBlocked=false;
                return turn*requested.magnitude;
            }
            LastIntervened=true;LastBlocked=true;
            AvoidanceCount++;
            LastReason="all nearby paths blocked: "+obstruction;
            lastSteer=Vector3.zero;cachedIntervened=true;cachedBlocked=true;
            return Vector3.zero;
        }
        public bool IsPassable(Vector3 direction)
        {
            if(actor==null||actor.World==null)return false;
            Vector3 pos=actor.transform.position;
            float endX=pos.x+direction.x*lookAhead,endZ=pos.z+direction.z*lookAhead;
            if(endX<=2.4f||endX>=actor.World.Width-2.4f||
                endZ<=2.4f||endZ>=actor.World.Depth-2.4f)
            {
                LastObstacleName="map boundary";
                LastObstaclePosition=new Vector3(endX,pos.y,endZ);
                LastReason="map boundary near x="+endX.ToString("F1")+
                    " z="+endZ.ToString("F1");
                return false;
            }
            float oldY=actor.World.GroundHeight(pos.x,pos.z);
            for(int i=1;i<=3;i++)
            {
                float alpha=i/3f;
                float x=Mathf.Lerp(pos.x,endX,alpha);
                float z=Mathf.Lerp(pos.z,endZ,alpha);
                float ground=actor.World.GroundHeight(x,z);
                if(ground-oldY>maxStepUp||oldY-ground>maxDrop)
                {
                    LastObstacleName="steep terrain";
                    LastObstaclePosition=new Vector3(x,ground,z);
                    LastReason="steep terrain near x="+x.ToString("F1")+" z="+z.ToString("F1");
                    return false;
                }
                oldY=ground;
            }
            Vector3 rayOrigin=pos+Vector3.up*.55f;
            int count=Physics.SphereCastNonAlloc(rayOrigin,radius,direction,hits,lookAhead,
                ~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var hit=hits[i].collider;
                if(hit!=null && !hit.transform.IsChildOf(actor.transform) &&
                   hit.GetComponentInParent<AdventurePickup>()==null &&
                   hit.GetComponentInParent<AdventureNPC>()==null)
                {
                    // Chunk terrain can be at the avatar's feet; only obstacles hit
                    // sufficiently above the terrain surface block the horizontal path.
                    if(hits[i].point.y>pos.y-.05f)
                    {
                        LastObstacleName=hit.name;
                        LastObstaclePosition=hits[i].point;
                        LastReason="obstacle "+hit.name+" near x="+
                            hits[i].point.x.ToString("F1")+" z="+
                            hits[i].point.z.ToString("F1");
                        return false;
                    }
                }
            }
            return true;
        }
    }
}