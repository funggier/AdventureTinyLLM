using UnityEngine;

namespace AIAdventureRPG
{
    // Single reference-space HUD: panels, typography, spacing and hit targets all
    // scale together, avoiding layout drift at different Game View resolutions.
    public sealed class AdventureHUD : MonoBehaviour
    {
        public AdventureAgent actor;
        Texture2D panel,fill;
        GUIStyle title,label,small,button,selectedButton;
        Vector2 statScroll,aiScroll,logScroll;
        readonly Color panelColor=new Color(.026f,.045f,.075f,.88f);

        void InitializeGUI()
        {
            // Domain reload may retain textures but not all GUIStyle objects.
            if(panel==null)
            {
                panel=new Texture2D(1,1);
                panel.SetPixel(0,0,panelColor);panel.Apply();
            }
            if(fill==null)
            {
                fill=new Texture2D(1,1);
                fill.SetPixel(0,0,Color.white);fill.Apply();
            }
            if(title!=null&&label!=null&&small!=null&&button!=null&&selectedButton!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold};
            title.normal.textColor=new Color(.85f,.94f,1f);
            label=new GUIStyle(GUI.skin.label){fontSize=14,fontStyle=FontStyle.Bold};
            label.normal.textColor=Color.white;
            small=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};
            small.normal.textColor=new Color(.82f,.88f,.95f);
            button=new GUIStyle(GUI.skin.button){fontSize=13,fontStyle=FontStyle.Bold,
                alignment=TextAnchor.MiddleCenter};
            selectedButton=new GUIStyle(button);
            selectedButton.normal.textColor=new Color(.30f,.95f,.78f);
            selectedButton.hover.textColor=new Color(.30f,.95f,.78f);
        }

        void OnDestroy()
        {
            if(panel!=null)Destroy(panel);
            if(fill!=null)Destroy(fill);
        }

        void Bar(string name,float amount,Color color,float maximum=100f)
        {
            maximum=Mathf.Max(1f,maximum);
            GUILayout.Label(name+"  "+Mathf.RoundToInt(amount)+"/"+
                Mathf.RoundToInt(maximum),label);
            Rect outer=GUILayoutUtility.GetRect(235f,16f,GUILayout.ExpandWidth(true));
            GUI.color=new Color(.10f,.13f,.20f,1f);GUI.DrawTexture(outer,fill);
            GUI.color=color;
            GUI.DrawTexture(new Rect(outer.x,outer.y,
                outer.width*Mathf.Clamp01(amount/maximum),outer.height),fill);
            GUI.color=Color.white;
            GUILayout.Space(5f);
        }

        void DrawModeButton(string name,AdventurePlayMode selection,bool enabled)
        {
            bool wasEnabled=GUI.enabled;
            GUI.enabled=enabled;
            var style=actor.ActivePlayMode==selection?selectedButton:button;
            if(GUILayout.Button(name,style,GUILayout.Height(34f),GUILayout.ExpandWidth(true)))
            {
                if(actor.TrySelectPlayMode(selection))GUIUtility.ExitGUI();
            }
            GUI.enabled=wasEnabled;
        }

        void DrawSelector(AdventureHUDLayout layout,AdventureTinyController tiny)
        {
            GUI.DrawTexture(layout.selector,panel);
            GUILayout.BeginArea(new Rect(layout.selector.x+12f,layout.selector.y+7f,
                layout.selector.width-24f,layout.selector.height-14f));
            GUILayout.Label("CONTROLLER  |  SELECT ONE MODE",label);
            GUILayout.BeginHorizontal();
            DrawModeButton("MANUAL",AdventurePlayMode.Manual,true);
            DrawModeButton("TRAIN TINY",AdventurePlayMode.TrainTiny,
                tiny!=null&&tiny.ServiceAvailable&&tiny.TrainingAvailable);
            DrawModeButton("USE TRAINED",AdventurePlayMode.TrainedTiny,
                tiny!=null&&tiny.ServiceAvailable&&tiny.HasTrainedWeights);
            DrawModeButton("OLLAMA",AdventurePlayMode.Ollama,true);
            GUILayout.EndHorizontal();
            GUILayout.Label(tiny==null?"Tiny AI component missing":
                tiny.ServiceStatus+(tiny.ServiceAvailable&&!tiny.HasTrainedWeights?
                    " | USE TRAINED is locked until learning occurs":""),small);
            GUILayout.EndArea();
        }

        void DrawStats(AdventureHUDLayout layout)
        {
            GUI.DrawTexture(layout.stats,panel);
            GUILayout.BeginArea(new Rect(layout.stats.x+12f,layout.stats.y+9f,
                layout.stats.width-24f,layout.stats.height-18f));
            statScroll=GUILayout.BeginScrollView(statScroll,false,true);
            GUILayout.Label("AI ADVENTURE RPG",title);
            GUILayout.Label("Active: "+actor.ActivePlayMode+" | 3rd-person",small);
            if(actor.CombatState!=null)
            {
                var combat=actor.CombatState;
                string sensed=combat.NearestMonster!=null&&
                    combat.NearestMonsterDistance<=combat.detectionRadius?
                    "enemy "+combat.NearestMonsterDistance.ToString("F1")+" units":
                    "no hostile in detection ring";
                GUILayout.Label("STATE: "+combat.Label+" | "+sensed,label);
                GUILayout.Label("Detection "+combat.detectionRadius.ToString("F1")+
                    " units | Low HP < "+combat.lowHealthPercent.ToString("F0")+
                    "% | Low MP < "+combat.lowMagicPercent.ToString("F0")+"%",small);
                if(combat.NearestMonster!=null&&
                    combat.NearestMonsterDistance<=combat.detectionRadius&&
                    combat.NearestMonster.IsWindingUp)
                    GUILayout.Label("DANGER: MONSTER PREPARING ATTACK — DODGE!",label);
            }
            GUILayout.Space(4f);
            Bar("HEALTH",actor.Stats.health,new Color(.91f,.27f,.34f));
            Bar("MP / MAGIC POINTS",actor.Stats.magicPoints,new Color(.35f,.46f,1f),
                actor.Stats.maxMagicPoints);
            GUILayout.Label("MP "+actor.Stats.magicPoints.ToString("F0")+"/"+
                actor.Stats.maxMagicPoints.ToString("F0")+
                " | F Special "+actor.specialAttackMpCost.ToString("F0")+
                " | Q Arc "+actor.arcPulseMpCost.ToString("F0")+
                " | R Heal "+actor.healMpCost.ToString("F0"),small);
            Bar("HAPPINESS",actor.Stats.happiness,new Color(.42f,.86f,.55f));
            Bar("BOREDOM",actor.Stats.boredom,new Color(.97f,.72f,.26f));
            GUILayout.Label(actor.Stats.CurrentZoneLabel+" | "+
                actor.Stats.CurrentZoneFatigueSeconds.ToString("F0")+"s in zone ("+
                actor.Stats.ZonesVisited+" visited)"+
                (actor.Stats.IsZoneOverstayed?" - ZONE FATIGUE":""),small);
            GUILayout.Label("Development "+actor.Stats.development+
                " | Discoveries "+actor.Stats.discoveries+
                " | Deaths "+actor.Stats.deaths,small);
            GUILayout.Label("Monsters defeated "+actor.Stats.monsterKills+
                " | Hits taken "+actor.Stats.damageEvents,small);
            if(actor.World!=null)
            {
                GUILayout.Label(actor.Stats.monsterKills>=actor.World.monsterKillGoal?
                    "SURVIVAL TRIAL COMPLETE":"Survival trial: defeat "+
                    actor.World.monsterKillGoal+" monsters ("+
                    actor.Stats.monsterKills+"/"+actor.World.monsterKillGoal+")",label);
                GUILayout.Label("Hostiles alive "+
                    actor.World.Monsters.FindAll(m=>m!=null&&m.IsAlive).Count+
                    " | Signs "+actor.World.Signs.Count,small);
            }
            if(actor.Stats.IsFullyBored)
                GUILayout.Label("BOREDOM FULL: Health, MP and Development decline",small);
            GUILayout.Label(actor.Stats.IsStagnating?"NO PROGRESS: Happiness decaying":
                "Time without progress: "+
                actor.Stats.SecondsWithoutProgress.ToString("F0")+"s",small);
            GUILayout.Label("Map X:"+actor.transform.position.x.ToString("F1")+
                "  Z:"+actor.transform.position.z.ToString("F1"),small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawLog(AdventureHUDLayout layout)
        {
            GUI.DrawTexture(layout.log,panel);
            GUILayout.BeginArea(new Rect(layout.log.x+11f,layout.log.y+9f,
                layout.log.width-22f,layout.log.height-18f));
            GUILayout.Label("ADVENTURE LOG",label);
            logScroll=GUILayout.BeginScrollView(logScroll,false,true);
            var logs=actor.Stats.recent;
            for(int i=0;i<Mathf.Min(8,logs.Count);i++)
                GUILayout.Label(logs[i],small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawAI(AdventureHUDLayout layout,AdventureTinyController tiny)
        {
            if(actor.mode!=ControlMode.Tiny&&actor.mode!=ControlMode.Ollama)return;
            Rect area=layout.controller;
            GUI.DrawTexture(area,panel);
            GUILayout.BeginArea(new Rect(area.x+12f,area.y+10f,
                area.width-24f,area.height-20f));
            aiScroll=GUILayout.BeginScrollView(aiScroll,false,true);
            if(actor.mode==ControlMode.Ollama)
            {
                GUILayout.Label("OLLAMA  |  MODEL CONTROL",title);
                GUILayout.Label(actor.Brain!=null?actor.Brain.Status:
                    "Ollama controller unavailable",small);
                GUILayout.Label("This mode does not train Tiny AI.",small);
                if(actor.Brain!=null)
                {
                    GUILayout.Label("Ollama memory: "+actor.Brain.Memory.Count+
                        " | Resets: "+actor.Brain.ResetCount,small);
                    if(GUILayout.Button("RESET OLLAMA CONTEXT",button,GUILayout.Height(32f)))
                    {actor.Brain.ResetContext();GUIUtility.ExitGUI();}
                }
            }
            else if(tiny!=null)
            {
                GUILayout.Label(tiny.trainInRealGame?
                    "TRAIN TINY  |  WEIGHTS UPDATING":"USE TRAINED TINY  |  WEIGHTS FROZEN",title);
                GUILayout.Label(tiny.Status,label);
                GUILayout.Label(tiny.ServiceStatus,small);
                GUILayout.Label("Learning steps "+tiny.TrainingSteps+
                    " | Accepted "+tiny.AcceptedOutcomes+
                    " / Rejected "+tiny.RejectedOutcomes,small);
                GUILayout.Label("Last outcome: "+tiny.LastOutcomeStatus,small);
                GUILayout.Label("No-progress penalties "+tiny.IdlePenaltiesAccepted+
                    "/"+tiny.IdlePenaltiesAttempted+" | Last "+
                    tiny.LastIdlePenalty.ToString("F3"),small);
                GUILayout.Label("Zone penalties "+tiny.ZonePenaltiesAccepted+
                    "/"+tiny.ZonePenaltiesAttempted,small);
                GUILayout.Label("Legal moves "+tiny.LastLegalMoveCount+
                    "/9 | Stale "+tiny.StaleBoundaryActionsDiscarded+
                    " | Escape "+(tiny.BoundaryEscapeActive?"ACTIVE":"off"),small);
                if(tiny.trainInRealGame)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("TRAINING SPEED  |  "+tiny.trainingSpeed+
                        "x requested / "+tiny.AppliedSpeed.ToString("F0")+"x active",label);
                    GUILayout.BeginHorizontal();
                    if(GUILayout.Button("1x",button,GUILayout.Height(30f)))tiny.SetTrainingSpeed(1);
                    if(GUILayout.Button("2x",button,GUILayout.Height(30f)))tiny.SetTrainingSpeed(2);
                    if(GUILayout.Button("4x",button,GUILayout.Height(30f)))tiny.SetTrainingSpeed(4);
                    if(GUILayout.Button("8x",button,GUILayout.Height(30f)))tiny.SetTrainingSpeed(8);
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("Measured sim "+tiny.EffectiveGameSpeed.ToString("F2")+
                    "x | API "+tiny.RequestsPerRealSecond.ToString("F1")+" actions/s",small);
                GUILayout.Label("API "+tiny.MeanApiLatencyMs.ToString("F0")+
                    "ms | Accepted "+tiny.AcceptedOutcomesPerRealMinute.ToString("F1")+
                    "/min | Signs "+tiny.ObservedSigns,small);
            }
            if(actor.Journal!=null)
            {
                GUILayout.Space(5f);
                GUILayout.Label("LAST FINITE MOVE",label);
                GUILayout.Label(actor.Journal.LastMovementReport,small);
                GUILayout.Label("OBSERVED EVENTS (X/Z COORDINATES)",label);
                GUILayout.Label(actor.Journal.Report(5),small);
            }
            GUILayout.Space(6f);
            GUILayout.Label("VISION: "+(actor.Vision!=null?
                actor.Vision.Report():"No sensor"),small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawContext(AdventureHUDLayout layout)
        {
            if(actor.DialogueOpen)
            {
                GUI.DrawTexture(layout.dialogue,panel);
                GUILayout.BeginArea(new Rect(layout.dialogue.x+14f,layout.dialogue.y+10f,
                    layout.dialogue.width-28f,layout.dialogue.height-20f));
                GUILayout.Label(actor.CurrentDialogue,title);
                GUILayout.Label("1 GOOD                           2 BAD",label);
                GUILayout.Label("Face NPC to confirm answer.",small);
                GUILayout.EndArea();
            }
            else if(!string.IsNullOrWhiteSpace(actor.CurrentSignMessage))
            {
                GUI.DrawTexture(layout.sign,panel);
                GUILayout.BeginArea(new Rect(layout.sign.x+14f,layout.sign.y+10f,
                    layout.sign.width-28f,layout.sign.height-20f));
                GUILayout.Label("SIGN  |  INFORMATION ONLY (REWARD 0)",label);
                GUILayout.Label(actor.CurrentSignMessage,small);
                GUILayout.EndArea();
            }
            else if(actor.mode==ControlMode.Manual)
            {
                GUI.DrawTexture(layout.hints,panel);
                GUI.Label(new Rect(layout.hints.x+6f,layout.hints.y+2f,
                    layout.hints.width-12f,24f),
                    "WASD | E normal/interact | F special (12 MP) | Q arc (20 MP) | R heal (25 MP)",small);
            }
        }

        void OnGUI()
        {
            if(actor==null||actor.Stats==null)return;
            InitializeGUI();
            var layout=AdventureHUDLayout.ForScreen(Screen.width,Screen.height);
            var oldMatrix=GUI.matrix;
            var oldColor=GUI.color;
            GUI.matrix=Matrix4x4.Scale(new Vector3(layout.scale,layout.scale,1f));
            try
            {
                var tiny=actor.GetComponent<AdventureTinyController>();
                GUI.color=Color.white;
                DrawStats(layout);
                DrawSelector(layout,tiny);
                DrawLog(layout);
                DrawAI(layout,tiny);
                DrawContext(layout);
            }
            finally
            {
                GUI.color=oldColor;
                GUI.matrix=oldMatrix;
            }
        }
    }
}
