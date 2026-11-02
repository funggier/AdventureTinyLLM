using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace AIAdventureRPG
{
    public sealed class AdventureBrain:MonoBehaviour
    {
        [Min(.1f)] public float commandEvery=.2f;
        public int memoryLimit=24;
        public string Status{get;private set;}="Idle";
        public bool Waiting{get;private set;}
        readonly List<string> memory=new List<string>();
        string memoryPath;
        public IReadOnlyList<string> Memory=>memory;
        public int ResetCount{get;private set;}
        public float ContextResetAt{get;private set;}=-1f;
        public void ResetContext()
        {
            modeGeneration++; // make every outstanding callback stale
            ContextResetAt=Time.time;
            Waiting=false;
            current=default;
            nextDecision=Time.time+Mathf.Max(.1f,commandEvery);
            memory.Clear();
            if(string.IsNullOrEmpty(memoryPath))LoadMemory();
            // LoadMemory can restore old content; clear again on first reset.
            memory.Clear();
            if(!string.IsNullOrEmpty(memoryPath))
            {
                try{File.WriteAllText(memoryPath,string.Empty);}
                catch(IOException e){Debug.LogWarning("Ollama memory reset: "+e.Message);}
            }
            ollama?.CancelPending();
            var actor=GetComponent<AdventureAgent>();
            if(actor!=null&&actor.mode==ControlMode.Ollama)
                actor.StopAIMovement("context reset");
            ResetCount++;
            Status="Ollama context reset ("+ResetCount+")";
        }
        AdventureOllama ollama;
        AdventureAction current;
        float nextDecision,nextRandom;
        int modeGeneration;
        public void EnterMode()
        {
            modeGeneration++;
            nextDecision=0f;
            current=default;
            Waiting=false;
            Status="Ollama ready";
        }
        public void LeaveMode()
        {
            modeGeneration++;
            Waiting=false;
            current=default;
            Status="Ollama inactive";
        }
        Vector3 wander=Vector3.forward;
        AdventureStats stats;
        void Awake()
        {
            ollama=GetComponent<AdventureOllama>();
            LoadMemory();
        }
        void OnEnable()
        {
            stats=GetComponent<AdventureStats>();
            if(stats!=null) stats.OutcomeObserved+=RememberOllamaOutcome;
        }
        void OnDisable()
        {
            LeaveMode();
            if(stats!=null) stats.OutcomeObserved-=RememberOllamaOutcome;
        }
        void RememberOllamaOutcome(string experience)
        {
            var actor=GetComponent<AdventureAgent>();
            if(actor!=null && actor.mode==ControlMode.Ollama)Remember(experience);
        }
        void LoadMemory()
        {
            string directory=Path.Combine(Application.persistentDataPath,"AIAdventureRPG");
            Directory.CreateDirectory(directory);
            memoryPath=Path.Combine(directory,"brain-memory.txt");
            if(!File.Exists(memoryPath))return;
            memory.Clear();
            try
            {
                foreach(var line in File.ReadAllLines(memoryPath))
                {
                    if(!string.IsNullOrWhiteSpace(line))memory.Add(line);
                    if(memory.Count>=memoryLimit)break;
                }
            }
            catch(IOException e){Debug.LogWarning("Adventure memory: "+e.Message);}
        }
        public void Remember(string experience)
        {
            if(string.IsNullOrWhiteSpace(experience))return;
            memory.Insert(0,experience.Replace("\r"," ").Replace("\n"," "));
            if(memory.Count>memoryLimit)memory.RemoveAt(memory.Count-1);
            if(string.IsNullOrEmpty(memoryPath))LoadMemory();
            try{File.WriteAllLines(memoryPath,memory.ToArray());}
            catch(IOException e){Debug.LogWarning("Adventure memory save: "+e.Message);}
        }
        public void Tick(AdventureAgent actor)
        {
            if(actor==null||Waiting||actor.AIMoveActive||Time.time<nextDecision)return;
            if(actor.mode==ControlMode.Ollama)
            {
                nextDecision=Time.time+Mathf.Max(.1f,commandEvery);
                if(ollama!=null)
                {
                    int generation=modeGeneration;
                    Waiting=true;Status="Ollama thinking...";
                    ollama.Decide(actor,string.Join("; ",
                        memory.GetRange(0,Mathf.Min(6,memory.Count)).ToArray()),action=>
                    {
                        if(generation!=modeGeneration||actor.mode!=ControlMode.Ollama)return;
                        Waiting=false;
                        current=action??ChooseLocal(actor);
                        Status=action.HasValue?"Ollama moving a measured distance":
                            "Ollama unavailable (measured local fallback)";
                        actor.Apply(current); // exactly once per response
                        nextDecision=Time.time+Mathf.Max(.1f,commandEvery);
                    });
                }
                else
                {
                    current=ChooseLocal(actor);Status="Local fallback";
                    actor.Apply(current);
                }
            }
            else
            {
                current=ChooseLocal(actor);Status="Local agent";
                actor.Apply(current);
                nextDecision=Time.time+Mathf.Max(.1f,commandEvery);
            }
        }
        AdventureAction ChooseLocal(AdventureAgent actor)
        {
            Vector3 direction=actor.GoalBearing();
            if(actor.DialogueOpen&&actor.CurrentNPC!=null)
            {
                // Local baseline uses the plain-language concept. AI model need not mimic this policy.
                // Baseline does not read the NPC's hidden teachingTopic.
                var heard=actor.CurrentNPC.SpokenWord;
                bool seenBefore=false;
                DialogueAnswer answer=DialogueAnswer.None;
                foreach(var past in memory)
                    if(past.Contains("action=Reply Good to topic "+heard) &&
                        past.Contains("MP ") && past.Contains("(+12.0)"))
                    {answer=DialogueAnswer.Good;seenBefore=true;break;}
                if(!seenBefore)answer=UnityEngine.Random.value<.5f?DialogueAnswer.Good:DialogueAnswer.Bad;
                return new AdventureAction(0,0,direction.x,direction.z,false,answer);
            }
            var enemy=actor.NearestMonster(9f);
            if(enemy!=null)
            {
                Vector3 target=enemy.transform.position-actor.transform.position;target.y=0;
                if(target.magnitude<actor.attackRange)
                {
                    if(actor.Stats.HasMP(actor.specialAttackMpCost)&&actor.Stats.health>32)
                        return new AdventureAction(0,0,target.x,target.z,
                            ability:AdventureAbility.SpecialAttack);
                    return new AdventureAction(0,0,target.x,target.z,true);
                }
                // Local safety fallback: close in when healthy, escape when weak.
                if(actor.Stats.health<32)target=-target;
                return new AdventureAction(target.x,target.z,target.x,target.z,
                    distance:Mathf.Clamp(target.magnitude-actor.attackRange+1f,1f,4f));
            }
            var npc=actor.NearestNPC();
            bool seesNPC=false;
            if(actor.Vision!=null)
                foreach(var thing in actor.Vision.Visible)
                    if(thing.Type=="NPC" && npc!=null && thing.Name==npc.displayName) {seesNPC=true;break;}
            if(seesNPC&&npc!=null&&npc.CanTalk)
            {
                Vector3 to=npc.transform.position-actor.transform.position;to.y=0;
                if(to.magnitude<3.5f)return new AdventureAction(0,0,to.x,to.z,true);
            }
            if(direction.sqrMagnitude>.001f)return new AdventureAction(direction.x,direction.z,
                direction.x,direction.z,distance:3f);
            if(actor.World!=null)
            {
                var p=actor.transform.position;
                if(p.x<=3f || p.x>=actor.World.Width-3f || p.z<=3f || p.z>=actor.World.Depth-3f)
                {
                    Vector3 center=new Vector3(actor.World.Width*.5f,p.y,actor.World.Depth*.5f);
                    Vector3 inward=(center-p).normalized;
                    return new AdventureAction(inward.x,inward.z,inward.x,inward.z,distance:4f);
                }
            }
            if(Time.time>=nextRandom)
            {
                float a=UnityEngine.Random.Range(-Mathf.PI,Mathf.PI);
                wander=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                nextRandom=Time.time+UnityEngine.Random.Range(2f,4f);
            }
            return new AdventureAction(wander.x,wander.z,wander.x,wander.z,distance:4f);
        }
    }
}