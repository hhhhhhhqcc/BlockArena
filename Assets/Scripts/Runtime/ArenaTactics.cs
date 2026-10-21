using System;
using System.Collections.Generic;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    /// <summary>Client-only selection and draft editing. Commands are validated and executed by the host.</summary>
    public sealed class ArenaTactics
    {
        public const int MaxNodes=10;
        static readonly string[] actions={"move","capture","ambush","wait","attack"};
        static readonly string[] conditions={"always","after_time","health_below","enemy_near"};
        readonly List<string> selection=new List<string>();
        readonly List<TacticNode> draft=new List<TacticNode>();
        sealed class PendingRoute { internal List<TacticNode> nodes;internal float expires;internal bool sawDifferent; }
        readonly Dictionary<string,PendingRoute> pendingRoutes=new Dictionary<string,PendingRoute>();
        readonly List<string> completedRoutes=new List<string>();
        static readonly IReadOnlyList<TacticNode> emptyRoute=new TacticNode[0];
        ArenaApp app;
        string observedPhase;
        int observedRound=-1;
        Vector2 boxStart,boxEnd;
        bool boxing,shiftBox,boxMoved,draggingUnit;
        string clickedUnit;
        float nextRouteSound;
        public bool Collapsed,SkillPickerOpen,OptionsOpen;
        public bool Targeting { get; private set; }
        public bool Dirty { get; private set; }
        public bool RoutesMixed { get; private set; }
        public int SelectedNode { get; set; }
        public bool Active { get { return app!=null&&app.State!=null&&app.Me!=null&&!app.Me.eliminated&&app.State.phase=="tactics"; } }
        public int SelectedCount { get { return selection.Count; } }
        public int RouteNodeCount { get { return draft.Count; } }
        internal int PendingRouteCount { get { return pendingRoutes.Count; } }
        public IReadOnlyList<string> SelectedUnitIds { get { return selection; } }
        public IReadOnlyList<TacticNode> DraftNodes { get { return draft; } }
        public bool BoxSelecting { get { return boxing&&shiftBox&&boxMoved; } }
        public Rect SelectionBox { get { return Rect.MinMaxRect(Mathf.Min(boxStart.x,boxEnd.x),Mathf.Min(boxStart.y,boxEnd.y),Mathf.Max(boxStart.x,boxEnd.x),Mathf.Max(boxStart.y,boxEnd.y)); } }

        public void Initialize(ArenaApp owner) { app=owner; }
        public bool ProcessInput()
        {
            ObservePhase();
            if(app==null||app.State==null||app.Me==null||app.Me.eliminated)return false;
            if(app.VerificationInput)return false;
            if(app.BattlefieldInputBlocked||app.Hud.PointerCaptured){CancelPointerGesture();return false;}
            if(app.Hud.KeyboardCaptured){CancelPointerGesture();return false;}
            Vector2 mouse=Input.mousePosition;
            if(app.State.phase=="battle")
            {
                if(Input.GetKeyDown(KeyCode.Escape))Targeting=false;
                if(Input.GetKeyDown(KeyCode.Q)){if(Targeting){if(!app.Hud.Contains(mouse))Cast(mouse);}else ArmSkill();return true;}
                if(Targeting&&Input.GetMouseButtonDown(1)){CancelTargeting();return true;}
                if(Targeting&&Input.GetMouseButtonDown(0)&&!app.Hud.Contains(mouse)){Cast(mouse);return true;}
                return Targeting;
            }
            Targeting=false;
            if(!CanEdit){CancelPointerGesture();return false;}
            if(Input.GetKeyDown(KeyCode.A)&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))
            {SelectAll();return true;}
            return ProcessTacticalPointer(mouse,Input.GetMouseButtonDown(0),Input.GetMouseButton(0),Input.GetMouseButtonUp(0),
                Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),Input.GetMouseButtonDown(1));
        }
        void ObservePhase()
        {
            if(app!=null&&app.Me!=null)
            {
                int removed=selection.RemoveAll(id=>!app.Me.units.Exists(u=>u.id==id));
                // Dismissing one member must not discard an unsaved route for the survivors.
                if(removed>0&&(!Dirty||selection.Count==0))LoadExistingRoute();
            }
            string phase=app==null||app.State==null?null:app.State.phase;
            int round=app==null||app.State==null?-1:app.State.round;
            if(observedPhase==phase&&observedRound==round){if(phase=="tactics")ReconcilePendingRoutes(Time.unscaledTime);return;}
            CancelPointerGesture();Targeting=false;SkillPickerOpen=false;OptionsOpen=false;
            if(phase==null||phase=="lobby"||phase=="recruit"||round!=observedRound){selection.Clear();draft.Clear();pendingRoutes.Clear();Dirty=false;RoutesMixed=false;SelectedNode=0;}
            observedPhase=phase;observedRound=round;
        }

        // The native probe and the physical mouse use this identical selection / planning path.
        internal bool ProcessTacticalPointer(Vector2 mouse,bool pressed,bool held,bool released,bool shift,bool rightPressed)
        {
            if(!CanEdit||app.World==null||app.BattlefieldInputBlocked||app.Hud.KeyboardCaptured){CancelPointerGesture();return false;}
            // A lost mouse-up (focus loss, a modal opening, etc.) must not leave a stale marquee.
            if(boxing&&!pressed&&!held&&!released){CancelPointerGesture();return false;}
            if(boxing&&rightPressed){CancelPointerGesture();return true;}
            if(rightPressed&&!app.Hud.Contains(mouse))
            {
                Point2 at;
                if(selection.Count==0)app.Notify("先选择生物");
                else if(PointAt(mouse,out at))AddNode(at);
                return true;
            }
            if(pressed&&!app.Hud.Contains(mouse))
            {
                boxing=true;boxStart=boxEnd=mouse;boxMoved=false;shiftBox=shift;
                app.World.TryPickUnit(mouse,out clickedUnit);
                draggingUnit=!shift&&!String.IsNullOrEmpty(clickedUnit);
                if(shift)app.Hud.ClosePinnedInspection();
                if(draggingUnit)
                {
                    app.Hud.ClosePinnedInspection();
                    SetSelection(new string[]{clickedUnit},false,false);
                    app.ProcessPlacementPointer(mouse,true,held,false);
                }
            }
            if(boxing&&held)
            {
                boxEnd=mouse;
                if((boxEnd-boxStart).sqrMagnitude>36)boxMoved=true;
                if(draggingUnit&&!app.Hud.Contains(mouse))app.ProcessPlacementPointer(mouse,false,true,false);
            }
            if(boxing&&released)
            {
                boxEnd=mouse;
                if((boxEnd-boxStart).sqrMagnitude>36)boxMoved=true;
                // Only a gesture released over the battlefield can place or select creatures.
                if(app.Hud.Contains(mouse)){CancelPointerGesture();return true;}
                if(draggingUnit)
                {
                    app.ProcessPlacementPointer(mouse,false,false,true);
                }
                else if(shiftBox&&boxMoved)
                {
                    var ids=new List<string>();Rect r=SelectionBox;
                    foreach(UnitState unit in app.Me.units)
                    {
                        Vector3 center;if(!app.World.TryGetUnitSelectionPoint(unit.id,out center))continue;
                        Vector3 screen=app.World.ViewCamera.WorldToScreenPoint(center);
                        if(screen.z>0&&r.Contains(screen)&&app.World.IsTacticalPointVisible(center)&&!app.Hud.Contains(new Vector2(screen.x,screen.y)))ids.Add(unit.id);
                    }
                    SetSelection(ids,shiftBox,false);
                }
                else if(!boxMoved)
                {
                    var ids=new List<string>();if(!String.IsNullOrEmpty(clickedUnit))ids.Add(clickedUnit);
                    SetSelection(ids,shiftBox,true);
                    if(!shiftBox)
                    {
                        if(!app.PinCreatureAt(mouse))
                        {
                            app.Hud.ClosePinnedInspection();
                            Point2 point;if(PointAt(mouse,out point))app.MoveSteveTo(point);
                        }
                    }
                }
                CancelPointerGesture();return true;
            }
            return boxing;
        }
        public void SelectAll(bool feedback=true)
        {
            if(!CanEdit)return;
            var ids=new List<string>();foreach(var unit in app.Me.units)ids.Add(unit.id);
            SetSelection(ids,false,false,feedback);
        }
        void SetSelection(ICollection<string> ids,bool append,bool toggle,bool feedback=true)
        {
            var next=append?new List<string>(selection):new List<string>();
            foreach(string id in ids)
            {
                if(app.Me==null||!app.Me.units.Exists(u=>u.id==id))continue;
                if(next.Contains(id)){if(append&&toggle)next.Remove(id);}
                else next.Add(id);
            }
            // Grabbing the same squad must keep its local draft, even before a server acknowledgement.
            if(next.Count==selection.Count&&new HashSet<string>(next).SetEquals(selection))return;
            // Changing groups commits the previous route, so building a second squad cannot discard the first.
            if(Active&&Dirty&&selection.Count>0)Save();
            selection.Clear();selection.AddRange(next);
            LoadExistingRoute();
            if(feedback)Feedback("select",.34f);
        }
        internal void SetSelectionForVerification(string[] ids){SetSelection(ids,false,false);}
        void LoadExistingRoute()
        {
            draft.Clear();Dirty=false;RoutesMixed=false;SelectedNode=0;
            if(app.Me==null||selection.Count==0)return;
            IReadOnlyList<TacticNode> first;TryGetRoute(selection[0],out first);
            for(int i=1;i<selection.Count;i++){
                IReadOnlyList<TacticNode> other;TryGetRoute(selection[i],out other);
                if(!SameNodes(first,other)){RoutesMixed=true;return;}
            }
            foreach(var node in first)draft.Add(Clone(node));
        }
        public bool TryGetRoute(string unitId,out IReadOnlyList<TacticNode> nodes)
        {
            if(string.IsNullOrEmpty(unitId)){nodes=emptyRoute;return false;}
            PendingRoute pending;
            if(pendingRoutes.TryGetValue(unitId,out pending)){nodes=pending.nodes;return nodes.Count>0;}
            return TryGetSnapshotRoute(unitId,out nodes);
        }
        bool TryGetSnapshotRoute(string unitId,out IReadOnlyList<TacticNode> nodes)
        {
            if(app.Me!=null)foreach(var plan in app.Me.tactics)
                if(Array.IndexOf(plan.unitIds,unitId)>=0){nodes=plan.nodes;return nodes.Count>0;}
            nodes=emptyRoute;return false;
        }
        // The normal input tick and native regression driver share this reconciliation path.
        internal bool ReconcilePendingRoutes(float now)
        {
            if(app.Me==null||!Active)return false;
            completedRoutes.Clear();bool expired=false,reload=false;
            foreach(var pair in pendingRoutes){
                bool owned=app.Me.units.Exists(u=>u.id==pair.Key);
                IReadOnlyList<TacticNode> actual;TryGetSnapshotRoute(pair.Key,out actual);
                bool matched=owned&&SameNodes(pair.Value.nodes,actual);
                if(owned&&!matched)pair.Value.sawDifferent=true;
                // A rapid A -> B -> A edit can still be looking at the old A snapshot.
                // Keep that final prediction until a differing snapshot returns to A, or timeout.
                bool acknowledged=matched&&pair.Value.sawDifferent;
                if(owned&&!acknowledged&&now<pair.Value.expires)continue;
                completedRoutes.Add(pair.Key);
                if(!matched){expired=true;if(selection.Contains(pair.Key))reload=true;}
            }
            foreach(string id in completedRoutes)pendingRoutes.Remove(id);
            // An acknowledged snapshot is already equal. Only expired predictions need a reload,
            // and an unsaved local edit always takes precedence over either kind of network update.
            if(reload&&!Dirty)LoadExistingRoute();
            return expired;
        }
        public bool TryGetSelectionPosition(string unitId,out Point2 point)
        {
            if(app.World!=null&&app.World.TryGetUnitPosition(unitId,out point))return true;
            UnitState unit=app.Me==null?null:app.Me.units.Find(u=>u.id==unitId);
            point=unit==null?new Point2():new Point2(unit.x,unit.y);return unit!=null;
        }
        public void AddNode(Point2 point,bool feedback=true)
        {
            if(!CanEdit||selection.Count==0)return;
            if(draft.Count>=MaxNodes){app.Notify("每条路线最多 10 个节点，可删除或调整已有节点。");return;}
            var node=new TacticNode{x=Mathf.Clamp(point.x,-405,405),y=Mathf.Clamp(point.y,-405,405),seconds=5,threshold=100};
            ObjectiveState objective=NearestObjective(node.x,node.y,50);
            if(objective!=null){node.action="capture";node.targetId=objective.id;node.x=objective.x;node.y=objective.y;}
            draft.Add(node);SelectedNode=draft.Count-1;Dirty=true;RoutesMixed=false;if(feedback)RouteFeedback();
        }
        public void AddWait()
        {
            if(!CanEdit||selection.Count==0)return;
            Point2 position=SelectionCenter();
            if(draft.Count>0)position=new Point2(draft[draft.Count-1].x,draft[draft.Count-1].y);
            int before=draft.Count;AddNode(position,false);
            if(draft.Count>before){draft[draft.Count-1].action="wait";draft[draft.Count-1].targetId=null;Dirty=true;}
        }
        public void CycleAction(int nodeIndex)
        {
            if(!EditableNode(nodeIndex))return;
            var node=draft[nodeIndex];node.action=Next(actions,node.action);
            if(node.action=="capture")
            {
                var objective=NearestObjective(node.x,node.y,140);
                if(objective!=null){node.targetId=objective.id;node.x=objective.x;node.y=objective.y;}
            }
            else node.targetId=null;
            Dirty=true;
        }
        public void CycleCondition(int nodeIndex)
        {
            if(!EditableNode(nodeIndex))return;
            var node=draft[nodeIndex];node.condition=Next(conditions,node.condition);
            node.threshold=node.condition=="health_below"?40:100;Dirty=true;
        }
        public void ChangeSeconds(int nodeIndex,float delta){if(!EditableNode(nodeIndex))return;float next=Mathf.Clamp(draft[nodeIndex].seconds+delta,1,20);if(next==draft[nodeIndex].seconds)return;draft[nodeIndex].seconds=next;Dirty=true;}
        public void ChangeThreshold(int nodeIndex,float delta)
        {if(!EditableNode(nodeIndex))return;var n=draft[nodeIndex];float next=Mathf.Clamp(n.threshold+delta,n.condition=="health_below"?5:25,n.condition=="health_below"?95:250);if(next==n.threshold)return;n.threshold=next;Dirty=true;}
        public void RemoveNode(int index){if(!EditableNode(index))return;draft.RemoveAt(index);SelectedNode=draft.Count==0?0:Mathf.Clamp(SelectedNode,0,draft.Count-1);Dirty=true;}
        public void MoveNode(int index,int offset)
        {if(!EditableNode(index))return;int other=index+offset;if(other<0||other>=draft.Count||other==index)return;var node=draft[index];draft[index]=draft[other];draft[other]=node;SelectedNode=other;Dirty=true;}
        public void ClearRoute(){if(!CanEdit||selection.Count==0||draft.Count==0&&!RoutesMixed)return;draft.Clear();SelectedNode=0;Dirty=true;RoutesMixed=false;}
        public GameCommand BuildPlanCommand()
        {
            int count=Math.Min(256,selection.Count);var ids=new string[count];for(int i=0;i<count;i++)ids[i]=selection[i];
            var nodes=new TacticNode[draft.Count];for(int i=0;i<nodes.Length;i++)nodes[i]=Clone(draft[i]);
            return new GameCommand{type="tactics_set",unitIds=ids,nodes=nodes};
        }
        public void Save()
        {
            if(!CanEdit||selection.Count==0||RoutesMixed)return;
            GameCommand command=BuildPlanCommand();
            foreach(string id in selection)
            {
                var copy=new List<TacticNode>();foreach(var node in draft)copy.Add(Clone(node));
                IReadOnlyList<TacticNode> actual;TryGetSnapshotRoute(id,out actual);
                pendingRoutes[id]=new PendingRoute{nodes=copy,expires=Time.unscaledTime+5f,sawDifferent=!SameNodes(copy,actual)};
            }
            for(int start=0;start<selection.Count;start+=256)
            {
                int count=Math.Min(256,selection.Count-start);var ids=new string[count];selection.CopyTo(start,ids,0,count);
                app.Send(new GameCommand{type="tactics_set",unitIds=ids,nodes=command.nodes});
            }
            Dirty=false;
        }
        public void Finish()
        {
            if(!Active)return;
            if(!app.Me.ready&&Dirty&&selection.Count>0)Save();
            app.Send(new GameCommand{type="tactics_ready"});
        }
        public bool IsDraftSynced
        {
            get
            {
                if(Dirty||RoutesMixed||selection.Count==0||app.Me==null)return false;
                foreach(string id in selection)
                {
                    bool matched=draft.Count==0;
                    foreach(var plan in app.Me.tactics)
                    {
                        if(Array.IndexOf(plan.unitIds,id)<0)continue;
                        matched=SameNodes(plan.nodes,draft);break;
                    }
                    if(!matched)return false;
                }
                return true;
            }
        }
        public void ArmSkill(bool feedback=true)
        {
            if(app.State==null||app.State.phase!="battle"||app.Me==null||app.Me.eliminated||app.BattlefieldInputBlocked||app.Hud.KeyboardCaptured)return;
            if(app.Me.commanderUses<=0||app.Me.commanderCooldown>0)return;
            Targeting=!Targeting;
            if(feedback)Feedback("ui_click",1);
            if(Targeting)app.Hud.ClosePinnedInspection();
        }
        public void CancelTargeting(){bool wasTargeting=Targeting;Targeting=false;CancelPointerGesture();if(wasTargeting)Feedback("ui_click",1);}
        public void SuspendInput(){Targeting=false;CancelPointerGesture();SkillPickerOpen=false;OptionsOpen=false;}
        void CancelPointerGesture(){bool owned=boxing||draggingUnit;boxing=false;draggingUnit=false;boxMoved=false;shiftBox=false;clickedUnit=null;if(owned&&app!=null)app.CancelDragging();}
        bool CanEdit { get { return Active&&app.Connected&&!app.Me.ready&&!app.ShowGameMenu; } }
        bool EditableNode(int index){return CanEdit&&index>=0&&index<draft.Count;}
        void Feedback(string cue,float volume){if(app!=null&&app.Sound!=null)app.Sound.Play(cue,Vector3.zero,volume);}
        void RouteFeedback(){if(Time.unscaledTime<nextRouteSound)return;nextRouteSound=Time.unscaledTime+.09f;Feedback("route_node",.38f);}
        public void Cast(Vector2 mouse)
        {
            if(app.Me==null||app.State==null||app.State.phase!="battle"||app.Me.eliminated||app.BattlefieldInputBlocked||app.Hud.KeyboardCaptured)return;
            if(app.Me.commanderUses<=0){app.Notify("本场指挥技已用尽。");Targeting=false;return;}
            if(app.Me.commanderCooldown>0){app.Notify("指挥技仍在冷却。");return;}
            Point2 at;if(!PointAt(mouse,out at))return;
            VoxelCreature view;FighterState fighter;
            string target=null;
            if(app.World.TryInspectCreature(mouse,out view,out fighter)&&fighter!=null&&fighter.ownerId!=app.PlayerId&&!fighter.dead)target=fighter.id;
            CommanderDefinition skill=BattlefieldCatalog.GetCommander(app.Me.commanderSkill);
            if(skill.id=="focus"&&target==null){app.Notify("选择敌人");return;}
            if(skill.id=="focus"){at.x=fighter.x;at.y=fighter.y;}
            float dx=at.x-app.Me.steve.x,dy=at.y-app.Me.steve.y;
            if(dx*dx+dy*dy>200*200){app.Notify("超出施法范围");return;}
            if((skill.id=="trap"||skill.id=="decoy"||skill.id=="rift")&&!BattlefieldCatalog.IsPassable(app.State.battlefield,at.x,at.y))
            {app.Notify("无法放置");return;}
            app.Send(new GameCommand{type="cast",x=at.x,y=at.y,targetId=target});Targeting=false;
        }
        public Point2 SelectionCenter()
        {
            float x=0,y=0;int count=0;
            if(app.Me!=null)foreach(var unit in app.Me.units)if(selection.Contains(unit.id))
            {
                Point2 point;
                if(TryGetSelectionPosition(unit.id,out point)){x+=point.x;y+=point.y;}
                else{x+=unit.x;y+=unit.y;}
                count++;
            }
            return count==0?new Point2():new Point2(x/count,y/count);
        }
        public Vector3 WorldPoint(float x,float y,float lift=0)
        {return app.World==null?new Vector3(x/25f,.06f+lift,y/25f):app.World.WorldSurfacePoint(x,y)+Vector3.up*lift;}
        public bool PointAt(Vector2 screen,out Point2 at)
        {
            at=new Point2();return app.World!=null&&app.World.TryGround(screen,out at);
        }
        ObjectiveState NearestObjective(float x,float y,float radius)
        {
            if(app.State==null||app.State.battlefield==null)return null;
            ObjectiveState closest=null;float best=radius*radius;
            foreach(var objective in app.State.battlefield.objectives){float dx=objective.x-x,dy=objective.y-y,d=dx*dx+dy*dy;if(d<best){best=d;closest=objective;}}
            return closest;
        }
        static string Next(string[] values,string value){return values[(Array.IndexOf(values,value)+1)%values.Length];}
        public static TacticNode Clone(TacticNode n){return new TacticNode{action=n.action,condition=n.condition,targetId=n.targetId,x=n.x,y=n.y,seconds=n.seconds,threshold=n.threshold};}
        static bool SameNodes(IReadOnlyList<TacticNode> a,IReadOnlyList<TacticNode> b)
        {
            if(a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++)if(a[i].action!=b[i].action||a[i].condition!=b[i].condition||!String.Equals(a[i].targetId??"",b[i].targetId??"",StringComparison.Ordinal)||Mathf.Abs(a[i].x-b[i].x)>.1f||Mathf.Abs(a[i].y-b[i].y)>.1f||a[i].seconds!=b[i].seconds||a[i].threshold!=b[i].threshold)return false;
            return true;
        }
    }
}
