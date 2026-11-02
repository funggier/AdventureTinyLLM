using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>Observable combat awareness, independent of the ML policy.
    /// Combat engages when a live hostile enters the detection ring or an
    /// attack connects/is attempted. It ends only after distance and grace.
    /// Low HP and MP are independent overlays; both may apply during combat.</summary>
    [RequireComponent(typeof(AdventureAgent),typeof(AdventureStats))]
    public sealed class AdventureCombatState : MonoBehaviour
    {
        [Min(2f)] public float detectionRadius=9f;
        [Min(0f)] public float exitGraceSeconds=1.5f;
        [Range(1f,50f)] public float lowHealthPercent=20f;
        [Range(1f,50f)] public float lowMagicPercent=20f;
        public bool InCombat{get;private set;}
        AdventureStats CurrentStats
        {
            get
            {
                if(stats==null)stats=GetComponent<AdventureStats>();
                return stats;
            }
        }
        public bool LowHP=>CurrentStats!=null&&CurrentStats.health<
            100f*lowHealthPercent/100f;
        public bool LowMP=>CurrentStats!=null&&CurrentStats.magicPoints<
            CurrentStats.maxMagicPoints*lowMagicPercent/100f;
        public string Label=>(InCombat?"COMBAT":"NORMAL")+
            (LowHP?" / LOW HP":"")+(LowMP?" / LOW MP":"");
        public AdventureMonster NearestMonster{get;private set;}
        public float NearestMonsterDistance{get;private set;}=999f;
        public Vector3 MonsterBearing{get;private set;}
        public float NearestVisibleThornDistance{get;private set;}=999f;
        public int CombatTransitions{get;private set;}
        AdventureAgent actor;
        AdventureStats stats;
        float lastEngagedAt,nextScan;
        void Awake()
        {
            actor=GetComponent<AdventureAgent>();
            stats=GetComponent<AdventureStats>();
        }
        public void Engage(AdventureMonster monster=null)
        {
            lastEngagedAt=Time.time;
            if(!InCombat){InCombat=true;CombatTransitions++;}
            if(monster!=null)NearestMonster=monster;
        }
        public void Refresh()
        {
            if(actor==null)actor=GetComponent<AdventureAgent>();
            if(stats==null)stats=GetComponent<AdventureStats>();
            if(actor==null||actor.World==null||!actor.World.HasGroundData)return;
            float best=999f;
            AdventureMonster nearest=null;
            Vector3 bearing=Vector3.zero;
            foreach(var monster in actor.World.Monsters)
            {
                if(monster==null||!monster.IsAlive)continue;
                Vector3 delta=monster.transform.position-transform.position;
                delta.y=0f;
                float distance=delta.magnitude;
                if(distance<best){best=distance;nearest=monster;
                    bearing=distance>.001f?delta/distance:Vector3.zero;}
            }
            NearestMonster=nearest;
            NearestMonsterDistance=best;
            MonsterBearing=bearing;
            float thorn=999f;
            if(actor.Vision!=null)
                foreach(var thing in actor.Vision.Visible)
                    if(thing.Type=="Thorn")thorn=Mathf.Min(thorn,thing.Distance);
            NearestVisibleThornDistance=thorn;
            if(nearest!=null&&best<=detectionRadius)
                Engage(nearest);
            else if(InCombat&&Time.time-lastEngagedAt>=exitGraceSeconds)
            {
                InCombat=false;CombatTransitions++;
            }
        }
        void Update()
        {
            if(Time.time<nextScan)return;
            nextScan=Time.time+.12f;
            Refresh();
        }
        void OnDrawGizmosSelected()
        {
            // Scene View only: visualization of the XZ radial detector.
            Gizmos.color=new Color(1f,.72f,.15f,.65f);
            const int segments=48;
            float r=Mathf.Max(2f,detectionRadius);
            Vector3 center=transform.position+Vector3.up*.1f;
            Vector3 first=center+new Vector3(r,0,0),previous=first;
            for(int i=1;i<=segments;i++)
            {
                float a=2f*Mathf.PI*i/segments;
                var next=center+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);
                Gizmos.DrawLine(previous,next);
                previous=next;
            }
        }
    }
}
