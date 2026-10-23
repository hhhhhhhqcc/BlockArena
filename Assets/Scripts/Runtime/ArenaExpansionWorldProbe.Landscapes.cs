using System.Collections;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaExpansionWorldProbe
    {
        Transform MapRoot(string map)
        {return app.World.transform.Find("Arena scenery/Emerging battlefields/Map "+map);}

        void VerifyFoundationHidden(GameState source)
        {
            Transform root=MapRoot(source.battlefield.mapId);
            Require(root!=null,"retained landscape root exists during expansion");
            foreach(Transform child in root){
                if(child.name=="Continuous biome ground and connected paths"||child.name=="Expanding map emergence front")continue;
                Require(!child.gameObject.activeInHierarchy,"foundation expands before any terrain, forest, backdrop or objective is exposed");
            }
        }
        IEnumerator VerifyLayeredEmergence(GameState reveal)
        {
            string map=reveal.battlefield.mapId;
            reveal.revealStageAge=1.27f;reveal.battlefield.revealAge=1.27f;Show(reveal);yield return Settle(.04f);
            Require(Mathf.Abs(app.World.ExpansionScale-2)<.001f,"foundation reaches full size before terrain emerges");
            Transform root=MapRoot(map);
            Require(!root.Find("Retained biome details").gameObject.activeInHierarchy,"scenery remains underground when foundation locks");
            Capture("map-"+map+"-01b-foundation");
            reveal.revealStageAge=2.2f;reveal.battlefield.revealAge=2.2f;Show(reveal);yield return Settle(.04f);
            int rising=0;
            foreach(var terrain in reveal.battlefield.terrain){
                Transform layer=root.Find(terrain.kind+" "+terrain.id);
                Require(layer!=null&&(layer.localScale-Vector3.one).sqrMagnitude<.000001f,"emerging terrain retains its authored proportions instead of stretching vertically");
                if(layer.gameObject.activeInHierarchy&&layer.localPosition.y<-.01f)rising++;
            }
            Require(rising>0,"terrain actually translates upward from below the foundation");
            foreach(var objective in reveal.battlefield.objectives)
                Require(!root.Find(objective.name).gameObject.activeInHierarchy,"resources appear after the walkable terrain");
            Capture("map-"+map+"-01c-terrain");
            reveal.revealStageAge=3.5f;reveal.battlefield.revealAge=3.5f;Show(reveal);yield return Settle(.04f);
            Require(root.Find("Retained biome details").gameObject.activeInHierarchy,"forest detail and landmarks rise after the terrain");
            foreach(var objective in reveal.battlefield.objectives){
                Transform layer=root.Find(objective.name);
                Require(layer.gameObject.activeInHierarchy&&(layer.localScale-Vector3.one).sqrMagnitude<.000001f&&layer.localPosition.y<BattlefieldCatalog.HeightAt(reveal.battlefield,objective.x,objective.y)+.1f,
                    "late resources rise from underground at full model scale");
            }
            Capture("map-"+map+"-01d-scenery");
        }
        void VerifyCircularFloor(GameState source)
        {
            Transform floor=MapRoot(source.battlefield.mapId).Find("Continuous biome ground and connected paths");
            Vector3[] vertices=floor.GetComponent<MeshFilter>().sharedMesh.vertices;
            float radius=23.9f*source.battlefield.scale,outer=0;
            var sectors=new bool[64];
            foreach(Vector3 vertex in vertices){
                float r=new Vector2(vertex.x,vertex.z).magnitude;outer=Mathf.Max(outer,r);
                Require(r<=radius+.002f,"continuous floor stays inside its circular parapet rather than leaking square corners");
                if(r>radius*.998f){int side=Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(vertex.z,vertex.x),Mathf.PI*2)/(Mathf.PI*2)*64)%64;sectors[side]=true;}
            }
            Require(Mathf.Abs(outer-radius)<.002f,"landscape reaches the designed arena rim");
            foreach(bool covered in sectors)Require(covered,"circular ground reaches the arena edge in every direction");
            float corner=Mathf.Sqrt(2)*BattlefieldCatalog.Extent(source.battlefield)/25;
            Require(corner<radius*Mathf.Cos(Mathf.PI/64),"all authoritative playable corners remain on the circular ground");
        }
        IEnumerator VerifyContinuousExpansion()
        {
            var source=AuthoritativeFixture("courtyards","clear",93);Show(source);app.World.ResetView();yield return Settle(.8f);
            Transform ground=MapRoot("courtyards").Find("Continuous biome ground and connected paths");
            Vector3[] authored=ground.GetComponent<MeshFilter>().sharedMesh.vertices;int anchor=-1;
            for(int n=0;n<authored.Length;n++){float r=new Vector2(authored[n].x,authored[n].z).magnitude;if(r>10&&r<11){anchor=n;break;}}
            Require(anchor>=0,"continuous growth samples an actual authored inner-ground vertex");
            var reveal=Clone(source);reveal.phase="reveal";reveal.revealStage="map";reveal.revealStageAge=0;reveal.battlefield.revealAge=0;
            Show(reveal);yield return Settle(1.1f);
            reveal.revealStage="terrain";Show(reveal);yield return Settle(.02f);
            float previousRadius=0,initialProjection=0,maxProjection=0;float started=Time.realtimeSinceStartup;
            foreach(float at in new[]{.08f,.34f,.62f,.92f,1.22f,1.5f,2.3f,3.6f,4.15f}){
                while(Time.realtimeSinceStartup<started+at)yield return null;
                yield return new WaitForEndOfFrame();
                float renderedAge=Time.realtimeSinceStartup-started;
                Vector3[] actual=ground.GetComponent<MeshFilter>().sharedMesh.vertices;float radius=0;
                foreach(Vector3 point in actual)radius=Mathf.Max(radius,new Vector2(point.x,point.z).magnitude);
                Require(radius>=previousRadius-.001f,"continuous terrain growth advances outward without retracting its perimeter");previousRadius=radius;
                Require(Vector3.Distance(actual[anchor],authored[anchor])<.0001f,"inner ground remains at its original world coordinates while new perimeter land appears");
                Require((ground.localScale-Vector3.one).sqrMagnitude<.000001f,"radial ground emergence never scales existing path or territory geometry");
                var own=source.players.Find(p=>p.id==app.PlayerId);
                Transform terrace=app.World.transform.Find("Arena scenery/Team "+own.slot+" independent preparation terrace");
                Require((terrace.localScale-Vector3.one).sqrMagnitude<.000001f&&Mathf.Abs(terrace.TransformVector(Vector3.right).magnitude-1)<.0001f,
                    "the local deployment plot retains its original world size throughout camera pullback");
                Transform outline=terrace.Find("Your army territory outline");
                Require(outline!=null&&outline.gameObject.activeInHierarchy&&outline.Find("Your army locating pennant").gameObject.activeInHierarchy,
                    "the randomly relocated local plot stays identifiable by its border and commander marker");
                Camera camera=app.World.ViewCamera;
                Vector3 right=camera.transform.right;right.y=0;right.Normalize();
                Vector3 centre=camera.WorldToScreenPoint(Vector3.zero),edge=camera.WorldToScreenPoint(right*radius);
                float projection=Vector2.Distance(centre,edge);
                if(initialProjection==0)initialProjection=projection;maxProjection=Mathf.Max(maxProjection,projection);
                if(renderedAge<=1.45f)Require(app.World.ExpansionCameraScale<app.World.ExpansionScale-.005f,"camera pullback trails visible foundation expansion instead of cancelling it");
                Capture("continuous-growth-"+Mathf.RoundToInt(at*100));
            }
            Require(maxProjection>initialProjection*1.12f,"the growing arena visibly expands on screen before the camera completes its pullback");
            Require(Mathf.Abs(previousRadius-47.8f)<.003f,"continuous radial reveal reaches the complete circular arena");
            Show(source);yield return Settle(.65f);VerifyOverview(source);VerifyGroundPicking(source);
        }
        IEnumerator VerifyProgressiveIce()
        {
            var source=AuthoritativeFixture("frost","snow",91);source.phase="tactics";
            TerrainState river=source.battlefield.terrain.Find(t=>t.shape=="river"&&t.kind=="water");
            Require(river!=null,"native freeze regression uses the authored curved river");
            float span=-1,previousFront=float.PositiveInfinity;
            foreach(float freeze in new[]{0f,.25f,.5f,.75f,1f}){
                river.freeze=freeze;Show(source);yield return Settle(.04f);
                Transform riverRoot=MapRoot("frost").Find("water "+river.id),ice=riverRoot.Find("Continuous freeze route"),water=riverRoot.Find("Continuous translucent river");
                Require(ice!=null&&water!=null,"river uses separate retained water and ice renderers");
                Require((ice.localScale-Vector3.one).sqrMagnitude<.000001f,"freeze never rescales or recentres the whole curved river");
                Mesh mesh=ice.GetComponent<MeshFilter>().sharedMesh;Vector3[] vertices=mesh.vertices;
                float min=float.PositiveInfinity,max=float.NegativeInfinity,nearest=float.PositiveInfinity;
                foreach(Vector3 vertex in vertices){
                    float x=vertex.x*25,y=vertex.z*25,centre=BattlefieldCatalog.RiverCentre(river,x),width=BattlefieldCatalog.RiverWidth(river,x);
                    Require(Mathf.Abs(y-centre)<=width+.003f,"ice vertices remain inside the authoritative curved river banks");
                    float distance=Mathf.Abs(y-centre)/width;nearest=Mathf.Min(nearest,distance);
                    min=Mathf.Min(min,x);max=Mathf.Max(max,x);
                }
                if(span<0)span=max-min;
                Require(Mathf.Abs(max-min-span)<.003f,"curved river retains its full length at every freezing stage");
                float visibleFreeze=.12f+.88f*freeze;
                Require(nearest<=previousFront+.0001f&&Mathf.Abs(nearest-(1-visibleFreeze))<.0002f,"ice fronts advance monotonically from first frost to the original centreline");previousFront=nearest;
                Require(water.gameObject.activeSelf==(freeze<1),"fully frozen water hides the overlapping transparent water surface");
                Require(ice.gameObject.activeSelf,"snowfall leaves a small visible bank frost before the authoritative full freeze");
                Require(ice.GetComponent<Renderer>().sortingOrder>water.GetComponent<Renderer>().sortingOrder,"ice and water have an explicit stable transparency order");
                int[] triangles=mesh.triangles;
                for(int n=0;n<triangles.Length;n++){
                    Vector3 vertex=riverRoot.TransformPoint(vertices[triangles[n]]);
                    foreach(var bridge in source.battlefield.terrain)if(bridge.kind=="bridge")
                        Require(!BattlefieldCatalog.Contains(bridge,vertex.x*25,vertex.z*25),"rendered ice triangles do not overlap bridge ramps");
                }
                Capture("river-ice-"+Mathf.RoundToInt(freeze*100));
            }
            // Reusing a retained map on a new round must not retain its old full ice.
            source.round++;river.freeze=0;source.phase="reveal";source.revealStage="map";source.revealStageAge=0;Show(source);yield return Settle(.04f);
            Transform reset=MapRoot("frost").Find("water "+river.id);
            Require(!reset.Find("Continuous freeze route").gameObject.activeSelf&&reset.Find("Continuous translucent river").gameObject.activeSelf,
                "a new round resets cached ice and restores the water immediately");
            source.phase="tactics";source.revealStage=null;source.battlefield.weather="fog";river.freeze=0;Show(source);yield return Settle(.04f);
            Require(!reset.Find("Continuous freeze route").gameObject.activeSelf,"weather changes do not inherit a previous snow round's ice");
            VerifyGrazingRidge();
            VerifyGroundPicking(source);
        }
        void VerifyGrazingRidge()
        {
            Camera camera=app.World.ViewCamera;Vector3 oldPosition=camera.transform.position;Quaternion oldRotation=camera.transform.rotation;
            float oldSize=camera.orthographicSize;int silhouettes=0,visibleSlopes=0;
            try{
                camera.orthographicSize=38.145695f;
                // Reproduce the reported near-cardinal view, then keep a small set
                // of fixed subpixel variants. This exposes float round-trip changes
                // without relying on an animation happening to stop at this angle.
                foreach(float yaw in new[]{180.4235f,180.42f,180.423f,180.424f,180.425f,180.427f,180.43f,180.435f}){
                    camera.transform.SetPositionAndRotation(new Vector3(.517390f,62.2f,69.998090f),Quaternion.Euler(41.531770f,yaw,0));
                    Vector3 crease=app.World.WorldSurfacePoint(192,227);Point2 hit;
                    bool picked=app.World.TryGround(camera.WorldToScreenPoint(crease),out hit);
                    bool matches=picked&&Mathf.Abs(hit.x-192)<.15f&&Mathf.Abs(hit.y-227)<.15f;
                    if(!matches){
                        Require(!app.World.IsTacticalPointVisible(crease),"a silhouette crease whose actual pixel ray reaches ground behind it is not advertised as clickable terrain");silhouettes++;
                    }
                    foreach(Point2 at in new[]{new Point2(192,239),new Point2(192,270),new Point2(194,290)}){
                        Vector3 surface=app.World.WorldSurfacePoint(at.x,at.y);
                        Require(app.World.IsTacticalPointVisible(surface),"nearby non-tangent highland approaches remain visible at the reproduced camera angle");
                        bool actual=app.World.TryGround(camera.WorldToScreenPoint(surface),out hit);
                        Require(actual&&Mathf.Abs(hit.x-at.x)<.15f&&Mathf.Abs(hit.y-at.y)<.15f,
                            "nearby visible slopes keep strict 0.15-unit ground picking while ridge silhouettes are rejected");visibleSlopes++;
                    }
                    Vector3 lifted=app.World.WorldSurfacePoint(192,227)+Vector3.up*.8f;
                    Require(app.World.IsTacticalPointVisible(lifted),"a visible creature-height point above the ridge keeps its original body visibility");
                }
            }finally{camera.transform.SetPositionAndRotation(oldPosition,oldRotation);camera.orthographicSize=oldSize;}
            Require(silhouettes>0&&visibleSlopes==24,"fixed near-cardinal views exercise a rejected silhouette and twenty-four strictly aligned nearby slope picks");
            Debug.Log("ARENA_GRAZING_GROUND_OK "+silhouettes+" silhouette cases / "+visibleSlopes+" strict visible slope picks");
        }
        float ActualIceFraction(TerrainState river)
        {
            Transform ice=MapRoot("frost").Find("water "+river.id).Find("Continuous freeze route");
            float open=1;
            foreach(Vector3 vertex in ice.GetComponent<MeshFilter>().sharedMesh.vertices){
                float x=vertex.x*25,centre=BattlefieldCatalog.RiverCentre(river,x),width=BattlefieldCatalog.RiverWidth(river,x);
                open=Mathf.Min(open,Mathf.Abs(vertex.z*25-centre)/width);
            }
            return 1-open;
        }
        IEnumerator VerifyClimateIceContinuity()
        {
            var source=AuthoritativeFixture("frost","snow",95);source.phase="reveal";source.revealStage="climate";source.revealStageAge=0;
            TerrainState river=source.battlefield.terrain.Find(t=>t.kind=="water"&&t.shape=="river");river.freeze=0;
            Show(source);yield return Settle(.025f);
            float prior=ActualIceFraction(river),elapsed=0;Require(prior<.01f,"snow climate begins with an open river rather than inherited full ice");
            foreach(float at in new[]{.3f,.7f,1.15f,1.8f}){
                yield return Settle(at-elapsed);elapsed=at;float ice=ActualIceFraction(river);
                Require(ice>=prior-.0001f&&ice<=.1201f,"continuous climate frost grows only at the riverbanks and never closes the channel");prior=ice;
                Require(river.freeze==0,"first-frost animation does not change server freeze or early river navigation");
                Capture("climate-first-frost-"+Mathf.RoundToInt(at*100));
            }
            source.phase="tactics";source.revealStage=null;Show(source);yield return Settle(.08f);
            Require(Mathf.Abs(ActualIceFraction(river)-.12f)<.0002f,"planning keeps the visible 12 percent bank frost and open central river");
            source.phase="battle";Show(source);yield return Settle(.08f);
            Require(ActualIceFraction(river)>=.12f,"combat continues from existing bank frost without rewinding the ice animation");
            prior=ActualIceFraction(river);
            foreach(float freeze in new[]{.2f,.45f,.7f,1f}){
                river.freeze=freeze;source.battlefield.elapsed=freeze*20;Show(source);float snapshotAt=Time.unscaledTime;yield return Settle(.05f);
                // Screenshot readback / PNG encoding can consume part of the next
                // snapshot interval. Check the rendered frame against that actual
                // clock, including the existing bounded network interpolation.
                float age=Mathf.Clamp(Time.unscaledTime-snapshotAt,0,.35f);
                float expected=freeze>=1?1:.12f+.88f*Mathf.Min(.995f,freeze+age/20);
                float ice=ActualIceFraction(river);bool advances=ice>prior&&Mathf.Abs(ice-expected)<.00025f;
                Require(advances,advances?"combat freeze visibly advances from the climate frost using authoritative server progress":
                    "combat freeze continuity mismatch: server="+freeze.ToString("F5")+" age="+age.ToString("F5")+" expected="+expected.ToString("F6")+" actual="+ice.ToString("F6")+" prior="+prior.ToString("F6"));prior=ice;
                Capture("combat-ice-continuity-"+Mathf.RoundToInt(freeze*100));
            }
            Transform water=MapRoot("frost").Find("water "+river.id).Find("Continuous translucent river");
            Require(!water.gameObject.activeSelf&&Mathf.Abs(prior-1)<.0001f,"only authoritative full freeze closes the entire river and hides underlying water");
        }
    }
}
