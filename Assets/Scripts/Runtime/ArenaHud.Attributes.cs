using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        static readonly string[] CreatureAttributeNames = { "攻击", "攻速", "射程", "护甲", "移速", "视野" };
        static readonly string[] CreatureAttributeIcons = { "sword", "speed", "range", "shield", "stride", "vision" };
        internal string PinnedConcealmentText { get { return RefreshPinnedInspection()?ConcealmentText(pinnedFighter):""; } }
        internal string HoveredConcealmentText { get { return ConcealmentText(inspectionFighter); } }
        internal static string ConcealmentText(FighterState fighter)
        {
            if(fighter==null||fighter.dead)return "";
            var text=new StringBuilder();int source=fighter.concealmentSources;
            if(source!=0){
                text.Append(fighter.hidden?"隐蔽 · ":"隐蔽来源 · ");
                if((source&1)!=0)text.Append("树林");
                if((source&2)!=0){if((source&1)!=0)text.Append(" / ");text.Append("烟幕");}
                if((source&4)!=0){if((source&3)!=0)text.Append(" / ");text.Append("伏击");}
            }else if(fighter.hidden)text.Append("隐蔽");
            if((source!=0||fighter.hidden)&&fighter.attackRevealRemaining>0){
                if(text.Length>0)text.Append('\n');text.Append("攻击暴露 ").Append(fighter.attackRevealRemaining.ToString("0.0")).Append(" 秒");
            }
            return text.ToString();
        }
        static float InspectionConcealmentHeight(FighterState fighter)
        {
            if(fighter==null||fighter.dead)return 0;
            bool source=fighter.hidden||fighter.concealmentSources!=0,reveal=source&&fighter.attackRevealRemaining>0;
            return source&&reveal?50:source||reveal?28:0;
        }
        static float InspectionSkillsTop(FighterState fighter) { return 258+InspectionConcealmentHeight(fighter); }
        void DrawConcealmentInspection(Rect panel,FighterState fighter)
        {
            string text=ConcealmentText(fighter);if(text.Length==0)return;
            float y=panel.y+251;
            ArenaIcons.Draw(new Rect(panel.x+18,y+3,20,20),fighter.hidden?"concealment":"vision");
            Text(new Rect(panel.x+44,y,panel.width-62,InspectionConcealmentHeight(fighter)),ArenaDescription.Format(text),micro,fighter.hidden?mint:gold);
        }

        // Combat fields already contain the host's weather, terrain and command modifiers.
        // Only movement has an additional status multiplier; read the shared simulation helper.
        internal string[] CreatureAttributeValues(CreatureDefinition definition,FighterState fighter,bool combat)
        {
            FighterState f=fighter??ArenaGame.PreviewFighter(new PlayerState(),new UnitState{kind=definition.id});
            BattlefieldState world=combat&&app.State!=null?app.State.battlefield:null;
            return new[]{
                f.attack.ToString("0.#"),f.attackSpeed.ToString("0.##")+"/秒",
                f.range.ToString("0.#"),(f.armor*100).ToString("0.#")+"%",
                BattlefieldVision.MovementSpeed(f).ToString("0.#"),BattlefieldVision.Range(world,f).ToString("0.#")
            };
        }
        internal string PinnedInspectionAttributeText {
            get {
                if(!RefreshPinnedInspection())return "";
                return AttributeText(CreatureAttributeValues(pinnedDefinition,pinnedFighter,app.State.phase=="battle"||app.State.phase=="results"));
            }
        }
        internal string BestiaryAttributeText(string kind)
        {
            CreatureDefinition d=CreatureCatalog.Get(kind);
            return "生命 "+d.hp.ToString("0.#")+"\n"+AttributeText(CreatureAttributeValues(d,null,false))+"\n"+(d.flying?"飞行":"地面");
        }
        static string AttributeText(string[] values)
        {
            var text=new StringBuilder();for(int i=0;i<values.Length;i++){
                if(i>0)text.Append('\n');text.Append(CreatureAttributeNames[i]).Append(' ').Append(values[i]);
            }
            return text.ToString();
        }
        void AttributeCell(Rect r,string icon,string name,string value,bool spacious=false)
        {
            RoundFill(r,new Color(.055f,.12f,.14f),6);
            float iconSize=spacious?24:21;
            ArenaIcons.Draw(new Rect(r.x+8,r.center.y-iconSize/2,iconSize,iconSize),icon);
            if(spacious){
                Text(new Rect(r.x+42,r.y+5,r.width-49,21),name,micro,muted);
                Text(new Rect(r.x+42,r.y+25,r.width-49,28),value,small,white);
            }else{
                Text(new Rect(r.x+35,r.y+4,45,r.height-6),name,micro,muted);
                int alignment=(int)small.alignment;small.alignment=TextAnchor.MiddleRight;
                Text(new Rect(r.x+77,r.y+1,r.width-84,r.height-2),value,small,white);
                small.alignment=(TextAnchor)alignment;
            }
        }
        void BestiaryAttributes(CreatureDefinition definition)
        {
            string[] values=CreatureAttributeValues(definition,null,false);
            AttributeCell(new Rect(10,266,166,59),"heart","生命",definition.hp.ToString("0.#"),true);
            for(int i=0;i<values.Length;i++){
                int slot=i+1;AttributeCell(new Rect(10+(slot%4)*174,266+(slot/4)*66,166,59),CreatureAttributeIcons[i],CreatureAttributeNames[i],values[i],true);
            }
            AttributeCell(new Rect(532,332,166,59),"stride","行动",definition.flying?"飞行":"地面",true);
        }
    }
}
