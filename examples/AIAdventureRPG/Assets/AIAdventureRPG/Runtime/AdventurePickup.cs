using UnityEngine;
namespace AIAdventureRPG
{
    [RequireComponent(typeof(Collider))]
    public sealed class AdventurePickup:MonoBehaviour
    {
        public AdventureObjectKind kind=AdventureObjectKind.Herb;
        public string displayName="";
        [Min(2)]public float respawnSeconds=18;
        public bool Available=>!hidden;
        public bool RequiresInteraction=>kind==AdventureObjectKind.SafeBerry||
            kind==AdventureObjectKind.PoisonBerry;
        [Min(.5f)] public float useDistance=2.6f;
        bool hidden;
        public int Pickups{get;private set;}
        float availableAt;
        Renderer[] renderers;
        Collider trigger;
        Vector3 initialPosition;
        void Awake()
        {
            trigger=GetComponent<Collider>();
            trigger.isTrigger=true;
            renderers=GetComponentsInChildren<Renderer>();
            initialPosition=transform.position;
        }
        void Update()
        {
            if(hidden && Time.time>=availableAt)
            {
                hidden=false;
                SetVisible(true);
            }
        }
        public bool Collect(AdventureStats stats,bool consumed=false)
        {
            if(!Available || stats==null)return false;
            stats.PickUp(kind,string.IsNullOrWhiteSpace(displayName)?name:displayName,consumed);
            Pickups++;
            availableAt=Time.time+respawnSeconds;
            hidden=true;
            SetVisible(false);
            return true;
        }
        public bool CanConsume(AdventureAgent actor)
        {
            if(!RequiresInteraction||!Available||actor==null)return false;
            Vector3 to=transform.position-actor.transform.position;to.y=0;
            return to.sqrMagnitude<=useDistance*useDistance &&
                (to.sqrMagnitude<.49f ||
                 Vector3.Dot(actor.transform.forward,to.normalized)>.25f);
        }
        public bool TryConsume(AdventureAgent actor)
        {
            if(!CanConsume(actor))return false;
            var stats=actor.Stats!=null?actor.Stats:actor.GetComponent<AdventureStats>();
            return Collect(stats,true);
        }
        void OnTriggerEnter(Collider other)
        {
            if(RequiresInteraction)return;
            var actor=other.GetComponentInParent<AdventureAgent>();
            if(actor!=null)Collect(actor.Stats);
        }
        void OnTriggerStay(Collider other)
        {
            if(RequiresInteraction)return;
            var actor=other.GetComponentInParent<AdventureAgent>();
            if(actor!=null)Collect(actor.Stats);
        }
        void SetVisible(bool on)
        {
            if(renderers==null)renderers=GetComponentsInChildren<Renderer>(true);
            if(trigger==null)trigger=GetComponent<Collider>();
            foreach(var renderer in renderers)if(renderer!=null)renderer.enabled=on;
            if(trigger!=null)trigger.enabled=on;
        }
        public float Remaining=>Mathf.Max(0f,availableAt-Time.time);
    }
}