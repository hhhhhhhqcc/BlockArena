using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        readonly Dictionary<Transform,MeshFilter[]> objectivePickParts=new Dictionary<Transform,MeshFilter[]>();

        // Use the visible structure's triangles, never a large invisible screen circle.
        public bool TryInspectObjective(Vector2 screen,out ObjectiveState objective)
        {
            objective=null;
            if(viewCamera==null||state==null||fieldMap==null||!fieldVisible||
                (state.phase!="tactics"&&state.phase!="battle"))return false;
            PrepareSceneryPicking();Ray ray=viewCamera.ScreenPointToRay(screen);
            float nearest=viewCamera.farClipPlane;Transform selectedRoot=null;
            foreach(var view in fieldMap.objectives){
                if(view.state==null||view.root==null||!view.root.gameObject.activeInHierarchy)continue;
                MeshFilter[] parts;if(!objectivePickParts.TryGetValue(view.root,out parts)){
                    parts=view.root.GetComponentsInChildren<MeshFilter>(true);objectivePickParts.Add(view.root,parts);
                }
                foreach(MeshFilter part in parts){
                    if(part==null||part.sharedMesh==null||!part.gameObject.activeInHierarchy||
                        part.name=="Ownership ring"||part.name=="Capture progress")continue;
                    Renderer renderer=part.GetComponent<Renderer>();
                    if(renderer==null||!renderer.enabled)continue;
                    Vector3 direction=part.transform.InverseTransformVector(ray.direction);
                    float factor=direction.magnitude;if(factor<.000001f)continue;
                    Ray localRay=new Ray(part.transform.InverseTransformPoint(ray.origin),direction);float entry;
                    if(!part.sharedMesh.bounds.IntersectRay(localRay,out entry)||entry/factor>nearest)continue;
                    PickMesh mesh;if(!pickMeshGeometry.TryGetValue(part.sharedMesh,out mesh)){
                        mesh=new PickMesh{vertices=part.sharedMesh.vertices,triangles=part.sharedMesh.triangles};pickMeshGeometry.Add(part.sharedMesh,mesh);
                    }
                    for(int i=0;i<mesh.triangles.Length;i+=3){
                        float distance;if(!RayTriangle(localRay,mesh.vertices[mesh.triangles[i]],mesh.vertices[mesh.triangles[i+1]],mesh.vertices[mesh.triangles[i+2]],out distance))continue;
                        distance/=factor;if(distance>=nearest)continue;
                        nearest=distance;objective=view.state;selectedRoot=view.root;
                    }
                }
            }
            if(objective==null)return false;
            RaycastHit hit;
            if(Physics.Raycast(ray,out hit,nearest+.012f,1<<SceneryPickLayer,QueryTriggerInteraction.Ignore)&&
                hit.distance<nearest-.015f&&!hit.transform.IsChildOf(selectedRoot)){
                objective=null;return false;
            }
            return true;
        }
    }
}
