using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // One retained ground mesh gives each map a continuous material composition.
        // Small detail batches sit on that landscape; they do not manufacture its shape.
        Transform BuildLandscapeFloor(FieldMapView map,BattlefieldState source)
        {
            string key=map.geometryKey+"-continuous-landscape";Mesh mesh;
            if(!fieldMeshes.TryGetValue(key,out mesh)){
                var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
                const int rings=64,segments=192;
                float radius=LandscapeRadius(map.scale),sector=Mathf.PI*2/64;
                for(int row=0;row<=rings;row++)for(int col=0;col<=segments;col++){
                    float a=col*Mathf.PI*2/segments;
                    // A 64-sided disc follows the arena parapet. Its inscribed radius
                    // still covers every corner of the authoritative playable square.
                    float rim=radius*Mathf.Cos(sector*.5f)/Mathf.Cos(Mathf.Repeat(a,sector)-sector*.5f);
                    float r=rim*row/rings,x=Mathf.Cos(a)*r/WorldScale,y=Mathf.Sin(a)*r/WorldScale;
                    // The top is flat outside authored highlands. The same exact ramp
                    // meshes that own game heights remain authoritative for picking.
                    vertices.Add(FieldAt(x,y,.055f));colors.Add(LandscapeGroundColor(map.id,source,x,y));
                    if(col<segments&&row<rings){int p=row*(segments+1)+col;triangles.Add(p);triangles.Add(p+1);triangles.Add(p+segments+1);triangles.Add(p+1);triangles.Add(p+segments+2);triangles.Add(p+segments+1);}
                }
                // A shallow retaining rim joins the disc to the stone foundation;
                // its circular edge must not look like a paper sheet over the old
                // octagon at the diagonals. Duplicate vertices keep a crisp lip.
                int skirt=vertices.Count;
                for(int col=0;col<=segments;col++){
                    Vector3 edge=vertices[rings*(segments+1)+col];Color ground=colors[rings*(segments+1)+col];
                    vertices.Add(edge);colors.Add(ground*.7f);
                    vertices.Add(new Vector3(edge.x,-.47f,edge.z));colors.Add(ground*.48f);
                    if(col<segments){int p=skirt+col*2;triangles.Add(p);triangles.Add(p+2);triangles.Add(p+1);triangles.Add(p+1);triangles.Add(p+2);triangles.Add(p+3);}
                }
                mesh=FinishLandscapeMesh(key,vertices,colors,triangles);fieldMeshes.Add(key,mesh);
            }
            map.floorMesh=mesh;map.floorVertices=mesh.vertices;
            map.revealFloorVertices=new Vector3[map.floorVertices.Length];
            map.revealFloorMesh=UnityEngine.Object.Instantiate(mesh);map.revealFloorMesh.name=key+" radial emergence";map.revealFloorMesh.MarkDynamic();
            return FieldPart("Continuous biome ground and connected paths",map.root,mesh,Vector3.zero);
        }

        void UpdateLandscapeExpansion(float spread)
        {
            // Uncover land at its authored world position. Scaling the complete
            // coloured ground made paths and army plots appear to shrink together
            // as the camera pulled out, hiding the actual outward expansion.
            if(fieldMap==null||Mathf.Abs(fieldMap.floorSpread-spread)<.00001f)return;
            fieldMap.floorSpread=spread;
            MeshFilter filter=fieldMap.floor.GetComponent<MeshFilter>();
            if(spread>=.99999f){filter.sharedMesh=fieldMap.floorMesh;return;}
            float radius=LandscapeRadius(fieldMap.scale)*spread,sector=Mathf.PI*2/64;
            for(int i=0;i<fieldMap.floorVertices.Length;i++){
                Vector3 point=fieldMap.floorVertices[i];float r=new Vector2(point.x,point.z).magnitude;
                if(r>.00001f){float a=Mathf.Atan2(point.z,point.x);float edge=radius*Mathf.Cos(sector*.5f)/Mathf.Cos(Mathf.Repeat(a,sector)-sector*.5f);float clamp=Mathf.Min(1,edge/r);point.x*=clamp;point.z*=clamp;}
                fieldMap.revealFloorVertices[i]=point;
            }
            fieldMap.revealFloorMesh.SetVertices(fieldMap.revealFloorVertices);
            filter.sharedMesh=fieldMap.revealFloorMesh;
        }

        static float LandscapeRadius(float scale){return 23.9f*scale;}

        static float LandscapeFalloff(float inner,float outer,float distance)
        {return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(inner,outer,distance));}
        float LandscapePathAmount(string map,float x,float y)
        {
            if(map=="courtyards")return Mathf.Max(LandscapeFalloff(35,90,Mathf.Min(Mathf.Abs(x),Mathf.Abs(y))),.7f*LandscapeFalloff(12,55,Mathf.Abs(Mathf.Sqrt(x*x+y*y)-580)));
            float trail=map=="bog"?Mathf.Abs(y+205*Mathf.Sin(x/750*Mathf.PI)):Mathf.Abs(y-x*.53f);
            float spine=.9f*LandscapeFalloff(20,map=="bog"?72:64,trail);
            // Wide, worn approaches connect the four deployment clearings to the
            // centre and cross the narrower resource trail, rather than ending in grass.
            float diagonal=Mathf.Min(Mathf.Abs(y-x*1.245f),Mathf.Abs(y+x*1.245f));
            return Mathf.Max(spine,.48f*LandscapeFalloff(18,68,diagonal));
        }
        Color LandscapeGroundColor(string map,BattlefieldState source,float x,float y)
        {
            bool frost=map=="frost",bog=map=="bog";
            Color baseColor=FieldColor(frost?"#A9C0B8":bog?"#71805D":"#91957A");
            Color pathColor=FieldColor(frost?"#D2DFD0":bog?"#A29B72":"#C9BF9A");
            float organic=.5f+.23f*Mathf.Sin(x*.008f+y*.006f)+.18f*Mathf.Sin(y*.017f-x*.004f)+.07f*Mathf.Sin(x*.031f+y*.027f);
            Color color=Color.Lerp(baseColor*.78f,baseColor*1.13f,organic);
            foreach(var terrain in source.terrain){
                if(terrain.shape=="river"){
                    float along=x-terrain.x,across=y-terrain.y-BattlefieldCatalog.RiverCentre(terrain,along),width=BattlefieldCatalog.RiverWidth(terrain,along);
                    float bank=LandscapeFalloff(width+10,width+115,Mathf.Abs(across))*LandscapeFalloff(terrain.halfWidth-5,terrain.halfWidth+45,Mathf.Abs(along));
                    color=Color.Lerp(color,FieldColor(frost?"#C6D6CD":"#889174"),bank*.48f);
                }else if(terrain.shape=="ellipse"){
                    float a=-terrain.rotation*Mathf.Deg2Rad,dx=x-terrain.x,dy=y-terrain.y;
                    float lx=(dx*Mathf.Cos(a)-dy*Mathf.Sin(a))/terrain.halfWidth,ly=(dx*Mathf.Sin(a)+dy*Mathf.Cos(a))/terrain.halfHeight;
                    float angle=Mathf.Atan2(ly,lx),r=Mathf.Sqrt(lx*lx+ly*ly);
                    // The tactical boundary remains the authoritative ellipse;
                    // material and sparse ground growth blend naturally beyond it.
                    float edge=1+.055f*Mathf.Sin(angle*5+terrain.x*.01f)+.04f*Mathf.Sin(angle*9-terrain.y*.02f);
                    float forest=LandscapeFalloff(.52f,1.34f,r/edge);
                    color=Color.Lerp(color,FieldColor(frost?"#91ADA0":"#58734C"),forest*.64f);
                }
            }
            return Color.Lerp(color,pathColor,LandscapePathAmount(map,x,y));
        }
        static int LandscapeSeed(string id)
        {unchecked{int value=197;foreach(char c in id)value=value*31+c;return value&int.MaxValue;}}

        static Mesh FinishLandscapeMesh(string key,List<Vector3> vertices,List<Color> colors,List<int> triangles)
        {
            var mesh=new Mesh{name=key};if(vertices.Count>65000)mesh.indexFormat=IndexFormat.UInt32;
            mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        Mesh FieldShapeSurface(string key,TerrainState terrain,float margin,float height,Color color)
        {
            Mesh mesh;if(fieldMeshes.TryGetValue(key,out mesh))return mesh;
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            if(terrain.shape=="river"){
                const int segments=112,bands=6;
                for(int col=0;col<=segments;col++){
                    float x=Mathf.Lerp(-terrain.halfWidth,terrain.halfWidth,col/(float)segments),center=BattlefieldCatalog.RiverCentre(terrain,x),half=BattlefieldCatalog.RiverWidth(terrain,x)*margin;
                    for(int row=0;row<=bands;row++){
                        vertices.Add(FieldAt(x,center+Mathf.Lerp(-half,half,row/(float)bands),height));
                        colors.Add(color*(.94f+.06f*Mathf.Sin(col*.18f+row*.3f)));
                        if(col<segments&&row<bands){int p=col*(bands+1)+row;triangles.Add(p);triangles.Add(p+1);triangles.Add(p+bands+1);triangles.Add(p+1);triangles.Add(p+bands+2);triangles.Add(p+bands+1);}
                    }
                }
            }else{
                const int segments=80,rings=5;
                for(int ring=0;ring<=rings;ring++)for(int n=0;n<=segments;n++){
                    float a=n*Mathf.PI*2/segments,r=ring/(float)rings;
                    vertices.Add(FieldAt(Mathf.Cos(a)*terrain.halfWidth*margin*r,Mathf.Sin(a)*terrain.halfHeight*margin*r,height));
                    colors.Add(color*(.94f+.07f*r));
                    if(ring<rings&&n<segments){int p=ring*(segments+1)+n;triangles.Add(p);triangles.Add(p+1);triangles.Add(p+segments+1);triangles.Add(p+1);triangles.Add(p+segments+2);triangles.Add(p+segments+1);}
                }
            }
            mesh=FinishLandscapeMesh(key,vertices,colors,triangles);fieldMeshes.Add(key,mesh);return mesh;
        }
        bool BuildNaturalFieldTerrain(FieldMapView map,BattlefieldState source,TerrainState t,FieldTerrainView view,string key)
        {
            if(t.shape!="river"&&t.shape!="ellipse")return false;
            bool frost=map.id=="frost";
            view.root.localRotation=Quaternion.Euler(0,-t.rotation,0);
            if(t.kind=="wetland"||t.kind=="water"){
                FieldPart("Continuous curved shoreline",view.root,FieldShapeSurface(key+"-shore",t,1.13f,.069f,FieldColor(frost?"#B5CECC":"#8B9370")),Vector3.zero);
                FieldPart("Connected riverbed",view.root,FieldShapeSurface(key+"-bed",t,1,.078f,FieldColor(frost?"#557D87":"#4C634D")),Vector3.zero);
                view.water=FieldPart("Continuous translucent river",view.root,FieldShapeSurface(key+"-water",t,1,0,Color.white),new Vector3(0,.105f,0),true);
                view.waterRenderer=view.water.GetComponent<MeshRenderer>();
                BuildFieldIce(view,t,"Continuous freeze route");
                FieldMesh("Riverbank reeds and worn shore strata",view.root,key+"-banks",b=>{
                    for(int n=0;n<=92;n++){
                        if(n%5==3||n%5==4)continue;
                        float x=Mathf.Lerp(-t.halfWidth,t.halfWidth,n/92f)+Mathf.Sin(n*7.3f)*4,center=BattlefieldCatalog.RiverCentre(t,x),half=BattlefieldCatalog.RiverWidth(t,x);
                        foreach(int sign in new[]{-1,1}){
                            Vector3 p=FieldAt(x,center+sign*(half+9+Mathf.Sin(n*1.7f)*8),.09f);
                            float width=.2f+(n%4)*.075f;
                            b.Box(p,new Vector3(width,.07f,width*.7f),FieldColor(frost?"#C9DCD3":"#7C8A68"),Quaternion.Euler(0,n*11,0));
                            if(!frost&&n%3!=0){float h=.2f+(n%4)*.07f;b.Box(p+Vector3.up*h*.5f,new Vector3(.035f,h,.035f),FieldColor("#9CA570"));}
                            if(frost&&n%4==0)b.Box(p+new Vector3(.16f,.035f,.08f),new Vector3(.6f,.12f,.35f),FieldColor("#E2ECE4"));
                        }
                    }
                },Vector3.zero);
                return true;
            }
            if(t.kind=="forest"){
                FieldMesh("Grouped woodland silhouettes with open interior",view.root,key+"-woodland",b=>{
                    var random=new System.Random(LandscapeSeed(t.id));
                    int treeCount=t.halfWidth*t.halfHeight>20000?29:19;
                    for(int n=0;n<treeCount;n++){
                        float cx=n%3==0?-.47f:n%3==1?.34f:.18f,cy=n%3==0?.14f:n%3==1?-.41f:.55f;
                        float lx=cx+((float)random.NextDouble()-.5f)*.61f,ly=cy+((float)random.NextDouble()-.5f)*.59f;
                        float norm=Mathf.Sqrt(lx*lx+ly*ly);if(norm>.92f){lx*=.92f/norm;ly*=.92f/norm;}
                        Point2 at=BattlefieldCatalog.TerrainPoint(t,lx*t.halfWidth,ly*t.halfHeight);
                        if(LandscapePathAmount(map.id,at.x,at.y)>.71f)continue;
                        float x=lx*t.halfWidth*WorldScale,z=ly*t.halfHeight*WorldScale;
                        float h=1.35f+(float)random.NextDouble()*1.23f;
                        // Trunks are thin and deliberately leave the troop level open.
                        b.Box(new Vector3(x,h*.38f,z),new Vector3(.13f,h*.76f,.13f),FieldColor(frost?"#667369":"#68583F"));
                        for(int tier=0;tier<3;tier++){
                            float width=(.83f+(n%4)*.13f)-tier*.21f,top=h*.62f+tier*.31f;
                            b.Box(new Vector3(x,top,z),new Vector3(width,.38f,width*.9f),FieldColor(frost?(tier==2?"#C5DCD0":"#527969"):(tier==2?"#8DA869":"#456B42")));
                            if(frost)b.Box(new Vector3(x,top+.22f,z),new Vector3(width*.96f,.055f,width*.88f),FieldColor("#E4F0E5"));
                        }
                    }
                    for(int n=0;n<87;n++){
                        float a=(float)random.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)random.NextDouble())*1.14f;
                        float x=Mathf.Cos(a)*t.halfWidth*r,y=Mathf.Sin(a)*t.halfHeight*r;
                        Point2 at=BattlefieldCatalog.TerrainPoint(t,x,y);if(LandscapePathAmount(map.id,at.x,at.y)>.6f)continue;
                        Vector3 p=FieldAt(x,y,.094f);
                        float w=.12f+(float)random.NextDouble()*.23f;
                        b.Box(p,new Vector3(w,.035f,w*1.7f),FieldColor(frost?"#C8DBCE":n%2==0?"#81955F":"#6B8050"),Quaternion.Euler(0,n*31,0));
                        if(!frost&&n%3==0)b.Box(p+Vector3.up*.065f,new Vector3(.035f,.15f,.035f),FieldColor("#A4AD74"));
                    }
                },Vector3.zero);
                view.fire=FieldMesh("Bounded woodland fire embers",view.root,key+"-fire",b=>{
                    for(int n=0;n<16;n++){float a=n*2.39996f,r=Mathf.Sqrt((n+.5f)/16f)*.9f;b.Box(FieldAt(Mathf.Cos(a)*t.halfWidth*r,Mathf.Sin(a)*t.halfHeight*r,.28f),new Vector3(.16f,.46f,.16f),FieldColor(n%2==0?"#FFD58B":"#EE9356"));}
                },Vector3.zero,true);view.fireRenderer=view.fire.GetComponent<MeshRenderer>();view.fire.gameObject.SetActive(false);
                return true;
            }
            return false;
        }

        Mesh FieldChasmSurface(string key,TerrainState t)
        {
            Mesh mesh;if(fieldMeshes.TryGetValue(key,out mesh))return mesh;
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            const int radial=16,angular=48;
            for(int quadrant=0;quadrant<4;quadrant++){
                int start=vertices.Count;
                for(int row=0;row<=radial;row++){
                    float r=Mathf.Lerp(t.halfWidth,t.radius,row/(float)radial);
                    float end=Mathf.Asin(Mathf.Clamp01(t.halfHeight/r));
                    for(int n=0;n<=angular;n++){
                        float a=Mathf.Lerp(end,Mathf.PI*.5f-end,n/(float)angular)+quadrant*Mathf.PI*.5f;
                        vertices.Add(FieldAt(Mathf.Cos(a)*r,Mathf.Sin(a)*r,.081f));
                        colors.Add(FieldColor("#27383D")*(.82f+.18f*row/radial));
                        if(row<radial&&n<angular){int p=start+row*(angular+1)+n;triangles.Add(p);triangles.Add(p+1);triangles.Add(p+angular+1);triangles.Add(p+1);triangles.Add(p+angular+2);triangles.Add(p+angular+1);}
                    }
                }
            }
            mesh=FinishLandscapeMesh(key,vertices,colors,triangles);fieldMeshes.Add(key,mesh);return mesh;
        }

        void BuildGrandLandscape(FieldMapView map,BattlefieldState source)
        {
            Color stone=FieldColor(map.id=="frost"?"#91A9AC":map.id=="bog"?"#7D896B":"#A7A58E");
            FieldMesh("Authored landmark skyline outside playable boundary",map.environment,map.geometryKey+"-skyline",b=>{
                float edge=(BattlefieldCatalog.Extent(source)+95)*WorldScale;
                for(int side=0;side<4;side++){
                    Quaternion turn=Quaternion.Euler(0,side*90,0);
                    for(int sign=-1;sign<=1;sign+=2){
                        Vector3 baseAt=turn*new Vector3(sign*12,0,edge);
                        if(map.id=="courtyards"){
                            for(int step=0;step<4;step++)b.Box(baseAt+Vector3.up*(step*.43f),new Vector3(5.8f-step*.68f,.46f,4.3f-step*.45f),stone*(.75f+step*.055f),turn);
                            b.Box(baseAt+Vector3.up*4.2f,new Vector3(1.6f,5.6f,1.6f),stone,turn);
                            b.Box(baseAt+Vector3.up*7.1f,new Vector3(2.3f,.42f,2.3f),stone*1.13f,turn);
                            b.Box(baseAt+Vector3.up*7.55f,new Vector3(.68f,.58f,.68f),FieldColor("#CFBD8E"),turn);
                        }else{
                            for(int step=0;step<5;step++){
                                float w=8.4f-step*1.24f,h=map.id=="frost"?1.12f:.68f;
                                b.Box(baseAt+new Vector3(sign*step*.22f,step*h*.8f,0),new Vector3(w,h,5.8f-step*.6f),stone*(.72f+step*.055f),turn);
                                if(map.id=="frost")b.Box(baseAt+new Vector3(sign*step*.22f,step*h*.8f+h*.51f,0),new Vector3(w*.97f,.14f,(5.8f-step*.6f)*.96f),FieldColor("#D5E5DD"),turn);
                            }
                            if(map.id=="bog"){
                                Vector3 p=baseAt+Vector3.up*2.6f;
                                for(int n=0;n<3;n++)b.Box(p+turn*new Vector3((n-1)*1.8f,0,.2f),new Vector3(.28f,3.6f,.28f),FieldColor("#756341"),turn);
                                b.Box(p+Vector3.up*1.9f,new Vector3(5,.28f,.42f),FieldColor("#8B7551"),turn);
                            }
                        }
                    }
                }
            },Vector3.zero);
            if(map.id=="courtyards"){
                FieldMesh("Four sunken ceremonial pylons in the unwalkable moat",map.environment,map.geometryKey+"-pylons",b=>{
                    for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2){
                        Vector3 p=FieldAt(sx*330,sz*330);
                        b.Box(p+Vector3.up*.16f,new Vector3(2.8f,.3f,2.8f),stone*.7f);
                        b.Box(p+Vector3.up*1.75f,new Vector3(1.05f,3.25f,1.05f),stone*.94f);
                        b.Box(p+Vector3.up*3.43f,new Vector3(1.65f,.25f,1.65f),stone*1.12f);
                        b.Box(p+Vector3.up*3.8f,new Vector3(.62f,.6f,.62f),FieldColor("#C6B581"),Quaternion.Euler(0,45,0));
                    }
                },Vector3.zero);
                FieldMesh("Ceremonial compass inlays follow the actual plateau",map.environment,map.geometryKey+"-compass",b=>{
                    for(int side=0;side<4;side++)for(int n=0;n<18;n++){
                        Point2 p=ArenaGame.LocalToWorld(side,0,24+n*10);
                        float height=BattlefieldCatalog.HeightAt(source,p.x,p.y);
                        b.Box(FieldAt(p.x,p.y,height+.073f),new Vector3(.38f,.012f,.18f),FieldColor(n%3==0?"#D6C48E":"#B9AF8D"),Quaternion.Euler(0,side*90,0));
                    }
                },Vector3.zero);
            }else{
                FieldMesh("Authored mining trails and ancient approach slabs",map.environment,map.geometryKey+"-approaches",b=>{
                    foreach(var objective in source.objectives){
                        if(objective.kind=="tower"||objective.kind=="beacon")continue;
                        for(int n=0;n<48;n++){
                            float u=n/48f,x=Mathf.Lerp(objective.x,0,u),y=Mathf.Lerp(objective.y,0,u)+Mathf.Sin(u*Mathf.PI)*48*Mathf.Sign(objective.x);
                            if(!BattlefieldCatalog.IsPassable(source,x,y,false,false,10))continue;
                            float height=BattlefieldCatalog.HeightAt(source,x,y);
                            Vector3 p=FieldAt(x,y,height+.078f);
                            if(map.id=="bog"){
                                float next=(n+1)/48f,nx=Mathf.Lerp(objective.x,0,next),ny=Mathf.Lerp(objective.y,0,next)+Mathf.Sin(next*Mathf.PI)*48*Mathf.Sign(objective.x);
                                if(!BattlefieldCatalog.IsPassable(source,nx,ny,false,false,10))continue;
                                Vector3 end=FieldAt(nx,ny,BattlefieldCatalog.HeightAt(source,nx,ny)+.078f),delta=end-p;
                                Quaternion q=Quaternion.LookRotation(delta.normalized,Vector3.up);
                                if(n%2==0)b.Box(p,new Vector3(.83f,.026f,.14f),FieldColor("#8B7952"),q);
                                foreach(int sign in new[]{-1,1})b.Box((p+end)*.5f+q*new Vector3(sign*.28f,.022f,0),new Vector3(.045f,.035f,delta.magnitude+.025f),FieldColor("#7A8172"),q);
                            }else if(n%2==0)b.Box(p,new Vector3(.65f,.035f,.35f),FieldColor(n%3==0?"#AFC6C4":"#BFD3CC"),Quaternion.Euler(0,n%3*9,0));
                        }
                    }
                },Vector3.zero);
            }
        }
    }
}

