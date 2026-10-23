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
            Ray ray=viewCamera.ScreenPointToRay(screen);float nearest=viewCamera.farClipPlane;
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
        static bool RayTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,out float distance,float edgeTolerance=0)
        {
            distance=0;Vector3 edge=b-a,other=c-a,p=Vector3.Cross(ray.direction,other);float determinant=Vector3.Dot(edge,p);
            if(Mathf.Abs(determinant)<.0000001f)return false;
            float inverse=1/determinant;Vector3 offset=ray.origin-a;float u=Vector3.Dot(offset,p)*inverse;
            if(u< -edgeTolerance||u>1+edgeTolerance)return false;Vector3 q=Vector3.Cross(offset,edge);float v=Vector3.Dot(ray.direction,q)*inverse;
            if(v< -edgeTolerance||u+v>1+edgeTolerance)return false;distance=Vector3.Dot(other,q)*inverse;return distance>0;
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
            Vector3 screen=viewCamera.WorldToScreenPoint(point);
            if(screen.z<viewCamera.nearClipPlane||screen.z>viewCamera.farClipPlane||screen.x<0||screen.x>Screen.width||screen.y<0||screen.y>Screen.height)return false;
            // Match the actual pointer path. ViewportPointToRay followed a slightly
            // different float round trip at a ridge silhouette and could classify
            // an unclickable, tangent crease as an actionable ground point.
            Ray ray=viewCamera.ScreenPointToRay(new Vector2(screen.x,screen.y));float distance=Vector3.Dot(point-ray.origin,ray.direction);
            // PhysX can miss a ridge's shared triangle edge. Visibility must use
            // the same retained walkable triangles as the actual ground click;
            // otherwise a hidden far slope is reported as an actionable point.
            Vector3 surface;
            if(TryIntersectWalkableSurface(ray,out surface)){
                float surfaceDistance=Vector3.Dot(surface-ray.origin,ray.direction);
                if(surfaceDistance<distance-.025f)return false;
                // A ray tangent to a steep corner can miss both adjoining faces
                // after pixel rounding and correctly reach ground behind it. Only
                // points on the walking surface need this reverse check; creature
                // body centres and lifted route markers remain visible above it.
                float walkingHeight=WorldSurfacePoint(point.x/WorldScale,point.z/WorldScale).y;
                if(Mathf.Abs(point.y-walkingHeight)<.005f&&surfaceDistance>distance+.025f)return false;
            }
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

        // Query the retained walkable triangles directly. A ray can just graze the
        // crease between two slopes without crossing the sampled height field;
        // marching then skips that visible surface and selects flat ground behind it.
        // Trees, ruins and decorative inlays do not change the walking surface.
        bool TryIntersectWalkableSurface(Ray ray,out Vector3 point)
        {
            point=Vector3.zero;if(ray.direction.y>=-.00001f)return false;
            float nearest=(.06f-ray.origin.y)/ray.direction.y;
            if(nearest<0)return false;
            point=ray.GetPoint(nearest);
            if(!solo&&fieldVisible&&fieldMap!=null&&fieldMap.root.gameObject.activeInHierarchy){
                PrepareSceneryPicking();
                foreach(var terrain in fieldMap.terrain){
                    if(terrain.kind!="highland"&&terrain.kind!="bridge")continue;
                    Transform top=terrain.root.Find("Walkable ramp and stone layers");
                    if(top==null||!top.gameObject.activeInHierarchy)continue;
                    MeshCollider collider=top.GetComponent<MeshCollider>();RaycastHit hit;
                    if(collider==null||!collider.enabled)continue;
                    if(collider.Raycast(ray,out hit,nearest+.001f)&&hit.distance<nearest){
                        nearest=hit.distance;point=hit.point;
                    }
                    // Also check the original triangles when PhysX hits a farther
                    // face after rejecting the nearer crease of the same mesh.
                    float distance;
                    if(TryPickRampTriangles(top,ray,nearest,out distance)){nearest=distance;point=ray.GetPoint(distance);}
                }
            }
            return true;
        }
        bool TryPickRampTriangles(Transform top,Ray ray,float maximum,out float nearest)
        {
            nearest=maximum;MeshFilter filter=top.GetComponent<MeshFilter>();
            if(filter==null||filter.sharedMesh==null)return false;
            Vector3 direction=top.InverseTransformVector(ray.direction);float factor=direction.magnitude;
            if(factor<.000001f)return false;
            Ray local=new Ray(top.InverseTransformPoint(ray.origin),direction);float entry;
            if(!filter.sharedMesh.bounds.IntersectRay(local,out entry)||entry/factor>maximum)return false;
            PickMesh mesh;
            if(!pickMeshGeometry.TryGetValue(filter.sharedMesh,out mesh)){
                mesh=new PickMesh{vertices=filter.sharedMesh.vertices,triangles=filter.sharedMesh.triangles};pickMeshGeometry.Add(filter.sharedMesh,mesh);
            }
            bool found=false;
            for(int t=0;t<mesh.triangles.Length;t+=3){
                float distance;
                if(!RayRampTriangle(local,mesh.vertices[mesh.triangles[t]],mesh.vertices[mesh.triangles[t+1]],mesh.vertices[mesh.triangles[t+2]],out distance))continue;
                distance/=factor;if(distance>=nearest)continue;nearest=distance;found=true;
            }
            return found;
        }
        static bool RayRampTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            // Camera screen projection is float precision. Double intermediates
            // and a 1e-4 barycentric edge margin retain shared creases without
            // shifting the ray or enlarging a creature's selection silhouette.
            distance=0;
            double ex=(double)b.x-a.x,ey=(double)b.y-a.y,ez=(double)b.z-a.z;
            double fx=(double)c.x-a.x,fy=(double)c.y-a.y,fz=(double)c.z-a.z;
            double px=ray.direction.y*fz-ray.direction.z*fy,py=ray.direction.z*fx-ray.direction.x*fz,pz=ray.direction.x*fy-ray.direction.y*fx;
            double determinant=ex*px+ey*py+ez*pz;if(System.Math.Abs(determinant)<1e-12)return false;
            double ox=(double)ray.origin.x-a.x,oy=(double)ray.origin.y-a.y,oz=(double)ray.origin.z-a.z;
            double u=(ox*px+oy*py+oz*pz)/determinant;
            const double edge=1e-4;if(u< -edge||u>1+edge)return false;
            double qx=oy*ez-oz*ey,qy=oz*ex-ox*ez,qz=ox*ey-oy*ex;
            double v=(ray.direction.x*qx+ray.direction.y*qy+ray.direction.z*qz)/determinant;
            if(v< -edge||u+v>1+edge)return false;
            double at=(fx*qx+fy*qy+fz*qz)/determinant;
            if(at<=0||at>float.MaxValue)return false;distance=(float)at;return true;
        }
    }
}
