using UnityEngine;
namespace AIAdventureRPG
{
    // One persistent third-person chase/orbit camera for both Human and AI control.
    [RequireComponent(typeof(Camera))]
    public sealed class AdventureCamera:MonoBehaviour
    {
        public AdventureAgent target;
        public float humanDistance=10f;
        public float humanHeight=3.0f;
        public float orbitYaw=0f;
        public float orbitPitch=27f;
        public float smoothing=12f;
        void LateUpdate()
        {
            if(target==null)return;
            // Right mouse controls orbit; wheel zoom. Mouse doesn't drag the character.
            if(Input.GetMouseButton(1))
            {
                orbitYaw+=Input.GetAxis("Mouse X")*2.5f;
                orbitPitch=Mathf.Clamp(orbitPitch-Input.GetAxis("Mouse Y")*2.3f,12f,65f);
            }
            humanDistance=Mathf.Clamp(humanDistance-Input.mouseScrollDelta.y,5f,17f);
            Vector3 focus=target.transform.position+Vector3.up*humanHeight*.55f;
            Quaternion orbit=Quaternion.Euler(orbitPitch,orbitYaw,0);
            Vector3 desired=focus+orbit*(Vector3.back*humanDistance);
            // Simple wall avoidance, so the camera does not go underground at slopes.
            RaycastHit hit;
            Vector3 ray=desired-focus;
            if(Physics.Raycast(focus,ray.normalized,out hit,ray.magnitude,~0,QueryTriggerInteraction.Ignore))
                desired=hit.point-ray.normalized*.3f;
            transform.position=Vector3.Lerp(transform.position,desired,Time.deltaTime*smoothing);
            transform.rotation=Quaternion.LookRotation(focus-transform.position);
        }
        public Vector3 HorizontalForward()
        {
            var forward=transform.forward;forward.y=0;
            return forward.sqrMagnitude>.001f?forward.normalized:Vector3.forward;
        }
        public Vector3 HorizontalRight()
        {
            var right=transform.right;right.y=0;
            return right.sqrMagnitude>.001f?right.normalized:Vector3.right;
        }
    }
}