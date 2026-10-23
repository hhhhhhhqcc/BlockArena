using System;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        ObjectiveState hoveredObjective;
        internal string HoveredObjectiveId { get { return hoveredObjective==null?null:hoveredObjective.id; } }
        internal string HoveredObjectiveText { get { return hoveredObjective==null?"":ObjectiveDescription(hoveredObjective); } }

        public void SetObjectiveInspection(ObjectiveState objective,Vector2 screen)
        {
            if(objective==null){ClearHover();return;}
            if(!LayerAcceptsPointer())return;
            SetHover(objective.name,ObjectiveDescription(objective),screen);hoveredObjective=objective;
        }
        internal string ObjectiveDescription(ObjectiveState objective)
        {
            bool heat=app.State!=null&&app.State.battlefield!=null&&app.State.battlefield.weather=="heat";
            string effect;
            switch(objective.kind){
                case "altar":effect="占领后，全军攻速 +5%。";break;
                case "tower":effect="全军获得侦察视野；不与自带侦察叠加。";break;
                case "beacon":effect="占领后，全军每秒回复 "+(heat?"0.8%":"1%")+" 最大生命。";break;
                case "supply":effect=objective.claimed?"补给已取走，本轮不再刷新。":"一次性补给：100 范围内友军回复 "+(heat?"16%":"20%")+" 最大生命。";break;
                default:effect="首次占领获得金币，可被其他军团夺取。";break;
            }
            if(objective.claimed)return effect;
            int reward=Math.Max(0,Math.Min(2,objective.reward));
            return effect+"\n仅己方生物在区域内时，3 秒占领。"+
                (reward>0?"\n每军团首次占领 +"+reward+" 金币；每轮目标共最多 +4。":"");
        }
        void ObjectiveInspection()
        {
            ObjectiveState objective=hoveredObjective;if(objective==null)return;
            const float panelWidth=330,innerWidth=290;
            string body=ArenaDescription.Format(ObjectiveDescription(objective));
            float bodyHeight=small.CalcHeight(new GUIContent(body),innerWidth);
            bool progress=!objective.claimed&&objective.contenderId!=null&&objective.capture>0;
            Rect r=TooltipRect(panelWidth,96+bodyHeight+(progress?24:0));hoverTooltipRect=r;
            Panel(r,new Color(.04f,.08f,.09f,.985f));
            string icon=objective.kind=="mine"?"coin":objective.kind=="tower"?"vision":objective.kind=="supply"?"bag":objective.kind=="beacon"?"heart":"speed";
            ArenaIcons.Draw(new Rect(r.x+16,r.y+16,28,28),icon);
            Text(new Rect(r.x+55,r.y+13,r.width-72,31),objective.name,label,gold);
            string owner=objective.claimed?"已领取":string.IsNullOrEmpty(objective.ownerId)?"无主":PlayerNameFor(objective.ownerId);
            Text(new Rect(r.x+20,r.y+47,innerWidth,24),owner,micro,muted);
            Text(new Rect(r.x+20,r.y+78,innerWidth,bodyHeight+3),body,small,white);
            if(progress){
                Rect bar=new Rect(r.x+20,r.yMax-15,innerWidth,5);RoundFill(bar,new Color(.12f,.20f,.20f),2);
                PlayerState contender=app.State.players.Find(p=>p.id==objective.contenderId);
                RoundFill(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(objective.capture),bar.height),contender==null?mint:Team(contender.slot),2);
            }
        }
    }
}
