using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        const int SceneryPickLayer=9;
        sealed class BodyGeometry { internal MeshFilter[] parts;internal Renderer[] renderers; }
        sealed class PickMesh { internal Vector3[] vertices;internal int[] triangles; }
        readonly Dictionary<VoxelCreature,BodyGeometry> bodyGeometry=new Dictionary<VoxelCreature,BodyGeometry>();
        readonly Dictionary<Mesh,PickMesh> pickMeshGeometry=new Dictionary<Mesh,PickMesh>();
        readonly HashSet<MeshFilter> sceneryPickMeshes=new HashSet<MeshFilter>();
        FieldMapView pickingMap;
        bool basePickingBuilt;
        int pickingSyncFrame=-1;

        BodyGeometry Body(VoxelCreature view)
        {
            BodyGeometry body;if(bodyGeometry.TryGetValue(view,out body))return body;
            Transform figure=view.transform.Find("Articulated model");
            body=new BodyGeometry{parts=figure==null?new MeshFilter[0]:figure.GetComponentsInChildren<MeshFilter>(true)};
            body.renderers=new Renderer[body.parts.Length];
            for(int i=0;i<body.parts.Length;i++)body.renderers[i]=body.parts[i].GetComponent<Renderer>();
            bodyGeometry.Add(view,body);return body;
        }
        internal bool TryGetBodyBounds(VoxelCreature view,out Bounds bounds)
        {
            bounds=new Bounds();bool found=false;BodyGeometry body=Body(view);
            foreach(Renderer part in body.renderers){
                if(part==null||!part.enabled||!part.gameObject.activeInHierarchy)continue;
                if(!found){bounds=part.bounds;found=true;}else bounds.Encapsulate(part.bounds);
            }
            return found;
        }
        bool TryPickVisibleBody(Vector2 screen,out VoxelCreature visual)
        {
            visual=null;if(viewCamera==null||state==null)return false;
            Ray ray=viewCamera.ScreenPointToRay(screen);float nearest=180;
            PrepareSceneryPicking();RaycastHit sceneryHit;
            if(Physics.Raycast(ray,out sceneryHit,nearest,1<<SceneryPickLayer,QueryTriggerInteraction.Ignore))nearest=sceneryHit.distance+.012f;
            foreach(VoxelCreature view in creatures.Values){
                if(view.Kind=="steve"||view.Dead||!view.gameObject.activeInHierarchy)continue;
                Bounds bounds;float entry;if(!TryGetBodyBounds(view,out bounds)||!bounds.IntersectRay(ray,out entry)||entry>nearest)continue;
                BodyGeometry body=Body(view);
                for(int i=0;i<body.parts.Length;i++){
                    MeshFilter part=body.parts[i];Renderer renderer=body.renderers[i];
                    if(renderer==null||!renderer.enabled||!part.gameObject.activeInHierarchy||part.sharedMesh==null)continue;
                    Vector3 localOrigin=part.transform.InverseTransformPoint(ray.origin),localDirection=part.transform.InverseTransformVector(ray.direction);
                    float factor=localDirection.magnitude;if(factor<.00001f)continue;
                    Ray localRay=new Ray(localOrigin,localDirection);
                    if(!part.sharedMesh.bounds.IntersectRay(localRay,out entry)||entry/factor>nearest)continue;
                    PickMesh mesh;if(!pickMeshGeometry.TryGetValue(part.sharedMesh,out mesh)){
                        mesh=new PickMesh{vertices=part.sharedMesh.vertices,triangles=part.sharedMesh.triangles};pickMeshGeometry.Add(part.sharedMesh,mesh);
                    }
                    for(int t=0;t<mesh.triangles.Length;t+=3){
                        float distance;if(!RayTriangle(localRay,mesh.vertices[mesh.triangles[t]],mesh.vertices[mesh.triangles[t+1]],mesh.vertices[mesh.triangles[t+2]],out distance))continue;
                        distance/=factor;if(distance>=nearest)continue;nearest=distance;visual=view;
                    }
                }
            }
            return visual!=null;
        }
        static bool RayTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            distance=0;Vector3 edge=b-a,other=c-a,p=Vector3.Cross(ray.direction,other);float determinant=Vector3.Dot(edge,p);
            if(Mathf.Abs(determinant)<.0000001f)return false;
            float inverse=1/determinant;Vector3 offset=ray.origin-a;float u=Vector3.Dot(offset,p)*inverse;
            if(u<0||u>1)return false;Vector3 q=Vector3.Cross(offset,edge);float v=Vector3.Dot(ray.direction,q)*inverse;
            if(v<0||u+v>1)return false;distance=Vector3.Dot(other,q)*inverse;return distance>0;
        }

        // Opaque map meshes double as a retained native ray-query acceleration structure.
        // They do not participate in movement: the host still owns all gameplay collision.
        void PrepareSceneryPicking()
        {
            if(!basePickingBuilt||pickingMap!=fieldMap){
                foreach(MeshFilter part in scenery.GetComponentsInChildren<MeshFilter>(true)){
                    if(sceneryPickMeshes.Contains(part)||part.sharedMesh==null)continue;
                    Renderer renderer=part.GetComponent<Renderer>();if(renderer==null)continue;
                    Material material=renderer.sharedMaterial;if(material==null||material.renderQueue>=3000)continue;
                    MeshCollider collider=part.GetComponent<MeshCollider>();if(collider==null)collider=part.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh=part.sharedMesh;part.gameObject.layer=SceneryPickLayer;sceneryPickMeshes.Add(part);
                }
                basePickingBuilt=true;pickingMap=fieldMap;pickingSyncFrame=-1;
            }
            if(pickingSyncFrame!=Time.frameCount){Physics.SyncTransforms();pickingSyncFrame=Time.frameCount;}
        }
        public bool IsTacticalPointVisible(Vector3 point)
        {
            if(viewCamera==null)return false;
            Vector3 screen=viewCamera.WorldToViewportPoint(point);
            if(screen.z<viewCamera.nearClipPlane||screen.z>viewCamera.farClipPlane||screen.x<0||screen.x>1||screen.y<0||screen.y>1)return false;
            Ray ray=viewCamera.ViewportPointToRay(screen);float distance=Vector3.Dot(point-ray.origin,ray.direction);
            PrepareSceneryPicking();RaycastHit hit;
            return !Physics.Raycast(ray,out hit,Mathf.Max(0,distance-.025f),1<<SceneryPickLayer,QueryTriggerInteraction.Ignore);
        }
        // A vertical query of the shipping ramp triangles, isolated from decorative
        // trees/altars, lets the native regression compare mesh and gameplay heights.
        internal bool TryGetRenderedRampHeight(float x,float y,out float height)
        {
            height=float.NegativeInfinity;if(fieldMap==null)return false;
            PrepareSceneryPicking();Ray ray=new Ray(new Vector3(x*WorldScale,30,y*WorldScale),Vector3.down);bool found=false;
            foreach(var terrain in fieldMap.terrain){
                if(terrain.kind!="highland"&&terrain.kind!="bridge")continue;
                Transform top=terrain.root.Find("Walkable ramp and stone layers");if(top==null)continue;
                MeshCollider collider=top.GetComponent<MeshCollider>();RaycastHit hit;
                if(collider!=null&&collider.Raycast(ray,out hit,60)){height=Mathf.Max(height,hit.point.y);found=true;}
            }
            return found;
        }

        // Find the first ray / height-field crossing. Fixed-point horizontal-plane
        // iteration oscillates at ramp edges, and cannot guarantee the nearest surface.
        bool TryIntersectWalkableSurface(Ray ray,out Vector3 point)
        {
            point=Vector3.zero;if(ray.direction.y>=-.00001f)return false;
            float top=.06f;if(!solo&&fieldVisible&&fieldState!=null)
                foreach(var terrain in fieldState.terrain)if(terrain.kind=="highland"||terrain.kind=="bridge")top=Mathf.Max(top,.06f+terrain.height*fieldRevealProgress);
            // Enclose the height field on both sides. Ending exactly on the base
            // plane can round GetPoint(end).y a few micrometres above 0.06 and
            // leave a flat-ground ray without a negative side for bisection.
            // This margin brackets the root; it does not relax the returned hit.
            const float bracketPadding=.002f;
            float start=Mathf.Max(0,(top+bracketPadding-ray.origin.y)/ray.direction.y),end=(.06f-bracketPadding-ray.origin.y)/ray.direction.y;
            if(end<start)return false;
            int steps=Mathf.Max(1,Mathf.CeilToInt((end-start)/.08f));float previous=start;
            for(int i=1;i<=steps;i++){
                float at=Mathf.Lerp(start,end,(float)i/steps);Vector3 sample=ray.GetPoint(at);
                float height=.06f+(solo?0:BattlefieldVisualHeight(sample.x/WorldScale,sample.z/WorldScale));
                if(sample.y<=height){
                    float low=previous,high=at;
                    for(int n=0;n<18;n++){
                        float middle=(low+high)*.5f;Vector3 p=ray.GetPoint(middle);
                        if(p.y>.06f+(solo?0:BattlefieldVisualHeight(p.x/WorldScale,p.z/WorldScale)))low=middle;else high=middle;
                    }
                    point=ray.GetPoint((low+high)*.5f);return true;
                }
                previous=at;
            }
            return false;
        }
    }
}
