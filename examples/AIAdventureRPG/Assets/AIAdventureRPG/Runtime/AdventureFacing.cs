using UnityEngine;

namespace AIAdventureRPG
{
    // Rotation policy for finite AI movement. The measured displacement is
    // authoritative: a steering detour or world-boundary clamp may change it
    // independently of the model's requested direction and explicit face.
    public static class AdventureFacing
    {
        public static Quaternion ForTravel(Quaternion current, Vector3 displacement,
            float fixedDeltaTime, float turnSpeed)
        {
            displacement.y=0f; // never tilt toward a slope
            if(displacement.sqrMagnitude<=.00000001f)
                return current; // blocked / idle: retain the previous heading
            var target=Quaternion.LookRotation(displacement.normalized,Vector3.up);
            return Quaternion.Slerp(current,target,
                Mathf.Clamp01(Mathf.Max(0f,fixedDeltaTime)*Mathf.Max(0f,turnSpeed)));
        }
    }
}
