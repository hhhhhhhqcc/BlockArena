using System;
using System.Collections.Generic;
using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        sealed class InspectionSkill { internal string id, name, icon; }
        readonly List<InspectionSkill> inspectionSkills = new List<InspectionSkill>();
        sealed class PinnedSkillLayout
        {
            internal string[] title, body;
            internal float lineHeight, titleHeight, bodyHeight, rowHeight;
        }
        readonly Dictionary<string,PinnedSkillLayout> pinnedLayouts = new Dictionary<string,PinnedSkillLayout>();
        GUIStyle pinnedLineStyle;
        int pinnedLayoutFontSize;
        string inspectionSubject, inspectionTeam, inspectionKind, inspectionSkillPlain = "";
        string pinnedInspectionId, pinnedInspectionOwner;
        int pinnedInspectionRound;
        Vector2 pinnedSkillScroll, pinnedPosition, pinnedDragOffset;
        bool pinnedPositionSet, inspectionDragging, inspectionPointerOwned;
        int inspectionPointerFrame=-1, inspectionReleasedFrame=-10;
        Rect inspectionTooltip;
        public string PinnedInspectionId { get { return pinnedInspectionId; } }
        public string PinnedInspectionOwnerId { get { return pinnedInspectionOwner; } }
        internal Rect PinnedInspectionScreenRect { get { Metrics();return PinnedInspectionAvailable?ToScreenRect(PinnedInspectionRect):Rect.zero; } }
        internal Rect PinnedDismissScreenRect { get { Metrics();if(!PinnedInspectionAvailable)return Rect.zero;Rect r=PinnedInspectionRect;return ToScreenRect(new Rect(r.x+17,r.yMax-48,r.width-32,34)); } }
        internal int PinnedInspectionSkillCount { get { return RefreshPinnedInspection()?InspectionSkillIds(pinnedDefinition,pinnedFighter).Length:0; } }
        internal string PinnedInspectionSkillText
        {
            get
            {
                if(!RefreshPinnedInspection())return "";
                var text=new StringBuilder();
                foreach(string id in InspectionSkillIds(pinnedDefinition,pinnedFighter)){
                    var ability=CreatureAbilities.Get(id);if(text.Length>0)text.Append('\n');
                    text.Append(ability.name).Append(" · ").Append(ArenaDescription.Brief(ability));
                }
                return text.ToString();
            }
        }
        internal Rect PinnedInspectionSkillsScreenRect { get { Metrics();return PinnedInspectionAvailable?ToScreenRect(PinnedSkillsViewport(PinnedInspectionRect)):Rect.zero; } }
        internal bool PinnedInspectionSkillsScrollable { get { Metrics();return PinnedInspectionAvailable&&PinnedSkillsHeight(pinnedDefinition,pinnedFighter,316)>PinnedSkillsViewport(PinnedInspectionRect).height; } }
        internal Vector2 PinnedInspectionSkillScroll { get { return pinnedSkillScroll; } set { pinnedSkillScroll=value; } }
        internal int PinnedSkillBodyLineCount(string abilityId) { Setup();Metrics();return LayoutPinnedSkill(CreatureAbilities.Get(abilityId),316).body.Length; }
        internal Rect[] PinnedSkillBodyLineScreenRects(string abilityId)
        {
            Metrics();if(!PinnedInspectionAvailable)return new Rect[0];
            Rect viewport=PinnedSkillsViewport(PinnedInspectionRect);float rowWidth=viewport.width-22,y=0;
            foreach(string id in InspectionSkillIds(pinnedDefinition,pinnedFighter)){
                PinnedSkillLayout layout=LayoutPinnedSkill(CreatureAbilities.Get(id),rowWidth);
                if(id==abilityId){
                    var result=new Rect[layout.body.Length];for(int i=0;i<result.Length;i++)result[i]=ToScreenRect(new Rect(viewport.x+10,viewport.y+y+10+layout.titleHeight+7+i*layout.lineHeight-pinnedSkillScroll.y,rowWidth-20,layout.lineHeight));
                    return result;
                }
                y+=layout.rowHeight;
            }
            return new Rect[0];
        }
        internal bool PinnedSkillTextFits(AbilityDefinition ability,float rowWidth)
        {
            Setup();Metrics();PinnedSkillLayout layout=LayoutPinnedSkill(ability,rowWidth);
            if(StripInspectionMarkup(string.Concat(layout.body)).Replace("\n","")!=
                StripInspectionMarkup(ArenaDescription.Format(ArenaDescription.Brief(ability))).Replace("\n",""))return false;
            foreach(string line in layout.body)
                if(pinnedLineStyle.CalcSize(new GUIContent(line)).x>rowWidth-20+.5f||
                    pinnedLineStyle.CalcHeight(new GUIContent(line),rowWidth-20)>layout.lineHeight+.5f)return false;
            foreach(string line in layout.title)
                if(pinnedLineStyle.CalcSize(new GUIContent(line)).x>rowWidth-46+.5f)return false;
            return layout.bodyHeight>=layout.body.Length*layout.lineHeight&&layout.rowHeight-8>=10+layout.titleHeight+7+layout.bodyHeight+10;
        }
        internal Rect RecruitRestoreScreenRect { get { Metrics();return ToScreenRect(RecruitRestoreRect); } }
        internal bool InspectionExpanded { get { return inspectionDefinition != null && Time.unscaledTime <= hoverUntil; } }
        internal int InspectionSkillCount { get { return InspectionExpanded ? inspectionSkills.Count : 0; } }
        internal string InspectionSkillText { get { return InspectionExpanded ? inspectionSkillPlain : ""; } }
        internal Rect InspectionTooltipScreenRect { get { return InspectionExpanded ? inspectionTooltip : Rect.zero; } }
        Rect PinnedInspectionRect {
            get {
                float h=InspectionPanelHeight(pinnedFighter,pinnedDefinition,true);
                if(!pinnedPositionSet){float x=width-388;if(MarketVisible)x=Mathf.Max(18,MarketRect.x-388);return new Rect(x,Mathf.Clamp(338,84,Mathf.Max(84,CommandDockRect.y-h-12)),370,h);}
                pinnedPosition.x=Mathf.Clamp(pinnedPosition.x,10,Mathf.Max(10,width-380));
                pinnedPosition.y=Mathf.Clamp(pinnedPosition.y,10,Mathf.Max(10,height-h-10));
                return new Rect(pinnedPosition.x,pinnedPosition.y,370,h);
            }
        }
        internal Rect PinnedInspectionDragScreenRect { get { Metrics();Rect r=PinnedInspectionRect;return PinnedInspectionAvailable?ToScreenRect(new Rect(r.x,r.y,r.width-54,73)):Rect.zero; } }
        internal bool InspectionDragging { get { return inspectionDragging; } }
        void UpdateInspectionPointer()
        {
            if(inspectionPointerFrame==Time.frameCount||app.VerificationInput)return;
            ProcessInspectionPointer(HudPointer,Input.GetMouseButtonDown(0),Input.GetMouseButton(0),Input.GetMouseButtonUp(0));
        }
        internal void ProcessInspectionPointer(Vector2 screen,bool pressed,bool held,bool released)
        {
            Metrics();inspectionPointerFrame=Time.frameCount;
            if(!PinnedInspectionAvailable||ModalOpen||audioPointerOwned){
                if(inspectionPointerOwned||inspectionDragging)inspectionReleasedFrame=Time.frameCount;
                inspectionPointerOwned=inspectionDragging=false;return;
            }
            Vector2 p=new Vector2(screen.x/scale,(Screen.height-screen.y)/scale);Rect r=PinnedInspectionRect;
            if(pressed&&!marketPointerOwned&&!skillPointerOwned&&!CommunicationCovers(p)&&!CommanderOverlayCovers(p)&&r.Contains(p)){
                inspectionPointerOwned=true;
                if(new Rect(r.x,r.y,r.width-54,73).Contains(p)){
                    pinnedPosition=r.position;pinnedPositionSet=true;inspectionDragging=true;pinnedDragOffset=p-pinnedPosition;ClearHover();
                }
            }
            if(inspectionDragging&&(held||released)){pinnedPosition=p-pinnedDragOffset;Rect clamped=PinnedInspectionRect;}
            if(released||(!held&&!pressed)){
                if(inspectionPointerOwned||inspectionDragging)inspectionReleasedFrame=Time.frameCount;
                inspectionPointerOwned=inspectionDragging=false;
            }
        }
        CreatureDefinition pinnedDefinition;
        FighterState pinnedFighter;
        UnitState pinnedUnit;
        PlayerState pinnedOwner;
        bool PinnedInspectionAvailable { get { return RefreshPinnedInspection()&&!app.ShowGameMenu&&!app.ShowRoom&&!app.ShowBestiary&&!RecruitVisible&&app.State.phase!="finished"&&app.State.phase!="lobby"; } }
        bool PinnedInspectionCovers(Vector2 p) { return PinnedInspectionAvailable&&PinnedInspectionRect.Contains(p); }

        void ResetInspection()
        {
            inspectionSubject=inspectionTeam=inspectionKind=null;inspectionTooltip=Rect.zero;
            inspectionSkills.Clear();inspectionSkillPlain="";
        }
        void BeginInspection(CreatureDefinition definition,FighterState fighter,UnitState unit,PlayerState owner)
        {
            string subject=fighter!=null?fighter.id:unit!=null?unit.id:definition.id;
            string team=owner!=null?owner.id:fighter!=null?fighter.ownerId:null;
            if(inspectionSubject!=subject||inspectionTeam!=team||inspectionKind!=definition.id){ResetInspection();inspectionSubject=subject;inspectionTeam=team;inspectionKind=definition.id;}
        }
        public void PinInspection(CreatureDefinition definition,FighterState fighter,UnitState unit,PlayerState owner,Vector2 screenPosition)
        {
            if(definition==null||app.State==null)return;
            pinnedInspectionId=fighter!=null?fighter.id:unit!=null?unit.id:null;
            pinnedInspectionOwner=owner!=null?owner.id:fighter!=null?fighter.ownerId:null;
            pinnedInspectionRound=app.State.round;pinnedDefinition=definition;pinnedFighter=fighter;pinnedUnit=unit;pinnedOwner=owner;
            pinnedSkillScroll=Vector2.zero;
            ClearHover();
        }
        public void ClosePinnedInspection(){if(inspectionPointerOwned||inspectionDragging)inspectionReleasedFrame=Time.frameCount;inspectionPointerOwned=inspectionDragging=false;pinnedInspectionId=pinnedInspectionOwner=null;pinnedDefinition=null;pinnedFighter=null;pinnedUnit=null;pinnedOwner=null;pinnedSkillScroll=Vector2.zero;}
        bool RefreshPinnedInspection()
        {
            if(string.IsNullOrEmpty(pinnedInspectionId)||app.State==null)return false;
            if(app.State.round!=pinnedInspectionRound||app.State.phase=="lobby"){ClosePinnedInspection();return false;}
            pinnedOwner=app.State.players.Find(p=>p.id==pinnedInspectionOwner);
            bool combat=app.State.phase=="battle"||app.State.phase=="results"||app.State.phase=="finished";
            pinnedFighter=combat?app.State.fighters.Find(f=>f.id==pinnedInspectionId):null;
            pinnedUnit=pinnedOwner==null?null:pinnedOwner.units.Find(u=>u.id==pinnedInspectionId);
            if(combat&&pinnedFighter==null){ClosePinnedInspection();return false;}
            if(pinnedFighter==null&&pinnedUnit==null){ClosePinnedInspection();return false;}
            if(pinnedFighter!=null)pinnedDefinition=CreatureCatalog.Get(pinnedFighter.kind);
            else {pinnedDefinition=CreatureCatalog.Get(pinnedUnit.kind);pinnedFighter=ArenaGame.PreviewFighter(pinnedOwner,pinnedUnit);}
            return pinnedDefinition!=null;
        }
        void CacheInspectionSkills()
        {
            string[] ids=InspectionSkillIds(inspectionDefinition,inspectionFighter);
            bool changed=ids.Length!=inspectionSkills.Count;if(!changed)for(int i=0;i<ids.Length;i++)if(inspectionSkills[i].id!=ids[i]){changed=true;break;}
            if(!changed)return;inspectionSkills.Clear();var plain=new StringBuilder();
            foreach(string id in ids){var a=CreatureAbilities.Get(id);inspectionSkills.Add(new InspectionSkill{id=id,name=a.name,icon=AbilityIcon(a)});if(plain.Length>0)plain.Append('、');plain.Append(a.name);}
            inspectionSkillPlain=plain.ToString();
        }
        static string[] InspectionSkillIds(CreatureDefinition d,FighterState f)
        { return f!=null&&f.skills!=null&&(f.neutral||f.skills.Length>0)?f.skills:d!=null&&d.skills!=null?d.skills:new string[0]; }
        static string AbilityIcon(AbilityDefinition ability)
        {return ability.effect=="poison"?"poison":ability.effect=="burn"?"fire":ability.effect=="slow"?"frost":ability.effect=="stun"?"stun":ability.icon=="armor"?"shield":ability.icon=="attack"?"sword":ability.icon??"book";}
        float InspectionPanelHeight(FighterState f,CreatureDefinition d,bool pinned)
        {
            if(pinned){
                float maximum=Mathf.Max(312,Mathf.Min(height-112,CommandDockRect.y-96));
                return Mathf.Min(maximum,InspectionSkillsTop(f)+PinnedSkillsHeight(d,f,316)+66);
            }
            int skills=InspectionSkillIds(d,f).Length;return Mathf.Min(height-112,286+InspectionConcealmentHeight(f)+Mathf.Max(1,Mathf.CeilToInt(skills/2f))*34);
        }
        Rect PinnedSkillsViewport(Rect panel)
        { float extra=InspectionConcealmentHeight(pinnedFighter);return new Rect(panel.x+17,panel.y+258+extra,panel.width-32,Mathf.Max(32,panel.height-320-extra)); }
        float PinnedSkillRowHeight(AbilityDefinition ability,float rowWidth)
        {
            return LayoutPinnedSkill(ability,rowWidth).rowHeight;
        }
        PinnedSkillLayout LayoutPinnedSkill(AbilityDefinition ability,float rowWidth)
        {
            // Native rich-text CalcHeight can disagree with the subsequent CJK wrap at
            // fractional GUI scales. Freeze the measured lines, then render each line
            // without a second wrap. Measurement and drawing use the identical style.
            if(pinnedLineStyle==null)pinnedLineStyle=new GUIStyle(small){wordWrap=false,alignment=TextAnchor.UpperLeft,clipping=TextClipping.Clip,padding=new RectOffset(),margin=new RectOffset()};
            if(pinnedLayoutFontSize!=small.fontSize||pinnedLineStyle.font!=small.font){
                pinnedLayoutFontSize=small.fontSize;pinnedLineStyle.fontSize=small.fontSize;pinnedLineStyle.font=small.font;pinnedLayouts.Clear();
            }
            string key=ability.id+":"+rowWidth.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
            PinnedSkillLayout layout;if(pinnedLayouts.TryGetValue(key,out layout))return layout;
            float lineHeight=Mathf.Ceil(Mathf.Max(pinnedLineStyle.lineHeight,pinnedLineStyle.CalcHeight(new GUIContent("生命 Ag"),1000)))+3;
            layout=new PinnedSkillLayout{
                title=WrapInspectionRichText(ability.name,Mathf.Max(40,rowWidth-46)),
                body=WrapInspectionRichText(ArenaDescription.Format(ArenaDescription.Brief(ability)),Mathf.Max(40,rowWidth-20)),
                lineHeight=lineHeight
            };
            layout.titleHeight=Mathf.Max(22,layout.title.Length*lineHeight);layout.bodyHeight=Mathf.Max(lineHeight,layout.body.Length*lineHeight);
            layout.rowHeight=10+layout.titleHeight+7+layout.bodyHeight+10+8;
            pinnedLayouts[key]=layout;return layout;
        }
        string[] WrapInspectionRichText(string rich,float textWidth)
        {
            var lines=new List<string>();var line=new StringBuilder();var tags=new List<string>();int visible=0;
            for(int i=0;i<rich.Length;){
                if(rich[i]=='<'){
                    int end=rich.IndexOf('>',i);if(end>=i){
                        string tag=rich.Substring(i,end-i+1);line.Append(tag);
                        if(tag.StartsWith("</",StringComparison.Ordinal)){if(tags.Count>0)tags.RemoveAt(tags.Count-1);}else tags.Add(tag);
                        i=end+1;continue;
                    }
                }
                if(rich[i]=='\r'){i++;continue;}
                int start=i;
                if(IsInspectionWordCharacter(rich[i])){
                    i++;while(i<rich.Length&&IsInspectionWordCharacter(rich[i]))i++;
                }else i+=char.IsHighSurrogate(rich[i])&&i+1<rich.Length?2:1;
                string glyph=rich.Substring(start,i-start);
                string endings=CloseInspectionTags(tags);
                if(glyph=="\n"||visible>0&&pinnedLineStyle.CalcSize(new GUIContent(line.ToString()+glyph+endings)).x>textWidth-2){
                    lines.Add(line.ToString()+endings);line.Length=0;foreach(string open in tags)line.Append(open);visible=0;
                    if(glyph=="\n")continue;
                }
                line.Append(glyph);visible++;
            }
            if(visible>0||lines.Count==0)lines.Add(line.ToString()+CloseInspectionTags(tags));
            return lines.ToArray();
        }
        static bool IsInspectionWordCharacter(char value)
        {
            return value>='0'&&value<='9'||value>='a'&&value<='z'||value>='A'&&value<='Z'||value=='%'||value=='.'||value=='+'||value=='-';
        }
        static string CloseInspectionTags(List<string> tags)
        {
            var result=new StringBuilder();for(int i=tags.Count-1;i>=0;i--){
                string tag=tags[i];int end=tag.IndexOf('=');if(end<0)end=tag.Length-1;
                result.Append("</").Append(tag,1,end-1).Append('>');
            }
            return result.ToString();
        }
        static string StripInspectionMarkup(string rich)
        {
            return System.Text.RegularExpressions.Regex.Replace(rich,"<[^>]+>","");
        }
        float PinnedSkillsHeight(CreatureDefinition definition,FighterState fighter,float rowWidth)
        {
            string[] ids=InspectionSkillIds(definition,fighter);if(ids.Length==0)return 32;
            float height=0;foreach(string id in ids)height+=PinnedSkillRowHeight(CreatureAbilities.Get(id),rowWidth);
            return height;
        }
        void DrawPinnedSkillEffects(Rect panel,CreatureDefinition definition,FighterState fighter)
        {
            Rect viewport=PinnedSkillsViewport(panel);float rowWidth=viewport.width-22;
            float contentHeight=PinnedSkillsHeight(definition,fighter,rowWidth);
            pinnedSkillScroll=BeginUiScroll(viewport,pinnedSkillScroll,new Rect(0,0,rowWidth,Mathf.Max(viewport.height,contentHeight)));
            float y=0;string[] ids=InspectionSkillIds(definition,fighter);
            foreach(string id in ids){
                var ability=CreatureAbilities.Get(id);PinnedSkillLayout layout=LayoutPinnedSkill(ability,rowWidth);float rowHeight=layout.rowHeight;
                RoundFill(new Rect(0,y,rowWidth,rowHeight-8),new Color(.065f,.13f,.15f),6);
                ArenaIcons.Draw(new Rect(8,y+10,22,22),AbilityIcon(ability));
                for(int i=0;i<layout.title.Length;i++)Text(new Rect(38,y+10+i*layout.lineHeight,rowWidth-46,layout.lineHeight),layout.title[i],pinnedLineStyle,mint);
                for(int i=0;i<layout.body.Length;i++)Text(new Rect(10,y+10+layout.titleHeight+7+i*layout.lineHeight,rowWidth-20,layout.lineHeight),layout.body[i],pinnedLineStyle,white);
                y+=rowHeight;
            }
            if(ids.Length==0)Text(new Rect(0,0,rowWidth,27),"—",micro,muted);
            GUI.EndScrollView();
        }
        void Inspection()
        {
            if(inspectionDefinition==null||inspectionSubject==pinnedInspectionId)return;
            Rect r=TooltipRect(370,InspectionPanelHeight(inspectionFighter,inspectionDefinition,false));inspectionTooltip=ToScreenRect(r);
            DrawInspectionPanel(r,inspectionDefinition,inspectionFighter,inspectionUnit,inspectionOwner,false);
        }
        void DrawPinnedInspection()
        {
            if(!PinnedInspectionAvailable)return;
            bool before=GUI.enabled;drawingArmy=true;
            DrawInspectionPanel(PinnedInspectionRect,pinnedDefinition,pinnedFighter,pinnedUnit,pinnedOwner,true);
            GUI.enabled=before;drawingArmy=false;
        }
        void DrawInspectionPanel(Rect r,CreatureDefinition d,FighterState f,UnitState unit,PlayerState owner,bool pinned)
        {
            float inner=r.width-32,hp=f==null?d.hp:f.hp,maxHp=f==null?d.hp:f.maxHp;
            Panel(r,new Color(.045f,.085f,.10f,.985f));
            if(pinned){
                RegisterPointerRect(new Rect(r.x,r.y,r.width-54,73));
                for(int i=0;i<3;i++)RoundFill(new Rect(r.center.x-10+i*7,r.y+7,3,3),inspectionDragging?mint:line,1);
            }
            Color accent=owner==null?mint:Team(owner.slot);
            Fill(new Rect(r.x+5,r.y+8,3,r.height-16),accent);
            ArenaIcons.Creature(new Rect(r.x+17,r.y+15,49,49),d.model??d.id,MobColor(d));
            Text(new Rect(r.x+77,r.y+13,r.width-127,32),d.name,label,white);
            Text(new Rect(r.x+77,r.y+45,r.width-100,24),owner==null?d.role:Trim(owner.name,8)+" · "+d.role,micro,accent);
            if(pinned&&IconButton(new Rect(r.xMax-43,r.y+12,29,29),"close","",new Color(.12f,.22f,.23f))){ClosePinnedInspection();return;}
            Rect bar=new Rect(r.x+17,r.y+82,inner,16);Fill(bar,new Color(.015f,.025f,.03f));Fill(new Rect(bar.x+2,bar.y+2,(bar.width-4)*Mathf.Clamp01(hp/Mathf.Max(1,maxHp)),12),f!=null&&f.dead?muted:new Color(.3f,.75f,.46f));
            Text(new Rect(bar.x,bar.y-5,bar.width,26),"<b>"+Mathf.CeilToInt(hp)+" / "+Mathf.CeilToInt(maxHp)+"</b>",centered,white);
            bool combat=app.State!=null&&(app.State.phase=="battle"||app.State.phase=="results"||app.State.phase=="finished");
            string[] attributes=CreatureAttributeValues(d,f,combat);
            for(int i=0;i<attributes.Length;i++)AttributeCell(new Rect(r.x+17+(i%2)*174,r.y+111+(i/2)*36,164,32),CreatureAttributeIcons[i],CreatureAttributeNames[i],attributes[i]);
            float sx=r.x+17;
            if(f!=null&&f.shield>0){ArenaIcons.Draw(new Rect(sx,r.y+225,21,21),"shield");Text(new Rect(sx+25,r.y+223,60,25),Mathf.CeilToInt(f.shield).ToString(),micro,mint);sx+=86;}
            string[] icons={"poison","fire","frost","stun","range","shield"};
            bool[] states={f!=null&&f.poisonTime>0,f!=null&&f.burnTime>0,f!=null&&f.slowTime>0,f!=null&&f.stunTime>0,f!=null&&f.rootTime>0,f!=null&&f.resistanceTime>0};
            for(int i=0;i<states.Length;i++)if(states[i]){ArenaIcons.Draw(new Rect(sx,r.y+225,21,21),icons[i]);sx+=31;}
            DrawConcealmentInspection(r,f);
            string[] ids=InspectionSkillIds(d,f);float skillTop=r.y+InspectionSkillsTop(f);
            Fill(new Rect(r.x+17,skillTop-8,inner,1),new Color(.24f,.39f,.38f));
            if(pinned)DrawPinnedSkillEffects(r,d,f);
            else for(int i=0;i<ids.Length;i++){
                var a=CreatureAbilities.Get(ids[i]);Rect chip=new Rect(r.x+17+(i%2)*(inner/2),skillTop+(i/2)*34,inner/2-4,29);
                RoundFill(chip,new Color(.08f,.16f,.17f),5);ArenaIcons.Draw(new Rect(chip.x+5,chip.y+4,20,20),AbilityIcon(a));
                Text(new Rect(chip.x+33,chip.y+3,chip.width-36,25),a.name,micro,mint);
            }
            if(!pinned&&ids.Length==0)Text(new Rect(r.x+17,skillTop,inner,27),"—",micro,muted);
            if(!pinned)return;
            bool preparation=app.State.phase=="prep"||app.State.phase=="recruit"||app.State.phase=="tactics";
            if(preparation&&owner!=null&&owner.id==app.PlayerId&&unit!=null){
                int refund=ArenaGame.DismissRefund(unit);bool enabled=GUI.enabled;
                GUI.enabled=enabled&&app.Connected&&!owner.ready&&owner.units.Count>1&&dismissalPendingId==null;
                if(IconButton(new Rect(r.x+17,r.yMax-48,inner,34),"coin","遣散  +"+refund,new Color(.29f,.23f,.14f))){dismissalPendingId=unit.id;dismissalPendingUntil=Time.unscaledTime+2;app.Send(new GameCommand{type="dismiss",unitId=unit.id});ClosePinnedInspection();}
                GUI.enabled=enabled;
            }else{
                Text(new Rect(r.x+18,r.yMax-45,inner,29),f!=null&&f.dead?"已阵亡":CategoryName(d.category)+" · "+TagNames(d.tags),micro,muted);
            }
        }
    }
}
