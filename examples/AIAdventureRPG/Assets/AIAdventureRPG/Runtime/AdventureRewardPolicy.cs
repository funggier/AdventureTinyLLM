using System;
using System.Globalization;
using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>Outcome-only priority-based survival rewards.
    /// Pre-state is the actual snapshot BEFORE the event, never the post-action HP.
    /// Clamped to [-2,+2] (death is uniquely the most severe outcome).
    /// No reward is emitted from observations or signs.</summary>
    public static class AdventureRewardPolicy
    {
        // Central tuning: adjust these numbers without touching action logic.
        public const float CriticalHPPercent=.20f;
        public const float CriticalMPPercent=.20f;
        public const float Death=-2f;
        public const float Kill=1.45f;
        public const float NormalHit=.35f;
        public const float SkillHit=.40f;
        public const float Dodge=.35f;
        public const float AvoidThorn=.28f;
        public const float NormalMiss=-.20f;
        public const float InvalidSpell=-.13f;
        public const float LowHPRecoveryBonus=.60f;
        public const float LowMPRecoveryBonus=.48f;

        static float ReadNumber(string source,string key,float fallback)
        {
            if(string.IsNullOrEmpty(source))return fallback;
            int i=source.IndexOf(" "+key+"=",StringComparison.Ordinal);
            if(i<0)return fallback;
            i+=key.Length+2;
            int end=i;
            while(end<source.Length&&(char.IsDigit(source[end])||
                source[end]=='.'||source[end]=='-'||source[end]=='+'))end++;
            float value;
            return float.TryParse(source.Substring(i,end-i),NumberStyles.Float,
                CultureInfo.InvariantCulture,out value)?value:fallback;
        }
        static bool Has(string text,string key)=>text.IndexOf(key,StringComparison.Ordinal)>=0;

        public static float FromOutcome(Vector4 delta,string eventText)
        {
            if(string.IsNullOrWhiteSpace(eventText))return 0f;
            if(Has(eventText," death=true"))return Death;
            if(Has(eventText," action=ReadSign"))return 0f;
            if(Has(eventText," action=AbilityRejected "))return InvalidSpell;
            if(Has(eventText," action=AttackMiss"))return NormalMiss;
            if(Has(eventText," action=DodgeMonster"))return Dodge;
            if(Has(eventText," action=AvoidThorn"))return AvoidThorn;

            float preHP=ReadNumber(eventText,"pre_hp",100f);
            float preMP=ReadNumber(eventText,"pre_mp",100f);
            float maxMP=Mathf.Max(1f,ReadNumber(eventText,"max_mp",100f));
            // Match the EXACT configurable thresholds used by the actor's
            // CombatState. This is a pre-event value, not the post-hit HP.
            float hpThreshold=Mathf.Clamp(ReadNumber(eventText,"hp_low_ratio",
                CriticalHPPercent),.01f,.5f);
            float mpThreshold=Mathf.Clamp(ReadNumber(eventText,"mp_low_ratio",
                CriticalMPPercent),.01f,.5f);
            bool criticalHP=preHP<100f*hpThreshold;
            bool criticalMP=preMP/maxMP<mpThreshold;
            bool herb=Has(eventText," action=Collect Herb")||Has(eventText," action=Eat Herb");
            bool berry=Has(eventText," action=Eat SafeBerry")||Has(eventText," action=Collect SafeBerry");
            bool manaPickup=Has(eventText," action=Collect LoreStone")||Has(eventText," action=Eat LoreStone");
            bool reply=Has(eventText," action=Reply ");
            bool ability=Has(eventText," action=Ability ");
            bool attack=Has(eventText," action=StrikeMonster");
            bool defeat=Has(eventText," action=DefeatMonster");
            float score=0f;

            // Actual hit / survival, using PRE-hit criticality.
            if(delta.x<-.001f)
                score+=delta.x*(criticalHP?.075f:.04f);
            else if(delta.x>.001f)
            {
                score+=Mathf.Min(.45f,delta.x*.012f);
                if(criticalHP)score+=herb?LowHPRecoveryBonus:.46f;
                else score+=.10f;
            }
            else if(herb||berry||Has(eventText," action=Ability Heal"))
                score-=.18f; // wasted consumable or invalid full-health recovery

            // Mana only matters if it really increased. Casting spends MP,
            // and a successful hit must outweigh the corresponding cost.
            if(delta.y>.001f)
            {
                score+=Mathf.Min(.32f,delta.y*.009f);
                score+=criticalMP?LowMPRecoveryBonus:.08f;
            }
            else if(delta.y<-.001f)
                score+=delta.y*.002f;
            else if(manaPickup||reply)
            {
                // No benefit when MP is capped. Reading is handled above.
                score-=.12f;
            }

            if(defeat)score+=Kill;
            else if(attack)score+=NormalHit;
            if(ability)
            {
                int hits=Mathf.RoundToInt(ReadNumber(eventText,"hits",0));
                int kills=Mathf.RoundToInt(ReadNumber(eventText,"kills",0));
                if(kills>0)score+=Kill+Mathf.Min(.25f,(kills-1)*.12f);
                if(hits>0)score+=Mathf.Min(.65f,hits*SkillHit);
                else if(!Has(eventText,"Ability Heal"))score-=.22f;
            }

            // Unrelated happiness/boredom changes are small and never
            // dominate lethal damage, a successful kill or true recovery.
            score+=delta.z*.002f-delta.w*.0002f;
            return Mathf.Clamp(score,-2f,1.95f);
        }

        public static float PassiveNoProgressPenalty(float elapsedGameSeconds,
            float secondsWithoutProgress,int progressEventsSinceAction,
            float progressGraceSeconds,bool readInformationalSign=false)
        {
            if(float.IsNaN(elapsedGameSeconds)||float.IsInfinity(elapsedGameSeconds)||
               float.IsNaN(secondsWithoutProgress)||float.IsInfinity(secondsWithoutProgress))
                return 0f;
            if(elapsedGameSeconds<.15f||elapsedGameSeconds>3.1f||
               progressEventsSinceAction!=0||readInformationalSign||
               secondsWithoutProgress<Mathf.Max(1f,progressGraceSeconds))
                return 0f;
            return -Mathf.Min(.035f,elapsedGameSeconds*.007f);
        }

        public static float ZoneStayPenalty(float elapsedGameSeconds,
            bool zoneOverstayed,int progressEventsSinceAction,bool readSign=false)
        {
            if(float.IsNaN(elapsedGameSeconds)||float.IsInfinity(elapsedGameSeconds)||
               elapsedGameSeconds<.15f||elapsedGameSeconds>3.1f||
               !zoneOverstayed||progressEventsSinceAction!=0||readSign)
                return 0f;
            return -Mathf.Min(.01f,elapsedGameSeconds*.002f);
        }
    }
}
