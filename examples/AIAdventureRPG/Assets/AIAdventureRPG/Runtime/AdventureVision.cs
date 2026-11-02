using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace AIAdventureRPG
{
    public sealed class AdventureVision:MonoBehaviour
    {
        [Min(3)] public float viewRange=30f;
        [Range(60,360)] public float fieldOfView=170f;
        public float eyeHeight=1.2f,scanEvery=.28f;
        public class VisibleThing
        {
            public string Type,Name,ReadableText;
            // Observable material color, not the hidden safe/poison rule.
            public float ColorRedMinusBlue;
            public float Distance;
            public Vector3 Bearing,WorldPosition;
        }
        readonly List<VisibleThing> visible=new List<VisibleThing>();
        public IReadOnlyList<VisibleThing> Visible=>visible;
        float nextScan;
        AdventureWorld world;
        void Update()
        {
            if(Time.time<nextScan)return;
            nextScan=Time.time+Mathf.Max(.1f,scanEvery);
            if(world==null)world=GetComponent<AdventureAgent>()?.World;
            visible.Clear();
            if(world==null)return;
            foreach(var item in world.Pickups)
                if(item!=null&&item.Available)
                {
                    // Do not leak the hidden game rule via an item kind or its name.
                    bool berry=item.kind==AdventureObjectKind.SafeBerry ||
                               item.kind==AdventureObjectKind.PoisonBerry;
                    float colorContrast=0f;
                    if(berry)
                    {
                        var renderer=item.GetComponent<Renderer>();
                        if(renderer!=null&&renderer.sharedMaterial!=null)
                        {
                            Color color=renderer.sharedMaterial.color;
                            colorContrast=Mathf.Clamp(color.r-color.b,-1f,1f);
                        }
                    }
                    TrySee(item.transform.position,berry?"Berry":item.kind.ToString(),
                        berry?item.displayName:item.name,item.transform,null,colorContrast);
                }
            foreach(var npc in world.NPCs)
                if(npc!=null)TrySee(npc.transform.position,"NPC",npc.displayName,npc.transform);
            foreach(var monster in world.Monsters)
                if(monster!=null&&monster.IsAlive)
                    TrySee(monster.transform.position,"Monster",monster.displayName,monster.transform);
            foreach(var sign in world.Signs)
                if(sign!=null)TrySee(sign.transform.position,"Sign",sign.displayName,
                    sign.transform,CanReadText(sign)?sign.signText:null);
            visible.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
        }
        bool InSight(Vector3 location,Transform target,float maxRange)
        {
            Vector3 offset=location-transform.position;offset.y=0;
            float distance=offset.magnitude;
            if(distance<.01f||distance>maxRange)return false;
            if(Vector3.Angle(transform.forward,offset/distance)>fieldOfView*.5f)return false;
            Vector3 from=transform.position+Vector3.up*eyeHeight;
            Vector3 ray=location+Vector3.up*.75f-from;
            RaycastHit hit;
            if(Physics.Raycast(from,ray.normalized,out hit,Mathf.Max(0,ray.magnitude-.15f),
                ~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(target)&&hit.transform!=target)return false;
            return true;
        }
        public bool CanSeeSign(AdventureSign sign)=>
            sign!=null && InSight(sign.transform.position,sign.transform,viewRange);
        public bool CanReadText(AdventureSign sign)=>
            sign!=null && InSight(sign.transform.position,sign.transform,
                Mathf.Min(viewRange,sign.readDistance));
        void TrySee(Vector3 location,string kind,string name,Transform target,
            string text=null,float colorContrast=0f)
        {
            if(!InSight(location,target,viewRange))return;
            Vector3 offset=location-transform.position;offset.y=0;
            float distance=offset.magnitude;
            visible.Add(new VisibleThing{Type=kind,Name=name,Distance=distance,
                Bearing=offset/distance,WorldPosition=location,ReadableText=text,
                ColorRedMinusBlue=colorContrast});
            var journal=GetComponent<AdventureEventJournal>();
            if(journal!=null)journal.Seen(target.GetEntityId().GetHashCode(),kind,name,location);
        }
        public VisibleThing NearestVisible()=>visible.Count>0?visible[0]:null;
        public string Report()
        {
            string edge="";
            var actor=GetComponent<AdventureAgent>();
            var scene=actor==null?null:actor.World;
            if(scene!=null)
            {
                if(transform.position.x<=4)edge+=" WEST boundary nearby;";
                if(transform.position.x>=scene.Width-4)edge+=" EAST boundary nearby;";
                if(transform.position.z<=4)edge+=" SOUTH boundary nearby;";
                if(transform.position.z>=scene.Depth-4)edge+=" NORTH boundary nearby;";
            }
            if(visible.Count==0)return "Nothing visible; explore."+edge;
            var entries=new List<string>();
            for(int i=0;i<Mathf.Min(12,visible.Count);i++)
            {
                var s=visible[i];
                string description=s.Type+" "+s.Name+" "+s.Distance.ToString("F1",CultureInfo.InvariantCulture)+
                    " units, bearing_x="+s.Bearing.x.ToString("F2",CultureInfo.InvariantCulture)+
                    " bearing_z="+s.Bearing.z.ToString("F2",CultureInfo.InvariantCulture)+
                    ", seen_at=(x="+s.WorldPosition.x.ToString("F1",CultureInfo.InvariantCulture)+
                    ",z="+s.WorldPosition.z.ToString("F1",CultureInfo.InvariantCulture)+")";
                if(s.Type=="Berry")
                    description+=" [appearance: "+(s.ColorRedMinusBlue>.3f?"gold-toned":
                        s.ColorRedMinusBlue<0f?"purple-toned":"mixed-colored")+"]";
                if(s.Type=="Sign")
                    description+=string.IsNullOrEmpty(s.ReadableText)?
                        " [text too far to read]":" [sign reads: \""+s.ReadableText.Replace("\"","'")+"\"]";
                entries.Add(description);
            }
            return string.Join("; ",entries.ToArray())+edge;
        }
    }
}