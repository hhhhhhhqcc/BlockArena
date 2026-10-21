using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        void DrawTacticsProjection()
        {
            var tactics=app.Tactics;if(app.World==null||app.Me==null)return;
            if(tactics.RoutesMixed)DrawMixedRoutes();
            else if(tactics.SelectedCount>0){
                Point2 previous=tactics.SelectionCenter();
                for(int i=0;i<tactics.RouteNodeCount;i++){
                    var node=tactics.DraftNodes[i];Point2 destination=new Point2(node.x,node.y);
                    DrawTerrainRoute(previous,destination,new Color(routeBlue.r,routeBlue.g,routeBlue.b,.88f),2);
                    Vector2 at;if(TacticProject(tactics.WorldPoint(node.x,node.y,.17f),out at)){
                        bool selected=tactics.SelectedNode==i;Color c=selected?gold:routeBlue;
                        DrawGroundRing(node.x,node.y,node.action=="ambush"?32:14,new Color(c.r,c.g,c.b,.7f),selected?2.6f:1.8f);
                        Rect marker=new Rect(at.x-13,at.y-31,26,26);RoundFill(marker,new Color(.025f,.07f,.09f,.95f),5);
                        Text(marker,(i+1).ToString(),centered,c);
                    }
                    previous=destination;
                }
            }
            if(tactics.BoxSelecting){
                Rect box=tactics.SelectionBox;box=new Rect(box.x/scale,(Screen.height-box.yMax)/scale,box.width/scale,box.height/scale);
                Fill(box,new Color(routeBlue.r,routeBlue.g,routeBlue.b,.10f));
                Fill(new Rect(box.x,box.y,box.width,2),routeBlue);Fill(new Rect(box.x,box.yMax-2,box.width,2),routeBlue);
                Fill(new Rect(box.x,box.y,2,box.height),routeBlue);Fill(new Rect(box.xMax-2,box.y,2,box.height),routeBlue);
            }
        }
        void DrawMixedRoutes()
        {
            var tactics=app.Tactics;mixedRouteShapes.Clear();Color faint=new Color(routeBlue.r,routeBlue.g,routeBlue.b,.32f);
            foreach(string id in tactics.SelectedUnitIds){
                IReadOnlyList<TacticNode> nodes;Point2 start;
                if(!tactics.TryGetRoute(id,out nodes)||nodes.Count==0||!tactics.TryGetSelectionPosition(id,out start))continue;
                DrawTerrainRoute(start,new Point2(nodes[0].x,nodes[0].y),faint,1.2f);
                int shape=17;unchecked{foreach(var n in nodes){shape=shape*31+n.x.GetHashCode();shape=shape*31+n.y.GetHashCode();}}
                if(!mixedRouteShapes.Add(shape))continue;
                for(int i=1;i<nodes.Count;i++)DrawTerrainRoute(new Point2(nodes[i-1].x,nodes[i-1].y),new Point2(nodes[i].x,nodes[i].y),faint,1.2f);
                foreach(var node in nodes){Vector2 at;if(TacticProject(tactics.WorldPoint(node.x,node.y,.16f),out at))RoundFill(new Rect(at.x-3,at.y-3,6,6),faint,3);}
            }
        }
        void DrawTerrainRoute(Point2 from,Point2 to,Color color,float thickness)
        {
            float dx=to.x-from.x,dy=to.y-from.y,distance=Mathf.Sqrt(dx*dx+dy*dy);if(distance<1)return;
            int steps=Mathf.Clamp(Mathf.CeilToInt(distance/6),1,192);Vector2 prior;bool priorVisible=TacticProject(app.Tactics.WorldPoint(from.x,from.y,.16f),out prior);
            for(int i=1;i<=steps;i++){
                float t=(float)i/steps;Vector2 at;bool visible=TacticProject(app.Tactics.WorldPoint(from.x+dx*t,from.y+dy*t,.16f),out at);
                if(priorVisible&&visible)DrawTacticLine(prior,at,color,thickness);prior=at;priorVisible=visible;
            }
            Vector2 tip,tail;
            if(!TacticProject(app.Tactics.WorldPoint(from.x+dx*.72f,from.y+dy*.72f,.17f),out tip)||!TacticProject(app.Tactics.WorldPoint(from.x+dx*.68f,from.y+dy*.68f,.17f),out tail))return;
            Vector2 tangent=(tip-tail).normalized,side=new Vector2(-tangent.y,tangent.x);
            DrawTacticLine(tip,tip-tangent*9+side*4,color,thickness);DrawTacticLine(tip,tip-tangent*9-side*4,color,thickness);
        }
        bool TacticProject(Vector3 position,out Vector2 point)
        {
            point=Vector2.zero;if(app.World==null||app.World.ViewCamera==null)return false;
            Vector3 screen=app.World.ViewCamera.WorldToScreenPoint(position);point=new Vector2(screen.x/scale,(Screen.height-screen.y)/scale);return screen.z>.1f&&point.x>=0&&point.x<=width&&point.y>=0&&point.y<=height&&app.World.IsTacticalPointVisible(position);
        }
        void DrawGroundRing(float x,float y,float radius,Color color,float thickness)
        {
            Vector2 prior=Vector2.zero;bool priorVisible=false;
            for(int i=0;i<=24;i++){
                float a=i*Mathf.PI*2/24;float px=x+Mathf.Cos(a)*radius,py=y+Mathf.Sin(a)*radius;
                Vector2 at;bool visible=TacticProject(app.Tactics.WorldPoint(px,py,.10f),out at);
                if(i>0&&visible&&priorVisible)DrawTacticLine(prior,at,color,thickness);
                prior=at;priorVisible=visible;
            }
        }
        static void DrawTacticLine(Vector2 from,Vector2 to,Color color,float thickness)
        {
            Vector2 delta=to-from;if(delta.sqrMagnitude<.01f)return;
            Matrix4x4 prior=GUI.matrix;
            // Compose in HUD-local coordinates. RotateAroundPivot unclips its pivot in
            // screen coordinates, which offsets every subdivided segment when scale != 1.
            GUI.matrix=prior*Matrix4x4.TRS(new Vector3(from.x,from.y,0),Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
            Fill(new Rect(0,-thickness*.5f,delta.magnitude,thickness),color);GUI.matrix=prior;
        }
        static void DrawRouteArrow(Vector2 from,Vector2 to,Color color)
        {
            Vector2 delta=to-from;if(delta.magnitude<14)return;
            DrawTacticLine(from,to,color,2);
            Vector2 direction=delta.normalized,side=new Vector2(-direction.y,direction.x),tip=Vector2.Lerp(from,to,.72f);
            DrawTacticLine(tip,tip-direction*10+side*5,color,2);DrawTacticLine(tip,tip-direction*10-side*5,color,2);
        }

        // Native raster checks use the exact shipping route renderer, including scale,
        // surface sampling and opaque-geometry visibility; no parallel test implementation.
        internal void DrawTacticalSegmentForVerification(Point2 from,Point2 to,Color color,float thickness)
        {
            Setup();Metrics();Matrix4x4 prior=GUI.matrix;bool enabled=GUI.enabled;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));GUI.enabled=true;
            DrawTerrainRoute(from,to,color,thickness);GUI.matrix=prior;GUI.enabled=enabled;
        }
    }
}
