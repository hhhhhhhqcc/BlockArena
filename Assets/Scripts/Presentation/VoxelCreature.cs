using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    // Every articulated body part shares one mesh across all creatures of that kind.
    // Colours live in the vertices, so an entire part uses a single instanced material.
    internal sealed class VoxelMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        public void Box(Vector3 center, Vector3 size, Color color) { Box(center,size,color,Quaternion.identity); }
        public void Box(Vector3 center, Vector3 size, Color color, Quaternion rotation)
        {
            Vector3 h=size*.5f;
            Face(center,rotation,new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(h.x,h.y,-h.z),Vector3.up,color*1.05f);
            Face(center,rotation,new Vector3(-h.x,-h.y,h.z),new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,-h.y,h.z),Vector3.down,color*.66f);
            Face(center,rotation,new Vector3(-h.x,-h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(h.x,-h.y,-h.z),Vector3.back,color*.91f);
            Face(center,rotation,new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z),new Vector3(-h.x,-h.y,h.z),Vector3.forward,color*.84f);
            Face(center,rotation,new Vector3(-h.x,-h.y,h.z),new Vector3(-h.x,h.y,h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,-h.y,-h.z),Vector3.left,color*.80f);
            Face(center,rotation,new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(h.x,h.y,h.z),new Vector3(h.x,-h.y,h.z),Vector3.right,color*.98f);
        }
        void Face(Vector3 c,Quaternion r,Vector3 a,Vector3 b,Vector3 d,Vector3 e,Vector3 n,Color col)
        {
            int i=vertices.Count; vertices.Add(c+r*a);vertices.Add(c+r*b);vertices.Add(c+r*d);vertices.Add(c+r*e);
            n=r*n;col.a=1;for(int j=0;j<4;j++){normals.Add(n);colors.Add(col);}
            triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);
        }
        public void Disc(float radius,float bottom,float top,int sides,Color surface,Color side)
        {
            for(int i=0;i<sides;i++){
                float a=(i+.5f)*Mathf.PI*2/sides,b=(i+1.5f)*Mathf.PI*2/sides;
                Vector3 p=new Vector3(Mathf.Cos(a)*radius,top,Mathf.Sin(a)*radius),q=new Vector3(Mathf.Cos(b)*radius,top,Mathf.Sin(b)*radius);
                Triangle(new Vector3(0,top,0),q,p,Vector3.up,surface);
                Vector3 lowP=new Vector3(p.x,bottom,p.z),lowQ=new Vector3(q.x,bottom,q.z);
                Vector3 normal=new Vector3((p.x+q.x)*.5f,0,(p.z+q.z)*.5f).normalized;
                Face(Vector3.zero,Quaternion.identity,p,q,lowQ,lowP,normal,side);
            }
        }
        public void Ring(float outer,float inner,Color color)
        {
            for(int i=0;i<32;i++){
                float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
                Vector3 p=new Vector3(Mathf.Cos(a)*outer,0,Mathf.Sin(a)*outer),q=new Vector3(Mathf.Cos(b)*outer,0,Mathf.Sin(b)*outer),r=new Vector3(Mathf.Cos(b)*inner,0,Mathf.Sin(b)*inner),s=new Vector3(Mathf.Cos(a)*inner,0,Mathf.Sin(a)*inner);
                Face(Vector3.zero,Quaternion.identity,q,p,s,r,Vector3.up,color);
            }
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal,Color color){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);for(int j=0;j<3;j++){normals.Add(normal);colors.Add(color);}triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);}
        public void MapColors(Func<Color, Color> remap) { for (int i = 0; i < colors.Count; i++) { Color color = remap(colors[i]); color.a = 1; colors[i] = color; } }
        public Mesh Finish(string name){Mesh mesh=new Mesh();mesh.name=name;if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;}
    }

    internal static class VoxelAssets
    {
        static Material lit,glow; static readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        public static Material Lit { get { Ensure();return lit; } }
        public static Material Glow { get { Ensure();return glow; } }
        static void Ensure(){if(lit!=null)return;Shader shader=Resources.Load<Shader>("ArenaVoxelLit");if(shader==null)shader=Shader.Find("Standard");lit=new Material(shader){name="Arena shared voxel material",enableInstancing=true};lit.SetFloat("_Glossiness",.1f);glow=new Material(lit){name="Arena ember material"};glow.SetFloat("_Glow",.65f);}
        public static Color Hex(string value){Color c;if(ColorUtility.TryParseHtmlString(value,out c))return c;return Color.white;}
        public static Mesh Cached(string key,Action<VoxelMesh> make){Mesh m;if(meshes.TryGetValue(key,out m))return m;VoxelMesh b=new VoxelMesh();make(b);m=b.Finish(key);meshes.Add(key,m);return m;}
        public static Transform Part(Transform parent,string name,Mesh mesh,Vector3 position,Material material=null,bool shadow=true){GameObject g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=position;g.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material==null?Lit:material;r.shadowCastingMode=shadow?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=shadow;return g.transform;}
        public static Mesh Block(Color color){string key="block"+ColorUtility.ToHtmlStringRGB(color);return Cached(key,b=>b.Box(Vector3.zero,Vector3.one,color));}
        public static Mesh TeamRing(int slot){Color[] teams={Hex("#7FE2BE"),Hex("#EDBC72"),Hex("#B49AF0"),Hex("#F38E9C")};return Cached("ring"+slot,b=>b.Ring(.57f,.49f,teams[Mathf.Clamp(slot,0,3)]));}
        public static Mesh SelectionRing(){return Cached("selection",b=>b.Ring(.7f,.64f,Hex("#FFF1AA")));}
        public static Mesh DamageNumber(int amount){int n=Mathf.Clamp(amount,0,999);return Cached("damage"+n,b=>{
            string text=n.ToString();int[] masks={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
            for(int d=0;d<text.Length;d++){float x=(d-(text.Length-1)*.5f)*.25f;int m=masks[text[d]-'0'];Color c=Hex("#FFF1BF");
                Vector3[] p={new Vector3(x,.32f,0),new Vector3(x+.09f,.23f,0),new Vector3(x+.09f,.07f,0),new Vector3(x,0,0),new Vector3(x-.09f,.07f,0),new Vector3(x-.09f,.23f,0),new Vector3(x,.16f,0)};
                for(int s=0;s<7;s++)if((m&(1<<s))!=0)b.Box(p[s],s==0||s==3||s==6?new Vector3(.18f,.035f,.025f):new Vector3(.035f,.13f,.025f),c);
            }
        });}
        public static Mesh CommanderDamageNumber(int amount){int n=Mathf.Clamp(amount,1,20);return Cached("commander-damage-"+n,b=>{
            Color red=Hex("#F88C91"),light=Hex("#FFF0CF");string text=n.ToString();float start=.05f;
            string[] heart={"01010","11111","11111","01110","00100"};
            for(int y=0;y<heart.Length;y++)for(int x=0;x<heart[y].Length;x++)if(heart[y][x]=='1')
                b.Box(new Vector3(-.47f+x*.055f,.3f-y*.055f,0),new Vector3(.052f,.052f,.035f),red);
            b.Box(new Vector3(-.12f,.16f,0),new Vector3(.13f,.04f,.03f),red);
            int[] masks={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
            for(int d=0;d<text.Length;d++){
                float x=start+d*.25f;int m=masks[text[d]-'0'];
                Vector3[] p={new Vector3(x,.32f,0),new Vector3(x+.09f,.23f,0),new Vector3(x+.09f,.07f,0),new Vector3(x,0,0),new Vector3(x-.09f,.07f,0),new Vector3(x-.09f,.23f,0),new Vector3(x,.16f,0)};
                for(int s=0;s<7;s++)if((m&(1<<s))!=0)b.Box(p[s],s==0||s==3||s==6?new Vector3(.18f,.035f,.03f):new Vector3(.035f,.13f,.03f),light);
            }
        });}
        public static Mesh ImpactTint(Mesh source)
        {
            string key="commander-flash/"+source.name;Mesh mesh;if(meshes.TryGetValue(key,out mesh))return mesh;
            mesh=UnityEngine.Object.Instantiate(source);mesh.name=key;Color[] colors=mesh.colors;
            for(int i=0;i<colors.Length;i++)colors[i]=Color.Lerp(colors[i],Hex("#FF696F"),.78f);
            mesh.colors=colors;meshes.Add(key,mesh);return mesh;
        }
    }

    public sealed partial class VoxelCreature : MonoBehaviour
    {
        public string UnitId, Kind;
        public Vector3 Target;
        public float Health = 1, MaximumHealth = 1, Attack, Size = 1;
        public bool Battle, Dead, Seen, LocalCommander;
        public int Slot;
        public float Height = 1.8f;

        Transform figure, head, body, bow, healthRoot, healthFill, teamRing, selection;
        MeshFilter teamRingMesh, healthFillMesh;
        Mesh healthyMesh, woundedMesh, criticalMesh;
        readonly List<Transform> legs = new List<Transform>();
        readonly List<Transform> arms = new List<Transform>();
        readonly List<Vector3> legRestPositions = new List<Vector3>();
        Vector3 headRestPosition;
        Quaternion facing = Quaternion.identity;
        float seed, deathProgress, movementBlend;
        int healthColor = -1;
        bool hovered;
        bool wasDragging,flashVisible;
        float selectionAmount,selectionVelocity,dragAmount,dragVelocity,landingAge=10,impactAge=10,impactStrength=1;
        MeshFilter[] commanderParts;
        Mesh[] commanderNormalMeshes,commanderFlashMeshes;

        public bool HealthBarVisible { get { return healthRoot != null && healthRoot.gameObject.activeInHierarchy; } }

        /// <summary>World hover is independent of selection. Steve never receives a creature HP bar.</summary>
        public void SetHovered(bool value)
        {
            hovered = value && Kind != "steve" && !Dead;
            if (healthRoot != null) healthRoot.gameObject.SetActive(Battle && hovered && !Dead);
        }

        public void Build(string kind)
        {
            Kind = kind;
            ConfigureBestiary();
            seed = UnityEngine.Random.value * Mathf.PI * 2;
            figure = new GameObject("Articulated model").transform;
            figure.SetParent(transform, false);
            BuildBody();
            if(Kind=="steve"){
                commanderParts=figure.GetComponentsInChildren<MeshFilter>();
                commanderNormalMeshes=new Mesh[commanderParts.Length];commanderFlashMeshes=new Mesh[commanderParts.Length];
                for(int i=0;i<commanderParts.Length;i++){commanderNormalMeshes[i]=commanderParts[i].sharedMesh;commanderFlashMeshes[i]=VoxelAssets.ImpactTint(commanderNormalMeshes[i]);}
            }
            if (head != null) headRestPosition = head.localPosition;
            for (int i = 0; i < legs.Count; i++) legRestPositions.Add(legs[i].localPosition);

            teamRing = VoxelAssets.Part(transform, "Team footprint", VoxelAssets.TeamRing(0), new Vector3(0, .025f, 0), null, false);
            teamRingMesh = teamRing.GetComponent<MeshFilter>();
            selection = VoxelAssets.Part(transform, "Selection", VoxelAssets.SelectionRing(), new Vector3(0, .04f, 0), VoxelAssets.Glow, false);
            selection.gameObject.SetActive(false);
            BuildSkillFeedback();
            float footprint = Mathf.Clamp(modelFootprint / .57f, .6f, 2.8f);
            teamRing.localScale = new Vector3(footprint, 1, footprint);
            selection.localScale = new Vector3(footprint, 1, footprint);

            healthRoot = new GameObject("Health billboard").transform;
            healthRoot.SetParent(transform, false);
            VoxelAssets.Part(healthRoot, "Health frame", VoxelAssets.Block(VoxelAssets.Hex("#101D20")), Vector3.zero, null, false).localScale = new Vector3(.98f, .13f, .04f);
            VoxelAssets.Part(healthRoot, "Health background", VoxelAssets.Block(VoxelAssets.Hex("#465751")), new Vector3(0, 0, -.025f), null, false).localScale = new Vector3(.9f, .065f, .025f);
            healthyMesh = VoxelAssets.Block(VoxelAssets.Hex("#A3EDB3"));
            woundedMesh = VoxelAssets.Block(VoxelAssets.Hex("#F3D480"));
            criticalMesh = VoxelAssets.Block(VoxelAssets.Hex("#F39D8D"));
            healthFill = VoxelAssets.Part(healthRoot, "Health", healthyMesh, new Vector3(0, 0, -.045f), VoxelAssets.Glow, false);
            healthFillMesh = healthFill.GetComponent<MeshFilter>();
            healthFill.localScale = new Vector3(.89f, .063f, .025f);
            healthRoot.gameObject.SetActive(false);

            CapsuleCollider collider = gameObject.AddComponent<CapsuleCollider>();
            collider.radius = modelFootprint * .72f;
            collider.height = Height;
            collider.center = new Vector3(0, Height * .5f + modelHover, 0);
            gameObject.layer = 8;
        }

        Transform Part(string name, Vector3 at, Action<VoxelMesh> build, Transform parent = null)
        {
            // Every species has a shared mesh cache; palette variants are baked once.
            return VoxelAssets.Part(parent == null ? figure : parent, name,
                VoxelAssets.Cached("bestiary-v3/" + Kind + "/" + name, mesh => { build(mesh); ApplyNativePalette(mesh); }), at);
        }
        static void B(VoxelMesh mesh, float x, float y, float z, float w, float h, float d, string color)
        {
            mesh.Box(new Vector3(x, y, z), new Vector3(w, h, d), VoxelAssets.Hex(color));
        }
        static void RotatedBox(VoxelMesh mesh, Vector3 at, Vector3 size, Vector3 rotation, string color)
        {
            mesh.Box(at, size, VoxelAssets.Hex(color), Quaternion.Euler(rotation));
        }
        static void Mottle(VoxelMesh mesh, Vector3 center, Vector3 size, int columns, int rows, int salt, string light, string dark)
        {
            // Small surface voxels are baked into the same shared mesh, not separate objects/materials.
            Color bright = VoxelAssets.Hex(light), shade = VoxelAssets.Hex(dark);
            float w = size.x / columns, h = size.y / rows;
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791));
                if (hash % 7 > 1) continue;
                float px = center.x - size.x * .5f + (x + .5f) * w;
                float py = center.y - size.y * .5f + (y + .5f) * h;
                mesh.Box(new Vector3(px, py, center.z - size.z * .5f - .006f), new Vector3(w * .94f, h * .94f, .01f), hash % 2 == 0 ? bright : shade);
                mesh.Box(new Vector3(-px + center.x * 2, py, center.z + size.z * .5f + .006f), new Vector3(w * .94f, h * .94f, .01f), hash % 3 == 0 ? bright : shade);
            }
            for (int row = 0; row < rows; row++)
            {
                if ((row + salt) % 3 != 0) continue;
                float py = center.y - size.y * .5f + (row + .5f) * h;
                mesh.Box(new Vector3(center.x - size.x * .5f - .006f, py, center.z + size.z * .14f), new Vector3(.01f, h * .9f, size.z * .23f), shade);
                mesh.Box(new Vector3(center.x + size.x * .5f + .006f, py, center.z - size.z * .19f), new Vector3(.01f, h * .9f, size.z * .3f), bright);
            }
        }

        void BuildBody()
        {
            if (Kind == "steve") { BuildHumanoid(true); return; }
            if (Kind == "copper_golem" || Kind == "zombified_piglin" || Kind == "zombie_villager") { BuildBestiary(); return; }
            switch (modelFamily)
            {
                case "slime": BuildSlime(); break;
                case "spider": BuildSpider(); break;
                case "creeper": BuildCreeper(); break;
                case "golem": BuildGolem(); break;
                case "skeleton": BuildSkeleton(); break;
                case "zombie": BuildHumanoid(false); break;
                default: BuildBestiary(); return;
            }
            VariantDetails();
        }

        void BuildSlime()
        {
            Height = 1.14f;
            body = Part("Jelly", Vector3.zero, mesh =>
            {
                B(mesh, 0, .48f, 0, 1.06f, .84f, 1.06f, "#74C96C");
                B(mesh, 0, .12f, 0, 1.08f, .15f, 1.08f, "#50A95A");
                B(mesh, 0, .905f, 0, 1.01f, .035f, 1.01f, "#A4E28A");
                B(mesh, 0, .48f, -.538f, .78f, .57f, .02f, "#8EDB7A");
                B(mesh, -.539f, .53f, -.07f, .02f, .52f, .71f, "#60B568");
                B(mesh, .539f, .53f, -.07f, .02f, .52f, .71f, "#93D780");
                B(mesh, -.215f, .6f, -.556f, .18f, .21f, .025f, "#294C36");
                B(mesh, .215f, .6f, -.556f, .18f, .21f, .025f, "#294C36");
                B(mesh, -.235f, .65f, -.572f, .06f, .055f, .015f, "#C1F3A8");
                B(mesh, .195f, .65f, -.572f, .06f, .055f, .015f, "#C1F3A8");
                B(mesh, .02f, .36f, -.555f, .22f, .085f, .025f, "#3D7546");
                B(mesh, -.36f, .78f, -.554f, .08f, .12f, .025f, "#C6F3A4");
                B(mesh, -.285f, .842f, -.554f, .22f, .045f, .025f, "#C6F3A4");
                B(mesh, -.27f, .934f, -.18f, .29f, .018f, .28f, "#C5F1A4");
                B(mesh, .27f, .934f, .27f, .13f, .018f, .13f, "#8BCF76");
                B(mesh, .35f, .72f, .546f, .13f, .14f, .02f, "#A1DF88");
            });
        }

        void BuildSpider()
        {
            Height = 1.02f;
            body = Part("Abdomen", new Vector3(0, .48f, .32f), mesh =>
            {
                B(mesh, 0, 0, 0, .88f, .46f, .98f, "#40303D");
                B(mesh, 0, .235f, .02f, .69f, .1f, .77f, "#5D3F4C");
                B(mesh, 0, .3f, .035f, .38f, .035f, .56f, "#79515A");
                B(mesh, -.46f, -.03f, 0, .07f, .3f, .7f, "#2D2833");
                B(mesh, .46f, -.03f, 0, .07f, .3f, .7f, "#4E3746");
                for (int i = 0; i < 3; i++) B(mesh, 0, .266f, -.27f + i * .24f, .48f, .025f, .065f, "#352D37");
                B(mesh, 0, -.075f, -.58f, .42f, .28f, .25f, "#332C37");
            });
            head = Part("Head", new Vector3(0, .44f, -.53f), mesh =>
            {
                B(mesh, 0, 0, 0, .68f, .37f, .48f, "#352A35");
                B(mesh, 0, .2f, -.015f, .53f, .045f, .34f, "#503743");
                B(mesh, -.13f, .055f, -.25f, .13f, .12f, .035f, "#EF5E54");
                B(mesh, .13f, .055f, -.25f, .13f, .12f, .035f, "#EF5E54");
                for (int side = -1; side <= 1; side += 2)
                {
                    B(mesh, side * .26f, .055f, -.25f, .065f, .065f, .025f, "#DC4B49");
                    B(mesh, side * .2f, .155f, -.25f, .06f, .045f, .025f, "#EC6E5D");
                    B(mesh, side * .345f, .035f, -.11f, .025f, .065f, .07f, "#D64A4D");
                    B(mesh, side * .12f, .085f, -.274f, .04f, .035f, .015f, "#FFD0A0");
                    B(mesh, side * .18f, -.15f, -.31f, .105f, .17f, .19f, "#898778");
                    B(mesh, side * .15f, -.235f, -.375f, .07f, .12f, .07f, "#D7CDA9");
                }
            });
            for (int i = 0; i < 8; i++)
            {
                int side = i < 4 ? -1 : 1, index = i % 4;
                Transform leg = Part("Leg" + i, new Vector3(side * .36f, .44f, (index - 1.5f) * .285f), mesh =>
                {
                    RotatedBox(mesh, new Vector3(side * .25f, .045f, 0), new Vector3(.58f, .105f, .12f), new Vector3(0, 0, side * 10), "#51404A");
                    B(mesh, side * .52f, .07f, 0, .15f, .15f, .15f, "#70515B");
                    RotatedBox(mesh, new Vector3(side * .65f, -.115f, 0), new Vector3(.105f, .48f, .105f), new Vector3(0, 0, side * 34), "#382D39");
                    B(mesh, side * .78f, -.32f, -.025f, .12f, .09f, .16f, "#2B2933");
                });
                legs.Add(leg);
            }
        }

        void BuildCreeper()
        {
            Height = 1.9f;
            body = Part("Body", Vector3.zero, mesh =>
            {
                B(mesh, 0, .84f, 0, .45f, .94f, .4f, "#69AE50");
                Mottle(mesh, new Vector3(0, .84f, 0), new Vector3(.45f, .94f, .4f), 4, 8, 17, "#98CA70", "#498744");
                B(mesh, 0, 1.285f, 0, .39f, .06f, .35f, "#8CC165");
            });
            head = Part("Head", new Vector3(0, 1.55f, -.025f), mesh =>
            {
                B(mesh, 0, 0, 0, .66f, .61f, .64f, "#80BC62");
                Mottle(mesh, Vector3.zero, new Vector3(.66f, .61f, .64f), 8, 8, 29, "#A7D27A", "#559747");
                B(mesh, -.19f, .085f, -.336f, .17f, .16f, .025f, "#20382A");
                B(mesh, .19f, .085f, -.336f, .17f, .16f, .025f, "#20382A");
                B(mesh, 0, -.04f, -.336f, .14f, .17f, .025f, "#20382A");
                B(mesh, 0, -.17f, -.336f, .34f, .12f, .025f, "#243B2A");
                B(mesh, -.13f, -.235f, -.336f, .08f, .13f, .025f, "#243B2A");
                B(mesh, .13f, -.235f, -.336f, .08f, .13f, .025f, "#243B2A");
                B(mesh, 0, -.208f, -.351f, .13f, .045f, .015f, "#76B05A");
                B(mesh, -.18f, .313f, .08f, .16f, .015f, .24f, "#B3D982");
                B(mesh, .16f, .313f, -.15f, .19f, .015f, .13f, "#639C50");
            });
            for (int i = 0; i < 4; i++)
            {
                legs.Add(Part("Foot" + i, new Vector3(i % 2 == 0 ? -.21f : .21f, .4f, i < 2 ? -.235f : .235f), mesh =>
                {
                    B(mesh, 0, -.17f, 0, .25f, .34f, .32f, "#659E4D");
                    Mottle(mesh, new Vector3(0, -.17f, 0), new Vector3(.25f, .34f, .32f), 3, 4, 9, "#8DB65D", "#45753D");
                    B(mesh, 0, -.325f, -.025f, .27f, .08f, .36f, "#3E6739");
                    B(mesh, -.06f, -.285f, -.195f, .06f, .06f, .015f, "#A0BC70");
                    B(mesh, .06f, -.285f, -.195f, .06f, .06f, .015f, "#A0BC70");
                }));
            }
        }

        void BuildGolem()
        {
            Height = 2.63f;
            body = Part("Torso", Vector3.zero, mesh =>
            {
                B(mesh, 0, 1.34f, 0, 1.07f, .98f, .62f, "#C9C9B1");
                B(mesh, 0, 1.72f, -.025f, 1.18f, .21f, .68f, "#DCDBC1");
                B(mesh, 0, .89f, 0, .85f, .2f, .52f, "#AAAE98");
                B(mesh, 0, 1.36f, -.327f, .48f, .5f, .04f, "#DBDCC1");
                Mottle(mesh, new Vector3(0, 1.34f, 0), new Vector3(1.07f, .98f, .62f), 8, 7, 41, "#D8D9C0", "#B1B8A1");
                B(mesh, -.29f, 1.43f, -.35f, .12f, .61f, .03f, "#527C49");
                B(mesh, -.17f, 1.24f, -.35f, .28f, .1f, .03f, "#6D994F");
                B(mesh, -.32f, 1.65f, -.35f, .28f, .1f, .03f, "#7E9E55");
                B(mesh, .25f, 1.08f, -.35f, .15f, .32f, .03f, "#6A8B51");
                B(mesh, .32f, 1.15f, -.37f, .07f, .07f, .025f, "#D29759");
                B(mesh, .12f, 1.49f, .328f, .12f, .59f, .025f, "#769453");
                B(mesh, .26f, 1.27f, .328f, .29f, .1f, .025f, "#91A660");
            });
            head = Part("Head", new Vector3(0, 2.15f, -.08f), mesh =>
            {
                B(mesh, 0, 0, 0, .64f, .68f, .62f, "#D1D0B8");
                B(mesh, 0, .26f, .015f, .69f, .13f, .65f, "#E0DEC3");
                B(mesh, 0, -.24f, -.03f, .56f, .16f, .56f, "#B7BBA4");
                B(mesh, -.18f, .09f, -.325f, .22f, .1f, .055f, "#A0A68F");
                B(mesh, .18f, .09f, -.325f, .22f, .1f, .055f, "#A0A68F");
                B(mesh, -.18f, .01f, -.33f, .13f, .055f, .025f, "#A35648");
                B(mesh, .18f, .01f, -.33f, .13f, .055f, .025f, "#A35648");
                B(mesh, -.18f, .01f, -.347f, .05f, .055f, .015f, "#5E413B");
                B(mesh, .18f, .01f, -.347f, .05f, .055f, .015f, "#5E413B");
                B(mesh, 0, -.08f, -.41f, .16f, .28f, .24f, "#ADB39B");
                B(mesh, 0, -.23f, -.34f, .27f, .04f, .03f, "#878F7F");
                B(mesh, -.33f, .12f, .08f, .025f, .16f, .2f, "#88A167");
                B(mesh, .16f, -.02f, .32f, .16f, .3f, .025f, "#BDC2A8");
            });
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                legs.Add(Part("Leg" + i, new Vector3(side * .29f, .88f, .02f), mesh =>
                {
                    B(mesh, 0, -.38f, 0, .36f, .76f, .43f, "#B3B9A1");
                    B(mesh, 0, -.39f, -.225f, .38f, .17f, .055f, "#CCD0B4");
                    B(mesh, 0, -.77f, -.08f, .43f, .15f, .59f, "#929F8A");
                    B(mesh, 0, -.71f, -.27f, .37f, .07f, .19f, "#C1C5A9");
                    B(mesh, side * .11f, -.32f, .226f, .12f, .42f, .025f, "#829B63");
                }));
                arms.Add(Part("Arm" + i, new Vector3(side * .73f, 1.76f, .01f), mesh =>
                {
                    B(mesh, 0, -.1f, 0, .39f, .32f, .46f, "#D3D3B9");
                    B(mesh, 0, -.5f, 0, .33f, .63f, .37f, "#B9C0A7");
                    B(mesh, 0, -.56f, -.2f, .36f, .16f, .055f, "#A3AC94");
                    B(mesh, 0, -.96f, -.02f, .42f, .39f, .45f, "#C3C8AC");
                    B(mesh, 0, -1.12f, -.04f, .44f, .1f, .48f, "#9BA68F");
                    B(mesh, side * .12f, -.47f, -.2f, .11f, .52f, .025f, "#6B8E51");
                    B(mesh, 0, -.73f, -.205f, .22f, .1f, .025f, "#8DA75D");
                }));
            }
        }

        void BuildSkeleton()
        {
            Height = 1.96f;
            body = Part("Ribcage", Vector3.zero, mesh =>
            {
                B(mesh, 0, 1.04f, .04f, .12f, .66f, .16f, "#A8AC99");
                B(mesh, 0, 1.29f, 0, .52f, .095f, .26f, "#E0DDC2");
                for (int i = 0; i < 3; i++)
                {
                    float y = 1.16f - i * .145f;
                    B(mesh, 0, y, -.065f, .45f - i * .04f, .065f, .12f, "#D6D5BD");
                    B(mesh, -.205f + i * .02f, y, .015f, .065f, .065f, .27f, "#B6BEA9");
                    B(mesh, .205f - i * .02f, y, .015f, .065f, .065f, .27f, "#CDD0B7");
                }
                B(mesh, 0, .755f, 0, .37f, .15f, .25f, "#B8C0AA");
                B(mesh, -.17f, .74f, -.065f, .1f, .18f, .21f, "#D7D5BA");
                B(mesh, .17f, .74f, -.065f, .1f, .18f, .21f, "#D7D5BA");
            });
            head = Part("Skull", new Vector3(0, 1.61f, -.035f), mesh =>
            {
                B(mesh, 0, .025f, .015f, .57f, .48f, .53f, "#DCDAC1");
                B(mesh, 0, .275f, .015f, .5f, .035f, .47f, "#EEE8CB");
                B(mesh, 0, -.22f, -.055f, .43f, .13f, .39f, "#BDC4AD");
                B(mesh, -.135f, .035f, -.263f, .18f, .16f, .035f, "#3B433D");
                B(mesh, .135f, .035f, -.263f, .18f, .16f, .035f, "#3B433D");
                B(mesh, -.13f, .12f, -.275f, .2f, .04f, .035f, "#B3BBA8");
                B(mesh, .13f, .12f, -.275f, .2f, .04f, .035f, "#B3BBA8");
                B(mesh, 0, -.08f, -.263f, .07f, .09f, .03f, "#586153");
                B(mesh, 0, -.19f, -.263f, .36f, .055f, .03f, "#66715F");
                for (int i = 0; i < 4; i++) B(mesh, (i - 1.5f) * .084f, -.19f, -.285f, .044f, .072f, .022f, "#DBDDC1");
                B(mesh, -.29f, -.025f, .045f, .02f, .14f, .17f, "#B5BFAC");
                B(mesh, .12f, .08f, .291f, .055f, .22f, .016f, "#B6BCA8");
                B(mesh, .18f, .16f, .291f, .13f, .045f, .016f, "#B6BCA8");
            });
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                legs.Add(Part("Leg" + i, new Vector3(side * .14f, .69f, 0), mesh =>
                {
                    B(mesh, 0, -.15f, 0, .13f, .28f, .15f, "#D7D8BD");
                    B(mesh, 0, -.31f, -.005f, .165f, .1f, .18f, "#BBC5AF");
                    B(mesh, 0, -.46f, 0, .105f, .23f, .13f, "#D4D6BC");
                    B(mesh, 0, -.6f, -.055f, .18f, .09f, .3f, "#ACB8A3");
                }));
                Transform arm = Part("Arm" + i, new Vector3(side * .32f, 1.26f, 0), mesh =>
                {
                    B(mesh, 0, -.13f, 0, .12f, .28f, .13f, "#DDDCC2");
                    B(mesh, 0, -.285f, 0, .145f, .085f, .15f, "#B6C0AC");
                    B(mesh, 0, -.43f, 0, .105f, .23f, .12f, "#D3D7BE");
                    B(mesh, 0, -.57f, -.01f, .15f, .1f, .17f, "#C4CEB5");
                });
                arms.Add(arm);
                if (i == 1) bow = Part("Carved bow", new Vector3(.045f, -.56f, -.07f), mesh =>
                {
                    B(mesh, 0, 0, -.2f, .065f, .32f, .065f, "#815F40");
                    B(mesh, 0, .205f, -.16f, .065f, .13f, .075f, "#AA7E4D");
                    B(mesh, 0, -.205f, -.16f, .065f, .13f, .075f, "#AA7E4D");
                    B(mesh, 0, .305f, -.095f, .055f, .09f, .12f, "#C49D63");
                    B(mesh, 0, -.305f, -.095f, .055f, .09f, .12f, "#C49D63");
                    B(mesh, 0, 0, -.017f, .014f, .68f, .014f, "#E8D8AD");
                    B(mesh, 0, 0, -.21f, .085f, .11f, .085f, "#674D3B");
                }, arm);
            }
        }

        void BuildHumanoid(bool steve)
        {
            Height = 1.94f;
            string skin = steve ? "#BE9072" : "#6E9A56";
            body = Part("Shirt", Vector3.zero, mesh =>
            {
                B(mesh, 0, 1.02f, 0, .56f, .65f, .33f, steve ? "#39A8AF" : "#4C958A");
                B(mesh, 0, 1.3f, -.014f, .23f, .09f, .35f, skin);
                B(mesh, 0, 1.25f, -.178f, .12f, .12f, .018f, steve ? "#27828D" : "#326F69");
                B(mesh, -.205f, 1.03f, -.178f, .055f, .49f, .02f, steve ? "#2C8D99" : "#3C7E73");
                B(mesh, .22f, 1.05f, .18f, .055f, .47f, .02f, steve ? "#6BB8B9" : "#72AD96");
                B(mesh, 0, .71f, 0, .56f, .12f, .34f, "#4A528A");
                B(mesh, 0, .73f, -.182f, .09f, .08f, .02f, "#38496E");
                if (!steve)
                {
                    Mottle(mesh, new Vector3(0, 1.02f, 0), new Vector3(.56f, .65f, .33f), 7, 8, 23, "#6AA492", "#36786E");
                    B(mesh, -.13f, .725f, -.188f, .12f, .09f, .025f, "#77A164");
                    B(mesh, .15f, .76f, -.188f, .1f, .075f, .025f, "#325C53");
                    B(mesh, .2f, 1.16f, .187f, .08f, .14f, .022f, "#658B56");
                }
                else
                {
                    B(mesh, -.1f, 1.04f, -.183f, .035f, .31f, .014f, "#59B8BB");
                    B(mesh, .12f, .84f, -.183f, .15f, .065f, .014f, "#2B8A98");
                }
            });
            head = Part("Head", new Vector3(0, 1.59f, -.01f), mesh =>
            {
                B(mesh, 0, 0, 0, .54f, .54f, .54f, skin);
                if (steve)
                {
                    B(mesh, 0, .245f, .012f, .56f, .09f, .56f, "#493629");
                    B(mesh, 0, .065f, .255f, .56f, .37f, .055f, "#443328");
                    B(mesh, -.257f, .11f, .03f, .055f, .26f, .45f, "#533B2C");
                    B(mesh, .257f, .11f, .03f, .055f, .26f, .45f, "#533B2C");
                    B(mesh, -.17f, .18f, -.282f, .18f, .09f, .025f, "#5D412D");
                    B(mesh, .2f, .18f, -.282f, .12f, .09f, .025f, "#5D412D");
                    B(mesh, -.1f, .303f, -.06f, .18f, .015f, .19f, "#6C4B31");
                    B(mesh, .15f, .303f, .13f, .12f, .015f, .15f, "#352B25");
                    B(mesh, -.135f, .025f, -.286f, .13f, .07f, .023f, "#E6DDD0");
                    B(mesh, .135f, .025f, -.286f, .13f, .07f, .023f, "#E6DDD0");
                    B(mesh, -.115f, .025f, -.303f, .055f, .07f, .016f, "#4D689C");
                    B(mesh, .115f, .025f, -.303f, .055f, .07f, .016f, "#4D689C");
                    B(mesh, 0, -.06f, -.295f, .09f, .095f, .065f, "#AA785A");
                    B(mesh, 0, -.168f, -.287f, .26f, .125f, .025f, "#694B38");
                    B(mesh, 0, -.14f, -.305f, .13f, .045f, .016f, "#D09A77");
                    B(mesh, -.28f, -.035f, .01f, .03f, .12f, .105f, "#AA785B");
                    B(mesh, .28f, -.035f, .01f, .03f, .12f, .105f, "#CB9C79");
                    B(mesh, -.15f, .07f, .294f, .12f, .12f, .015f, "#61442F");
                }
                else
                {
                    Mottle(mesh, Vector3.zero, new Vector3(.54f, .54f, .54f), 8, 8, 37, "#94B66E", "#567E48");
                    B(mesh, -.135f, .06f, -.286f, .16f, .095f, .026f, "#283E2D");
                    B(mesh, .135f, .06f, -.286f, .16f, .095f, .026f, "#283E2D");
                    B(mesh, -.135f, .121f, -.291f, .17f, .03f, .026f, "#94B66E");
                    B(mesh, .135f, .121f, -.291f, .17f, .03f, .026f, "#476C41");
                    B(mesh, 0, -.04f, -.29f, .075f, .085f, .045f, "#5B824A");
                    B(mesh, 0, -.16f, -.286f, .25f, .07f, .026f, "#375A37");
                    B(mesh, -.065f, -.145f, -.303f, .045f, .035f, .015f, "#ACB783");
                    B(mesh, .14f, -.08f, -.29f, .075f, .06f, .022f, "#819D5E");
                    B(mesh, -.16f, .278f, .08f, .14f, .015f, .19f, "#9FBB78");
                }
            });
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                legs.Add(Part("Leg" + i, new Vector3(side * .145f, .68f, 0), mesh =>
                {
                    B(mesh, 0, -.28f, 0, .25f, .57f, .28f, steve ? "#515F9B" : "#4E5887");
                    B(mesh, side * .105f, -.27f, -.15f, .04f, .43f, .016f, "#414D7E");
                    B(mesh, 0, -.33f, .147f, .19f, .1f, .016f, "#6575A5");
                    B(mesh, 0, -.575f, -.035f, .26f, .13f, .35f, "#3C4C5D");
                    B(mesh, 0, -.565f, -.215f, .22f, .055f, .022f, "#59697A");
                    if (!steve) { B(mesh, 0, -.29f, -.15f, .13f, .14f, .025f, "#72935B"); B(mesh, -.03f, -.23f, -.167f, .08f, .04f, .015f, "#8DA46B"); }
                }));
                arms.Add(Part("Arm" + i, new Vector3(side * .395f, 1.28f, 0), mesh =>
                {
                    B(mesh, 0, -.28f, 0, .21f, .64f, .25f, skin);
                    B(mesh, 0, -.085f, 0, .225f, .28f, .27f, steve ? "#3EA4AD" : "#4C9187");
                    B(mesh, 0, -.225f, -.147f, .215f, .035f, .025f, steve ? "#2B8895" : "#377B71");
                    B(mesh, 0, -.53f, -.137f, .16f, .095f, .02f, steve ? "#CE9E7B" : "#8DA866");
                    B(mesh, side * .11f, -.36f, 0, .015f, .19f, .19f, steve ? "#AB7B60" : "#537B47");
                    if (!steve) B(mesh, -.035f, -.37f, -.144f, .08f, .12f, .025f, "#A4B775");
                }));
            }
        }

        public void ResetVisual(string id, int slot, Vector3 position)
        {
            UnitId = id; Slot = slot; Target = position;
            transform.position = position;
            transform.localScale = Vector3.one;
            figure.localScale = Vector3.one;
            figure.localPosition = Vector3.up * modelHover;
            facing = Quaternion.Euler(0, (Kind == "steve" ? 0 : 180) - slot * 90, 0);
            figure.rotation = facing;
            Dead = false; deathProgress = 0; Attack = 0; movementBlend = 0; Seen = true;
            hovered = false; healthColor = -1;
            LocalCommander=false;wasDragging=false;selectionAmount=selectionVelocity=dragAmount=dragVelocity=0;landingAge=impactAge=10;
            ResetExtraFeedback();
            ResetArenaEventFeedback();
            SetCommanderFlash(false);
            teamRingMesh.sharedMesh = VoxelAssets.TeamRing(slot);
            healthRoot.gameObject.SetActive(false);
            selection.gameObject.SetActive(false);
            gameObject.SetActive(true);
        }

        void OnDisable()
        {
            hovered = false;
            ResetArenaEventFeedback();
            SetSkillHighlighted(false);executionPlaying=false;
            SetCommanderFlash(false);
            if (healthRoot != null) healthRoot.gameObject.SetActive(false);
        }

        public void PlayDefeatImpact(float strength=1)
        {
            if(Kind=="steve"){impactAge=0;impactStrength=.65f+Mathf.Clamp01(strength)*.6f;}
        }
        public void CancelDefeatImpact()
        {
            impactAge=10;SetCommanderFlash(false);executionPlaying=false;
            if(figure!=null)figure.gameObject.SetActive(!commanderEliminated);
        }
        void SetCommanderFlash(bool visible)
        {
            if(flashVisible==visible||commanderParts==null)return;flashVisible=visible;
            for(int i=0;i<commanderParts.Length;i++)commanderParts[i].sharedMesh=visible?commanderFlashMeshes[i]:commanderNormalMeshes[i];
        }
        static void Spring(ref float value,ref float velocity,float target,float frequency,float damping,float dt)
        {
            // Small substeps keep this underdamped spring stable during a slow rendered frame.
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.016f));float step=dt/steps;
            for(int i=0;i<steps;i++){velocity+=((target-value)*frequency*frequency-2*damping*frequency*velocity)*step;value+=velocity*step;}
        }

        public void Step(float dt, float time, Camera camera, bool selected, Vector3? drag)
        {
            dt = Mathf.Max(dt, 0);
            bool dragging=drag.HasValue&&!Battle&&Kind!="steve"&&!Dead;
            if(wasDragging&&!dragging)landingAge=0;
            wasDragging=dragging;landingAge=Mathf.Min(10,landingAge+dt);impactAge=Mathf.Min(10,impactAge+dt);
            Spring(ref dragAmount,ref dragVelocity,dragging?1:0,18,.64f,dt);
            bool focused=!Dead&&(selected||hovered||(Kind=="steve"&&LocalCommander));
            Spring(ref selectionAmount,ref selectionVelocity,focused?1:0,20,.58f,dt);
            Vector3 aim = drag.HasValue ? drag.Value : Target;
            Vector3 delta = aim - transform.position;
            float desiredMovement = Mathf.Clamp01(delta.magnitude * 11);
            movementBlend = Mathf.MoveTowards(movementBlend, desiredMovement, dt * 5);
            transform.position = Vector3.Lerp(transform.position, aim, 1 - Mathf.Exp(-13 * dt));
            Vector3 flatDelta = new Vector3(delta.x, 0, delta.z);
            if (flatDelta.sqrMagnitude > .0015f)
                facing = Quaternion.Slerp(facing, Quaternion.LookRotation(-flatDelta.normalized, Vector3.up), 1 - Mathf.Exp(-10 * dt));

            float tempo = modelFamily == "golem" ? 5.2f : modelFamily == "spider" ? 12 : 8;
            float walk = Mathf.Sin(time * tempo + seed);
            float dance = Mathf.Sin(time * 3.2f + seed);
            float idle = Battle ? 0 : 1;
            float strike = Attack > 0 ? Mathf.Sin(Mathf.Clamp01(Attack / .22f) * Mathf.PI) : 0;
            float bounce = Mathf.Abs(walk) * movementBlend * (modelFamily == "golem" ? .018f : .032f) + Mathf.Abs(dance) * idle * .025f;
            float lean = modelFamily == "zombie" ? 3 + movementBlend * 3 : modelFamily == "spider" ? 0 : movementBlend * 2;
            float sway = dance * idle * (modelFamily == "golem" ? 1.4f : 3);
            if (Dead) deathProgress = Mathf.Min(1, deathProgress + dt * 3); else deathProgress = 0;
            figure.rotation = facing * Quaternion.Euler(lean - strike * 5, idle * dance * 4, Dead ? deathProgress * 82 : sway);
            figure.localPosition = new Vector3(0, bounce - deathProgress * .1f, 0) + facing * Vector3.back * (strike * .07f);
            figure.localScale = Vector3.one;
            transform.localScale = Vector3.one * Mathf.Max(.12f, Size * (1 - deathProgress * .65f));

            if (modelFamily == "slime")
            {
                float pulse = Mathf.Sin(time * (4.2f + movementBlend * 1.5f) + seed);
                float hop = Mathf.Max(0, pulse);
                float squash = Mathf.Cos(time * (4.2f + movementBlend * 1.5f) + seed);
                figure.localScale = new Vector3(1 + squash * .09f, 1 - squash * .15f, 1 + squash * .09f);
                figure.localPosition += Vector3.up * hop * (.12f + movementBlend * .16f);
            }
            if (Kind == "creeper") figure.localScale = Vector3.one * (1 + strike * .11f);

            for (int i = 0; i < legs.Count; i++)
            {
                float sign = i % 2 == 0 ? 1 : -1;
                if (modelFamily == "spider")
                {
                    int side = i < 4 ? -1 : 1, index = i % 4;
                    float step = Mathf.Sin(time * 12 + seed + index * 1.45f + (side < 0 ? Mathf.PI : 0));
                    legs[i].localRotation = Quaternion.Euler(step * movementBlend * 10, side * (index - 1.5f) * 23 + step * (movementBlend * 17 + idle * 2), side * (5 + dance * idle * 2));
                    legs[i].localPosition = legRestPositions[i] + Vector3.up * Mathf.Max(0, step) * movementBlend * .055f;
                }
                else
                {
                    if (Kind == "creeper") sign = i == 0 || i == 3 ? 1 : -1;
                    float stride = modelFamily == "golem" ? 20 : modelFamily == "zombie" ? 27 : 34;
                    legs[i].localRotation = Quaternion.Euler(walk * sign * (movementBlend * stride + idle * 5), 0, Kind == "steve" ? idle * dance * sign * 1.5f : 0);
                }
            }
            for (int i = 0; i < arms.Count; i++)
            {
                float sign = i == 0 ? -1 : 1;
                float x = -walk * sign * (movementBlend * 25 + idle * 7), y = 0, z = sign * idle * dance * 4;
                if (modelFamily == "zombie") { x = 74 + dance * 3 + walk * sign * movementBlend * 8 + strike * 22; z = sign * (3 + idle * dance * 3); }
                else if (Kind == "wither_skeleton") { x = 15 - walk * sign * movementBlend * 20 + (i == 1 ? strike * 95 : 0); }
                else if (modelFamily == "skeleton") { x = Battle ? (i == 1 ? 79 : 67 - strike * 17) : 15 - walk * sign * (movementBlend * 17 + 4); y = Battle ? sign * (i == 1 ? 7 : -12) : 0; }
                else if (modelFamily == "golem") { x = -walk * sign * movementBlend * 19 + strike * 112; z = sign * 4; }
                arms[i].localRotation = Quaternion.Euler(x, y, z);
            }
            if (bow != null) bow.localRotation = Quaternion.Euler(Battle ? -79f : -10f, 0, 0);
            if (head != null)
            {
                head.localRotation = Quaternion.Euler(-strike * 9 + (modelFamily == "zombie" ? 5 : 0), idle * dance * 7, modelFamily == "zombie" ? -5 + idle * dance * 2 : 0);
                head.localPosition = headRestPosition + Vector3.up * (idle * Mathf.Sin(time * 3.2f + seed + .4f) * .01f);
            }
            AnimateBestiary(time, idle, strike, walk);
            if(!Dead){
                float settle=landingAge<.7f?Mathf.Exp(-landingAge*7)*Mathf.Cos(landingAge*20):0;
                float stretch=dragAmount*.065f-settle*.16f;
                figure.localScale=Vector3.Scale(figure.localScale,new Vector3(1-stretch*.45f,1+stretch,1-stretch*.45f));
                figure.localPosition+=Vector3.up*(dragAmount*.23f+Mathf.Max(0,Mathf.Sin(landingAge*20))*Mathf.Exp(-landingAge*7)*.11f);
            }
            if(Kind=="steve"&&impactAge<1.3f){
                float jump=Mathf.Sin(Mathf.Clamp01((impactAge-.055f)/.66f)*Mathf.PI)*impactStrength;
                float press=Mathf.Max(0,1-impactAge/.13f)*impactStrength,settle=impactAge>.65f?Mathf.Exp(-(impactAge-.65f)*8)*Mathf.Sin((impactAge-.65f)*23)*impactStrength:0;
                float stretch=jump*.19f-press*.24f-settle*.1f;
                Vector3 outward=new Vector3(Target.x,0,Target.z).normalized;
                figure.localPosition+=Vector3.up*(jump*.72f)+outward*(jump*.36f);
                figure.localScale=Vector3.Scale(figure.localScale,new Vector3(1-stretch*.52f,1+stretch,1-stretch*.52f));
                figure.rotation*=Quaternion.Euler(jump*-24,0,Mathf.Sin(impactAge*24)*Mathf.Exp(-impactAge*4)*12);
                for(int i=0;i<arms.Count;i++)arms[i].localRotation*=Quaternion.Euler(-jump*72,0,(i==0?-1:1)*jump*32);
                if(head!=null)head.localRotation*=Quaternion.Euler(-jump*15,0,0);
            }
            SetCommanderFlash(Kind=="steve"&&impactAge<.34f&&(impactAge<.13f||impactAge>.22f));
            StepExecution(time);
            float footprint=Mathf.Clamp(modelFootprint/.57f,.6f,2.8f);
            float ringSize=footprint*(.82f+Mathf.Clamp(selectionAmount,0,1.3f)*.2f+dragAmount*.13f);
            selection.localScale=new Vector3(ringSize,1,ringSize);
            selection.localPosition=new Vector3(0,.04f+dragAmount*.012f,0);
            bool visibleCommander=Kind!="steve"||figure.gameObject.activeSelf;
            selection.gameObject.SetActive(!Dead&&visibleCommander&&selectionAmount>.02f);
            float teamSize=footprint*(Kind=="steve"&&LocalCommander?1.2f:1);
            teamRing.localScale=new Vector3(teamSize,1,teamSize);
            teamRing.gameObject.SetActive(!Dead&&visibleCommander);
            StepSkillFeedback(time,footprint);
            bool showHealth = Battle && hovered && !Dead && Kind != "steve";
            healthRoot.gameObject.SetActive(showHealth);
            if (showHealth && camera != null)
            {
                healthRoot.position = transform.position + Vector3.up * ((Height + modelHover) * Size + .23f);
                healthRoot.rotation = camera.transform.rotation;
                float amount = Mathf.Clamp01(MaximumHealth > 0 ? Health / MaximumHealth : 0);
                healthFill.localScale = new Vector3(.89f * amount, .063f, .025f);
                healthFill.localPosition = new Vector3(-.445f * (1 - amount), 0, -.045f);
                int color = amount > .55f ? 0 : amount > .25f ? 1 : 2;
                if (color != healthColor) { healthFillMesh.sharedMesh = color == 0 ? healthyMesh : color == 1 ? woundedMesh : criticalMesh; healthColor = color; }
            }
            StepArenaEventFeedback(time);
        }
    }
}
