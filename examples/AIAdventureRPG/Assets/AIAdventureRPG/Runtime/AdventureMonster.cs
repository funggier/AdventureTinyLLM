using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>Small hostile world creature, independently simulated from the AI policy.</summary>
    public sealed class AdventureMonster:MonoBehaviour
    {
        public string displayName="Wild Boar";
        [Min(1f)] public float maxHealth=42f;
        [Min(0f)] public float damage=12f;
        [Min(.1f)] public float aggroRange=10f;
        [Min(.1f)] public float attackRange=2.1f;
        [Min(.1f)] public float attackInterval=2.3f;
        [Min(.15f)] public float attackWindup=.55f;
        public bool IsWindingUp{get;private set;}
        public float WindupEndsAt{get;private set;}
        public int DodgedAttacks{get;private set;}
        [Min(.1f)] public float walkSpeed=2.4f;
        [Min(3f)] public float respawnSeconds=40f;
        public float Health{get;private set;}
        public bool IsAlive{get;private set;}
        public int Deaths{get;private set;}
        public int Attacks{get;private set;}
        public AdventureWorld World{get;private set;}
        public AdventureAgent Target{get;private set;}
        public float LastHitTime{get;private set;}
        float respawnAt,nextAttackAt,homeY,nextUpdate;
        Vector3 windupTargetPosition;
        Light attackWarning;
        Vector3 home;
        Renderer[] bodyRenderers;
        Collider[] colliders;

        public void Initialize(AdventureWorld world,AdventureAgent target)
        {
            World=world;Target=target;home=transform.position;homeY=home.y;
            bodyRenderers=GetComponentsInChildren<Renderer>(true);
            colliders=GetComponentsInChildren<Collider>(true);
            Health=maxHealth;IsAlive=true;
        }
        void SetAttackWarning(bool visible)
        {
            if(visible&&attackWarning==null)
            {
                var cue=new GameObject("Monster Attack Warning");
                cue.transform.SetParent(transform,false);
                cue.transform.localPosition=Vector3.up*.9f;
                attackWarning=cue.AddComponent<Light>();
                attackWarning.type=LightType.Point;
                attackWarning.color=new Color(1f,.19f,.10f);
                attackWarning.range=3.5f;
                attackWarning.intensity=4f;
                attackWarning.shadows=LightShadows.None;
            }
            if(attackWarning!=null)attackWarning.enabled=visible;
        }
        void Update()
        {
            if(World==null||!World.HasGroundData||Target==null)return;
            if(!IsAlive)
            {
                if(Time.time>=respawnAt)
                {
                    Health=maxHealth;IsAlive=true;IsWindingUp=false;
                    SetAttackWarning(false);transform.position=home;
                    foreach(var r in bodyRenderers)if(r!=null)r.enabled=true;
                    foreach(var c in colliders)if(c!=null)c.enabled=true;
                }
                return;
            }
            if(Time.time<nextUpdate)return;
            float dt=Mathf.Min(.2f,Mathf.Max(.02f,Time.deltaTime));
            nextUpdate=Time.time+.07f;
            Vector3 to=Target.transform.position-transform.position;to.y=0f;
            float dist=to.magnitude;
            if(Target.Stats==null||Target.Stats.health<=0)
            {IsWindingUp=false;SetAttackWarning(false);return;}
            if(IsWindingUp && Time.time>=WindupEndsAt)
            {
                IsWindingUp=false;
                SetAttackWarning(false);
                if(dist<=attackRange)
                {
                    Attacks++;
                    Target.CombatState?.Engage(this);
                    Target.Stats.TakeDamage(displayName,damage);
                }
                else
                {
                    // A genuine escape requires target displacement since the
                    // strike was telegraphed (not a monster suddenly teleporting).
                    Vector3 moved=Target.transform.position-windupTargetPosition;
                    moved.y=0f;
                    if(moved.sqrMagnitude>=.36f)
                    {
                        DodgedAttacks++;
                        Target.Stats.ReportAvoidedMonsterAttack(displayName);
                    }
                }
            }
            if(dist>aggroRange)return;
            Vector3 dir=to.normalized;
            if(dir.sqrMagnitude>.01f)
                transform.rotation=Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(dir),180f*dt);
            if(dist>attackRange*.85f)
            {
                Vector3 attempt=transform.position+dir*Mathf.Min(walkSpeed*dt,dist-attackRange*.85f);
                float surface=World.GroundHeight(attempt.x,attempt.z);
                float oldGround=World.GroundHeight(transform.position.x,transform.position.z);
                if(!float.IsNaN(surface)&&!float.IsNaN(oldGround)&&
                    !float.IsInfinity(surface)&&!float.IsInfinity(oldGround)&&
                    Mathf.Abs(surface-oldGround)<=1.15f)
                {
                    attempt.y=surface+homeY-World.GroundHeight(home.x,home.z);
                    transform.position=attempt;
                }
            }
            if(dist<=attackRange && !IsWindingUp && Time.time>=nextAttackAt)
            {
                nextAttackAt=Time.time+attackInterval;
                WindupEndsAt=Time.time+Mathf.Max(.15f,attackWindup);
                IsWindingUp=true;
                windupTargetPosition=Target.transform.position;
                SetAttackWarning(true);
                Target.CombatState?.Engage(this);
            }
        }
        public bool TakeHit(AdventureAgent attacker,float amount,bool reportStats=true)
        {
            if(!IsAlive||attacker==null||!World.HasGroundData)return false;
            float before=Health;
            Health=Mathf.Max(0f,Health-Mathf.Max(0f,amount));
            LastHitTime=Time.time;
            bool killed=Health<=0;
            if(reportStats)attacker.Stats.StrikeMonster(displayName,before-Health,killed);
            if(killed)
            {
                Deaths++;
                IsAlive=false;
                IsWindingUp=false;
                SetAttackWarning(false);
                respawnAt=Time.time+respawnSeconds;
                foreach(var r in bodyRenderers)if(r!=null)r.enabled=false;
                foreach(var c in colliders)if(c!=null)c.enabled=false;
            }
            return killed;
        }
    }
}