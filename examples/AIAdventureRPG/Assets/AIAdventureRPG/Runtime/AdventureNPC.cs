using UnityEngine;
namespace AIAdventureRPG
{
    public sealed class AdventureNPC:MonoBehaviour
    {
        public string displayName="Sage";
        public AdventureObjectKind teachingTopic=AdventureObjectKind.Herb;
        public float talkDistance=4.5f;
        public float facingThreshold=.84f;
        public float repeatDelay=9f;
        public float responseWindow=12f;
        public float nextConversationAt;
        public string SpokenWord=>teachingTopic==AdventureObjectKind.Herb?"Herb":teachingTopic==AdventureObjectKind.Thorn?"Thorn":"Rune";
        public bool CanTalk=>Time.time>=nextConversationAt;
        public bool FacingCorrect(AdventureAgent agent)
        {
            if(agent==null)return false;
            var delta=transform.position-agent.transform.position;delta.y=0;
            return delta.magnitude<=talkDistance && delta.sqrMagnitude>.01f &&
                Vector3.Dot(agent.transform.forward,delta.normalized)>=facingThreshold;
        }
        public bool TryTalk(AdventureAgent agent)
        {
            if(!CanTalk || !FacingCorrect(agent))return false;
            nextConversationAt=Time.time+repeatDelay;
            agent.Stats.Meet(displayName,SpokenWord);
            agent.BeginDialogue(this);
            return true;
        }
    }
}