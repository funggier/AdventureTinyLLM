using UnityEngine;
using System;
using System.Globalization;
namespace AIAdventureRPG
{
    [RequireComponent(typeof(Rigidbody),typeof(AdventureStats))]
    public sealed class AdventureAgent : MonoBehaviour
    {
        public ControlMode mode=ControlMode.Manual;
        public AdventurePlayMode ActivePlayMode
        {
            get
            {
                if(mode==ControlMode.Ollama)return AdventurePlayMode.Ollama;
                if(mode==ControlMode.Tiny)
                {
                    var tiny=GetComponent<AdventureTinyController>();
                    return tiny!=null&&tiny.trainInRealGame?
                        AdventurePlayMode.TrainTiny:AdventurePlayMode.TrainedTiny;
                }
                return AdventurePlayMode.Manual;
            }
        }
        public bool TrySelectPlayMode(AdventurePlayMode selection)
        {
            var tiny=GetComponent<AdventureTinyController>();
            bool train=selection==AdventurePlayMode.TrainTiny;
            bool trained=selection==AdventurePlayMode.TrainedTiny;
            if(train||trained)
            {
                if(tiny==null||!tiny.ServiceAvailable)return false;
                if(train&&!tiny.TrainingAvailable)return false;
                if(trained&&!tiny.HasTrainedWeights)return false;
            }
            if(ActivePlayMode==selection)return true;
            // Stop the previous policy before selecting the next controller.
            // This also restores game speed after leaving training.
            if(mode==ControlMode.Tiny&&tiny!=null)tiny.LeaveMode();
            if(mode==ControlMode.Ollama)Brain?.LeaveMode();
            StopAIMovement("cancelled");
            mode=selection==AdventurePlayMode.Ollama?ControlMode.Ollama:
                train||trained?ControlMode.Tiny:ControlMode.Manual;
            move=Vector3.zero;
            if(mode==ControlMode.Ollama)Brain?.EnterMode();
            if(mode==ControlMode.Tiny)
            {
                tiny.SetWorldTraining(train);
                tiny.EnterMode();
            }
            Stats?.Record("Controller: "+selection);
            return true;
        }
        public float speed=6.5f, sprintMultiplier=1.45f, turnSpeed=9f;
        public AdventureStats Stats {get;private set;}
        public AdventureWorld World {get;private set;}
        public AdventureBrain Brain {get;private set;}
        public AdventureVision Vision {get;private set;}
        public AdventureNPC CurrentNPC {get;private set;}
        public string CurrentSignMessage{get;private set;}="";
        public event Action<string,string> SignRead;
        float signMessageUntil;
        [Min(.1f)]public float attackDamage=14f;
        [Min(.1f)]public float attackRange=2.9f;
        [Min(.1f)]public float attackCooldown=.72f;
        [Header("Magic combat")]
        [Min(1f)]public float specialAttackDamage=32f;
        [Min(0f)]public float specialAttackMpCost=12f;
        [Min(0f)]public float arcPulseMpCost=20f;
        [Min(0f)]public float healMpCost=25f;
        [Min(1f)]public float arcPulseDamage=26f;
        [Min(1f)]public float arcPulseRadius=5.3f;
        [Min(1f)]public float healAmount=30f;
        AdventureCombatVFX combatEffects;
        public AdventureCombatState CombatState{get;private set;}
        public AdventurePerceptionMemory PerceptionMemory{get;private set;}
        AdventurePickup noticedThorn;
        float noticedThornDistance,moveStartDamageEvents;
        readonly System.Collections.Generic.HashSet<int> evadedThorns=
            new System.Collections.Generic.HashSet<int>();
        float nextAttackAt;
        public bool DialogueOpen=>CurrentNPC!=null && Time.time<dialogueExpires;
        public string CurrentDialogue=>DialogueOpen?CurrentNPC.displayName+": "+CurrentNPC.SpokenWord+" — Good or Bad?":"";
        public bool IsSprinting {get; private set;}
        public AdventureAction CurrentAction {get;private set;}
        public bool NavigationIntervened{get;private set;}
        public AdventureEventJournal Journal{get;private set;}
        readonly AdventureMoveBudget aiBudget=new AdventureMoveBudget();
        public bool AIMoveActive=>mode!=ControlMode.Manual&&aiBudget.Active;
        public float AIMoveRemaining=>aiBudget.Remaining;
        public event Action<string,float,float> AIMoveFinished;
        float blockedMoveSeconds;
        AdventureNavigation navigation;
        Rigidbody body;
        Vector3 move;
        float dialogueExpires,interactCooldown,nextBlockedFeedback;
        public void Initialize(AdventureWorld world)
        {
            World=world;
            Stats=GetComponent<AdventureStats>();
            Brain=GetComponent<AdventureBrain>();
            Vision=GetComponent<AdventureVision>();
            body=GetComponent<Rigidbody>();
            CombatState=GetComponent<AdventureCombatState>();
            BindPerceptionMemory();
        }
        void BindPerceptionMemory()
        {
            if(PerceptionMemory!=null)
                PerceptionMemory.ImportantChange-=OnPerceptionAlert;
            PerceptionMemory=GetComponent<AdventurePerceptionMemory>();
            if(PerceptionMemory==null)
                PerceptionMemory=gameObject.AddComponent<AdventurePerceptionMemory>();
            PerceptionMemory.ImportantChange+=OnPerceptionAlert;
        }
        void Awake()
        {
            Stats=GetComponent<AdventureStats>();Brain=GetComponent<AdventureBrain>();
            Vision=GetComponent<AdventureVision>();body=GetComponent<Rigidbody>();
            navigation=GetComponent<AdventureNavigation>();
            Journal=GetComponent<AdventureEventJournal>();
            if(Journal==null)Journal=gameObject.AddComponent<AdventureEventJournal>();
            combatEffects=GetComponent<AdventureCombatVFX>();
            if(combatEffects==null)combatEffects=gameObject.AddComponent<AdventureCombatVFX>();
            CombatState=GetComponent<AdventureCombatState>();
            if(CombatState==null)CombatState=gameObject.AddComponent<AdventureCombatState>();
            BindPerceptionMemory();
            if(Stats!=null)Stats.OutcomeObserved+=OnObservedOutcome;
        }
        void OnDestroy()
        {
            if(Stats!=null)Stats.OutcomeObserved-=OnObservedOutcome;
            if(PerceptionMemory!=null)PerceptionMemory.ImportantChange-=OnPerceptionAlert;
        }
        void OnPerceptionAlert(AdventurePerceptionMemory.PerceptionAlert alert)
        {
            // Awareness itself carries NO reward; it only interrupts a stale
            // finite travel so the current policy may reconsider the scene.
            if(mode==ControlMode.Manual||alert==null)return;
            StopAIMovement(alert.urgent?"imminent attack":"new perception");
            if(mode==ControlMode.Tiny)
                GetComponent<AdventureTinyController>()?.ReconsiderForPerception(alert.urgent);
            else if(mode==ControlMode.Ollama)
                Brain?.ReconsiderForPerception(alert);
        }
        void OnObservedOutcome(string summary)
        {
            if(Journal!=null)Journal.Record(AdventureEventJournal.Kind.Outcome,
                "Game outcome",summary,transform.position);
            // Do not blindly continue a long command after taking real damage.
            // Give AI a fresh decision with the observed result and coordinates.
            if(mode!=ControlMode.Manual && aiBudget.Active&&
                Stats!=null&&Stats.LastOutcomeDeltas.x<-.001f)
                StopAIMovement("hazard");
        }
        public void StopAIMovement(string reason="cancelled")
        {
            if(!aiBudget.Active)return;
            aiBudget.Cancel();
            move=Vector3.zero;
            Journal?.Moved(reason,aiBudget.Requested,aiBudget.Travelled,transform.position);
            AIMoveFinished?.Invoke(reason,aiBudget.Requested,aiBudget.Travelled);
        }
        void FinishAIMove(string reason,string obstruction="",Vector3? finalPosition=null)
        {
            if(!aiBudget.Active)return;
            aiBudget.Cancel();
            move=Vector3.zero;
            Vector3 final=finalPosition??body.position;
            Journal?.Moved(reason,aiBudget.Requested,aiBudget.Travelled,
                final,obstruction);
            // Only reward avoidance if the AI actually saw the hazard before
            // walking, increased its distance, and suffered no contact damage.
            if(reason=="reached"&&noticedThorn!=null&&noticedThorn.Available&&
                Stats!=null&&Stats.damageEvents==moveStartDamageEvents&&
                noticedThornDistance<4.5f)
            {
                Vector3 away=final-noticedThorn.transform.position;away.y=0;
                int id=noticedThorn.GetEntityId().GetHashCode();
                if(away.magnitude>=Mathf.Max(2f,noticedThornDistance+1.25f)&&
                    evadedThorns.Add(id))
                    Stats.ReportAvoidedThorn(noticedThorn.displayName);
            }
            noticedThorn=null;
            AIMoveFinished?.Invoke(reason,aiBudget.Requested,aiBudget.Travelled);
            if(mode==ControlMode.Ollama&&reason=="blocked")
                Brain?.Remember("Movement blocked: "+Journal.LastMovementReport);
        }
        void Update()
        {
            // A Unity domain reload can retain this native avatar and terrain mesh
            // but clear the managed voxel provider. Never move on fake Y=0 ground.
            if(World==null || !World.HasGroundData){move=Vector3.zero;return;}
            if(CurrentNPC!=null && !DialogueOpen) CurrentNPC=null;
            if(Time.time>=signMessageUntil)CurrentSignMessage="";
            Stats.Tick(Time.deltaTime);
            if(Stats.health<=0) {Stats.ReportDeath();StopAIMovement("death");Stats.ResetLife();body.position=World.SpawnPoint;move=Vector3.zero;}
            if(Input.GetKeyDown(KeyCode.Tab))
            {
                // Keyboard cycling never starts training. Training is opt-in
                // through its dedicated HUD button, not an accidental TAB press.
                if(mode==ControlMode.Manual)TrySelectPlayMode(AdventurePlayMode.Ollama);
                else if(mode==ControlMode.Ollama)
                {
                    if(!TrySelectPlayMode(AdventurePlayMode.TrainedTiny))
                        TrySelectPlayMode(AdventurePlayMode.Manual);
                }
                else TrySelectPlayMode(AdventurePlayMode.Manual);
            }
            if(mode==ControlMode.Manual)
            {
                IsSprinting=Input.GetKey(KeyCode.LeftShift);
                float horizontal=Input.GetAxisRaw("Horizontal");
                float vertical=Input.GetAxisRaw("Vertical");
                var cam=Camera.main==null?null:Camera.main.GetComponent<AdventureCamera>();
                Vector3 intended=cam!=null
                    ?cam.HorizontalRight()*horizontal+cam.HorizontalForward()*vertical
                    :new Vector3(horizontal,0,vertical);
                intended=Vector3.ClampMagnitude(intended,1f);
                AdventureAction a=new AdventureAction(intended.x,intended.z);
                if(Input.GetKeyDown(KeyCode.E)) a.interact=true;
                if(Input.GetKeyDown(KeyCode.F))a.ability=AdventureAbility.SpecialAttack;
                if(Input.GetKeyDown(KeyCode.Q))a.ability=AdventureAbility.ArcPulse;
                if(Input.GetKeyDown(KeyCode.R))a.ability=AdventureAbility.Heal;
                if(Input.GetKeyDown(KeyCode.Alpha1)) a.answer=DialogueAnswer.Good;
                if(Input.GetKeyDown(KeyCode.Alpha2)) a.answer=DialogueAnswer.Bad;
                Apply(a);
            }
            else if(mode==ControlMode.Tiny)
            {
                IsSprinting=false;
                var tiny=GetComponent<AdventureTinyController>();
                if(tiny!=null)tiny.Tick();else Apply(default);
            }
            else {IsSprinting=false;Brain?.Tick(this);}
        }
        public void Apply(AdventureAction a)
        {
            CurrentAction=a;
            Vector3 desired=Vector3.ClampMagnitude(new Vector3(Mathf.Clamp(a.moveX,-1,1),0,Mathf.Clamp(a.moveZ,-1,1)),1);
            // AI steering is evaluated at the point of actual physics movement,
            // not once at command issue. The facing must follow that result.
            NavigationIntervened=false;
            if(mode==ControlMode.Manual)
            {
                // Preserve human movement exactly as before: continuous WASD.
                move=desired*speed*(IsSprinting?sprintMultiplier:1);
            }
            else
            {
                move=Vector3.zero;
                blockedMoveSeconds=0f;
                aiBudget.Begin(new Vector2(a.moveX,a.moveZ),a.moveDistance);
                noticedThorn=null;
                moveStartDamageEvents=Stats==null?0:Stats.damageEvents;
                if(aiBudget.Active&&Vision!=null&&World!=null)
                {
                    float best=4.5f;
                    foreach(var thing in Vision.Visible)
                        if(thing.Type=="Thorn"&&thing.Distance<best)
                            foreach(var pickup in World.Pickups)
                                if(pickup!=null&&pickup.Available&&
                                    pickup.kind==AdventureObjectKind.Thorn&&
                                    (pickup.transform.position-thing.WorldPosition).sqrMagnitude<.2f&&
                                    !evadedThorns.Contains(pickup.GetEntityId().GetHashCode()))
                                {noticedThorn=pickup;best=thing.Distance;break;}
                    noticedThornDistance=best;
                }
                if(aiBudget.Active && Journal!=null)
                    Journal.Record(AdventureEventJournal.Kind.Movement,"command",
                        "Move ("+a.moveX.ToString("F2")+","+a.moveZ.ToString("F2")+") for "+
                        aiBudget.Requested.ToString("F2")+" units",transform.position);
            }
            Vector3 face=new Vector3(a.faceX,0,a.faceZ);
            if(mode!=ControlMode.Manual&&aiBudget.Active)
            {
                // During finite AI travel the actual XZ displacement (which
                // can differ because of navigation/bounds) owns orientation.
                // Explicit facing remains available for stationary interactions.
                face=Vector3.zero;
            }
            else if(face.sqrMagnitude<.01f)face=move;
            if(face.sqrMagnitude>.01f)
            {
                // AI is explicitly facing something while stationary (for
                // conversation or a melee cast). Finish that turn before
                // resolving the action, rather than checking the old heading.
                // Manual turning and AI movement-based facing stay unchanged.
                if(mode!=ControlMode.Manual&&!aiBudget.Active&&
                    (a.interact||a.ability!=AdventureAbility.None||a.answer!=DialogueAnswer.None))
                    transform.rotation=Quaternion.LookRotation(face);
                else transform.rotation=Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(face),Time.deltaTime*turnSpeed);
            }
            if(a.answer!=DialogueAnswer.None && DialogueOpen)
            {
                var npc=CurrentNPC;
                if(npc!=null && npc.FacingCorrect(this))
                {
                    Stats.Reply(npc.displayName,npc.teachingTopic,a.answer);
                }
                CurrentNPC=null;
            }
            if(a.ability!=AdventureAbility.None)TryUseAbility(a.ability);
            else if(a.interact)TryInteract();
        }
        void FixedUpdate()
        {
            if(World==null||!World.HasGroundData||body==null)return;
            Vector3 current=body.position;
            Vector3 velocity=move;
            if(mode!=ControlMode.Manual)
            {
                if(!aiBudget.Active)return;
                Vector3 requestedHeading=new Vector3(aiBudget.Direction.x,0,aiBudget.Direction.y);
                if(navigation==null)navigation=GetComponent<AdventureNavigation>();
                if(navigation!=null)
                {
                    requestedHeading=navigation.Steer(requestedHeading);
                    if(navigation.LastIntervened)
                    {
                        if(!NavigationIntervened&&Journal!=null)
                            Journal.Record(AdventureEventJournal.Kind.Blocked,
                                navigation.LastObstacleName,navigation.LastReason,
                                navigation.LastObstaclePosition);
                        NavigationIntervened=true;
                    }
                }
                velocity=requestedHeading*speed;
            }
            float amount=velocity.magnitude*Time.fixedDeltaTime;
            if(mode!=ControlMode.Manual)
            {
                amount=aiBudget.Allow(amount);
                if(velocity.sqrMagnitude>.00001f)
                    velocity=velocity.normalized*(amount/Mathf.Max(.0001f,Time.fixedDeltaTime));
            }
            Vector3 requested=current+velocity*Time.fixedDeltaTime;
            Vector3 p=requested;
            p.x=Mathf.Clamp(p.x,2f,World.Width-2f);p.z=Mathf.Clamp(p.z,2f,World.Depth-2f);
            float ground=World.GroundHeight(p.x,p.z);
            if(float.IsNaN(ground)||float.IsInfinity(ground))
            {
                move=Vector3.zero;
                if(mode!=ControlMode.Manual)FinishAIMove("blocked","invalid terrain");
                return;
            }
            p.y=ground+.87f;
            if(mode!=ControlMode.Manual && (new Vector2(requested.x-p.x,requested.z-p.z)).sqrMagnitude>.00005f && Time.time>=nextBlockedFeedback)
            {
                nextBlockedFeedback=Time.time+4f;
                if(mode==ControlMode.Ollama) Brain?.Remember("Movement blocked by map boundary at x="+p.x.ToString("F1")+" z="+p.z.ToString("F1")+"; try changing direction");
            }
            if(mode!=ControlMode.Manual)
            {
                float actual=Vector2.Distance(new Vector2(current.x,current.z),
                    new Vector2(p.x,p.z));
                if(actual<.0001f&&(amount>.001f||
                    (navigation!=null&&navigation.LastBlocked)))blockedMoveSeconds+=Time.fixedDeltaTime;
                else blockedMoveSeconds=0f;
                bool reached=aiBudget.Advance(actual);
                // Face the horizontal direction we actually travel, including
                // the detours chosen by AdventureNavigation. Do not pitch when
                // traversing slopes or rotate if movement is blocked.
                body.MoveRotation(AdventureFacing.ForTravel(body.rotation,
                    p-current,Time.fixedDeltaTime,turnSpeed));
                body.MovePosition(p);
                if(reached)FinishAIMove("reached",finalPosition:p);
                else if(blockedMoveSeconds>=.45f ||
                    (navigation!=null&&navigation.LastBlocked&&blockedMoveSeconds>=.35f))
                    FinishAIMove("blocked",navigation!=null&&navigation.LastBlocked?
                        navigation.LastReason:
                        (requested.x!=p.x||requested.z!=p.z?"world boundary":"no progress"),p);
                return;
            }
            body.MovePosition(p);
        }
        public AdventureNPC NearestNPC(float maxDistance=5.5f)
        {
            if(World==null)return null;
            AdventureNPC best=null;float dist=maxDistance;
            foreach(var npc in World.NPCs)
            {
                if(npc==null)continue;
                float d=Vector3.Distance(transform.position,npc.transform.position);
                if(d<dist){best=npc;dist=d;}
            }
            return best;
        }
        public AdventureMonster NearestMonster(float maxDistance=3.5f)
        {
            if(World==null)return null;
            AdventureMonster nearest=null;
            float minDistance=maxDistance;
            foreach(var monster in World.Monsters)
            {
                if(monster==null||!monster.IsAlive)continue;
                Vector3 delta=monster.transform.position-transform.position;
                delta.y=0;
                float distance=delta.magnitude;
                if(distance<minDistance)
                {
                    nearest=monster;minDistance=distance;
                }
            }
            return nearest;
        }
        public bool TryAttackNearest()
        {
            if(Time.time<nextAttackAt)return false;
            var monster=NearestMonster(attackRange);
            if(monster==null){Stats?.ReportAttackMiss("no target");return false;}
            Vector3 toward=monster.transform.position-transform.position;toward.y=0;
            if(toward.sqrMagnitude<.001f ||
                Vector3.Dot(transform.forward,toward.normalized)<.45f)
            {Stats?.ReportAttackMiss("wrong facing");return false;}
            nextAttackAt=Time.time+attackCooldown;
            CombatState?.Engage(monster);
            monster.TakeHit(this,attackDamage);
            combatEffects?.PlayNormal(monster.transform.position);
            return true;
        }
        public bool TryUseAbility(AdventureAbility ability)
        {
            if(ability==AdventureAbility.NormalAttack)return TryAttackNearest();
            if((ability!=AdventureAbility.SpecialAttack&&ability!=AdventureAbility.ArcPulse&&
                ability!=AdventureAbility.Heal)||Stats==null||World==null||!World.HasGroundData)
                return false;
            if(Time.time<nextAttackAt)
            {
                Stats.RejectAbility(ability.ToString(),"cooldown");
                return false;
            }
            float cost=ability==AdventureAbility.SpecialAttack?specialAttackMpCost:
                ability==AdventureAbility.ArcPulse?arcPulseMpCost:healMpCost;
            if(!Stats.HasMP(cost))
            {
                Stats.RejectAbility(ability.ToString(),"insufficient MP");
                return false;
            }
            AdventureMonster target=null;
            if(ability==AdventureAbility.SpecialAttack)
            {
                target=NearestMonster(attackRange);
                if(target==null)
                {
                    Stats.RejectAbility(ability.ToString(),"no nearby monster");
                    return false;
                }
                Vector3 toward=target.transform.position-transform.position;toward.y=0f;
                if(toward.sqrMagnitude<.001f||
                    Vector3.Dot(transform.forward,toward.normalized)<.45f)
                {
                    Stats.RejectAbility(ability.ToString(),"not facing monster");
                    return false;
                }
            }
            if(ability==AdventureAbility.Heal&&Stats.health>=100f)
            {
                Stats.RejectAbility(ability.ToString(),"HP already full");
                return false;
            }
            float beforeMP=Stats.magicPoints;
            if(!Stats.TrySpendMP(cost))return false;
            nextAttackAt=Time.time+Mathf.Max(.1f,attackCooldown);
            if(ability==AdventureAbility.SpecialAttack)
            {
                CombatState?.Engage(target);
                float hp=target.Health;
                bool kill=target.TakeHit(this,specialAttackDamage,false);
                Stats.ResolveAbility("SpecialAttack",beforeMP,hp>target.Health?1:0,kill?1:0);
                combatEffects?.PlaySpecial(transform.position+transform.forward*.8f+Vector3.up,
                    target.transform.position);
                return true;
            }
            if(ability==AdventureAbility.ArcPulse)
            {
                int hits=0,kills=0;
                foreach(var enemy in World.Monsters)
                {
                    if(enemy==null||!enemy.IsAlive)continue;
                    Vector3 delta=enemy.transform.position-transform.position;delta.y=0f;
                    if(delta.sqrMagnitude>arcPulseRadius*arcPulseRadius)continue;
                    CombatState?.Engage(enemy);
                    float hp=enemy.Health;
                    if(enemy.TakeHit(this,arcPulseDamage,false))kills++;
                    if(enemy.Health<hp)hits++;
                }
                Stats.ResolveAbility("ArcPulse",beforeMP,hits,kills);
                combatEffects?.PlayArcPulse(transform.position);
                return true;
            }
            float was=Stats.health;
            Stats.health=AdventureRules.Stat(Stats.health+healAmount);
            Stats.ResolveAbility("Heal",beforeMP,0,0,Stats.health-was);
            combatEffects?.PlayHeal(transform.position);
            return true;
        }
        public void ReadSign(AdventureSign sign)
        {
            if(sign==null)return;
            if(Stats==null)Stats=GetComponent<AdventureStats>();
            if(Stats==null)return;
            CurrentSignMessage=sign.signText;
            signMessageUntil=Time.time+12f;
            Stats.Record("Read "+sign.displayName+": "+sign.signText);
            if(mode==ControlMode.Ollama)Brain?.Remember("Read physical sign "+sign.displayName+": "+sign.signText);
            // Sign reading is observation only: never call OutcomeObserved.
            SignRead?.Invoke(sign.displayName,sign.signText);
        }
        public bool TryReadNearestSign(float radius=5.5f)
        {
            if(World==null||Vision==null)return false;
            foreach(var sign in World.Signs)
                if(sign!=null&&sign.CanRead(this)&&sign.TryRead(this))return true;
            return false;
        }
        public bool TryEatNearestBerry()
        {
            if(World==null)return false;
            AdventurePickup nearest=null;
            float closest=float.MaxValue;
            foreach(var berry in World.Pickups)
            {
                if(berry==null||!berry.CanConsume(this))continue;
                Vector3 delta=berry.transform.position-transform.position;delta.y=0;
                float distance=delta.sqrMagnitude;
                if(distance>=closest)continue;
                closest=distance;nearest=berry;
            }
            return nearest!=null&&nearest.TryConsume(this);
        }
        public bool TryInteract()
        {
            // E is shared by signs, berries, NPC conversations and melee.
            // With no nearby enemy this is NOT a missed melee attack; don't
            // penalize AI for using E on an informational object.
            if(NearestMonster(attackRange)!=null&&TryAttackNearest())return true;
            if(TryReadNearestSign())return true;
            if(TryEatNearestBerry())return true;
            return TryTalkNearest();
        }
        public bool TryTalkNearest()
        {
            if(Time.time<interactCooldown)return false;
            interactCooldown=Time.time+.4f;
            var npc=NearestNPC();return npc!=null&&npc.TryTalk(this);
        }
        public void BeginDialogue(AdventureNPC npc)
        {
            CurrentNPC=npc;dialogueExpires=Time.time+npc.responseWindow;
            if(mode==ControlMode.Ollama)Brain?.Remember("NPC said "+npc.SpokenWord+". Choose GOOD or BAD.");
        }
        public string LocationReport()
        {
            if(World==null)return "Map not initialized";
            Vector3 p=transform.position;
            Vector3 f=transform.forward;
            // +X = east, +Z = north. This is self-location only, not an omniscient world map.
            return "SELF_POSITION: x="+p.x.ToString("F1",CultureInfo.InvariantCulture)+
                " z="+p.z.ToString("F1",CultureInfo.InvariantCulture)+
                " y="+p.y.ToString("F1",CultureInfo.InvariantCulture)+
                "; map_bounds: x=0.."+World.Width.ToString("F0",CultureInfo.InvariantCulture)+
                ", z=0.."+World.Depth.ToString("F0",CultureInfo.InvariantCulture)+
                "; facing: east_x="+f.x.ToString("F2",CultureInfo.InvariantCulture)+
                " north_z="+f.z.ToString("F2",CultureInfo.InvariantCulture)+
                "; directions: +X east, -X west, +Z north, -Z south";
        }
        public Vector3 GoalBearing()
        {
            if(DialogueOpen&&CurrentNPC!=null)
            {
                Vector3 x=CurrentNPC.transform.position-transform.position;x.y=0;return x.normalized;
            }
            var seen=Vision!=null?Vision.NearestVisible():null;
            return seen!=null?seen.Bearing:Vector3.zero;
        }
    }
}