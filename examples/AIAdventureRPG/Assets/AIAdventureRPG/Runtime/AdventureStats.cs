using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AIAdventureRPG
{
    public sealed class AdventureStats : MonoBehaviour
    {
        [Range(0,100)] public float health=72,happiness=55,boredom=0;
        [Header("Magic resource — casting costs MP")]
        [Range(0,100)] public float magicPoints=40;
        [Range(1,100)] public float maxMagicPoints=100;
        public bool HasMP(float cost)=>cost>=0f&&magicPoints+0.0001f>=cost;
        public bool TrySpendMP(float cost)
        {
            if(float.IsNaN(cost)||float.IsInfinity(cost)||!HasMP(cost))return false;
            magicPoints=Mathf.Max(0f,magicPoints-cost);return true;
        }
        public void RestoreMP(float amount)
        {
            if(amount>0f&&!float.IsNaN(amount)&&!float.IsInfinity(amount))
                magicPoints=Mathf.Min(maxMagicPoints,magicPoints+amount);
        }
        public int development,discoveries,interactions,monsterKills,monsterHits,damageEvents,deaths;

        [Header("No Meaningful Progress — movement alone does not count")]
        [Min(1f)] public float progressGraceSeconds=12f;
        [Min(0f)] public float happinessLossPerMinute=3f;
        [Header("Adventure zones (64 x 64 map = 4 x 4 broad zones by default)")]
        public bool enableZoneFatigue=true;
        [Min(2)] public int zoneCellSize=16;
        [Min(1f)] public float zoneGraceSeconds=35f;
        [Min(0f)] public float zoneBoredomRate=.25f;
        [Min(0f)] public float zoneFatigueRecoveryRate=.5f;
        [Min(0f)] public float zoneSwitchConfirmSeconds=1.5f;
        public int CurrentZoneX{get;private set;}
        public int CurrentZoneZ{get;private set;}
        public string CurrentZoneLabel=>ZoneLabel(CurrentZoneX,CurrentZoneZ);
        public int ZonesVisited=>zoneFatigue.Count;
        public float CurrentZoneFatigueSeconds{get;private set;}
        public float ZoneBoredomLastTick{get;private set;}
        public bool IsZoneOverstayed=>enableZoneFatigue&&
            CurrentZoneFatigueSeconds>=Mathf.Max(1f,zoneGraceSeconds);
        [Header("Full Boredom Penalties (per game minute)")]
        [Min(0f)] public float healthLossPerMinute=3f;
        [Min(0f)] public float mpLossPerMinute=2f;
        [Min(.1f)] public float developmentLossInterval=30f;

        public bool IsFullyBored=>boredom>=100f;
        public bool IsStagnating=>secondsWithoutProgress>=Mathf.Max(1f,progressGraceSeconds);
        public float SecondsWithoutProgress=>secondsWithoutProgress;
        public int MeaningfulProgressCount{get;private set;}

        // Only actual events affect action outcomes; passive no-progress changes
        // do not get attributed to the last pickup or a nearby NPC.
        public Vector4 LastOutcomeDeltas{get;private set;}
        public string lastEvent="Explore the world";
        public readonly List<string> recent=new List<string>();
        // Parallel timestamps permit rebuilding an Ollama prompt strictly
        // from events that occurred AFTER RESET OLLAMA CONTEXT.
        readonly List<float> recentTimes=new List<float>();
        public event Action<string> OutcomeObserved;
        public string LastOutcome{get;private set;}="No interactions yet";
        public float LastOutcomeGameTime{get;private set;}=-1f;

        float windowTime,windowScore,boredomPenaltyTime,secondsWithoutProgress;
        readonly Dictionary<Vector2Int,float> zoneFatigue=new Dictionary<Vector2Int,float>();
        readonly List<Vector2Int> zoneKeys=new List<Vector2Int>();
        bool zoneInitialized;
        bool lethalOutcomeReported;
        Vector2Int currentZone,pendingZone;
        float pendingZoneTime;
        string logPath;
        void Awake()
        {
            string folder=Path.Combine(Application.persistentDataPath,"AIAdventureRPG");
            Directory.CreateDirectory(folder);
            // Keep the old knowledge CSV intact; MP has a distinct schema.
            logPath=Path.Combine(folder,"events-magic-v3.csv");
            if(!File.Exists(logPath))
                File.WriteAllText(logPath,"utc,event,health,mp,happiness,boredom,development\n");
        }
        public static Vector2Int ZoneOf(Vector3 position,int cellSize)
        {
            int size=Mathf.Max(2,cellSize);
            return new Vector2Int(Mathf.FloorToInt(position.x/size),
                Mathf.FloorToInt(position.z/size));
        }
        static string ZoneLabel(int x,int z)
        {
            return "Zone "+(x+1)+":"+(z+1);
        }
        void UpdateZone(float delta)
        {
            ZoneBoredomLastTick=0f;
            if(!enableZoneFatigue||delta<=0f)return;
            Vector2Int observed=ZoneOf(transform.position,zoneCellSize);
            if(!zoneInitialized)
            {
                currentZone=observed;
                pendingZone=observed;
                zoneInitialized=true;
            }
            if(observed==currentZone)
            {
                pendingZone=currentZone;
                pendingZoneTime=0f;
            }
            else
            {
                if(observed!=pendingZone)
                {
                    pendingZone=observed;
                    pendingZoneTime=0f;
                }
                pendingZoneTime+=delta;
                if(pendingZoneTime>=zoneSwitchConfirmSeconds)
                {
                    currentZone=observed;
                    pendingZone=currentZone;
                    pendingZoneTime=0f;
                }
            }
            CurrentZoneX=currentZone.x;
            CurrentZoneZ=currentZone.y;
            zoneKeys.Clear();
            zoneKeys.AddRange(zoneFatigue.Keys);
            foreach(var zone in zoneKeys)
            {
                if(zone==currentZone)continue;
                zoneFatigue[zone]=Mathf.Max(0f,
                    zoneFatigue[zone]-delta*Mathf.Max(0f,zoneFatigueRecoveryRate));
            }
            float before=0f;
            zoneFatigue.TryGetValue(currentZone,out before);
            float after=before+delta;
            zoneFatigue[currentZone]=after;
            CurrentZoneFatigueSeconds=after;
            float grace=Mathf.Max(1f,zoneGraceSeconds);
            float overdue=Mathf.Max(0f,after-grace)-Mathf.Max(0f,before-grace);
            if(overdue>0f)
            {
                ZoneBoredomLastTick=overdue*Mathf.Max(0f,zoneBoredomRate);
                boredom=AdventureRules.Stat(boredom+ZoneBoredomLastTick);
            }
        }
        void MeaningfulProgress()
        {
            MeaningfulProgressCount++;
            secondsWithoutProgress=0f;
        }
        // Zone residence alone generates passive boredom. Movement speed and
        // standing still are intentionally not part of this simulation rule.
        public void Tick(float delta)
        {
            delta=Mathf.Max(0f,delta);
            UpdateZone(delta);

            float beforeIdle=secondsWithoutProgress;
            secondsWithoutProgress+=delta;
            float grace=Mathf.Max(1f,progressGraceSeconds);
            float affectedTime=Mathf.Max(0f,secondsWithoutProgress-grace)
                -Mathf.Max(0f,beforeIdle-grace);
            if(affectedTime>0f)
                happiness=AdventureRules.Stat(happiness-
                    affectedTime/60f*Mathf.Max(0f,happinessLossPerMinute));

            if(IsFullyBored)
            {
                float minutes=delta/60f;
                health=AdventureRules.Stat(health-minutes*Mathf.Max(0f,healthLossPerMinute));
                magicPoints=Mathf.Max(0f,magicPoints-minutes*Mathf.Max(0f,mpLossPerMinute));
                // Happiness is already decaying from lack of progress; avoid
                // double-counting full boredom on the same Happiness statistic.
                boredomPenaltyTime+=delta;
                float interval=Mathf.Max(.1f,developmentLossInterval);
                if(boredomPenaltyTime>=interval)
                {
                    int loss=Mathf.FloorToInt(boredomPenaltyTime/interval);
                    boredomPenaltyTime-=loss*interval;
                    if(development>0)
                    {
                        development=Mathf.Max(0,development-loss);
                        Record("Full boredom: development decreased");
                    }
                }
            }
            else boredomPenaltyTime=0;

            windowTime+=delta;
            if(windowTime>=300f)
            {
                development=Mathf.Max(0,development+
                    Mathf.RoundToInt(windowScore>0?windowScore:-5));
                Record("5-minute development transfer");
                windowTime=0;windowScore=0;
            }
        }

        public void PickUp(AdventureObjectKind kind,string objectName,bool consumed=false)
        {
            var before=Snapshot();
            float beforeHealth=health,beforeMP=magicPoints;
            if(kind==AdventureObjectKind.LoreStone)
                RestoreMP(18f);
            else
                health=AdventureRules.HealthAfter(health,kind);
            windowScore+=health-beforeHealth+Mathf.Max(0f,magicPoints-beforeMP)*.2f;
            discoveries++;
            boredom=AdventureRules.Stat(boredom-9);

            switch(kind)
            {
                case AdventureObjectKind.Herb:
                    happiness=AdventureRules.Stat(happiness+6);
                    MeaningfulProgress();
                    break;
                case AdventureObjectKind.SafeBerry:
                    happiness=AdventureRules.Stat(happiness+8);
                    MeaningfulProgress();
                    break;
                case AdventureObjectKind.LoreStone:
                    happiness=AdventureRules.Stat(happiness+4);
                    MeaningfulProgress();
                    break;
                case AdventureObjectKind.Thorn:
                    happiness=AdventureRules.Stat(happiness-2);
                    break;
                case AdventureObjectKind.PoisonBerry:
                    happiness=AdventureRules.Stat(happiness-9);
                    break;
            }
            ReportOutcome(objectName,(consumed?"Eat ":"Collect ")+kind,before);
        }
        public void ReportAttackMiss(string reason)
        {
            ReportOutcome("Self","AttackMiss reason="+reason,Snapshot());
        }
        public void ReportAvoidedMonsterAttack(string monster)
        {
            ReportOutcome("Self","DodgeMonster "+monster,Snapshot());
        }
        public void ReportAvoidedThorn(string thorn)
        {
            ReportOutcome("Self","AvoidThorn "+thorn,Snapshot());
        }
        public void TakeDamage(string monster,float amount)
        {
            if(amount<=0||health<=0)return;
            var before=Snapshot();
            health=AdventureRules.Stat(health-amount);
            happiness=AdventureRules.Stat(happiness-2);
            damageEvents++;
            ReportOutcome(monster,"HitByMonster",before);
        }
        public void StrikeMonster(string monster,float damage,bool killed)
        {
            var before=Snapshot();
            if(damage<=0f)return;
            monsterHits++;
            if(killed)
            {
                monsterKills++;
                happiness=AdventureRules.Stat(happiness+15);
            }
            // Damage to a monster counts as real progress even before a kill,
            // but do not award Happiness merely for swinging an attack.
            MeaningfulProgress();
            ReportOutcome(monster,killed?"DefeatMonster":"StrikeMonster",before);
        }
        // Invalid ability attempts still have an observable result for the
        // policy to learn from, but never create damage, MP costs or VFX.
        public void RejectAbility(string ability,string reason)
        {
            ReportOutcome("Self","AbilityRejected "+ability+" reason="+reason,Snapshot());
        }
        // Cast results are recorded once after actual effects, so the Tiny
        // action ID receives the MP expenditure and combat/heal result together.
        public void ResolveAbility(string ability,float mpBefore,int hitCount,int killCount,float healed=0f)
        {
            var before=Snapshot();
            before.magicPoints=mpBefore;
            before.health=Mathf.Max(0f,health-healed);
            if(hitCount>0)
            {
                monsterHits+=hitCount;
                monsterKills+=killCount;
                happiness=AdventureRules.Stat(happiness+15f*killCount);
                MeaningfulProgress();
            }
            if(healed>0f)MeaningfulProgress();
            ReportOutcome("Self","Ability "+ability+" hits="+hitCount+
                " kills="+killCount+" healed="+healed.ToString("F1",CultureInfo.InvariantCulture),before);
        }
        public void Meet(string npc,string spokenTopic)
        {
            var before=Snapshot();
            happiness=AdventureRules.Stat(happiness+9);
            interactions++;
            windowScore+=happiness-before.happiness;
            boredom=AdventureRules.Stat(boredom-10);
            MeaningfulProgress();
            ReportOutcome(npc,"Talk; NPC says "+spokenTopic,before);
        }
        public void Reply(string npc,AdventureObjectKind topic,DialogueAnswer answer)
        {
            var before=Snapshot();
            int score=AdventureRules.MagicReplyReward(topic,answer);
            magicPoints=Mathf.Clamp(magicPoints+score,0f,maxMagicPoints);
            windowScore+=Mathf.Max(0,score)*.2f;
            if(magicPoints>before.magicPoints)
            {
                happiness=AdventureRules.Stat(happiness+3);
                MeaningfulProgress();
            }
            ReportOutcome(npc,"Reply "+answer+" to topic "+TopicName(topic),before);
        }
        static string TopicName(AdventureObjectKind topic)
        {
            return topic==AdventureObjectKind.Herb?"Herb":topic==AdventureObjectKind.Thorn?"Thorn":
                topic==AdventureObjectKind.SafeBerry?"Berry":topic==AdventureObjectKind.PoisonBerry?"Berry":"Rune";
        }
        struct StatSnapshot
        {
            public float health,magicPoints,happiness,boredom;
            public int development;
        }
        StatSnapshot Snapshot()
        {
            return new StatSnapshot{health=health,magicPoints=magicPoints,
                happiness=happiness,boredom=boredom,development=development};
        }
        void ReportOutcome(string source,string action,StatSnapshot before)
        {
            var p=transform.position;
            string summary="source="+source+" action="+action+
                " pre_hp="+before.health.ToString("F2",CultureInfo.InvariantCulture)+
                " pre_mp="+before.magicPoints.ToString("F2",CultureInfo.InvariantCulture)+
                " max_mp="+maxMagicPoints.ToString("F2",CultureInfo.InvariantCulture)+
                " hp_low_ratio="+(GetComponent<AdventureCombatState>()!=null?
                    GetComponent<AdventureCombatState>().lowHealthPercent*.01f:
                    AdventureRewardPolicy.CriticalHPPercent).ToString("F3",CultureInfo.InvariantCulture)+
                " mp_low_ratio="+(GetComponent<AdventureCombatState>()!=null?
                    GetComponent<AdventureCombatState>().lowMagicPercent*.01f:
                    AdventureRewardPolicy.CriticalMPPercent).ToString("F3",CultureInfo.InvariantCulture)+
                " at (x="+p.x.ToString("F1",CultureInfo.InvariantCulture)+
                ",z="+p.z.ToString("F1",CultureInfo.InvariantCulture)+")"+
                " HP "+before.health.ToString("F0")+"->"+health.ToString("F0")+
                " ("+Delta(health-before.health)+")"+
                " MP "+before.magicPoints.ToString("F0")+"->"+magicPoints.ToString("F0")+
                " ("+Delta(magicPoints-before.magicPoints)+")"+
                " Happiness "+before.happiness.ToString("F0")+"->"+happiness.ToString("F0")+
                " ("+Delta(happiness-before.happiness)+")"+
                " Boredom "+before.boredom.ToString("F0")+"->"+boredom.ToString("F0")+
                " ("+Delta(boredom-before.boredom)+")"+
                " Development "+before.development+"->"+development+
                ((health<=0f&&(before.health>0f||action=="Death"))?" death=true":"");
            if(health<=0f&&(before.health>0f||action=="Death"))
                lethalOutcomeReported=true;
            LastOutcome=summary;
            LastOutcomeGameTime=Time.time;
            LastOutcomeDeltas=new Vector4(health-before.health,magicPoints-before.magicPoints,
                happiness-before.happiness,boredom-before.boredom);
            Record(source+" / "+action+" | HP "+Delta(health-before.health)+
                "  MP "+Delta(magicPoints-before.magicPoints)+
                "  Happy "+Delta(happiness-before.happiness)+
                "  B "+Delta(boredom-before.boredom));
            OutcomeObserved?.Invoke(summary);
        }
        static string Delta(float value)
        {
            return value.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture);
        }
        public void Record(string message)
        {
            lastEvent=message;
            recent.Insert(0,Time.time.ToString("F1",CultureInfo.InvariantCulture)+"s  "+message);
            recentTimes.Insert(0,Time.time);
            if(recent.Count>8)
            {
                recent.RemoveAt(recent.Count-1);
                recentTimes.RemoveAt(recentTimes.Count-1);
            }
            try
            {
                string[] fields={DateTime.UtcNow.ToString("o"),message,
                    health.ToString("F1",CultureInfo.InvariantCulture),
                    magicPoints.ToString("F1",CultureInfo.InvariantCulture),
                    happiness.ToString("F1",CultureInfo.InvariantCulture),
                    boredom.ToString("F1",CultureInfo.InvariantCulture),development.ToString()};
                for(int i=0;i<fields.Length;i++)
                    fields[i]="\""+fields[i].Replace("\"","\"\"")+"\"";
                if(!string.IsNullOrEmpty(logPath))
                    File.AppendAllText(logPath,string.Join(",",fields)+"\n");
            }
            catch(IOException error)
            {
                Debug.LogWarning("Adventure log: "+error.Message);
            }
        }
        // Called once at actual death, immediately before respawning.
        // An already-reported lethal pickup/hit must not be rewarded twice.
        public void ReportDeath()
        {
            deaths++;
            if(!lethalOutcomeReported)
                ReportOutcome("Self","Death",Snapshot());
            Record("Death #"+deaths+"; respawn at camp");
        }
        public void ResetLife()
        {
            lethalOutcomeReported=false;
            health=72;
            // Death never grants free MP; retain only half the remaining mana.
            magicPoints=Mathf.Clamp(magicPoints*.5f,0f,maxMagicPoints);
            boredom=0;
            happiness=AdventureRules.Stat(Mathf.Max(20,happiness));
            secondsWithoutProgress=0;
            zoneFatigue.Clear();zoneInitialized=false;
            CurrentZoneFatigueSeconds=0;ZoneBoredomLastTick=0;
            Record("Restored after collapse");
        }
        public string ContextSummary(bool includeRecent=true,float since=-1f)
        {
            var filtered=new List<string>();
            if(includeRecent)
                for(int i=0;i<recent.Count;i++)
                    if(since<0f||(i<recentTimes.Count&&recentTimes[i]>since))
                        filtered.Add(recent[i]);
            return "HP="+health.ToString("F0")+
                " MP="+magicPoints.ToString("F0")+
                "/"+maxMagicPoints.ToString("F0")+
                " happiness="+happiness.ToString("F0")+
                " boredom="+boredom.ToString("F0")+
                " no_progress_seconds="+secondsWithoutProgress.ToString("F0")+
                " state="+(GetComponent<AdventureCombatState>()!=null?
                    GetComponent<AdventureCombatState>().Label:"NORMAL")+
                " zone="+CurrentZoneLabel+
                " zone_dwell_seconds="+CurrentZoneFatigueSeconds.ToString("F0")+
                " development="+development+
                (includeRecent?" recent="+string.Join("; ",filtered.ToArray()):"");
        }
    }
}