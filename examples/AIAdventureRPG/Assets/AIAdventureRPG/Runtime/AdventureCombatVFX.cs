using System.Collections;
using UnityEngine;

namespace AIAdventureRPG
{
    /// <summary>Procedural, asset-free impact/cast effects. Normal hits are deliberately
    /// understated; MP abilities have stronger glowing bursts, light and smoke.</summary>
    public sealed class AdventureCombatVFX : MonoBehaviour
    {
        public int NormalBursts{get;private set;}
        public int SpecialBursts{get;private set;}
        public int SkillBursts{get;private set;}
        public int LiveEffects{get;private set;}
        static Material particleMaterial;
        static Texture2D softParticle;
        static Texture2D SoftParticle()
        {
            if(softParticle!=null)return softParticle;
            const int size=32;
            softParticle=new Texture2D(size,size,TextureFormat.RGBA32,false);
            softParticle.name="Adventure Soft Particle";
            softParticle.hideFlags=HideFlags.HideAndDontSave;
            softParticle.wrapMode=TextureWrapMode.Clamp;
            softParticle.filterMode=FilterMode.Bilinear;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=(x+0.5f-size*.5f)/(size*.5f);
                float dy=(y+0.5f-size*.5f)/(size*.5f);
                float falloff=Mathf.Pow(Mathf.Clamp01(1f-dx*dx-dy*dy),2.5f);
                softParticle.SetPixel(x,y,new Color(1f,1f,1f,falloff));
            }
            softParticle.Apply();
            return softParticle;
        }

        static Material FindMaterial()
        {
            if(particleMaterial!=null)return particleMaterial;
            var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if(shader==null)shader=Shader.Find("Particles/Standard Unlit");
            if(shader==null)shader=Shader.Find("Legacy Shaders/Particles/Additive");
            if(shader==null)shader=Shader.Find("Sprites/Default");
            if(shader!=null)
            {
                particleMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                if(particleMaterial.HasProperty("_BaseColor"))
                    particleMaterial.SetColor("_BaseColor",Color.white);
                if(particleMaterial.HasProperty("_BaseMap"))
                    particleMaterial.SetTexture("_BaseMap",SoftParticle());
                if(particleMaterial.HasProperty("_MainTex"))
                    particleMaterial.SetTexture("_MainTex",SoftParticle());
                // Transparent soft billboard rather than an opaque square in URP.
                if(particleMaterial.HasProperty("_Surface"))
                {
                    particleMaterial.SetFloat("_Surface",1f);
                    particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    particleMaterial.SetOverrideTag("RenderType","Transparent");
                }
                if(particleMaterial.HasProperty("_SrcBlend"))
                    particleMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if(particleMaterial.HasProperty("_DstBlend"))
                    particleMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if(particleMaterial.HasProperty("_ZWrite"))particleMaterial.SetInt("_ZWrite",0);
                particleMaterial.renderQueue=3000;
            }
            return particleMaterial;
        }
        void Burst(Vector3 position,Color tint,int count,float size,float speed,
            float life,bool light=false)
        {
            GameObject fx=new GameObject("CombatFX");
            fx.transform.position=position;
            var ps=fx.AddComponent<ParticleSystem>();
            // Unity auto-plays a newly attached ParticleSystem immediately.
            // Stop it before editing duration/curves to avoid runtime warnings.
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.playOnAwake=false;
            main.loop=false;
            main.duration=Mathf.Max(.2f,life);
            main.startLifetime=new ParticleSystem.MinMaxCurve(life*.45f,life);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.5f,size);
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.35f,speed);
            main.startColor=tint;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=Mathf.Max(8,count);
            main.gravityModifier=.20f;
            var emission=ps.emission;emission.enabled=false;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Sphere;
            shape.radius=.25f;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            var mat=FindMaterial();
            if(mat!=null)renderer.sharedMaterial=mat;
            ps.Play();
            ps.Emit(count);
            if(light)
            {
                var glow=fx.AddComponent<Light>();
                glow.type=LightType.Point;
                glow.color=tint;
                glow.intensity=3f;
                glow.range=5f;
                glow.shadows=LightShadows.None;
            }
            LiveEffects++;
            float ttl=Mathf.Max(1.1f,life+0.4f);
            if(light)StartCoroutine(FadeLight(fx.GetComponent<Light>(),Mathf.Min(1.1f,ttl)));
            StartCoroutine(ReleaseCounter(ttl));
            Destroy(fx,ttl);
        }
        // A bright expanding glyph gives each magical ability a legible
        // silhouette, even in daylight. Ordinary melee deliberately has none.
        void MagicRing(Vector3 center,Color tint,float finalRadius,float life)
        {
            var ring=new GameObject("CombatFX - Expanding Magic Ring");
            ring.transform.position=center;
            var line=ring.AddComponent<LineRenderer>();
            line.useWorldSpace=false;
            line.loop=true;
            line.positionCount=64;
            line.alignment=LineAlignment.View;
            line.textureMode=LineTextureMode.Stretch;
            line.numCornerVertices=2;
            line.numCapVertices=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows=false;
            var baseMaterial=FindMaterial();
            Material ownedMaterial=null;
            if(baseMaterial!=null)
            {
                var lineMaterial=new Material(baseMaterial);
                ownedMaterial=lineMaterial;
                if(lineMaterial.HasProperty("_BaseMap"))
                    lineMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);
                if(lineMaterial.HasProperty("_MainTex"))
                    lineMaterial.SetTexture("_MainTex",Texture2D.whiteTexture);
                lineMaterial.name="Adventure Temporary Magic Ring";
                line.sharedMaterial=lineMaterial;
            }
            LiveEffects++;
            StartCoroutine(ExpandMagicRing(line,ownedMaterial,tint,
                Mathf.Max(.2f,finalRadius),Mathf.Max(.1f,life)));
        }
        IEnumerator ExpandMagicRing(LineRenderer line,Material material,
            Color tint,float limit,float life)
        {
            var positions=new Vector3[64];
            float elapsed=0f;
            while(line!=null&&elapsed<life)
            {
                float t=Mathf.Clamp01(elapsed/life);
                float radius=Mathf.Lerp(.35f,limit,1f-(1f-t)*(1f-t));
                for(int i=0;i<positions.Length;i++)
                {
                    float angle=i*2f*Mathf.PI/positions.Length;
                    positions[i]=new Vector3(Mathf.Cos(angle)*radius,0f,
                        Mathf.Sin(angle)*radius);
                }
                line.SetPositions(positions);
                line.widthMultiplier=Mathf.Lerp(.28f,.035f,t);
                Color fading=tint;fading.a=Mathf.Lerp(.95f,0f,t);
                line.startColor=Color.white;line.endColor=Color.white;
                if(material!=null&&material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor",fading);
                elapsed+=Time.deltaTime;
                yield return null;
            }
            if(line!=null)Destroy(line.gameObject);
            if(material!=null)Destroy(material);
            LiveEffects=Mathf.Max(0,LiveEffects-1);
        }
        IEnumerator FadeLight(Light glow,float seconds)
        {
            if(glow==null)yield break;
            float start=glow.intensity,elapsed=0f;
            while(glow!=null&&elapsed<seconds)
            {
                elapsed+=Time.deltaTime;
                float remaining=1f-Mathf.Clamp01(elapsed/seconds);
                glow.intensity=start*remaining*remaining;
                yield return null;
            }
        }
        IEnumerator ReleaseCounter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            LiveEffects=Mathf.Max(0,LiveEffects-1);
        }
        public void PlayNormal(Vector3 hit)
        {
            NormalBursts++;
            // No light, smoke plume, or magical halo: weak pale dust only.
            Burst(hit+Vector3.up*.4f,new Color(.80f,.76f,.65f,.55f),
                7,.09f,.8f,.22f);
        }
        public void PlaySpecial(Vector3 hand,Vector3 hit)
        {
            SpecialBursts++;
            Burst(hand,new Color(.35f,.45f,1f,1f),27,.19f,3.6f,1.4f,true);
            Burst(hit+Vector3.up*.8f,new Color(.60f,.26f,1f,1f),
                42,.31f,4.4f,1.6f,true);
            Burst(hit,new Color(.48f,.45f,.62f,.38f),
                18,.38f,1.5f,1.9f);
            MagicRing(hit+Vector3.up*.15f,new Color(.77f,.35f,1f),1.65f,.95f);
        }
        public void PlayArcPulse(Vector3 center)
        {
            SkillBursts++;
            Burst(center+Vector3.up*1.1f,new Color(.16f,.78f,1f,1f),
                75,.38f,6f,2.4f,true);
            Burst(center+Vector3.up*.4f,new Color(.30f,.48f,.56f,.32f),
                28,.52f,2.5f,2.7f);
            MagicRing(center-Vector3.up*.60f,new Color(.18f,.91f,1f),5.3f,1.25f);
        }
        public void PlayHeal(Vector3 center)
        {
            SkillBursts++;
            Burst(center+Vector3.up*1.1f,new Color(.33f,1f,.49f,1f),
                56,.26f,2.5f,2.1f,true);
            Burst(center+Vector3.up*.3f,new Color(.40f,.65f,.51f,.24f),
                16,.41f,1.25f,2.2f);
            MagicRing(center-Vector3.up*.58f,new Color(.25f,1f,.54f),2.3f,1.45f);
        }
    }
}
