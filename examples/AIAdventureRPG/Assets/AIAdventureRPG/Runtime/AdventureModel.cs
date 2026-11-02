using System;
using UnityEngine;
namespace AIAdventureRPG
{
    public enum AdventureObjectKind { Thorn, Herb, LoreStone, SafeBerry, PoisonBerry }
    public enum ControlMode { Manual, Local, Ollama, Tiny }
    // UI selection. Tiny train and trained inference use one control backend,
    // but the user explicitly selects which lifecycle is allowed.
    public enum AdventurePlayMode { Manual, TrainTiny, TrainedTiny, Ollama }
    public enum DialogueAnswer { None, Good, Bad }
    // Skill is separate from movement and from E (ordinary interact/attack).
    public enum AdventureAbility { None, NormalAttack, SpecialAttack, ArcPulse, Heal }
    [Serializable] public struct AdventureAction
    {
        public float moveX,moveZ,faceX,faceZ;
        // AI only: horizontal game-world units to travel, then stop.
        // Manual mode ignores this field and retains continuous WASD movement.
        public float moveDistance;
        public bool interact;
        public DialogueAnswer answer;
        public AdventureAbility ability;
        public AdventureAction(float x,float z,float fx=0,float fz=0,bool interact=false,DialogueAnswer answer=DialogueAnswer.None,float distance=0f,AdventureAbility ability=AdventureAbility.None)
        {moveX=x;moveZ=z;faceX=fx;faceZ=fz;this.interact=interact;this.answer=answer;moveDistance=distance;this.ability=ability;}
    }
    public static class AdventureRules
    {
        public static float Stat(float value)=>Mathf.Clamp(value,0,100);
        public static float HealthAfter(float health,AdventureObjectKind kind)
        {
            switch(kind)
            {
                case AdventureObjectKind.Thorn:return Stat(health-18);
                case AdventureObjectKind.Herb:return Stat(health+26);
                case AdventureObjectKind.SafeBerry:return Stat(health+12);
                case AdventureObjectKind.PoisonBerry:return Stat(health-32);
                default:return Stat(health);
            }
        }
        public static int MagicReplyReward(AdventureObjectKind topic,DialogueAnswer reply)
        {
            if(reply==DialogueAnswer.None)return 0;
            bool good=topic==AdventureObjectKind.Herb||topic==AdventureObjectKind.SafeBerry;
            return (good && reply==DialogueAnswer.Good || !good && reply==DialogueAnswer.Bad)?12:-7;
        }
    }
}