using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        const int IceSegments=112,IceBands=4;

        void BuildFieldIce(FieldTerrainView view,TerrainState source,string name)
        {
            // Each river owns its mutable mesh. Cached shoreline/water meshes stay
            // immutable, including when another map or round reuses their geometry.
            int count=(IceSegments+1)*(IceBands+1)*2;
            view.iceVertices=new Vector3[count];view.iceColors=new Color[count];
            view.iceMesh=new Mesh{name=source.id+" ice advancing from fixed banks"};view.iceMesh.MarkDynamic();
            view.ice=FieldPart(name,view.root,view.iceMesh,new Vector3(0,.14f,0),true);
            view.iceRenderer=view.ice.GetComponent<MeshRenderer>();
            view.waterRenderer.sortingOrder=-30;view.iceRenderer.sortingOrder=-29;
            view.iceCrystals=FieldMesh("First frost along fixed riverbanks",view.root,"bank-frost-"+source.id+"-"+source.halfWidth,b=>{
                for(int n=0;n<30;n++){
                    float x=Mathf.Lerp(-source.halfWidth+12,source.halfWidth-12,n/29f);
                    float centre=source.shape=="river"?BattlefieldCatalog.RiverCentre(source,x):0;
                    float width=source.shape=="river"?BattlefieldCatalog.RiverWidth(source,x):source.halfHeight;
                    for(int side=-1;side<=1;side+=2){
                        Vector3 p=FieldAt(x,centre+side*width*.965f,.025f);bool bridge=false;
                        if(fieldState!=null)foreach(var t in fieldState.terrain)if(t.kind=="bridge"&&IceVertexOnBridge(p,source,t)){bridge=true;break;}
                        if(bridge)continue;
                        float size=.1f+(n%3)*.045f;
                        b.Box(p,new Vector3(size,.045f,size*1.7f),FieldColor("#EEFDFF"),Quaternion.Euler(0,n*47,0));
                        if(n%3==0)b.Box(p+new Vector3(0,.05f,0),new Vector3(.065f,.13f,.065f),FieldColor("#CBEFF8"),Quaternion.Euler(0,0,18));
                    }
                }
            },new Vector3(0,.15f,0),true);
            view.iceCrystals.gameObject.SetActive(false);
            RebuildFieldIce(view,source,0);view.ice.gameObject.SetActive(false);
        }

        void SyncFieldIceCycle(int round)
        {
            if(fieldMap==null||fieldState==null)return;
            string cycle=round+":"+fieldState.seed+":"+fieldState.weather;
            foreach(var view in fieldMap.terrain){
                if(view.iceMesh==null||view.iceCycle==cycle)continue;
                view.iceCycle=cycle;view.iceFraction=-1;
                // Apply the new round immediately, before its first rendered frame.
                if(view.state!=null)UpdateFieldIce(view,view.state,Time.unscaledTime,0);
            }
        }
        float FieldIceFraction(TerrainState terrain)
        {
            float amount=Mathf.Clamp01(terrain.freeze);
            if(amount>=1)return 1;
            if(fieldWeatherId=="snow"&&terrain.kind=="water"){
                // Server freeze starts at combat elapsed = 0. A small, explicitly
                // bank-bound first frost makes the climate reveal visible during
                // the long planning phase; the channel remains visibly open and
                // navigability still changes only at the server's freeze = 1.
                float preview=fieldPhase=="reveal"&&fieldRevealStage=="climate"?
                    .12f*FieldLayerProgress(fieldStageAge+Mathf.Max(0,Time.unscaledTime-fieldSnapshotTime),0,ArenaGame.RevealClimateSeconds):
                    fieldPhase=="tactics"||fieldPhase=="battle"||fieldPhase=="results"||fieldPhase=="finished"?.12f:0;
                if(fieldPhase=="battle")amount=Mathf.Min(.995f,amount+Mathf.Clamp(Time.unscaledTime-fieldSnapshotTime,0,.35f)/20);
                // Combat continues from the preview instead of clearing and replaying it.
                amount=preview+(1-preview)*amount;
            }
            return amount;
        }
        void UpdateFieldIce(FieldTerrainView view,TerrainState terrain,float time,int index)
        {
            float amount=FieldIceFraction(terrain);
            bool dry=fieldWeatherId=="heat"&&terrain.kind=="wetland";
            view.water.gameObject.SetActive(!dry&&amount<1);
            Color water=FieldColor(terrain.kind=="water"?"#6CA6C0":"#64998B");water.a=.68f;TintField(view.waterRenderer,water);
            // Once a bank freezes, the water stays below it. Bobbing through a
            // transparent ice sheet produced the old flickering double surface.
            view.water.localPosition=new Vector3(0,.105f+(amount<=.0001f?Mathf.Sin(time*.9f+index)*.006f:0),0);
            view.ice.localPosition=new Vector3(0,.14f,0);view.ice.localScale=Vector3.one;
            view.ice.gameObject.SetActive(!dry&&amount>.0001f);
            view.iceCrystals.gameObject.SetActive(!dry&&amount>.04f);
            if(Mathf.Abs(amount-view.iceFraction)>.0001f)RebuildFieldIce(view,terrain,amount);
            TintField(view.iceRenderer,new Color(1,1,1,.96f));
        }
        void RebuildFieldIce(FieldTerrainView view,TerrainState terrain,float amount)
        {
            view.iceFraction=amount;view.iceTriangles.Clear();
            int stride=IceBands+1,bankSize=(IceSegments+1)*stride;
            Color bank=FieldColor("#F0FBFE"),front=FieldColor("#9ACBD9");
            for(int side=0;side<2;side++)for(int col=0;col<=IceSegments;col++){
                float x=Mathf.Lerp(-terrain.halfWidth,terrain.halfWidth,col/(float)IceSegments);
                float centre=terrain.shape=="river"?BattlefieldCatalog.RiverCentre(terrain,x):0;
                float width=terrain.shape=="river"?BattlefieldCatalog.RiverWidth(terrain,x):terrain.halfHeight;
                for(int row=0;row<=IceBands;row++){
                    int p=side*bankSize+col*stride+row;
                    float across=width*(1-amount*row/IceBands)*(side==0?-1:1);
                    view.iceVertices[p]=FieldAt(x,centre+across,0);
                    float grain=.96f+.035f*Mathf.Sin(col*.71f+row*1.3f);
                    view.iceColors[p]=Color.Lerp(bank,front,row/(float)IceBands)*grain;
                }
            }
            for(int side=0;side<2;side++)for(int col=0;col<IceSegments;col++)for(int row=0;row<IceBands;row++){
                int p=side*bankSize+col*stride+row;
                if(side==0){AddIceTriangle(view,terrain,p,p+1,p+stride);AddIceTriangle(view,terrain,p+1,p+stride+1,p+stride);}
                else {AddIceTriangle(view,terrain,p,p+stride,p+1);AddIceTriangle(view,terrain,p+1,p+stride,p+stride+1);}
            }
            view.iceMesh.SetVertices(view.iceVertices);view.iceMesh.SetColors(view.iceColors);
            view.iceMesh.SetTriangles(view.iceTriangles,0);view.iceMesh.RecalculateNormals();view.iceMesh.RecalculateBounds();
        }
        void AddIceTriangle(FieldTerrainView view,TerrainState water,int a,int b,int c)
        {
            // Keep the sheet out of bridge ramps, including their lowest edges.
            // This is checked against the same authored footprints as navigation.
            if(fieldState!=null)foreach(var bridge in fieldState.terrain){
                if(bridge.kind!="bridge")continue;
                if(IceVertexOnBridge(view.iceVertices[a],water,bridge)||IceVertexOnBridge(view.iceVertices[b],water,bridge)||IceVertexOnBridge(view.iceVertices[c],water,bridge))return;
            }
            view.iceTriangles.Add(a);view.iceTriangles.Add(b);view.iceTriangles.Add(c);
        }
        static bool IceVertexOnBridge(Vector3 point,TerrainState water,TerrainState bridge)
        {
            Point2 at=BattlefieldCatalog.TerrainPoint(water,point.x/WorldScale,point.z/WorldScale);
            return BattlefieldCatalog.Contains(bridge,at.x,at.y);
        }
    }
}
