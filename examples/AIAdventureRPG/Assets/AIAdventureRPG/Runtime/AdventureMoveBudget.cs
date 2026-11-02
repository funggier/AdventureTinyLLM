using UnityEngine;

namespace AIAdventureRPG
{
    // Game-space horizontal distance budget, independent of Unity frame rate.
    // The executor credits only actual Rigidbody displacement; blocked steps
    // cannot consume the commanded distance.
    public sealed class AdventureMoveBudget
    {
        public float Requested {get;private set;}
        public float Travelled {get;private set;}
        public bool Active {get;private set;}
        public float Remaining=>Mathf.Max(0f,Requested-Travelled);
        public Vector2 Direction {get;private set;}
        public void Begin(Vector2 direction,float distance)
        {
            Requested=0;Travelled=0;Active=false;Direction=Vector2.zero;
            if(!float.IsNaN(distance)&&!float.IsInfinity(distance)&&
               direction.sqrMagnitude>.0001f&&distance>0f)
            {
                Requested=Mathf.Clamp(distance,.1f,12f);
                Direction=direction.normalized;
                Active=true;
            }
        }
        public float Allow(float intendedStep)=>Active?Mathf.Min(Remaining,Mathf.Max(0f,intendedStep)):0f;
        public bool Advance(float actualXZ)
        {
            if(!Active)return false;
            if(float.IsNaN(actualXZ)||float.IsInfinity(actualXZ))return false;
            Travelled=Mathf.Min(Requested,Travelled+Mathf.Max(0f,actualXZ));
            if(Remaining<=.0001f)return true;
            return false;
        }
        public void Cancel(){Active=false;}
    }
}
