using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>
    /// Perception-only spatial memory. Groups observations into configurable X/Z
    /// coordinate cells, retains at most five FIFO snapshots per cell, and alerts
    /// controllers on genuinely new nearby objects with 9/18-unit hysteresis.
    /// No rewards are generated here: awareness alone is not an achievement.
    /// </summary>
    [RequireComponent(typeof(AdventureAgent))]
    public sealed class AdventurePerceptionMemory : MonoBehaviour
    {
        [Header("Reactive perception (world units)")]
        [Min(1f)] public float alertRadius=9f;
        [Min(2f)] public float rearmRadius=18f;
        [Min(.1f)] public float eventCooldown=.65f;
        [Header("Map memory")]
        [Min(.5f)] public float coordinateCellSize=2f;
        [Range(1,5)] public int snapshotsPerCoordinate=5;
        [Min(5f)] public float staticResampleSeconds=8f;
        [Range(64,4096)] public int maxRememberedCells=2048;
        [Min(1f)] public float autosaveGameSeconds=10f;
        public int AlertCount{get;private set;}
        public int UrgentThreatAlerts{get;private set;}
        public int HistoryCellCount=>cells.Count;
        public int TrackedObjects=>tracks.Count;
        public float LastAlertAt{get;private set;}=-999f;
        public PerceptionAlert LastAlert{get;private set;}
        public event Action<PerceptionAlert> ImportantChange;

        [Serializable] public sealed class Snapshot
        {
            public string kind,name,change,utc;
            public Vector3 position;
            public int source;
        }
        [Serializable] public sealed class Cell
        {
            public int x,z;
            public List<Snapshot> history=new List<Snapshot>();
        }
        [Serializable] sealed class FileData
        {
            public int version=1,worldSeed,width,depth;
            public List<Cell> cells=new List<Cell>();
        }
        public sealed class PerceptionAlert
        {
            public int entityId;
            public string kind,name;
            public Vector3 position;
            public bool moved;
            public bool urgent;
            public string reason;
            public int previousAtCoordinate;
            public string history;
        }
        sealed class Track
        {
            public string kind,name;
            public Vector3 lastPosition;
            public float lastSeen,lastSample;
            public int cellX,cellZ;
            public bool armed=true,everMoved;
        }

        readonly Dictionary<Vector2Int,Cell> cells=new Dictionary<Vector2Int,Cell>();
        readonly Dictionary<int,Track> tracks=new Dictionary<int,Track>();
        // Attack windups are distinct urgent events. One alert per committed
        // attack, even when the same monster stays inside the inner ring.
        readonly Dictionary<int,float> seenWindupEnds=new Dictionary<int,float>();
        readonly List<PerceptionAlert> queued=new List<PerceptionAlert>();
        AdventureAgent actor;
        bool dirty,loaded;
        float nextSave,nextSaveRealtime,lastDispatch=-999f;
        string dataPath;
        public string SavePath=>dataPath;

        void Awake(){actor=GetComponent<AdventureAgent>();}
        void OnDisable(){Save();}
        void OnApplicationQuit(){Save();}

        static bool Important(string kind)=>
            kind=="Monster"||kind=="Herb"||kind=="Thorn"||
            kind=="LoreStone"||kind=="Berry"||kind=="NPC";

        Vector2Int Coordinate(Vector3 p)
        {
            float size=Mathf.Max(.5f,coordinateCellSize);
            return new Vector2Int(Mathf.RoundToInt(p.x/size),Mathf.RoundToInt(p.z/size));
        }
        void EnsureLoaded()
        {
            if(loaded)return;
            if(actor==null)actor=GetComponent<AdventureAgent>();
            if(actor==null||actor.World==null||!actor.World.HasGroundData)return;
            loaded=true;
            string folder=Path.Combine(Application.persistentDataPath,"AIAdventureRPG");
            dataPath=Path.Combine(folder,"perception-map-v1-"+actor.World.mapSeed+".json");
            try
            {
                if(!File.Exists(dataPath))return;
                var fromFile=JsonUtility.FromJson<FileData>(File.ReadAllText(dataPath));
                if(fromFile==null||fromFile.version!=1||
                    fromFile.worldSeed!=actor.World.mapSeed||
                    fromFile.width!=actor.World.Width||fromFile.depth!=actor.World.Depth||
                    fromFile.cells==null)return;
                foreach(var cell in fromFile.cells)
                {
                    if(cell==null||cell.history==null)continue;
                    // Enforce FIFO bound even when a file was manually modified.
                    while(cell.history.Count>5)cell.history.RemoveAt(0);
                    cells[new Vector2Int(cell.x,cell.z)]=cell;
                }
            }
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||
                                    e is ArgumentException)
            {
                Debug.LogWarning("[AIAdventureRPG] Perception map could not load: "+e.Message);
            }
        }
        void Save()
        {
            if(!dirty||!loaded||string.IsNullOrEmpty(dataPath)||actor==null||actor.World==null)return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
                var data=new FileData{worldSeed=actor.World.mapSeed,
                    width=Mathf.RoundToInt(actor.World.Width),depth=Mathf.RoundToInt(actor.World.Depth)};
                foreach(var cell in cells.Values)data.cells.Add(cell);
                string temp=dataPath+".tmp";
                File.WriteAllText(temp,JsonUtility.ToJson(data,false),new UTF8Encoding(false));
                // Keep a valid previous map until replacement is complete.
                if(File.Exists(dataPath))File.Replace(temp,dataPath,null);
                else File.Move(temp,dataPath);
                dirty=false;
            }
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[AIAdventureRPG] Perception map save failed: "+e.Message);
            }
        }
        void Record(Vector3 p,int id,string kind,string name,string change)
        {
            var key=Coordinate(p);
            Cell cell;
            if(!cells.TryGetValue(key,out cell))
            {
                if(cells.Count>=maxRememberedCells)
                {
                    // Oldest coordinate history leaves first; never grow without bound.
                    var oldest=default(Vector2Int);
                    string oldestDate=null;
                    foreach(var item in cells)
                    {
                        string timestamp=item.Value.history.Count>0?
                            item.Value.history[item.Value.history.Count-1].utc:"";
                        if(oldestDate==null||string.CompareOrdinal(timestamp,oldestDate)<0)
                            {oldest=item.Key;oldestDate=timestamp;}
                    }
                    cells.Remove(oldest);
                }
                cell=new Cell{x=key.x,z=key.y};
                cells[key]=cell;
            }
            cell.history.Add(new Snapshot{kind=kind,name=name,change=change,
                utc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),
                position=p,source=id});
            int maximum=Mathf.Clamp(snapshotsPerCoordinate,1,5);
            while(cell.history.Count>maximum)cell.history.RemoveAt(0);
            dirty=true;
        }
        public IReadOnlyList<Snapshot> HistoryAt(Vector3 coordinate)
        {
            EnsureLoaded();
            Cell cell;
            return cells.TryGetValue(Coordinate(coordinate),out cell)?
                cell.history:(IReadOnlyList<Snapshot>)Array.Empty<Snapshot>();
        }
        public bool HasMovedObject(int entityId)
        {
            Track track;
            return tracks.TryGetValue(entityId,out track)&&track.everMoved;
        }
        public int ChangeCountAt(Vector3 coordinate)
        {
            var history=HistoryAt(coordinate);
            int changes=0;
            for(int i=1;i<history.Count;i++)
                if(history[i].change=="Moved"||history[i].change=="Changed"||
                   history[i].change=="MovedAway")changes++;
            return changes;
        }
        public string DescribeHistoryAt(Vector3 coordinate)
        {
            var history=HistoryAt(coordinate);
            if(history.Count==0)return "No history recorded at this coordinate";
            var parts=new List<string>();
            for(int i=history.Count-1;i>=0;i--)
            {
                var snap=history[i];
                parts.Add(snap.kind+" "+snap.name+" "+snap.change+
                    " at x="+snap.position.x.ToString("F1",CultureInfo.InvariantCulture)+
                    " z="+snap.position.z.ToString("F1",CultureInfo.InvariantCulture));
            }
            return string.Join(" | ",parts.ToArray());
        }
        static float HorizontalDistance(Vector3 a,Vector3 b)
        {
            Vector3 delta=a-b;delta.y=0f;
            return delta.magnitude;
        }
        /// <summary>
        /// Select a remembered coordinate AT DECISION TIME, without looking up
        /// unseen world objects. A telegraphed nearby strike takes priority,
        /// then a recently observed discovery, then any detected monster,
        /// then an ordinary visible object.
        /// </summary>
        public bool TryDecisionFocus(IReadOnlyList<AdventureVision.VisibleThing> visible,
            AdventureCombatState combat,out Vector3 coordinate,out int identity)
        {
            EnsureLoaded();
            coordinate=Vector3.zero;
            identity=0;
            bool monsterSensed=combat!=null&&combat.NearestMonster!=null&&
                combat.NearestMonster.IsAlive&&
                combat.NearestMonsterDistance<=combat.detectionRadius;
            if(monsterSensed&&combat.NearestMonster.IsWindingUp)
            {
                coordinate=combat.NearestMonster.transform.position;
                identity=combat.NearestMonster.transform.GetEntityId().GetHashCode();
                return true;
            }
            // The recent alert is memory of a real observation, not a request
            // to reveal the target's undiscovered current position.
            if(HasRecentAlert()&&LastAlert!=null&&
                HorizontalDistance(transform.position,LastAlert.position)<=
                    Mathf.Max(1f,alertRadius))
            {
                coordinate=LastAlert.position;identity=LastAlert.entityId;
                return true;
            }
            if(monsterSensed)
            {
                coordinate=combat.NearestMonster.transform.position;
                identity=combat.NearestMonster.transform.GetEntityId().GetHashCode();
                return true;
            }
            if(visible!=null)
                foreach(var thing in visible)
                    if(thing!=null&&Important(thing.Type))
                    {
                        coordinate=thing.WorldPosition;identity=thing.Identity;
                        return true;
                    }
            return false;
        }
        public string DecisionMapReport(IReadOnlyList<AdventureVision.VisibleThing> seen,
            int max=4,AdventureCombatState combat=null)
        {
            EnsureLoaded();
            var lines=new List<string>();
            var included=new HashSet<Vector2Int>();
            Vector3 focus;int id;
            if(TryDecisionFocus(seen,combat,out focus,out id))
            {
                included.Add(Coordinate(focus));
                lines.Add("Priority observed/remembered coordinate ("+
                    focus.x.ToString("F1",CultureInfo.InvariantCulture)+","+
                    focus.z.ToString("F1",CultureInfo.InvariantCulture)+
                    ") last 5 records: "+DescribeHistoryAt(focus));
            }
            if(seen!=null)
                foreach(var thing in seen)
                {
                    if(lines.Count>=max)break;
                    if(thing==null||!Important(thing.Type)||!included.Add(Coordinate(thing.WorldPosition)))
                        continue;
                    lines.Add("Visible "+thing.Type+" "+thing.Name+
                        " ("+thing.WorldPosition.x.ToString("F1",CultureInfo.InvariantCulture)+
                        ","+thing.WorldPosition.z.ToString("F1",CultureInfo.InvariantCulture)+
                        ") last 5 records: "+DescribeHistoryAt(thing.WorldPosition));
                }
            if(combat!=null&&lines.Count<max&&combat.NearestMonster!=null&&
                combat.NearestMonster.IsAlive&&
                combat.NearestMonsterDistance<=combat.detectionRadius)
            {
                Vector3 sensed=combat.NearestMonster.transform.position;
                if(included.Add(Coordinate(sensed)))
                    lines.Add("Combat sensor "+combat.NearestMonster.displayName+
                        " ("+sensed.x.ToString("F1",CultureInfo.InvariantCulture)+
                        ","+sensed.z.ToString("F1",CultureInfo.InvariantCulture)+
                        ") last 5 records: "+DescribeHistoryAt(sensed));
            }
            return lines.Count>0?string.Join("; ",lines.ToArray()):
                "No relevant sensed or visible coordinates";
        }
        public bool HasRecentAlert(float seconds=5f)=>
            LastAlert!=null&&Time.time-LastAlertAt<=seconds;

        public void ObserveAttackWindup(AdventureMonster monster,float distance)
        {
            EnsureLoaded();
            if(!loaded||monster==null||!monster.IsAlive||!monster.IsWindingUp||
                distance>Mathf.Max(1f,alertRadius)||
                Time.time>=monster.WindupEndsAt)return;
            int id=monster.transform.GetEntityId().GetHashCode();
            float previous;
            if(seenWindupEnds.TryGetValue(id,out previous)&&
                Mathf.Abs(previous-monster.WindupEndsAt)<.0001f)return;
            seenWindupEnds[id]=monster.WindupEndsAt;

            // Urgent attack signals must bypass ordinary discovery batching
            // (.65s), otherwise a .55s windup may have already landed.
            // Keep the world state unchanged; only request reconsideration.
            queued.RemoveAll(a=>a.entityId==id);
            var alert=new PerceptionAlert{
                entityId=id,kind="Monster",name=monster.displayName,
                reason="AttackWindup",urgent=true,
                position=monster.transform.position,
                history=DescribeHistoryAt(monster.transform.position)
            };
            LastAlert=alert;
            LastAlertAt=Time.time;
            AlertCount++;
            UrgentThreatAlerts++;
            ImportantChange?.Invoke(alert);
        }
        public void ObserveProximityMonster(AdventureMonster monster,float distance)
        {
            if(monster==null||!monster.IsAlive)return;
            // The combat proximity sensor is omnidirectional, unlike normal
            // forward-facing Vision. It supplies the position only while in
            // the real 9-unit proximity detection range.
            int id=monster.transform.GetEntityId().GetHashCode();
            if(distance>Mathf.Max(1f,alertRadius))
            {
                Track tracked;
                if(distance>Mathf.Max(alertRadius+.1f,rearmRadius)&&
                    tracks.TryGetValue(id,out tracked))tracked.armed=true;
                return;
            }
            Vector3 delta=monster.transform.position-transform.position;
            delta.y=0f;
            var sensed=new AdventureVision.VisibleThing{
                Identity=id,Type="Monster",Name=monster.displayName,
                WorldPosition=monster.transform.position,Distance=distance,
                Bearing=distance>.001f?delta/distance:Vector3.zero
            };
            AcceptScan(new[]{sensed});
        }
        public void AcceptScan(IReadOnlyList<AdventureVision.VisibleThing> visible)
        {
            EnsureLoaded();
            if(!loaded||visible==null)return;
            float now=Time.time;
            float inner=Mathf.Max(1f,alertRadius);
            float outer=Mathf.Max(inner+.1f,rearmRadius);
            Vector3 viewer=transform.position;
            for(int i=0;i<visible.Count;i++)
            {
                var observed=visible[i];
                if(observed==null||!Important(observed.Type)||observed.Identity==0)continue;
                int id=observed.Identity;
                Track track;
                bool first=!tracks.TryGetValue(id,out track);
                if(first)
                {
                    track=new Track{kind=observed.Type,name=observed.Name,
                        lastPosition=observed.WorldPosition,lastSeen=now,
                        lastSample=-999f,cellX=int.MinValue,cellZ=int.MinValue};
                    tracks[id]=track;
                }
                var key=Coordinate(observed.WorldPosition);
                Vector3 difference=observed.WorldPosition-track.lastPosition;
                difference.y=0f;
                bool moved=!first&&difference.sqrMagnitude>=.36f;
                bool cellMoved=!first&&(key.x!=track.cellX||key.y!=track.cellZ);
                bool renamed=!first&&(track.kind!=observed.Type||track.name!=observed.Name);
                bool periodic=now-track.lastSample>=Mathf.Max(5f,staticResampleSeconds);
                if(first||cellMoved||moved||renamed||periodic)
                {
                    // A monster that moved away leaves historical evidence at
                    // its OLD coordinate as well as the newly observed position.
                    if(cellMoved&&track.cellX!=int.MinValue)
                        Record(track.lastPosition,id,track.kind,track.name,"MovedAway");
                    Record(observed.WorldPosition,id,observed.Type,observed.Name,
                        renamed?"Changed":moved||cellMoved?"Moved":
                        first?"FirstSeen":"Unchanged");
                    track.lastSample=now;
                }
                if(moved||cellMoved)track.everMoved=true;
                track.cellX=key.x;track.cellZ=key.y;
                track.lastPosition=observed.WorldPosition;
                track.lastSeen=now;
                track.kind=observed.Type;track.name=observed.Name;
                Vector3 offset=observed.WorldPosition-viewer;offset.y=0f;
                float distance=offset.magnitude;
                if(distance>outer)track.armed=true;
                if(distance<=inner&&track.armed)
                {
                    track.armed=false;
                    var alert=new PerceptionAlert{entityId=id,kind=observed.Type,
                        name=observed.Name,position=observed.WorldPosition,
                        moved=moved||cellMoved,
                        previousAtCoordinate=Mathf.Max(0,HistoryAt(observed.WorldPosition).Count-1),
                        history=DescribeHistoryAt(observed.WorldPosition)};
                    queued.Add(alert);
                }
            }
            // Objects not currently visible never become "new" merely because
            // the player turned away. Rearm only after a proven >18-unit gap
            // from the last OBSERVED position, or if seen beyond the outer ring.
            foreach(var track in tracks.Values)
            {
                Vector3 delta=track.lastPosition-viewer;delta.y=0f;
                if(delta.sqrMagnitude>outer*outer)track.armed=true;
            }
        }
        void Update()
        {
            EnsureLoaded();
            if(!loaded)return;
            float now=Time.time;
            // At 4x/8x accelerated training, game seconds elapse much faster
            // than disk time. Bound autosave I/O in both time domains to
            // prevent frequent JSON rewrites while hundreds of cells update.
            if(dirty&&now>=nextSave&&Time.realtimeSinceStartup>=nextSaveRealtime)
            {
                nextSave=now+Mathf.Max(1f,autosaveGameSeconds);
                nextSaveRealtime=Time.realtimeSinceStartup+5f;
                Save();
            }
            if(queued.Count==0||now-lastDispatch<Mathf.Max(.1f,eventCooldown))return;
            lastDispatch=now;
            // One decision refresh per scan batch, not one per plant.
            PerceptionAlert latest=queued[0];
            queued.Clear();
            LastAlert=latest;LastAlertAt=now;AlertCount++;
            ImportantChange?.Invoke(latest);
        }
    }
}
