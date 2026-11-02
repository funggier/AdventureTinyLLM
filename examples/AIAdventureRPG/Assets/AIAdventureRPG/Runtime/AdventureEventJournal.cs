using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AIAdventureRPG
{
    // One bounded chronology shared by Ollama prompt, Tiny numerical sensors
    // and HUD. Entries are observations, not omniscient hidden game state.
    public sealed class AdventureEventJournal : MonoBehaviour
    {
        public enum Kind { None, Seen, Movement, Blocked, Outcome, Danger }
        public struct Event
        {
            public Kind kind;
            public Vector3 position;
            public string subject,detail;
            public float time;
        }
        readonly List<Event> recent=new List<Event>();
        readonly HashSet<int> discovered=new HashSet<int>();
        public Event Latest{get;private set;}
        public int Count=>recent.Count;
        public float LastRequestedDistance{get;private set;}
        public float LastTravelledDistance{get;private set;}
        public float LastMovementStatus{get;private set;}
        public Vector3 LastStopPosition{get;private set;}
        public float LastSeenKind{get;private set;}
        public Vector3 LastSeenPosition{get;private set;}
        public string LastMovementReport{get;private set;}="No AI move yet";
        static string Coord(Vector3 p)=>"x="+p.x.ToString("F1",CultureInfo.InvariantCulture)+
            ",z="+p.z.ToString("F1",CultureInfo.InvariantCulture);
        public void Record(Kind kind,string subject,string detail,Vector3 position)
        {
            Latest=new Event{kind=kind,subject=subject,detail=detail,position=position,time=Time.time};
            recent.Insert(0,Latest);
            if(recent.Count>36)recent.RemoveAt(recent.Count-1);
        }
        public void Seen(int identity,string type,string name,Vector3 coordinate)
        {
            if(!discovered.Add(identity))return;
            LastSeenKind=type=="Monster"?-1f:
                type=="Thorn"||type=="Berry"?-.5f:
                type=="Herb"||type=="LoreStone"?1f:type=="NPC"?.5f:.25f;
            LastSeenPosition=coordinate;
            Record(Kind.Seen,type,name+" seen",coordinate);
        }
        public void Moved(string result,float requested,float travelled,Vector3 coordinate,string obstruction="")
        {
            LastRequestedDistance=requested;LastTravelledDistance=travelled;
            LastMovementStatus=result=="reached"?1f:result=="blocked"?-1f:0f;
            LastStopPosition=coordinate;
            LastMovementReport=result+" "+travelled.ToString("F2",CultureInfo.InvariantCulture)+
                "/"+requested.ToString("F2",CultureInfo.InvariantCulture)+
                " units at ("+Coord(coordinate)+")"+(string.IsNullOrEmpty(obstruction)?"":
                    " cause="+obstruction);
            Record(result=="blocked"?Kind.Blocked:Kind.Movement,
                result,LastMovementReport,coordinate);
        }
        public string Report(int max=8)
        {
            return ReportSince(-1f,max);
        }
        public string ReportSince(float since,int max=8)
        {
            if(recent.Count==0)return "No observations yet";
            var parts=new List<string>();
            for(int i=0;i<recent.Count&&parts.Count<max;i++)
            {
                var item=recent[i];
                if(since>=0f&&item.time<=since)continue;
                parts.Add(item.kind+" "+item.subject+" at ("+Coord(item.position)+"): "+item.detail);
            }
            return parts.Count==0?"No observations since context reset":
                string.Join("; ",parts.ToArray());
        }
    }
}
