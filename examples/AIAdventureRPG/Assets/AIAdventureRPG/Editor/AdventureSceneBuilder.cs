using System;
using AIAdventureRPG;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIAdventureRPG.Editor
{
    public static class AdventureSceneBuilder
    {
        public const string ScenePath="Assets/AIAdventureRPG/Scenes/AdventureValley.unity";
        const string MaterialFolder="Assets/AIAdventureRPG/Materials/";
        [MenuItem("AI Adventure RPG/Build New Adventure Valley")]
        public static void Build()
        {
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.51f,.63f,.72f);
            RenderSettings.fog=true;
            RenderSettings.fogColor=new Color(.56f,.69f,.78f);
            RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogStartDistance=32;
            RenderSettings.fogEndDistance=100;

            GameObject root=new GameObject("00 - AI ADVENTURE WORLD");
            var world=root.AddComponent<AdventureWorld>();
            world.mapWidth=64;world.mapDepth=64;world.mapSeed=20261101;
            world.terrainMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Content/Materials/VoxelTerrain.mat");

            var character=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            character.name="01 - ADVENTURER (Human / AI)";
            character.transform.localScale=Vector3.one;
            character.transform.position=new Vector3(11,12,13);
            var body=character.AddComponent<Rigidbody>();
            body.isKinematic=true;
            body.useGravity=false;
            body.interpolation=RigidbodyInterpolation.Interpolate;
            body.constraints=RigidbodyConstraints.FreezeRotation;
            character.GetComponent<Renderer>().sharedMaterial=MaterialAsset("Player_Armor",new Color(.18f,.39f,.55f));
            // Silhouette is a small RPG character rather than the original test sphere.
            var head=Primitive(PrimitiveType.Sphere,"Face",character.transform,
                new Vector3(0,.87f,0),new Vector3(.70f,.70f,.70f),new Color(.95f,.75f,.59f));
            Primitive(PrimitiveType.Cube,"Backpack",character.transform,new Vector3(0,.05f,-.51f),
                new Vector3(.78f,.90f,.38f),new Color(.34f,.23f,.12f));
            Primitive(PrimitiveType.Cube,"Headband",character.transform,new Vector3(0,1.1f,.06f),
                new Vector3(.76f,.14f,.70f),new Color(.80f,.61f,.25f));
            Primitive(PrimitiveType.Sphere,"Facing marker",character.transform,new Vector3(0,.72f,.42f),
                Vector3.one*.17f,new Color(.04f,.09f,.15f));
            var actor=character.AddComponent<AdventureAgent>();
            // Serialized and adjustable in the Inspector, not just created
            // transiently during Play Mode by AdventureAgent.Awake.
            character.AddComponent<AdventureCombatState>();
            actor.mode=ControlMode.Manual;
            character.AddComponent<AdventureVision>();
            character.AddComponent<AdventureBrain>();
            character.AddComponent<AdventureNavigation>();
            character.AddComponent<AdventureTinyController>();
            var api=character.AddComponent<AdventureOllama>();
            api.endpoint="http://127.0.0.1:11434/api/chat";
            api.model="qwen3.5:4b";
            // AdventureAgent's RequireComponent already attaches AdventureStats.
            // Adding a second Stats component duplicates HP/MP sources and logs.
            world.player=actor;

            var camObject=new GameObject("02 - ADVENTURE CAMERA (3rd person)");
            camObject.tag="MainCamera";
            var cam=camObject.AddComponent<Camera>();
            cam.fieldOfView=58f;cam.nearClipPlane=.035f;cam.farClipPlane=240;
            cam.clearFlags=CameraClearFlags.Skybox;
            camObject.AddComponent<AudioListener>();
            var controller=camObject.AddComponent<AdventureCamera>();
            controller.target=actor;
            camObject.transform.position=new Vector3(11,22,0);
            camObject.transform.rotation=Quaternion.Euler(28,0,0);

            var hud=new GameObject("03 - MMORPG HUD");
            hud.AddComponent<AdventureHUD>().actor=actor;

            var lighting=new GameObject("04 - LIGHTING");
            var sun=new GameObject("Sun");
            sun.transform.SetParent(lighting.transform);
            sun.transform.rotation=Quaternion.Euler(47,-36,0);
            var light=sun.AddComponent<Light>();
            light.type=LightType.Directional;
            light.color=new Color(1,.95f,.85f);
            light.intensity=1.7f;light.shadows=LightShadows.Soft;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="AIAdventureRPG";
            PlayerSettings.productName="AI Adventure RPG";
            PlayerSettings.runInBackground=true;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AIAdventureRPG] Created new adventure scene: "+ScenePath);
        }
        static GameObject Primitive(PrimitiveType shape,string name,Transform parent,Vector3 localPosition,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(shape);
            go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=localPosition;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=MaterialAsset(name+"_"+color.GetHashCode().ToString("X"),color);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static Material MaterialAsset(string name,Color color)
        {
            string path=MaterialFolder+name+".mat";
            var result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(result==null)
            {
                Shader shader=Shader.Find("Universal Render Pipeline/Lit");
                if(shader==null)shader=Shader.Find("Standard");
                result=new Material(shader);
                AssetDatabase.CreateAsset(result,path);
            }
            result.color=color;
            EditorUtility.SetDirty(result);
            return result;
        }
        [MenuItem("AI Adventure RPG/Run Design Smoke Tests")]
        public static void Check()
        {
            int pass=0;
            Assert(AdventureRules.HealthAfter(60,AdventureObjectKind.Thorn)==42,"thorn causes 18 damage",ref pass);
            Assert(AdventureRules.HealthAfter(60,AdventureObjectKind.Herb)==86,"herb heals 26",ref pass);
            Assert(AdventureRules.HealthAfter(95,AdventureObjectKind.Herb)==100,"HP capped",ref pass);
            Assert(AdventureRules.MagicReplyReward(AdventureObjectKind.Herb,DialogueAnswer.Good)==12,"herb dialogue reward",ref pass);
            Assert(AdventureRules.MagicReplyReward(AdventureObjectKind.Thorn,DialogueAnswer.Bad)==12,"thorn dialogue reward",ref pass);
            Assert(AdventureRules.MagicReplyReward(AdventureObjectKind.Herb,DialogueAnswer.Bad)==-7,"wrong dialogue penalty",ref pass);
            Assert(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null,"new scene exists",ref pass);
            var s=new HMGF.World.WorldSettings(64,32,64,16,1f,4,20261101,true,3);
            var runtime=new HMGF.Core.ModuleRuntime(.1);
            runtime.Start(new HMGF.Core.IGameModule[]{new HMGF.World.WorldModule(s)});
            var world=runtime.Services.Get<HMGF.World.IWorldQuery>();
            Assert(world.Surface(10,13)>0,"original terrain generator surface",ref pass);
            Debug.Log("[AIAdventureRPG] SMOKE PASS "+pass+"/8");
            runtime.Dispose();
        }
        static void Assert(bool value,string name,ref int passed)
        {
            if(!value)throw new Exception("[AIAdventureRPG] Test FAILED: "+name);
            passed++;Debug.Log("[AIAdventureRPG] PASS "+name);
        }
    }
}