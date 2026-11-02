using System;
using System.Collections.Generic;
using HMGF.Core;
using HMGF.World;
using HMGF.Presentation;
using UnityEngine;

namespace AIAdventureRPG
{
    // Uses the real HMGF VoxelWorld, TerrainGenerator, VoxelMesher and VoxelTerrainView.
    // Does not use a fake tiled plane or a copy of the legacy CityBuilder scene.
    public sealed class AdventureWorld:MonoBehaviour
    {
        public Material terrainMaterial;
        [Range(24,128)]public int mapWidth=64;
        [Range(24,128)]public int mapDepth=64;
        public int mapSeed=20261101;
        [Header("Training encounters (distributed NPCs, clustered resource types)")]
        public bool generateDistributedTraining=true;
        [Range(3,10)] public int trainingSectorsPerAxis=8;
        [Range(1,4)] public int clustersPerResourceType=2;
        [Range(3,16)] public int pickupsPerCluster=12;
        [Range(1,16)] public int thornPickupsPerCluster=8;
        [Range(4f,12f)] public float resourceClusterRadius=8f;
        [Range(0,20)] public int scatteredHerbCount=4;
        [Range(0,48)] public int scatteredThornCount=24;
        [Range(0,2)] public int npcsPerFourSectors=1;
        public AdventureAgent player;
        public int DistributedPickupCount{get;private set;}
        public int DistributedNPCCount{get;private set;}
        public int OccupiedTrainingSectors{get;private set;}
        public int ResourceClusterCount{get;private set;}
        public float Width=>mapWidth;
        public float Depth=>mapDepth;
        // An editor domain reload can preserve generated mesh GameObjects while
        // clearing non-serialized C# module objects; never treat that as ground Y=0.
        public bool HasGroundData=>query!=null && runtime!=null;
        public Vector3 SpawnPoint {get;private set;}
        public readonly List<AdventurePickup> Pickups=new List<AdventurePickup>();
        public readonly List<AdventureNPC> NPCs=new List<AdventureNPC>();
        public readonly List<AdventureSign> Signs=new List<AdventureSign>();
        public readonly List<AdventureMonster> Monsters=new List<AdventureMonster>();
        [Header("Survival encounters: food, signs, hostile wildlife")]
        public bool enableSurvivalEncounters=true;
        [Range(0,16)] public int hostileMonsterCount=12;
        [Min(1)] public int monsterKillGoal=3;
        [Range(0,12)] public int berryPairCount=6;
        readonly List<Material> materials=new List<Material>();
        ModuleRuntime runtime;
        IWorldQuery query;
        GameObject owned;
        VoxelTerrainView terrainView;
        Transform meshChunkRoot,terrainSelectionRoot;
        float nextTerrainIntegrityCheck;
        float nextRuntimeRecoveryAttempt;
        public int TerrainRecoveryCount{get;private set;}
        public int RuntimeRecoveryCount{get;private set;}
        void Update()
        {
            // Presentation health is separate from world simulation: the voxel
            // data can be intact even when a view has shut down or missed rebuilds.
            if(query==null||runtime==null)
            {
                if(Time.realtimeSinceStartup>=nextRuntimeRecoveryAttempt)
                {
                    nextRuntimeRecoveryAttempt=Time.realtimeSinceStartup+2f;
                    RecoverRuntimeAfterReload();
                }
                return;
            }
            if(terrainView==null||meshChunkRoot==null)return;
            if(Time.realtimeSinceStartup<nextTerrainIntegrityCheck)return;
            nextTerrainIntegrityCheck=Time.realtimeSinceStartup+2f;
            if(!terrainView.IsInitialized)
            {
                terrainView.Initialize(query,runtime.Events,new VoxelMesher(),meshChunkRoot,
                    terrainSelectionRoot,terrainMaterial,8);
                TerrainRecoveryCount++;
                Debug.LogWarning("[AIAdventureRPG] Terrain presentation reinitialized after interruption.",this);
            }
            else if(terrainView.ChunkCount==0&&terrainView.PendingChunkCount==0)
            {
                terrainView.RequestRebuildAll();
                TerrainRecoveryCount++;
                Debug.LogWarning("[AIAdventureRPG] Terrain chunk rebuild requeued after empty view.",this);
            }
        }
        static Transform FindChildByName(Transform parent,string exactName)
        {
            if(parent==null)return null;
            foreach(Transform child in parent)
                if(child.name==exactName)return child;
            return null;
        }
        void RecoverRuntimeAfterReload()
        {
            // Unity's script/domain reload does not necessarily destroy native scene
            // GameObjects, but non-serialized ModuleRuntime/IWorldQuery references
            // disappear. Rebuild ONLY the simulation provider and terrain view;
            // retain existing NPCs, pickups, avatar, and their gameplay state.
            try
            {
                if(owned==null)
                {
                    var existing=transform.Find("ADVENTURE WORLD - generated");
                    if(existing!=null)owned=existing.gameObject;
                }
                if(owned==null)
                {
                    // Initial scene without a generated world: Start/Build owns setup.
                    return;
                }
                runtime?.Dispose();
                runtime=new ModuleRuntime(.1);
                runtime.Start(new IGameModule[]{
                    new WorldModule(new WorldSettings(mapWidth,32,mapDepth,16,1f,4,mapSeed,true,3))
                });
                query=runtime.Services.Get<IWorldQuery>();
                if(query==null)throw new InvalidOperationException("World service missing during recovery");
                // Transform.Find treats '/' as a hierarchy separator. The actual
                // group names contain literal slashes, so match direct children.
                var chunkGroup=FindChildByName(owned.transform,"Terrain / Voxel chunks");
                var selectionGroup=FindChildByName(owned.transform,"Terrain / Selection");
                if(chunkGroup==null||selectionGroup==null)throw new InvalidOperationException("Terrain roots missing");
                meshChunkRoot=chunkGroup;
                terrainSelectionRoot=selectionGroup;
                if(terrainView==null)terrainView=owned.GetComponentInChildren<VoxelTerrainView>(true);
                if(terrainView==null)throw new InvalidOperationException("Terrain view missing");
                terrainView.Shutdown();
                // The old C# mesh dictionary can be lost on domain reload while its
                // native GameObjects remain. Disable and retire those orphan meshes
                // before creating replacement views to avoid double colliders.
                for(int i=meshChunkRoot.childCount-1;i>=0;i--)
                {
                    var old=meshChunkRoot.GetChild(i).gameObject;
                    old.SetActive(false);
                    Destroy(old);
                }
                terrainView.preserveOnDisable=true;
                terrainView.Initialize(query,runtime.Events,new VoxelMesher(),
                    meshChunkRoot,terrainSelectionRoot,terrainMaterial,8);
                Pickups.Clear();NPCs.Clear();Signs.Clear();Monsters.Clear();
                Pickups.AddRange(owned.GetComponentsInChildren<AdventurePickup>(true));
                NPCs.AddRange(owned.GetComponentsInChildren<AdventureNPC>(true));
                Signs.AddRange(owned.GetComponentsInChildren<AdventureSign>(true));
                Monsters.AddRange(owned.GetComponentsInChildren<AdventureMonster>(true));
                SpawnPoint=At(11,13,1.02f);
                foreach(var creature in Monsters)
                    if(creature!=null)creature.Initialize(this,player);
                if(player!=null)
                {
                    float floor=GroundHeight(player.transform.position.x,player.transform.position.z);
                    var body=player.GetComponent<Rigidbody>();
                    var position=body!=null?body.position:player.transform.position;
                    // Keep existing X/Z and gameplay stats; repair only invalid Y.
                    if(position.y<floor+.84f)
                    {
                        position.y=floor+.87f;
                        if(body!=null)body.position=position;
                        player.transform.position=position;
                    }
                    player.Initialize(this);
                }
                RuntimeRecoveryCount++;
                nextTerrainIntegrityCheck=0;
                Debug.LogWarning("[AIAdventureRPG] Restored voxel runtime after domain reload, " +
                    "rebound "+Pickups.Count+" pickups and "+NPCs.Count+" NPCs.",this);
            }
            catch(Exception exception)
            {
                // Never let FixedUpdate snap the actor to Y=0 if recovery fails.
                query=null;
                Debug.LogError("[AIAdventureRPG] Voxel runtime recovery failed: "+exception,this);
            }
        }
        void Start()
        {
            try{Build();}
            catch(Exception e){Debug.LogException(e,this);enabled=false;}
        }
        void Build()
        {
            var settings=new WorldSettings(mapWidth,32,mapDepth,16,1f,4,mapSeed,true,3);
            runtime=new ModuleRuntime(.1);
            runtime.Start(new IGameModule[]{new WorldModule(settings)});
            query=runtime.Services.Get<IWorldQuery>();
            owned=new GameObject("ADVENTURE WORLD - generated");
            owned.transform.SetParent(transform,false);
            var meshRoot=NewGroup("Terrain / Voxel chunks");
            var selectionRoot=NewGroup("Terrain / Selection");
            meshChunkRoot=meshRoot.transform;
            terrainSelectionRoot=selectionRoot.transform;
            terrainView=NewGroup("VoxelTerrainView").AddComponent<VoxelTerrainView>();
            // A disable/enable (including some editor lifecycle changes) must not
            // dispose the only terrain meshes while the world simulation lives.
            terrainView.preserveOnDisable=true;
            if(terrainMaterial==null)
            {
                var shader=Shader.Find("Universal Render Pipeline/Lit");
                terrainMaterial=new Material(shader!=null?shader:Shader.Find("Standard"));
                materials.Add(terrainMaterial);
            }
            terrainView.Initialize(query,runtime.Events,new VoxelMesher(),meshRoot.transform,
                selectionRoot.transform,terrainMaterial,8);
            var objects=NewGroup("World Items / Respawn");
            var people=NewGroup("Characters / NPC");
            var nature=NewGroup("Nature / Decorations");
            var structures=NewGroup("Ruins / Landmarks");
            var signs=NewGroup("Survival / Signs");
            var enemies=NewGroup("Survival / Monsters");

            SpawnPoint=At(11,13,1.02f);
            // These locations are on the dry bank of the preexisting river generator.
            AddPickup(objects.transform,"Wild herb",AdventureObjectKind.Herb,15,13,16f);
            AddPickup(objects.transform,"Bitter herb",AdventureObjectKind.Herb,11,22,19f);
            AddPickup(objects.transform,"Herb patch",AdventureObjectKind.Herb,20,24,25f);
            AddPickup(objects.transform,"Forest herb",AdventureObjectKind.Herb,7,35,16f);
            AddPickup(objects.transform,"River herb",AdventureObjectKind.Herb,20,41,22f);
            AddPickup(objects.transform,"Thorn bush",AdventureObjectKind.Thorn,18,19,22f);
            AddPickup(objects.transform,"Thorn thicket",AdventureObjectKind.Thorn,6,27,20f);
            AddPickup(objects.transform,"Hidden thorns",AdventureObjectKind.Thorn,20,38,25f);
            AddPickup(objects.transform,"Thorn roots",AdventureObjectKind.Thorn,10,48,21f);
            AddPickup(objects.transform,"Runic fragment",AdventureObjectKind.LoreStone,21,32,30f);
            AddPickup(objects.transform,"Old stone",AdventureObjectKind.LoreStone,13,44,40f);
            AddNPC(people.transform,"Herbalist Mira",AdventureObjectKind.Herb,18,13,new Color(.30f,.85f,.65f));
            AddNPC(people.transform,"Scout Rowan",AdventureObjectKind.Thorn,12,36,new Color(.96f,.65f,.35f));
            AddNPC(people.transform,"Archivist",AdventureObjectKind.LoreStone,28,49,new Color(.62f,.58f,.95f));

            var random=new System.Random(mapSeed);
            for(int i=0;i<46;i++)
            {
                float x=4+random.Next(mapWidth-8),z=4+random.Next(mapDepth-8);
                if(Math.Abs(x-SpawnPoint.x)<5 && Math.Abs(z-SpawnPoint.z)<5)continue;
                AddTree(nature.transform,x,z,random);
            }
            for(int i=0;i<12;i++)
            {
                int x=3+random.Next(mapWidth-6),z=3+random.Next(mapDepth-6);
                AddRock(structures.transform,x,z,random);
            }
            if(generateDistributedTraining)
                DistributeTrainingEncounters(objects.transform,people.transform,nature.transform,structures.transform);
            if(enableSurvivalEncounters)
                AddSurvivalEncounters(objects.transform,signs.transform,enemies.transform,
                    nature.transform,structures.transform);
            if(player!=null)
            {
                var body=player.GetComponent<Rigidbody>();
                if(body!=null)body.position=SpawnPoint;
                else player.transform.position=SpawnPoint;
                player.Initialize(this);
            }
            Debug.Log("[AIAdventureRPG] World ready. Native HMGF voxel chunks="+query.Settings.Width+"x"+
                query.Settings.Depth+", items="+Pickups.Count+", NPCs="+NPCs.Count+
                ", distributed="+DistributedPickupCount+" pickups/"+DistributedNPCCount+
                " NPC across "+OccupiedTrainingSectors+" training sectors, signs="+Signs.Count+
                ", hostile monsters="+Monsters.Count);
        }

        void DistributeTrainingEncounters(Transform objects,Transform people,Transform nature,Transform structures)
        {
            // NPCs remain spread out, while item TYPES form separate patches.
            // All positions are sampled from the seeded voxel world, not meshes.
            int sectors=Mathf.Clamp(trainingSectorsPerAxis,3,10);
            var rng=new System.Random(unchecked(mapSeed*31+54721));
            var occupied=new List<Vector2>();
            foreach(var item in Pickups)if(item!=null)
                occupied.Add(new Vector2(item.transform.position.x,item.transform.position.z));
            foreach(var npc in NPCs)if(npc!=null)
                occupied.Add(new Vector2(npc.transform.position.x,npc.transform.position.z));
            foreach(Transform tree in nature)
                occupied.Add(new Vector2(tree.position.x,tree.position.z));
            foreach(Transform rock in structures)
                occupied.Add(new Vector2(rock.position.x,rock.position.z));
            occupied.Add(new Vector2(SpawnPoint.x,SpawnPoint.z));
            var occupiedSectors=new HashSet<Vector2Int>();
            DistributedPickupCount=0;
            DistributedNPCCount=0;
            ResourceClusterCount=0;
            OccupiedTrainingSectors=0;

            for(int gz=0;gz<sectors;gz++)for(int gx=0;gx<sectors;gx++)
            {
                if(npcsPerFourSectors<=0||gx%2!=1||gz%2!=1)continue;
                for(int slot=0;slot<npcsPerFourSectors;slot++)
                {
                    Vector2 position;
                    if(!TryTrainingSpot(gx,gz,sectors,3.3f,rng,occupied,out position))continue;
                    int ordinal=(gx+gz/2+slot)%3;
                    var topic=ordinal==0?AdventureObjectKind.Herb:
                              ordinal==1?AdventureObjectKind.Thorn:AdventureObjectKind.LoreStone;
                    Color color=ordinal==0?new Color(.24f,.70f,.48f):
                                ordinal==1?new Color(.90f,.53f,.20f):
                                new Color(.53f,.49f,.86f);
                    AddNPC(people,"Trail "+(ordinal==0?"Herbalist ":ordinal==1?"Scout ":"Archivist ")+
                        (DistributedNPCCount+1).ToString("D2"),topic,position.x,position.y,color);
                    occupied.Add(position);
                    MarkTrainingSector(position,sectors,occupiedSectors);
                    DistributedNPCCount++;
                }
            }

            // Two separated gathering areas per type by default. These are not
            // evenly mixed by sector: finding a rune grove or thorn thicket
            // requires travel. Nearby pickups respawn at their original spots.
            var kinds=new[]{AdventureObjectKind.Herb,AdventureObjectKind.Thorn,
                AdventureObjectKind.LoreStone};
            var centers=new List<Vector2>();
            float radius=Mathf.Max(4f,resourceClusterRadius);
            int clusters=Mathf.Clamp(clustersPerResourceType,1,4);
            int each=Mathf.Clamp(pickupsPerCluster,3,16);
            foreach(var kind in kinds)
            {
                int typeCount=kind==AdventureObjectKind.Thorn?
                    Mathf.Clamp(thornPickupsPerCluster,1,16):each;
                for(int group=0;group<clusters;group++)
                {
                    Vector2 center=default;
                    bool found=false;
                    for(int attempt=0;attempt<128;attempt++)
                    {
                        int gx=rng.Next(sectors),gz=rng.Next(sectors);
                        if(!TryTrainingSpot(gx,gz,sectors,2f,rng,occupied,out center))continue;
                        if((center-new Vector2(SpawnPoint.x,SpawnPoint.z)).sqrMagnitude<
                            (radius+5f)*(radius+5f))continue;
                        bool nearCenter=false;
                        foreach(var previous in centers)
                            if((center-previous).sqrMagnitude<(radius*2f+2f)*(radius*2f+2f))
                                {nearCenter=true;break;}
                        if(nearCenter)continue;
                        found=true;break;
                    }
                    if(!found)continue;
                    centers.Add(center);
                    ResourceClusterCount++;
                    for(int slot=0;slot<typeCount;slot++)
                    {
                        Vector2 position;
                        if(slot==0)position=center;
                        else if(!TryClusterSpot(center,radius,1.9f,rng,occupied,out position))
                            continue;
                        AddPickup(objects,"Cluster "+kind+" "+ResourceClusterCount.ToString("D2")+
                            "-"+(slot+1).ToString("D2"),kind,position.x,position.y,
                            12f+(float)rng.NextDouble()*18f);
                        occupied.Add(position);
                        MarkTrainingSector(position,sectors,occupiedSectors);
                        DistributedPickupCount++;
                    }
                }
            }

            // Common herbs remain discoverable away from the patches.
            // Runes stay concentrated; thorns also occur as distributed hazards.
            for(int i=0;i<Mathf.Max(0,scatteredHerbCount);i++)
            {
                Vector2 position=default;
                bool found=false;
                for(int attempt=0;attempt<40;attempt++)
                {
                    if(!TryTrainingSpot(rng.Next(sectors),rng.Next(sectors),sectors,
                        2.4f,rng,occupied,out position))continue;
                    bool nearCluster=false;
                    foreach(var center in centers)
                        if((position-center).sqrMagnitude<radius*radius)
                            {nearCluster=true;break;}
                    if(!nearCluster){found=true;break;}
                }
                if(!found)continue;
                AddPickup(objects,"Trail Herb "+(i+1).ToString("D2"),
                    AdventureObjectKind.Herb,position.x,position.y,
                    12f+(float)rng.NextDouble()*18f);
                occupied.Add(position);
                MarkTrainingSector(position,sectors,occupiedSectors);
                DistributedPickupCount++;
            }
            // Spread hazardous thorns through all 4x4 broad adventure zones,
            // not just the thorn patches. Shuffle zone order reproducibly so
            // spawns are spatially broad without teaching hidden locations.
            int[] zoneOrder=new int[16];
            for(int i=0;i<16;i++)zoneOrder[i]=i;
            for(int i=15;i>0;i--)
            {
                int j=rng.Next(i+1);
                int old=zoneOrder[i];zoneOrder[i]=zoneOrder[j];zoneOrder[j]=old;
            }
            for(int i=0;i<Mathf.Max(0,scatteredThornCount);i++)
            {
                int region=zoneOrder[i%16];
                int broadX=region%4,broadZ=region/4;
                Vector2 position=default;
                bool found=false;
                for(int attempt=0;attempt<32;attempt++)
                {
                    // Pick a training micro-sector inside this broad zone.
                    int sx=Mathf.Clamp(Mathf.FloorToInt((broadX+(float)rng.NextDouble())*sectors/4f),0,sectors-1);
                    int sz=Mathf.Clamp(Mathf.FloorToInt((broadZ+(float)rng.NextDouble())*sectors/4f),0,sectors-1);
                    if(!TryTrainingSpot(sx,sz,sectors,2.3f,rng,occupied,out position))continue;
                    int actualX=Mathf.Clamp(Mathf.FloorToInt(position.x*4f/mapWidth),0,3);
                    int actualZ=Mathf.Clamp(Mathf.FloorToInt(position.y*4f/mapDepth),0,3);
                    if(actualX!=broadX||actualZ!=broadZ)continue;
                    found=true;break;
                }
                if(!found)continue;
                AddPickup(objects,"Trail Thorn "+(i+1).ToString("D2"),
                    AdventureObjectKind.Thorn,position.x,position.y,
                    12f+(float)rng.NextDouble()*18f);
                occupied.Add(position);
                MarkTrainingSector(position,sectors,occupiedSectors);
                DistributedPickupCount++;
            }
            OccupiedTrainingSectors=occupiedSectors.Count;
        }

        void MarkTrainingSector(Vector2 position,int sectors,HashSet<Vector2Int> occupiedSectors)
        {
            int x=Mathf.Clamp(Mathf.FloorToInt(position.x*sectors/mapWidth),0,sectors-1);
            int z=Mathf.Clamp(Mathf.FloorToInt(position.y*sectors/mapDepth),0,sectors-1);
            occupiedSectors.Add(new Vector2Int(x,z));
        }

        bool TryClusterSpot(Vector2 center,float radius,float separation,
            System.Random rng,List<Vector2> occupied,out Vector2 result)
        {
            result=default;
            for(int attempt=0;attempt<160;attempt++)
            {
                float angle=(float)rng.NextDouble()*Mathf.PI*2f;
                float distance=Mathf.Sqrt((float)rng.NextDouble())*radius;
                float x=center.x+Mathf.Cos(angle)*distance;
                float z=center.y+Mathf.Sin(angle)*distance;
                if(x<3||z<3||x>mapWidth-3||z>mapDepth-3)continue;
                if(!SafeDryTrainingGround(x,z))continue;
                var candidate=new Vector2(x,z);
                bool close=false;
                foreach(var other in occupied)
                    if((candidate-other).sqrMagnitude<separation*separation)
                        {close=true;break;}
                if(close)continue;
                result=candidate;
                return true;
            }
            return false;
        }

        bool TryTrainingSpot(int gx,int gz,int sectors,float separation,
            System.Random rng,List<Vector2> occupied,out Vector2 result)
        {
            result=default;
            for(int attempt=0;attempt<24;attempt++)
            {
                float x=(gx+.14f+.72f*(float)rng.NextDouble())*mapWidth/sectors;
                float z=(gz+.14f+.72f*(float)rng.NextDouble())*mapDepth/sectors;
                if(x<3||z<3||x>mapWidth-3||z>mapDepth-3)continue;
                float dx=x-SpawnPoint.x,dz=z-SpawnPoint.z;
                if(dx*dx+dz*dz<16f)continue;
                bool close=false;
                foreach(var used in occupied)
                {
                    float vx=x-used.x,vz=z-used.y;
                    if(vx*vx+vz*vz<separation*separation){close=true;break;}
                }
                if(close||!SafeDryTrainingGround(x,z))continue;
                result=new Vector2(x,z);
                return true;
            }
            return false;
        }

        bool SafeDryTrainingGround(float x,float z)
        {
            int cx=Mathf.FloorToInt(x),cz=Mathf.FloorToInt(z);
            int surface=query.Surface(cx,cz);
            if(surface<3||surface>=query.Settings.Height-3)return false;
            int[] offsets={-1,0,1};
            foreach(int ox in offsets)foreach(int oz in offsets)
            {
                int px=cx+ox,pz=cz+oz;
                if(px<1||pz<1||px>=mapWidth-1||pz>=mapDepth-1)return false;
                int here=query.Surface(px,pz);
                if(Mathf.Abs(here-surface)>1)return false;
                // Surface() returns the highest SOLID, so water may sit ABOVE it.
                for(int y=here;y<=here+2&&y<query.Settings.Height;y++)
                    if(query.Query(new Cell3(px,y,pz))==VoxelKind.Water)return false;
            }
            return true;
        }
        GameObject NewGroup(string name)
        {
            var go=new GameObject(name);go.transform.SetParent(owned.transform,false);return go;
        }
        public float GroundHeight(float x,float z)
        {
            // A missing provider is NOT a real terrain surface at sea level.
            // Callers controlling physics must check HasGroundData before moving.
            return query==null?float.NaN:query.Surface(Mathf.Clamp(Mathf.FloorToInt(x),0,mapWidth-1),
                Mathf.Clamp(Mathf.FloorToInt(z),0,mapDepth-1))*query.Settings.VoxelSize;
        }
        Vector3 At(float x,float z,float rise)
        {
            return new Vector3(x,GroundHeight(x,z)+rise,z);
        }
        Material Mat(Color color,float metallic=0)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null)shader=Shader.Find("Standard");
            var m=new Material(shader);m.color=color;
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);
            if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.3f);
            materials.Add(m);return m;
        }
        GameObject Shape(PrimitiveType type,string name,Transform parent,Vector3 location,Vector3 scale,Color color,bool collider)
        {
            var item=GameObject.CreatePrimitive(type);
            item.name=name;item.transform.SetParent(parent,true);item.transform.position=location;item.transform.localScale=scale;
            item.GetComponent<Renderer>().material=Mat(color);
            if(!collider)Destroy(item.GetComponent<Collider>());
            return item;
        }
        void AddPickup(Transform parent,string name,AdventureObjectKind kind,float x,float z,float respawn)
        {
            Vector3 center=At(x,z,.82f);
            GameObject item=Shape(PrimitiveType.Sphere,name,parent,center,Vector3.one*1.2f,
                kind==AdventureObjectKind.Herb?new Color(.17f,.91f,.43f):
                kind==AdventureObjectKind.Thorn?new Color(.86f,.23f,.28f):
                kind==AdventureObjectKind.SafeBerry?new Color(.97f,.71f,.15f):
                kind==AdventureObjectKind.PoisonBerry?new Color(.48f,.18f,.61f):
                new Color(.36f,.7f,1f),true);
            var pickup=item.AddComponent<AdventurePickup>();
            pickup.kind=kind;pickup.respawnSeconds=respawn;
            pickup.displayName=kind==AdventureObjectKind.SafeBerry?"Amberberry":
                kind==AdventureObjectKind.PoisonBerry?"Duskberry":name;
            Pickups.Add(pickup);
            if(kind==AdventureObjectKind.Thorn)
                for(int i=0;i<5;i++)
                {
                    float angle=i*Mathf.PI*2/5;
                    var spike=Shape(PrimitiveType.Capsule,"Thorn",item.transform,
                        center+new Vector3(Mathf.Cos(angle)*.45f,.25f,Mathf.Sin(angle)*.45f),
                        new Vector3(.17f,.9f,.17f),new Color(.60f,.09f,.14f),false);
                    spike.transform.localRotation=Quaternion.Euler(25,i*72,25);
                }
            if(kind==AdventureObjectKind.Herb)
                for(int i=0;i<4;i++)
                {
                    float a=i*1.57f;
                    Shape(PrimitiveType.Cube,"Leaf",item.transform,center+
                        new Vector3(Mathf.Sin(a)*.54f,.28f,Mathf.Cos(a)*.54f),
                        new Vector3(.22f,.15f,.9f),new Color(.16f,.58f,.23f),false)
                        .transform.rotation=Quaternion.Euler(25,i*90,20);
                }
        }
        void AddSurvivalEncounters(Transform pickups,Transform signRoot,Transform monsters,
            Transform nature,Transform structures)
        {
            var random=new System.Random(unchecked(mapSeed*37+9059));
            var occupied=new List<Vector2>();
            foreach(var item in Pickups)if(item!=null)
                occupied.Add(new Vector2(item.transform.position.x,item.transform.position.z));
            foreach(var npc in NPCs)if(npc!=null)
                occupied.Add(new Vector2(npc.transform.position.x,npc.transform.position.z));
            foreach(Transform t in nature)occupied.Add(new Vector2(t.position.x,t.position.z));
            foreach(Transform t in structures)occupied.Add(new Vector2(t.position.x,t.position.z));
            occupied.Add(new Vector2(SpawnPoint.x,SpawnPoint.z));
            // Camp information is accessible before entering hostile territory.
            AddSign(signRoot,"Camp safety board",12f,16f,
                "Amberberry is edible and restores health.\nDuskberry is poisonous. Avoid purple fruit. Press E to eat.");
            AddSign(signRoot,"Camp combat board",17f,14f,
                "Wild boars will pursue you.\nFace a monster and press E to strike.\nRetreat or heal if HP is low.");
            for(int i=0;i<berryPairCount;i++)
            {
                int gx=(i*5+1)%8,gz=(i*3+2)%8;
                Vector2 safe,poison;
                if(!TryTrainingSpot(gx,gz,8,2.4f,random,occupied,out safe) &&
                    !TryAnySurvivalSpot(random,occupied,2.4f,out safe))continue;
                AddPickup(pickups,"Amberberry patch "+i,AdventureObjectKind.SafeBerry,
                    safe.x,safe.y,35f);occupied.Add(safe);
                // Try an adjacent sector: risk labels must not be revealed by kind.
                if(TryTrainingSpot((gx+1)%8,gz,8,2.4f,random,occupied,out poison)||
                    TryAnySurvivalSpot(random,occupied,2.4f,out poison))
                {
                    AddPickup(pickups,"Duskberry patch "+i,AdventureObjectKind.PoisonBerry,
                        poison.x,poison.y,40f);
                    occupied.Add(poison);
                }
                if(i%2==0)
                {
                    Vector2 marker;
                    if(TryTrainingSpot(gx,(gz+1)%8,8,2.4f,random,occupied,out marker)||
                        TryAnySurvivalSpot(random,occupied,2.4f,out marker))
                    {
                        AddSign(signRoot,"Forager notes "+i,marker.x,marker.y,
                            "Amberberry (gold) heals; Duskberry (purple) is poisonous. Press E to eat.");
                        occupied.Add(marker);
                    }
                }
            }
            var monsterPositions=new List<Vector2>();
            for(int i=0;i<hostileMonsterCount;i++)
            {
                // Spread enemies apart so a beginner is not attacked by a pack
                // before learning combat. Keep the safe spawn radius intact.
                int gx=(i*5+4)%8,gz=(i*3+3)%8;
                Vector2 location=default;
                bool found=false;
                for(int trial=0;trial<32;trial++)
                {
                    bool sampled=trial==0?
                        TryTrainingSpot(gx,gz,8,3f,random,occupied,out location):
                        TryAnySurvivalSpot(random,occupied,3f,out location);
                    if(!sampled)continue;
                    if((location-new Vector2(SpawnPoint.x,SpawnPoint.z)).sqrMagnitude<100f)continue;
                    bool nearAnother=false;
                    foreach(var other in monsterPositions)
                        if((location-other).sqrMagnitude<64f){nearAnother=true;break;}
                    if(nearAnother)continue;
                    found=true;break;
                }
                if(!found)continue;
                AddMonster(monsters,"Wild Boar "+(i+1).ToString("D2"),location.x,location.y,
                    i%3==0);
                occupied.Add(location);
                monsterPositions.Add(location);
            }
        }
        bool TryAnySurvivalSpot(System.Random rng,List<Vector2> occupied,
            float spacing,out Vector2 result)
        {
            result=default;
            for(int attempt=0;attempt<300;attempt++)
            {
                float x=3.5f+(float)rng.NextDouble()*(mapWidth-7);
                float z=3.5f+(float)rng.NextDouble()*(mapDepth-7);
                Vector2 candidate=new Vector2(x,z);
                if((candidate-new Vector2(SpawnPoint.x,SpawnPoint.z)).sqrMagnitude<100f)continue;
                if(!SafeDryTrainingGround(x,z))continue;
                bool near=false;
                foreach(var other in occupied)
                    if((candidate-other).sqrMagnitude<spacing*spacing){near=true;break;}
                if(near)continue;
                result=candidate;
                return true;
            }
            return false;
        }
        bool SafeSignGround(float x,float z)
        {
            int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);
            if(ix<2||iz<2||ix>=mapWidth-2||iz>=mapDepth-2)return false;
            int y=query.Surface(ix,iz);
            if(y<3||y>=query.Settings.Height-3)return false;
            for(int h=y;h<=y+2;h++)
                if(query.Query(new Cell3(ix,h,iz))==VoxelKind.Water)return false;
            return true;
        }
        void AddSign(Transform parent,string name,float x,float z,string content)
        {
            if(!SafeSignGround(x,z))
            {
                var rng=new System.Random(unchecked(mapSeed+name.Length*173+Mathf.RoundToInt(x*41+z*31)));
                bool found=false;
                for(int k=0;k<50;k++)
                {
                    float xx=x+(float)(rng.NextDouble()-.5)*9f;
                    float zz=z+(float)(rng.NextDouble()-.5)*9f;
                    if(!SafeSignGround(xx,zz))continue;
                    x=xx;z=zz;found=true;break;
                }
                if(!found)return;
            }
            var ground=At(x,z,0);
            var board=Shape(PrimitiveType.Cube,name,parent,ground+Vector3.up*2.15f,
                new Vector3(3.6f,1.9f,.28f),new Color(.51f,.34f,.19f),true);
            var sign=board.AddComponent<AdventureSign>();
            sign.displayName=name;sign.signText=content;
            Signs.Add(sign);
            var post=Shape(PrimitiveType.Cylinder,"Post",board.transform,
                ground+Vector3.up*.95f,new Vector3(.24f,.93f,.24f),
                new Color(.36f,.23f,.13f),false);
            var ink=new GameObject("Written text");
            ink.transform.SetParent(board.transform,true);
            ink.transform.position=board.transform.position+Vector3.back*.17f;
            ink.transform.rotation=Quaternion.Euler(0,180,0);
            var text=ink.AddComponent<TextMesh>();
            text.text=content;
            text.fontSize=22;text.characterSize=.085f;
            text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
            text.color=new Color(.96f,.91f,.73f);
        }
        void AddMonster(Transform parent,string name,float x,float z,bool strong)
        {
            var center=At(x,z,1.0f);
            var root=Shape(PrimitiveType.Capsule,name,parent,center,
                new Vector3(1.5f,1.35f,1.9f),
                strong?new Color(.48f,.12f,.12f):new Color(.43f,.30f,.23f),true);
            var monster=root.AddComponent<AdventureMonster>();
            monster.displayName=name;
            monster.maxHealth=strong?70f:42f;
            monster.damage=strong?17f:11f;
            monster.walkSpeed=strong?2.7f:2.3f;
            monster.aggroRange=strong?11f:9f;
            monster.respawnSeconds=45f;
            monster.Initialize(this,player);
            Monsters.Add(monster);
            Shape(PrimitiveType.Sphere,"Head",root.transform,
                center+new Vector3(0f,.35f,.84f),
                new Vector3(.90f,.77f,.80f),
                strong?new Color(.58f,.20f,.17f):new Color(.54f,.40f,.32f),false);
            for(int side=-1;side<=1;side+=2)
                Shape(PrimitiveType.Capsule,"Tusk",root.transform,
                    center+new Vector3(side*.38f,.13f,1.24f),
                    new Vector3(.18f,.56f,.19f),Color.white,false);
        }
        void AddNPC(Transform parent,string name,AdventureObjectKind topic,float x,float z,Color color)
        {
            Vector3 center=At(x,z,1.05f);
            var root=Shape(PrimitiveType.Capsule,name,parent,center,new Vector3(1.4f,1.6f,1.4f),color,true);
            var npc=root.AddComponent<AdventureNPC>();
            npc.displayName=name;npc.teachingTopic=topic;NPCs.Add(npc);
            Shape(PrimitiveType.Sphere,"Head",root.transform,center+Vector3.up*1.25f,
                Vector3.one*.80f,new Color(.98f,.79f,.64f),false);
            Shape(PrimitiveType.Cube,"Satchel",root.transform,center+Vector3.back*.65f,
                new Vector3(.7f,.7f,.4f),new Color(.35f,.23f,.12f),false);
            root.transform.rotation=Quaternion.Euler(0,145,0);
        }
        void AddTree(Transform parent,float x,float z,System.Random random)
        {
            Vector3 basePos=At(x,z,0);
            float height=2.2f+(float)random.NextDouble()*2f;
            var trunk=Shape(PrimitiveType.Cylinder,"Tree trunk",parent,basePos+Vector3.up*height*.5f,
                new Vector3(.38f,height*.5f,.38f),new Color(.43f,.25f,.14f),true);
            Shape(PrimitiveType.Sphere,"Foliage",trunk.transform,basePos+Vector3.up*(height+1f),
                new Vector3(2.6f,2.8f,2.6f),new Color(.14f+.10f*(float)random.NextDouble(),.39f,.19f),false);
        }
        void AddRock(Transform parent,float x,float z,System.Random random)
        {
            float size=.6f+(float)random.NextDouble()*1.7f;
            var rock=Shape(PrimitiveType.Cube,"Ancient stone",parent,At(x,z,size*.35f),
                new Vector3(size*1.4f,size*.8f,size),new Color(.38f,.46f,.52f),true);
            rock.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*180,10);
        }
        void OnDestroy()
        {
            if(terrainView!=null)terrainView.Shutdown();
            runtime?.Dispose();
            for(int i=0;i<materials.Count;i++)if(materials[i]!=null)Destroy(materials[i]);
        }
    }
}