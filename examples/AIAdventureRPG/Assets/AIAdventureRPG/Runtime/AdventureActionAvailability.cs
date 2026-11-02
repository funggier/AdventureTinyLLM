using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>
    /// Authoritative legal movement choices for the nine Tiny movement logits.
    /// Removes actions that would hit the hard world bounds BEFORE sampling.
    /// No rewarded steering or fabricated action: model selects from legal moves.
    /// </summary>
    public static class AdventureActionAvailability
    {
        // Must match adventure_tiny_llm.contracts.MOVEMENTS ordering.
        public static readonly Vector2[] Directions={
            Vector2.zero,Vector2.up,Vector2.down,Vector2.left,Vector2.right,
            new Vector2(-.7071f,.7071f),new Vector2(.7071f,.7071f),
            new Vector2(-.7071f,-.7071f),new Vector2(.7071f,-.7071f)
        };
        public static bool[] ForPosition(Vector3 position,float width,float depth,
            float probe=2.0f,bool avoidIdleAtBoundary=false,
            int escapeX=0,int escapeZ=0)
        {
            bool[] allowed=new bool[Directions.Length];
            // Match the exact clamp in AdventureAgent.FixedUpdate (2..width-2).
            // Do not treat walking PARALLEL to an edge as illegal when the
            // character is already precisely at x=2 or z=2.
            float minX=2f-.001f,maxX=width-2f+.001f;
            float minZ=2f-.001f,maxZ=depth-2f+.001f;
            allowed[0]=true; // safe fallback always exists
            for(int i=1;i<Directions.Length;i++)
            {
                Vector2 d=Directions[i];
                float x=position.x+d.x*probe,z=position.z+d.y*probe;
                allowed[i]=x>=minX&&x<=maxX&&z>=minZ&&z<=maxZ;
            }
            if(escapeX!=0||escapeZ!=0)
            {
                // Temporary anti-stuck recovery: only model actions moving
                // toward the interior of the boundary that trapped the agent.
                // This is an explicit behavior constraint, never a reward.
                for(int i=0;i<allowed.Length;i++)
                {
                    Vector2 d=Directions[i];
                    if(escapeX>0&&d.x<=.01f || escapeX<0&&d.x>=-.01f ||
                       escapeZ>0&&d.y<=.01f || escapeZ<0&&d.y>=-.01f)
                        allowed[i]=false;
                }
                // Leave a safe idle option if all directional paths vanished.
                if(LegalCount(allowed)==0)allowed[0]=true;
                return allowed;
            }
            if(avoidIdleAtBoundary)
            {
                bool nearBorder=position.x<=minX+probe||position.x>=maxX-probe||
                                position.z<=minZ+probe||position.z>=maxZ-probe;
                if(nearBorder)
                {
                    for(int i=1;i<allowed.Length;i++)
                        if(allowed[i]){allowed[0]=false;break;}
                }
            }
            return allowed;
        }
        public static bool IsLegal(float moveX,float moveZ,bool[] legal)
        {
            if(legal==null||legal.Length!=Directions.Length)return false;
            for(int i=0;i<Directions.Length;i++)
                if(Mathf.Abs(Directions[i].x-moveX)<.03f&&
                   Mathf.Abs(Directions[i].y-moveZ)<.03f)return legal[i];
            return false;
        }
        public static int LegalCount(bool[] legal)
        {
            int count=0;
            if(legal!=null)foreach(bool flag in legal)if(flag)count++;
            return count;
        }
    }
}
