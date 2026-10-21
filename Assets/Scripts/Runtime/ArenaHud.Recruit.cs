using System.Collections.Generic;
using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        const float RecruitCardWidth = 308, RecruitCardGap = 22, RecruitEffectsY = 249;
        const float RecruitBodyWidth = RecruitCardWidth - 36, RecruitSheetPadding = 215;
        sealed class RecruitEffect
        {
            internal string name, icon, plain, rich;
            internal float height;
        }
        readonly Dictionary<string,List<RecruitEffect>> recruitEffects = new Dictionary<string,List<RecruitEffect>>();
        bool recruitHidden;
        int recruitVisibilityRound=-1;
        public bool RecruitVisible { get { SyncRecruitVisibility();return RecruitPending&&!recruitHidden; } }
        public bool RecruitOverlayVisible { get { return RecruitVisible; } }
        bool RecruitPending { get { return app.State!=null&&app.State.phase=="recruit"&&app.Me!=null&&app.Me.recruitPending&&!app.Me.eliminated; } }
        Rect RecruitRestoreRect { get { return ReadyRect; } }
        void SyncRecruitVisibility(){if(app.State==null){recruitVisibilityRound=-1;recruitHidden=false;return;}if(recruitVisibilityRound!=app.State.round){recruitVisibilityRound=app.State.round;recruitHidden=false;}if(!RecruitPending)recruitHidden=false;}
        public void SetRecruitVisible(bool visible){SyncRecruitVisibility();if(RecruitPending)recruitHidden=!visible;ClearHover();}
        public void ToggleRecruit(){SetRecruitVisible(!RecruitVisible);}
        void DrawRecruitRestore(){if(RecruitPending&&!RecruitVisible&&IconButton(RecruitRestoreRect,"bag","招募援军",new Color(.2f,.35f,.30f)))SetRecruitVisible(true);}
        string refreshPendingOffer;
        float refreshPendingUntil;

        List<RecruitEffect> RecruitEffectRows(CreatureDefinition creature)
        {
            List<RecruitEffect> rows;
            if (!recruitEffects.TryGetValue(creature.id, out rows)) {
                rows = new List<RecruitEffect>();
                foreach (string id in creature.skills) {
                    AbilityDefinition ability = CreatureAbilities.Get(id);
                    string brief = ArenaDescription.Brief(ability);
                    rows.Add(new RecruitEffect { name = ability.name, icon = AbilityIcon(ability), plain = brief, rich = ArenaDescription.Format(brief) });
                }
                recruitEffects.Add(creature.id, rows);
            }
            return rows;
        }

        float MeasureRecruitEffects(CreatureDefinition creature)
        {
            float result = 0;
            foreach (var effect in RecruitEffectRows(creature)) {
                effect.height = small.CalcHeight(new GUIContent(effect.rich), RecruitBodyWidth);
                result += 25 + effect.height + 12;
            }
            return result;
        }

        float RecruitCardHeight
        {
            get {
                float effectsHeight = 0;
                if (app.Me != null && app.Me.shop != null)
                    for (int i = 0; i < Mathf.Min(3, app.Me.shop.Length); i++)
                        effectsHeight = Mathf.Max(effectsHeight, MeasureRecruitEffects(CreatureCatalog.Get(app.Me.shop[i])));
                // Reserve a separate bottom purchase row so longer abilities never cover its button.
                return RecruitEffectsY + effectsHeight + 84;
            }
        }

        internal bool RecruitEffectsFit
        {
            get {
                Setup(); Metrics();
                float cardHeight = RecruitCardHeight;
                if (app.Me == null || app.Me.shop == null || cardHeight + RecruitSheetPadding > height - 36) return false;
                for (int i = 0; i < Mathf.Min(3, app.Me.shop.Length); i++)
                    if (RecruitEffectsY + MeasureRecruitEffects(CreatureCatalog.Get(app.Me.shop[i])) > cardHeight - 84 + .5f) return false;
                return RecruitCardWidth * 3 + RecruitCardGap * 2 <= width - 36;
            }
        }
        internal bool RecruitCreatureEffectsFit(CreatureDefinition creature)
        {
            Setup(); Metrics();
            return RecruitEffectsY + MeasureRecruitEffects(creature) + 84 + RecruitSheetPadding <= height - 36;
        }
        internal int RecruitSkillCount
        {
            get {
                int count = 0;
                if (app.Me != null && app.Me.shop != null)
                    for (int i = 0; i < Mathf.Min(3, app.Me.shop.Length); i++) count += RecruitEffectRows(CreatureCatalog.Get(app.Me.shop[i])).Count;
                return count;
            }
        }
        internal string RecruitEffectText
        {
            get {
                var text = new StringBuilder();
                if (app.Me != null && app.Me.shop != null)
                    for (int i = 0; i < Mathf.Min(3, app.Me.shop.Length); i++)
                        foreach (var effect in RecruitEffectRows(CreatureCatalog.Get(app.Me.shop[i]))) text.Append(effect.name).Append(" · ").Append(effect.plain).Append('\n');
                return text.ToString();
            }
        }

        void Recruit()
        {
            Shade();
            string[] choices = app.Me.shop; if (choices == null) return;
            float cardHeight = RecruitCardHeight, total = RecruitCardWidth * 3 + RecruitCardGap * 2;
            float x = (width-total)/2, y = (height-cardHeight-RecruitSheetPadding)/2;
            Text(new Rect(x,y,total,26),"第 "+app.State.round.ToString("00")+" 轮  /  招募",small,gold);
            Text(new Rect(x,y+32,total-250,54),app.State.round==1?"第一份契约":"招募援军",title,white);
            ArenaIcons.Draw(new Rect(x+total-205,y+39,36,36),"coin");
            Text(new Rect(x+total-160,y+37,160,40),app.Me.gold+" 金币",heading,gold);
            if(IconButton(new Rect(x+total-44,y-6,44,35),"close","",new Color(.14f,.23f,.23f)))SetRecruitVisible(false);
            bool pending = (pendingRound==app.State.round && Time.unscaledTime<pendingUntil) ||
                (refreshPendingOffer==app.Me.recruitOfferId&&Time.unscaledTime<refreshPendingUntil);
            for (int i=0;i<Mathf.Min(3,choices.Length);i++) {
                CreatureDefinition item = CreatureCatalog.Get(choices[i]); Color c = MobColor(item);
                Rect card = new Rect(x+i*(RecruitCardWidth+RecruitCardGap),y+127,RecruitCardWidth,cardHeight);
                Panel(card,new Color(.09f,.13f,.14f,.99f));
                recruitCards[i].Step(card.Contains(Mouse())?1:0,18,.6f);
                float entry = Mathf.Clamp(recruitMotion.value,0,1.12f), lift = recruitCards[i].value*5;
                Fill(new Rect(card.x+8,card.y+8,(card.width-16)*Mathf.Clamp01(entry),3),c);
                float iconScale = .83f+entry*.17f+recruitCards[i].value*.04f;
                ArenaIcons.Creature(new Rect(card.center.x-66*iconScale,card.y+66-48*iconScale+(1-entry)*18-lift,132*iconScale,96*iconScale),item.model??item.id,c);
                Text(new Rect(card.x+18,card.y+124,RecruitBodyWidth,36),item.name,heading,white);
                Text(new Rect(card.x+18,card.y+164,RecruitBodyWidth,25),CategoryName(item.category)+" · "+item.role,small,Color.Lerp(c,white,.45f));
                Stat(new Rect(card.x+18,card.y+199,82,28),"heart",item.hp.ToString("0.#"));
                Stat(new Rect(card.x+110,card.y+199,82,28),"sword",item.attack.ToString("0.#"));
                Stat(new Rect(card.x+202,card.y+199,88,28),"speed",item.attackSpeed.ToString("0.##"));
                Fill(new Rect(card.x+18,card.y+236,RecruitBodyWidth,1),new Color(.34f,.42f,.38f,.55f));
                float effectY = card.y+RecruitEffectsY;
                foreach (var effect in RecruitEffectRows(item)) {
                    ArenaIcons.Draw(new Rect(card.x+18,effectY,22,22),effect.icon);
                    Text(new Rect(card.x+49,effectY-1,card.width-67,25),"<b>"+effect.name+"</b>",small,mint);
                    Text(new Rect(card.x+18,effectY+25,RecruitBodyWidth,effect.height+3),effect.rich,small,white);
                    effectY += 25+effect.height+12;
                }
                bool affordable = app.Me.gold>=item.price; GUI.enabled = !pending&&affordable&&app.Connected;
                if (IconButton(new Rect(card.x+18,card.yMax-66,RecruitBodyWidth,49),"coin",pending?"确认中…":affordable?"招募  ·  "+item.price:"需要  "+item.price,new Color(.18f,.61f,.55f),"purchase")) {
                    pendingRound=app.State.round; pendingUntil=Time.unscaledTime+3; app.Send(new GameCommand{type="choose",index=i,offerId=app.Me.recruitOfferId});
                }
                GUI.enabled = true;
            }
            float footerY = y+127+cardHeight+20;
            GUI.enabled = !pending&&app.Connected&&app.Me.gold>=ArenaGame.RecruitRefreshPrice;
            if(IconButton(new Rect(x,footerY,226,45),"refresh","换一批 · "+ArenaGame.RecruitRefreshPrice+" 金币",new Color(.29f,.24f,.15f),"refresh")) {
                refreshPendingOffer=app.Me.recruitOfferId;refreshPendingUntil=Time.unscaledTime+2;
                app.Send(new GameCommand{type="recruit_refresh",offerId=app.Me.recruitOfferId});
            }
            GUI.enabled=app.Connected;
            if(IconButton(new Rect(x+240,footerY,210,45),"close","回到场地",new Color(.15f,.23f,.23f)))SetRecruitVisible(false);
            GUI.enabled = !pending&&app.Connected;
            if (IconButton(new Rect(x+total-268,footerY,268,45),"skip","跳过招募",new Color(.28f,.24f,.17f))) app.Send(new GameCommand{type="skip"});
            GUI.enabled = true;
            Text(new Rect(x+469,footerY+10,200,31),"剩余 "+Mathf.CeilToInt(app.State.timeLeft)+" 秒",small,muted);
        }
    }
}
