using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AIAdventureRPG
{
    // Independent Tiny policy. Live training is OPTIONAL and opt-in.
    // Only an actual applied model action ID may receive post-action feedback.
    public sealed class AdventureTinyController : MonoBehaviour
    {
        public string apiEndpoint="http://127.0.0.1:8765";
        public string agentId="tiny-unity";
        [Tooltip("Minimum game-time pause between completed finite AI move commands.")]
        [Min(.1f)] public float decisionInterval=.2f;
        [Min(1)] public int timeoutSeconds=5;
        public bool deterministic=true;
        [Header("Legal move selection at the world boundary")]
        public bool preventBlockedBoundaryActions=true;
        [Min(.5f)] public float boundaryProbe=2f;
        public int LastLegalMoveCount{get;private set;}=9;
        public int StaleBoundaryActionsDiscarded{get;private set;}
        public bool BoundaryEscapeActive{get;private set;}
        public int BoundaryEscapeCount{get;private set;}
        [Min(3f)] public float boundaryEscapeDepth=16f;
        int escapeX,escapeZ;
        [Tooltip("Off: evaluation only. On: post-action Unity events update model if API online training is enabled.")]
        public bool trainInRealGame=false;
        public int SuccessfulActions{get;private set;}
        public int FailedActions{get;private set;}
        public int OnlineUpdates{get;private set;}
        public int AcceptedOutcomes{get;private set;}
        public int RejectedOutcomes{get;private set;}
        public int IdlePenaltiesAccepted{get;private set;}
        public int IdlePenaltiesAttempted{get;private set;}
        public int ZonePenaltiesAccepted{get;private set;}
        public int ZonePenaltiesAttempted{get;private set;}
        public float LastIdlePenalty{get;private set;}
        public string Status{get;private set;}="Not connected";
        public string LastOutcomeStatus{get;private set;}="No observed outcomes";
        public bool ModelTrained{get;private set;}
        public bool ServiceAvailable{get;private set;}
        public bool HasTrainedWeights{get;private set;}
        public bool TrainingAvailable{get;private set;}
        public int TrainingSteps{get;private set;}
        public string ServiceStatus{get;private set;}="Checking Tiny API...";
        float nextHealthCheck;
        bool checkingHealth;
        int requestGeneration;
        public bool InFlight{get;private set;}
        UnityWebRequest activeActRequest;
        public string LastActionId{get;private set;}="";
        public string LastObservation{get;private set;}="";
        public int ObservedSigns{get;private set;}
        public string LastSignText{get;private set;}="";
        public AdventureAction LastAction{get;private set;}
        public string ModeLabel=>trainInRealGame?"TRAIN IN GAME":"EVALUATE";
        [Tooltip("Simulated-time multiplier during real-game training only. Does not accelerate the HTTP model itself.")]
        [Range(1,8)] public int trainingSpeed=1;
        public float AppliedSpeed{get;private set;}=1f;
        public float EffectiveGameSpeed{get;private set;}
        public float RequestsPerRealSecond{get;private set;}
        public float AcceptedOutcomesPerRealMinute{get;private set;}
        public float MeanApiLatencyMs{get;private set;}
        float baselineFixedDeltaTime=.02f;
        float benchmarkStartReal,benchmarkStartGame;
        int benchmarkStartRequests,benchmarkStartOutcomes;
        int timedRequests;
        float accumulatedLatencyMs;
        AdventureAgent actor;
        AdventureStats stats;
        AdventureAction current;
        float nextDecision,issuedAt;
        float appliedAt,lastPassivePenaltyAt;
        int appliedProgressCount;
        bool lastActionReadSign;
        bool resetting;
        bool hadNavigationOverride;
        bool movementForcedByEscape;
        readonly float[] latestDelta=new float[4];
        float prevHP,prevMP,prevHappiness,prevBoredom;

        [Serializable] sealed class HealthResponse
        {
            public bool trained,training_available;
            public int training_steps,online_updates,observation_size;
            public string status,checkpoint,action_protocol;
        }
        [Serializable] sealed class ActRequest
        {
            public string agent_id;
            public float[] observation;
            public bool deterministic;
            public bool[] valid_moves;
        }
        [Serializable] sealed class ActResponse
        {
            public float move_x,move_z,face_x,face_z,move_distance,value;
            public bool interact,model_trained;
            public string ability;
            public string answer,action_id;
        }
        [Serializable] sealed class ResetRequest {public string agent_id;}
        [Serializable] sealed class ExperienceRequest
        {
            public string agent_id,action_id,credit,@event;
            public float reward;
            public bool navigation_override;
        }
        [Serializable] sealed class SignObserveRequest
        {
            public string agent_id,kind,source,text;
        }
        [Serializable] sealed class ExperienceResponse
        {
            public bool accepted;
            public int online_updates;
            public string reason;
        }
        void Awake()
        {
            actor=GetComponent<AdventureAgent>();
            stats=GetComponent<AdventureStats>();
            // Editor Play Mode normally begins at scale 1. Restore on disable/exit.
            baselineFixedDeltaTime=Mathf.Clamp(Time.fixedDeltaTime, .005f, .04f);
        }
        void OnEnable()
        {
            if(actor==null)actor=GetComponent<AdventureAgent>();
            if(stats==null)stats=GetComponent<AdventureStats>();
            if(stats!=null)stats.OutcomeObserved+=CaptureOutcome;
            if(actor!=null)
            {
                actor.SignRead+=OnSignRead;
                actor.AIMoveFinished+=OnMoveFinished;
            }
            Snapshot();
            // Entering Play Mode must never start training automatically.
            SetWorldTraining(false);
            nextHealthCheck=0f;
            ResetSpeedBenchmark();
        }
        void OnDisable()
        {
            BoundaryEscapeActive=false;escapeX=0;escapeZ=0;
            if(stats!=null)stats.OutcomeObserved-=CaptureOutcome;
            if(actor!=null)
            {
                actor.SignRead-=OnSignRead;
                actor.AIMoveFinished-=OnMoveFinished;
            }
            StopAllCoroutines();
            current=default;
            requestGeneration++;
            checkingHealth=false;
            InFlight=false;
            RestoreGameSpeed();
        }
        void OnDestroy(){RestoreGameSpeed();}
        void Update()
        {
            if(Time.realtimeSinceStartup>=nextHealthCheck&&!checkingHealth)
            {
                nextHealthCheck=Time.realtimeSinceStartup+3f;
                StartCoroutine(CheckHealth());
            }
            // Always restore normal speed when control returns to Human / Ollama or evaluation.
            float requested=(actor!=null && actor.mode==ControlMode.Tiny && trainInRealGame)?trainingSpeed:1f;
            if(!Mathf.Approximately(AppliedSpeed,requested))ApplyGameSpeed(requested);
            float elapsed=Time.realtimeSinceStartup-benchmarkStartReal;
            if(elapsed>.01f)
            {
                EffectiveGameSpeed=(Time.time-benchmarkStartGame)/elapsed;
                RequestsPerRealSecond=(SuccessfulActions-benchmarkStartRequests)/elapsed;
                AcceptedOutcomesPerRealMinute=60f*(AcceptedOutcomes-benchmarkStartOutcomes)/elapsed;
            }
        }
        IEnumerator CheckHealth()
        {
            checkingHealth=true;
            using(var request=UnityWebRequest.Get(apiEndpoint.TrimEnd('/')+"/health"))
            {
                request.timeout=3;
                yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var data=JsonUtility.FromJson<HealthResponse>(request.downloadHandler.text);
                        if(data==null||data.status!="ready"||data.observation_size!=56||
                           data.action_protocol!="explicit-action-id-v6-spatial")
                            throw new Exception("Tiny API requires 56-feature spatial perception policy; restart updated service");
                        ServiceAvailable=true;
                        TrainingAvailable=data.training_available;
                        TrainingSteps=data.training_steps;
                        OnlineUpdates=data.online_updates;
                        HasTrainedWeights=data.trained&&data.training_steps>0;
                        ModelTrained=HasTrainedWeights;
                        ServiceStatus=HasTrainedWeights?
                            "Trained model available ("+TrainingSteps+" updates)":
                            "Fresh model: not yet trained";
                    }
                    catch(Exception exception)
                    {
                        ServiceAvailable=false;HasTrainedWeights=false;TrainingAvailable=false;
                        ServiceStatus="Tiny API response invalid: "+exception.Message;
                    }
                }
                else
                {
                    ServiceAvailable=false;HasTrainedWeights=false;TrainingAvailable=false;
                    ServiceStatus="Tiny API unavailable: "+request.error;
                }
            }
            checkingHealth=false;
            if(actor!=null&&actor.mode==ControlMode.Tiny&&
                (!ServiceAvailable||(trainInRealGame&&!TrainingAvailable)||
                (!trainInRealGame&&!HasTrainedWeights)))
            {
                actor.TrySelectPlayMode(AdventurePlayMode.Manual);
            }
        }
        public void LeaveMode()
        {
            requestGeneration++;
            StopAllCoroutines();
            checkingHealth=false;
            InFlight=false;
            current=default;
            LastActionId="";
            RestoreGameSpeed();
            // Leaving Tiny prevents any later /experience from this controller.
            trainInRealGame=false;
            var nav=GetComponent<AdventureNavigation>();
            if(nav!=null)nav.enableForAI=true;
            nextHealthCheck=0f;
        }
        void ResetSpeedBenchmark()
        {
            benchmarkStartReal=Time.realtimeSinceStartup;
            benchmarkStartGame=Time.time;
            benchmarkStartRequests=SuccessfulActions;
            benchmarkStartOutcomes=AcceptedOutcomes;
            EffectiveGameSpeed=0f;
            RequestsPerRealSecond=0f;
            AcceptedOutcomesPerRealMinute=0f;
        }
        void ApplyGameSpeed(float speed)
        {
            speed=Mathf.Clamp(speed,1f,8f);
            Time.timeScale=speed;
            // Moderate physics step growth: avoids 8x physics calls while keeping
            // movement increments bounded for the existing kinematic controller.
            Time.fixedDeltaTime=baselineFixedDeltaTime*Mathf.Sqrt(speed);
            AppliedSpeed=speed;
            ResetSpeedBenchmark();
        }
        void RestoreGameSpeed()
        {
            Time.timeScale=1f;
            Time.fixedDeltaTime=baselineFixedDeltaTime;
            AppliedSpeed=1f;
        }
        public void SetTrainingSpeed(int multiplier)
        {
            if(multiplier!=1&&multiplier!=2&&multiplier!=4&&multiplier!=8)
                throw new ArgumentOutOfRangeException("multiplier","Use 1, 2, 4, or 8");
            trainingSpeed=multiplier;
            ApplyGameSpeed(actor!=null&&actor.mode==ControlMode.Tiny&&trainInRealGame?trainingSpeed:1);
        }
        public void SetWorldTraining(bool enabled)
        {
            trainInRealGame=enabled;
            ApplyGameSpeed(actor!=null&&actor.mode==ControlMode.Tiny&&enabled?trainingSpeed:1);
            // Raw policy in training: avoid contaminating the policy gradient with
            // a handcrafted steering assistant. Evaluation can use that assistant.
            var nav=GetComponent<AdventureNavigation>();
            if(nav!=null)nav.enableForAI=!enabled;
            LastActionId="";
            lastPassivePenaltyAt=Time.time;
            LastOutcomeStatus=enabled?"Training: stochastic exploration + event/idle rewards":"Evaluation, weights frozen; navigation assistance on";
        }
        void Snapshot()
        {
            if(stats==null)return;
            prevHP=stats.health;prevMP=stats.magicPoints;
            prevHappiness=stats.happiness;prevBoredom=stats.boredom;
        }
        void OnSignRead(string source,string text)
        {
            if(actor==null||actor.mode!=ControlMode.Tiny)return;
            LastSignText=text;
            lastActionReadSign=true;
            StartCoroutine(SendSignObservation(source,text));
        }
        IEnumerator SendSignObservation(string source,string text)
        {
            byte[] body=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SignObserveRequest{
                agent_id=agentId,kind="sign",source=source,text=text}));
            using(var request=new UnityWebRequest(apiEndpoint.TrimEnd('/')+"/observe","POST"))
            {
                request.uploadHandler=new UploadHandlerRaw(body);
                request.downloadHandler=new DownloadHandlerBuffer();
                request.timeout=timeoutSeconds;
                request.SetRequestHeader("Content-Type","application/json");
                yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success)ObservedSigns++;
                else LastOutcomeStatus="Sign data API: "+request.responseCode+" "+request.error;
                // Sign has zero Reward by definition. No /experience call.
            }
        }
        bool IsNearbyNaturalEvent(string outcome)
        {
            if(actor==null||actor.World==null)return false;
            int start=outcome.IndexOf("source=",StringComparison.Ordinal);
            int end=outcome.IndexOf(" action=",StringComparison.Ordinal);
            if(start<0||end<=start+7)return false;
            string source=outcome.Substring(start+7,end-(start+7));
            foreach(var pickup in actor.World.Pickups)
                if(pickup!=null&&(pickup.name==source||pickup.displayName==source))
                {
                    Vector3 v=actor.transform.position-pickup.transform.position;
                    v.y=0;
                    if(v.sqrMagnitude<8f)return true;
                }
            foreach(var npc in actor.World.NPCs)
                if(npc!=null&&npc.displayName==source)
                {
                    Vector3 v=actor.transform.position-npc.transform.position;
                    v.y=0;
                    if(v.sqrMagnitude<npc.talkDistance*npc.talkDistance)return true;
                }
            foreach(var monster in actor.World.Monsters)
                if(monster!=null&&monster.displayName==source)
                {
                    Vector3 d=actor.transform.position-monster.transform.position;
                    d.y=0;
                    if(d.sqrMagnitude<15f)return true;
                }
            return false;
        }
        void OnMoveFinished(string result,float requested,float actual)
        {
            if(result!="blocked"||actor==null||actor.mode!=ControlMode.Tiny||
                !trainInRealGame||string.IsNullOrEmpty(LastActionId))return;
            if(movementForcedByEscape)
            {
                LastOutcomeStatus="Forced boundary escape excluded from policy credit";
                LastActionId="";
                return;
            }
            // Failure of the requested heading is the model's own negative
            // outcome, even if collision avoidance tried but found no path.
            // Never credit a successful detour as a learned move.
            string id=LastActionId;
            LastActionId="";
            LastOutcomeStatus="Blocked after "+actual.ToString("F1")+
                " of "+requested.ToString("F1")+" units";
            // Real obstruction, single action ID, direction + distance gradient.
            StartCoroutine(SendExperience(id,"move",-.12f,
                "source=World action=Blocked at x="+
                actor.Journal.LastStopPosition.x.ToString("F1")+" z="+
                actor.Journal.LastStopPosition.z.ToString("F1"),false));
        }
        void CaptureOutcome(string outcome)
        {
            // Use only this interaction's actual deltas. Continuous boredom
            // penalties between interactions must not be assigned to a pickup.
            Vector4 changes=stats.LastOutcomeDeltas;
            latestDelta[0]=Mathf.Clamp(changes.x/100f,-1f,1f);
            latestDelta[1]=Mathf.Clamp(changes.y/100f,-1f,1f);
            latestDelta[2]=Mathf.Clamp(changes.z/100f,-1f,1f);
            latestDelta[3]=Mathf.Clamp(changes.w/100f,-1f,1f);
            Snapshot();
            if(actor==null||actor.mode!=ControlMode.Tiny||!trainInRealGame)return;
            string actionId=LastActionId;
            if(string.IsNullOrEmpty(actionId)||Time.time-issuedAt>3f)
            {
                LastOutcomeStatus="No recent applied Tiny action for this outcome";
                RejectedOutcomes++;
                return;
            }
            // Passive death can originate from zone exhaustion, not an object.
            bool passiveDeath=outcome.Contains("source=Self action=Death");
            bool movementOutcome=outcome.Contains("source=Self action=DodgeMonster")||
                outcome.Contains("source=Self action=AvoidThorn");
            bool normalMiss=outcome.Contains("source=Self action=AttackMiss");
            bool abilityOutcome=(outcome.Contains("source=Self action=Ability ")||
                outcome.Contains("source=Self action=AbilityRejected ")) &&
                actor.CurrentAction.ability!=AdventureAbility.None;
            if(!passiveDeath&&!abilityOutcome&&!movementOutcome&&!normalMiss&&
                !IsNearbyNaturalEvent(outcome))
            {
                LastOutcomeStatus="Outcome source is not near avatar; not training";
                RejectedOutcomes++;
                return;
            }
            string credit=abilityOutcome?"ability":
                          movementOutcome?"move":
                          normalMiss?"interact":
                          outcome.Contains("action=Reply ")?"answer":
                          outcome.Contains("action=Talk;")||outcome.Contains("action=DefeatMonster")||
                          outcome.Contains("action=StrikeMonster")||
                          outcome.Contains("action=Eat ")?"interact":"move";
            // Direction during boundary recovery was constrained by a safety
            // rule, not learned; do not claim its movement outcomes as policy skill.
            if(credit=="move"&&movementForcedByEscape)
            {
                LastActionId="";
                LastOutcomeStatus="Boundary escape movement excluded from RL credit";
                return;
            }
            float reward=AdventureRewardPolicy.FromOutcome(changes,outcome);
            // Consume the local ID: duplicate triggers must not receive reward twice.
            LastActionId="";
            StartCoroutine(SendExperience(actionId,credit,reward,outcome,
                credit=="move"&&hadNavigationOverride));
        }
        void FinalizePassiveBoredom()
        {
            if(actor==null||stats==null||actor.mode!=ControlMode.Tiny||
                !trainInRealGame||resetting||string.IsNullOrEmpty(LastActionId)||
                movementForcedByEscape)return;
            float seconds=Time.time-appliedAt;
            int recentProgress=stats.MeaningfulProgressCount-appliedProgressCount;
            float noProgress=AdventureRewardPolicy.PassiveNoProgressPenalty(
                seconds,stats.SecondsWithoutProgress,recentProgress,
                stats.progressGraceSeconds,lastActionReadSign);
            float zoneStay=AdventureRewardPolicy.ZoneStayPenalty(
                seconds,stats.IsZoneOverstayed,recentProgress,lastActionReadSign);
            // Single reward/optimizer update for this action, never two
            // independently attributed penalties from one movement command.
            float penalty=Mathf.Max(-.045f,noProgress+zoneStay);
            // Limit gradient/checkpoint calls under Fast Training.
            if(penalty>=0f||Time.time-lastPassivePenaltyAt<1.5f||
               hadNavigationOverride)return;
            string id=LastActionId;
            LastActionId="";
            lastPassivePenaltyAt=Time.time;
            LastIdlePenalty=penalty;
            IdlePenaltiesAttempted++;
            if(zoneStay<0f)ZonePenaltiesAttempted++;
            StartCoroutine(SendExperience(id,"move",penalty,
                "source=Self action="+(noProgress<0f?"PassiveNoProgress":"PassiveZoneStay")+
                " zoneOverstay="+(zoneStay<0f?"true":"false")+
                " at applied_action_end",false));
        }
        IEnumerator SendExperience(string actionId,string credit,float reward,string outcome,bool intervention)
        {
            byte[] data=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ExperienceRequest{
                agent_id=agentId,action_id=actionId,credit=credit,reward=reward,
                navigation_override=intervention,
                @event=outcome.Length>200?outcome.Substring(0,200):outcome}));
            using(var req=new UnityWebRequest(apiEndpoint.TrimEnd('/')+"/experience","POST"))
            {
                req.uploadHandler=new UploadHandlerRaw(data);
                req.downloadHandler=new DownloadHandlerBuffer();
                req.timeout=timeoutSeconds;
                req.SetRequestHeader("Content-Type","application/json");
                yield return req.SendWebRequest();
                if(req.result==UnityWebRequest.Result.Success)
                {
                    var result=JsonUtility.FromJson<ExperienceResponse>(req.downloadHandler.text);
                    if(result!=null)
                    {
                        OnlineUpdates=result.online_updates;
                        if(result.accepted)
                        {
                            AcceptedOutcomes++;
                            if(outcome.Contains("action=PassiveNoProgress")||
                                outcome.Contains("action=PassiveZoneStay"))IdlePenaltiesAccepted++;
                            if(outcome.Contains("zoneOverstay=true"))ZonePenaltiesAccepted++;
                        }
                        else RejectedOutcomes++;
                        LastOutcomeStatus=result.accepted?"Game reward "+reward.ToString("F2")+" credited "+actionId.Substring(0,8):
                            "Not trained: "+result.reason;
                    }
                }
                else
                {
                    RejectedOutcomes++;
                    LastOutcomeStatus="Outcome API: "+req.responseCode+" "+req.error;
                }
            }
        }
        public void EnterMode()
        {
            nextDecision=0;
            current=default;
            LastActionId="";
            hadNavigationOverride=false;
            lastPassivePenaltyAt=Time.time;
            resetting=true;
            Snapshot();
            StartCoroutine(ResetHistory());
        }
        IEnumerator ResetHistory()
        {
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ResetRequest{agent_id=agentId}));
            using(var request=new UnityWebRequest(apiEndpoint.TrimEnd('/')+"/reset","POST"))
            {
                request.uploadHandler=new UploadHandlerRaw(bytes);
                request.downloadHandler=new DownloadHandlerBuffer();
                request.timeout=timeoutSeconds;
                request.SetRequestHeader("Content-Type","application/json");
                yield return request.SendWebRequest();
            }
            resetting=false;
        }
        public int ReactiveReplans{get;private set;}
        public void ReconsiderForPerception()
        {
            if(actor==null||actor.mode!=ControlMode.Tiny)return;
            // Discard stale in-flight policy responses and one-time action
            // attribution. No reward is assigned for mere detection.
            requestGeneration++;
            activeActRequest?.Abort();
            LastActionId="";
            nextDecision=Time.time;
            ReactiveReplans++;
            Status="New object detected: refreshing Tiny observation";
        }
        public void Tick()
        {
            if(actor==null||actor.World==null)return;
            if(resetting)return;
            // The physics motor owns the finite travel until its distance is
            // consumed; do not re-apply a direction every Update frame.
            if(actor.AIMoveActive)
            {
                if(actor.NavigationIntervened)hadNavigationOverride=true;
                return;
            }
            if(Time.time>=nextDecision&&!InFlight)
            {
                nextDecision=Time.time+Mathf.Max(.1f,decisionInterval);
                StartCoroutine(RequestAction());
            }
        }
        public float[] Encode()
        {
            var v=new float[56];
            if(actor==null||actor.World==null)return v;
            Vector3 p=actor.transform.position,forward=actor.transform.forward;
            float width=Mathf.Max(1,actor.World.Width),depth=Mathf.Max(1,actor.World.Depth);
            v[0]=p.x/width;v[1]=p.z/depth;v[2]=p.y/32f;
            v[3]=stats.health/100f;v[4]=stats.magicPoints/Mathf.Max(1f,stats.maxMagicPoints);
            v[5]=stats.happiness/100f;v[6]=stats.boredom/100f;
            v[7]=forward.x;v[8]=forward.z;
            v[9]=actor.DialogueOpen?1:0;
            var seen=actor.Vision==null?null:actor.Vision.NearestVisible();
            bool monsterVisible=false;
            // Only imminent hostile threats replace the nearest object.
            // A distant boar should not conceal nearby food from the model.
            // Reuse the 32-feature shape; v30=-1 means a close hostile.
            if(actor.Vision!=null)
                foreach(var thing in actor.Vision.Visible)
                    if(thing.Type=="Monster"&&thing.Distance<=12f)
                        {seen=thing;monsterVisible=true;break;}
            if(seen!=null)
            {
                if(seen.Type=="Herb")v[10]=1;
                else if(seen.Type=="Thorn")v[11]=1;
                else if(seen.Type=="LoreStone")v[12]=1;
                else if(seen.Type=="NPC")v[13]=1;
                else if(seen.Type=="Berry")
                {
                    // v10=.5 says berry, v11 is its observed red-blue tint.
                    // Neither carries the hidden toxic/safe gameplay enum.
                    v[10]=.5f;
                    v[11]=seen.ColorRedMinusBlue;
                }
                else if(seen.Type=="Sign")v[13]=.5f;
                v[14]=seen.Bearing.x;v[15]=seen.Bearing.z;
                v[16]=Mathf.Clamp01(seen.Distance/30f);
                v[17]=seen.WorldPosition.x/width;v[18]=seen.WorldPosition.z/depth;
            }
            v[19]=actor.Vision==null?0:Mathf.Clamp01(actor.Vision.Visible.Count/10f);
            v[20]=latestDelta[0];v[21]=latestDelta[1];
            v[22]=latestDelta[2];v[23]=latestDelta[3];
            v[24]=p.x<4?1:0;v[25]=p.x>width-4?1:0;
            v[26]=p.z<4?1:0;v[27]=p.z>depth-4?1:0;
            // Replace global clock phase with locally observable zone fatigue.
            // 0.5 means the current zone has just become stale.
            v[28]=Mathf.Clamp01(stats.CurrentZoneFatigueSeconds/
                Mathf.Max(1f,stats.zoneGraceSeconds)*.5f);
            v[29]=Mathf.Clamp01(stats.discoveries/100f);
            var npc=actor.NearestNPC();
            v[30]=monsterVisible?-1f:(npc!=null&&npc.FacingCorrect(actor)?1f:0f);
            v[31]=1;
            // Episode feedback from actual measured travel and physical vision;
            // original 32 features retain their established ordering.
            var journal=actor.Journal;
            if(journal!=null)
            {
                v[32]=Mathf.Clamp01(journal.LastRequestedDistance/12f);
                v[33]=Mathf.Clamp01(journal.LastTravelledDistance/12f);
                v[34]=journal.LastMovementStatus;
                v[35]=Mathf.Clamp01(journal.LastStopPosition.x/width);
                v[36]=Mathf.Clamp01(journal.LastStopPosition.z/depth);
                v[37]=journal.LastSeenKind;
                v[38]=Mathf.Clamp01(journal.LastSeenPosition.x/width);
                v[39]=Mathf.Clamp01(journal.LastSeenPosition.z/depth);
            }
            v[40]=stats.HasMP(actor.specialAttackMpCost)?1f:0f;
            v[41]=stats.HasMP(actor.arcPulseMpCost)?1f:0f;
            v[42]=stats.HasMP(actor.healMpCost)&&stats.health<100f?1f:0f;
            var combat=actor.CombatState;
            if(combat!=null)
            {
                combat.Refresh();
                v[43]=combat.InCombat?1f:0f;
                v[44]=combat.LowHP?1f:0f;
                v[45]=combat.LowMP?1f:0f;
                bool sensed=combat.NearestMonster!=null&&
                    combat.NearestMonsterDistance<=combat.detectionRadius;
                v[46]=sensed?Mathf.Clamp01(combat.NearestMonsterDistance/
                    Mathf.Max(1f,combat.detectionRadius)):1f;
                v[47]=sensed?combat.MonsterBearing.x:0f;
                v[48]=sensed?combat.MonsterBearing.z:0f;
                v[49]=sensed&&combat.NearestMonster.IsWindingUp?1f:0f;
                v[50]=combat.NearestVisibleThornDistance<999f?
                    Mathf.Clamp01(combat.NearestVisibleThornDistance/12f):1f;
            }
            var memory=actor.PerceptionMemory;
            if(memory!=null)
            {
                // The past five coordinate snapshots are looked up at the
                // policy DECISION moment; no stale cache from prior actions.
                v[51]=memory.HasRecentAlert()?1f:0f;
                Vector3 focus;int objectId;
                if(memory.TryDecisionFocus(actor.Vision==null?null:actor.Vision.Visible,
                    combat,out focus,out objectId))
                {
                    var history=memory.HistoryAt(focus);
                    int changes=memory.ChangeCountAt(focus);
                    v[52]=Mathf.Clamp01(history.Count/5f);
                    v[53]=Mathf.Clamp01(changes/5f);
                    v[54]=history.Count>1?
                        Mathf.Clamp01(1f-changes/(float)(history.Count-1)):0f;
                    v[55]=memory.HasMovedObject(objectId)?1f:0f;
                }
            }
            return v;
        }
        void RefreshBoundaryEscape()
        {
            if(!preventBlockedBoundaryActions||actor==null||actor.World==null||stats==null)
            {
                BoundaryEscapeActive=false;escapeX=0;escapeZ=0;
                return;
            }
            Vector3 p=actor.transform.position;
            float maxX=actor.World.Width-2f,maxZ=actor.World.Depth-2f;
            if(BoundaryEscapeActive)
            {
                float depth=Mathf.Max(3f,boundaryEscapeDepth);
                bool xClear=escapeX==0||(escapeX>0?p.x>=2f+depth:p.x<=maxX-depth);
                bool zClear=escapeZ==0||(escapeZ>0?p.z>=2f+depth:p.z<=maxZ-depth);
                if(xClear&&zClear)
                {
                    BoundaryEscapeActive=false;escapeX=0;escapeZ=0;
                }
                return;
            }
            if(!stats.IsStagnating)return;
            // Intervene at the outer rim before the agent can return to the
            // exact corner; do not wait for the hard clamp at x/z = 2/62.
            float near=Mathf.Min(8f,Mathf.Max(3f,boundaryEscapeDepth));
            escapeX=p.x<=2f+near?1:p.x>=maxX-near?-1:0;
            escapeZ=p.z<=2f+near?1:p.z>=maxZ-near?-1:0;
            if(escapeX!=0||escapeZ!=0)
            {
                BoundaryEscapeActive=true;
                BoundaryEscapeCount++;
            }
        }
        IEnumerator RequestAction()
        {
            int generation=requestGeneration;
            InFlight=true;
            float startedReal=Time.realtimeSinceStartup;
            float[] values=Encode();
            LastObservation="X="+values[0].ToString("F2")+" Z="+values[1].ToString("F2")+
                " HP="+values[3].ToString("F2")+" MP="+values[4].ToString("F2")+
                " combat="+values[43]+" lowHP="+values[44]+" lowMP="+values[45]+
                " monster="+values[46]+" windup="+values[49];
            RefreshBoundaryEscape();
            bool[] legal=preventBlockedBoundaryActions?
                AdventureActionAvailability.ForPosition(actor.transform.position,
                    actor.World.Width,actor.World.Depth,boundaryProbe,stats.IsStagnating,
                    escapeX,escapeZ):null;
            LastLegalMoveCount=legal==null?9:AdventureActionAvailability.LegalCount(legal);
            byte[] body=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new ActRequest{agent_id=agentId,observation=values,
                    deterministic=!trainInRealGame&&deterministic,valid_moves=legal}));
            using(var request=new UnityWebRequest(apiEndpoint.TrimEnd('/')+"/act","POST"))
            {
                activeActRequest=request;
                request.uploadHandler=new UploadHandlerRaw(body);
                request.downloadHandler=new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type","application/json");
                request.timeout=timeoutSeconds;
                yield return request.SendWebRequest();
                if(ReferenceEquals(activeActRequest,request))activeActRequest=null;
                if(generation!=requestGeneration||actor==null||actor.mode!=ControlMode.Tiny)
                {
                    InFlight=false;
                    yield break;
                }
                accumulatedLatencyMs+=(Time.realtimeSinceStartup-startedReal)*1000f;
                timedRequests++;
                MeanApiLatencyMs=accumulatedLatencyMs/timedRequests;
                if(request.result!=UnityWebRequest.Result.Success)
                {
                    FailedActions++;LastActionId="";
                    Status="Tiny API: "+request.error;current=default;
                }
                else
                {
                    try
                    {
                        var r=JsonUtility.FromJson<ActResponse>(request.downloadHandler.text);
                        if(r==null||string.IsNullOrEmpty(r.action_id)||float.IsNaN(r.move_x)||
                           float.IsInfinity(r.move_x)||float.IsNaN(r.move_z)||float.IsInfinity(r.move_z)||
                           float.IsNaN(r.move_distance)||float.IsInfinity(r.move_distance)||
                           r.move_distance<0f||r.move_distance>12f)
                            throw new Exception("Incompatible Tiny API v5 tactical action_id");
                        DialogueAnswer answer=DialogueAnswer.None;
                        if(string.Equals(r.answer,"good",StringComparison.OrdinalIgnoreCase))answer=DialogueAnswer.Good;
                        if(string.Equals(r.answer,"bad",StringComparison.OrdinalIgnoreCase))answer=DialogueAnswer.Bad;
                        FinalizePassiveBoredom();
                        // A delayed reply can be stale if the avatar moved closer
                        // to a different wall. Never execute/credit that action.
                        bool stillLegal=!preventBlockedBoundaryActions||
                            AdventureActionAvailability.IsLegal(r.move_x,r.move_z,
                                AdventureActionAvailability.ForPosition(
                                    actor.transform.position,actor.World.Width,
                                    actor.World.Depth,boundaryProbe,stats.IsStagnating,
                                     escapeX,escapeZ));
                        if(!stillLegal)
                        {
                            LastActionId="";current=default;
                            nextDecision=Time.time;
                            StaleBoundaryActionsDiscarded++;
                            Status="Discarded stale out-of-bounds action";
                        }
                        else
                        {
                        AdventureAbility chosenAbility=r.ability=="special"?AdventureAbility.SpecialAttack:
                            r.ability=="arc"?AdventureAbility.ArcPulse:
                            r.ability=="heal"?AdventureAbility.Heal:AdventureAbility.None;
                        current=new AdventureAction(Mathf.Clamp(r.move_x,-1,1),Mathf.Clamp(r.move_z,-1,1),
                            Mathf.Clamp(r.face_x,-1,1),Mathf.Clamp(r.face_z,-1,1),r.interact,answer,
                            r.move_distance,chosenAbility);
                        LastAction=current;
                        LastActionId=r.action_id;
                        issuedAt=Time.time;
                        appliedAt=Time.time;
                        // Do not issue another policy action until the measured
                        // distance completes or a collision stops the motor.
                        nextDecision=Time.time+Mathf.Max(.1f,decisionInterval);
                        appliedProgressCount=stats.MeaningfulProgressCount;
                        lastActionReadSign=false;
                        movementForcedByEscape=BoundaryEscapeActive;
                        hadNavigationOverride=false;
                        SuccessfulActions++;ModelTrained=r.model_trained;
                        Status="Tiny "+(ModelTrained?"trained":"random")+" / "+ModeLabel+
                            " #"+SuccessfulActions+" | walk "+r.move_distance.ToString("F1")+" units";
                        if(actor.mode==ControlMode.Tiny)
                        {
                            actor.Apply(current);
                            if(actor.NavigationIntervened)hadNavigationOverride=true;
                        }
                        current=default; // movement remains owned by AdventureAgent
                        }
                    }
                    catch(Exception e){FailedActions++;LastActionId="";current=default;Status="Tiny action error: "+e.Message;}
                }
            }
            InFlight=false;
        }
    }
}