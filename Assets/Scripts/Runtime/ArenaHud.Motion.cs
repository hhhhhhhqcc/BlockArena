using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        sealed class Spring
        {
            public float value, velocity;
            int frame = -1;
            public void Step(float target, float frequency = 19, float damping = .66f)
            {
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
                for (int i = 0; i < 3; i++) {
                    float step = dt / 3;
                    velocity += ((target - value) * frequency * frequency - 2 * damping * frequency * velocity) * step;
                    value += velocity * step;
                }
            }
        }
        sealed class CoinPopup
        {
            public int amount;
            public float start, received;
        }
        sealed class PlayerVisual
        {
            public float trailHp, displayHp, damagedAt = -10;
            public bool awaitingImpact;
            public readonly List<CoinPopup> coins = new List<CoinPopup>();
        }

        readonly Dictionary<string, PlayerVisual> playerVisuals = new Dictionary<string, PlayerVisual>();
        readonly Dictionary<int, Spring> buttonMotion = new Dictionary<int, Spring>();
        readonly List<Rect> handRects = new List<Rect>();
        readonly List<Rect> textRects = new List<Rect>();
        readonly Spring marketMotion = new Spring(), recruitMotion = new Spring(), hoverMotion = new Spring();
        readonly Spring[] marketCards = { new Spring(), new Spring(), new Spring(), new Spring() };
        readonly Spring[] recruitCards = { new Spring(), new Spring(), new Spring() };
        GameState lastObservedState;
        Vector2 marketPosition, marketDragOffset;
        bool marketPositionSet, marketDragging, marketPointerOwned;
        int pointerFrame = -1, releasedPointerFrame = -10, motionFrame = -1, feedbackCount;
        float finishedAt = -10, lastButtonOffset;
        string hoverKey;
        bool wasRecruiting;

        public Action<float, float> AudioVolumesChanged;
        public Action<string> SoundCue;
        public Func<string, bool> CommanderImpactPending;
        public float MusicVolume = .45f, SfxVolume = .7f;
        public bool ShowCommanderRange { get; set; } = true;
        public Action<bool> CommanderRangeChanged;
        public bool ShowCreatureRange { get; set; } = true;
        public Action<bool> CreatureRangeChanged;
        float rememberedMusic = .45f, rememberedSfx = .7f;
        bool audioPointerOwned, drawingAudio;
        int audioPointerFrame = -1, audioReleasedFrame = -10, audioSlider = -1;

        // Loading persisted values never emits a callback or a sound.
        public void SetAudioVolumes(float music, float sfx)
        {
            MusicVolume = SanitizeVolume(music); SfxVolume = SanitizeVolume(sfx);
            if (MusicVolume > 0 || SfxVolume > 0) { rememberedMusic = MusicVolume; rememberedSfx = SfxVolume; }
        }
        static float SanitizeVolume(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value); }
        void ChangeAudioVolumes(float music, float sfx)
        {
            music = SanitizeVolume(music); sfx = SanitizeVolume(sfx);
            if (Mathf.Abs(music - MusicVolume) < .0001f && Mathf.Abs(sfx - SfxVolume) < .0001f) return;
            MusicVolume = music; SfxVolume = sfx;
            if (AudioVolumesChanged != null) AudioVolumesChanged(MusicVolume, SfxVolume);
        }
        bool AudioCovers(Vector2 p) { return ShowSettings; }
        bool AudioBlocksUnderlying { get { return audioPointerOwned || ShowSettings; } }
        void UpdateAudioPointer()
        {
            if (audioPointerFrame == Time.frameCount || app.VerificationInput) return;
            ProcessAudioPointer(Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
        }
        internal void ProcessAudioPointer(Vector2 screen, bool pressed, bool held, bool released)
        {
            Metrics(); audioPointerFrame = Time.frameCount;
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (pressed && !marketPointerOwned && AudioCovers(p)) {
                audioPointerOwned = true; audioSlider = -1; ClearHover();
                if (SettingsControlsAvailable && settingsTab==0 && AudioMusicSliderRect.Contains(p)) audioSlider = 0;
                else if (SettingsControlsAvailable && settingsTab==0 && AudioSfxSliderRect.Contains(p)) audioSlider = 1;
                if (audioSlider >= 0) ClickFeedback("ui_click");
            }
            if (audioPointerOwned && audioSlider >= 0 && ShowAudio && (pressed || held || released)) {
                Rect track = audioSlider == 0 ? AudioMusicSliderRect : AudioSfxSliderRect;
                float value = Mathf.Clamp01((p.x - track.x - 5) / (track.width - 10));
                ChangeAudioVolumes(audioSlider == 0 ? value : MusicVolume, audioSlider == 1 ? value : SfxVolume);
            }
            if (released || (!held && !pressed)) {
                if (audioPointerOwned) audioReleasedFrame = Time.frameCount;
                audioPointerOwned = false; audioSlider = -1;
            }
        }

        bool SettingsRangeToggle(Rect r,string text,string icon,bool on)
        {
            bool clicked=Button(r,"",new Color(.065f,.135f,.15f),null,"ui_toggle",true);
            ArenaIcons.Draw(new Rect(r.x+10,r.y+10+lastButtonOffset,24,24),icon);
            Text(new Rect(r.x+44,r.y+11+lastButtonOffset,r.width-111,27),text,small,white);
            Rect track=new Rect(r.xMax-55,r.y+13,43,20);RoundFill(track,on?new Color(.15f,.45f,.38f):new Color(.13f,.22f,.25f),10);
            RoundFill(new Rect(track.x+(on?24:3),track.y+3,14,14),on?mint:muted,7);return clicked;
        }
        void DrawAudioSlider(Rect r,float value,bool dragging)
        {
            RegisterPointerRect(r);RoundFill(new Rect(r.x+5,r.center.y-3,r.width-10,6),new Color(.015f,.04f,.05f),3);
            RoundFill(new Rect(r.x+5,r.center.y-2,(r.width-10)*Mathf.Clamp01(value),4),mint,2);
            float x=r.x+5+(r.width-10)*Mathf.Clamp01(value);
            RoundFill(new Rect(x-8,r.center.y-10,16,20),new Color(.065f,.12f,.13f),8);
            RoundFill(new Rect(x-6,r.center.y-8,12,16),dragging?white:gold,6);
        }

        public bool PointerCaptured
        {
            get { Metrics(); UpdateMarketPointer(); return inspectionPointerOwned || inspectionDragging || inspectionReleasedFrame==Time.frameCount || marketPointerOwned || marketDragging || releasedPointerFrame == Time.frameCount || audioPointerOwned || audioReleasedFrame == Time.frameCount || skillPointerOwned || skillReleasedFrame == Time.frameCount; }
        }
        internal int CoinFeedbackCount { get { return feedbackCount; } }
        internal int ActiveCoinFeedbackCount
        {
            get {
                int count = 0;
                foreach (var row in playerVisuals.Values) foreach (var coin in row.coins)
                    if (Time.unscaledTime >= coin.start && Time.unscaledTime < coin.start + 1.25f) count++;
                return count;
            }
        }

        // Screen-space API matches Input.mousePosition (origin at bottom left).
        public Rect MarketScreenRect
        {
            get { Metrics(); Rect r = MarketRect; return new Rect(r.x * scale, Screen.height - r.yMax * scale, r.width * scale, r.height * scale); }
        }
        bool ModalOpen
        {
            get { return ShowSettings || app.QuickStarting || app.ShowGameMenu || app.ShowRoom || app.ShowBestiary || app.State == null || app.State.phase == "lobby" || app.State.phase == "finished" ||
                RecruitVisible; }
        }
        bool MarketVisible { get { return app.CanPrepare && (app.ShowShop || marketMotion.value > .015f); } }

        void EnsureMarketPosition()
        {
            if (!marketPositionSet) { marketPosition = new Vector2(width - 508, CommandDockRect.y - 446); marketPositionSet = true; }
            marketPosition.x = Mathf.Clamp(marketPosition.x, 10, Mathf.Max(10, width - 486));
            marketPosition.y = Mathf.Clamp(marketPosition.y, 10, Mathf.Max(10, height - 444));
        }
        void UpdateMarketPointer()
        {
            UpdateAudioPointer();
            UpdateInspectionPointer();
            UpdateSkillPointer();
            if (pointerFrame == Time.frameCount) return;
            if (app.VerificationInput) return;
            ProcessMarketPointer(Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
        }
        internal void ProcessMarketPointer(Vector2 screen, bool pressed, bool held, bool released)
        {
            Metrics(); pointerFrame = Time.frameCount; EnsureMarketPosition();
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (inspectionPointerOwned || inspectionDragging || inspectionReleasedFrame==Time.frameCount || audioPointerOwned || skillPointerOwned || (pressed && (AudioCovers(p) || PinnedInspectionCovers(p) || SkillUiCovers(p)))) return;
            if (!MarketVisible || ModalOpen) {
                if (marketPointerOwned || marketDragging) releasedPointerFrame = Time.frameCount;
                marketPointerOwned = marketDragging = false;
                return;
            }
            Rect r = MarketRect;
            if (pressed && r.Contains(p)) {
                marketPointerOwned = true;
                if (app.ShowShop && new Rect(r.x, r.y, r.width - 54, 49).Contains(p)) {
                    marketDragging = true; marketDragOffset = p - marketPosition; ClearHover();
                }
            }
            if (marketDragging && (held || released)) {
                marketPosition = p - marketDragOffset; EnsureMarketPosition();
            }
            if (released || (!held && !pressed)) {
                if (marketPointerOwned || marketDragging) releasedPointerFrame = Time.frameCount;
                marketPointerOwned = marketDragging = false;
            }
        }

        public bool WantsHandCursor(Vector2 screen)
        {
            if (PointerCaptured) return true;
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (SettingsControlsAvailable && settingsTab==0 && (AudioMusicSliderRect.Contains(p) || AudioSfxSliderRect.Contains(p))) return true;
            if (app.CanPrepare && app.ShowShop && !ModalOpen) {
                Rect r = MarketScreenRect;
                if (new Rect(r.x, r.yMax - 49 * scale, r.width - 54 * scale, 49 * scale).Contains(screen)) return true;
            }
            for (int i = handRects.Count - 1; i >= 0; i--) if (handRects[i].Contains(screen)) return true;
            return false;
        }
        public bool WantsTextCursor(Vector2 screen)
        {
            foreach (Rect r in textRects) if (r.Contains(screen)) return true;
            return false;
        }
        void RegisterPointerRect(Rect r, bool textInput = false)
        {
            if (!GUI.enabled || !LayerAcceptsPointer()) return;
            Vector2 a = GUIUtility.GUIToScreenPoint(r.position), b = GUIUtility.GUIToScreenPoint(new Vector2(r.xMax, r.yMax));
            Rect screen = new Rect(a.x, Screen.height - b.y, b.x - a.x, b.y - a.y);
            if (textInput) textRects.Add(screen); else handRects.Add(screen);
        }
        void PrepareHover(string key)
        {
            if (hoverKey != key || Time.unscaledTime > hoverUntil + .2f) {
                hoverMotion.value = hoverMotion.velocity = 0; hoverKey = key;
            }
        }

        // The caller passes null on first connection/reconnection; old snapshots never replay income.
        public void ObserveState(GameState oldState, GameState nextState)
        {
            if (nextState != null && ReferenceEquals(lastObservedState, nextState)) return;
            bool baseline = oldState == null || nextState == null || nextState.round < oldState.round ||
                (nextState.phase == "lobby" && oldState.phase != "lobby") ||
                (oldState.phase == "lobby" && nextState.round <= 1) ||
                (oldState.phase == "finished" && nextState.phase != "finished");
            if (baseline) {
                ResetRoster(); ClosePinnedInspection(); recruitHidden=false; recruitVisibilityRound=-1;
                playerVisuals.Clear(); feedbackCount = 0;
                marketPointerOwned = marketDragging = false;
                CloseSkillDetails(); ShowOwnedSkills = false; skillPointerOwned = false; ownedSkillScroll = Vector2.zero; skillCountsFrame = -1;
            }
            ObserveEconomy(oldState,nextState,baseline);
            lastObservedState = nextState;
            skillCountsFrame = -1;
            if (nextState == null) { finishedAt = -10; return; }
            float now = Time.unscaledTime;
            if (nextState.phase == "finished" && (oldState == null || oldState.phase != "finished")) finishedAt = now;
            foreach (PlayerState p in nextState.players) {
                PlayerVisual row;
                if (!playerVisuals.TryGetValue(p.id, out row)) {
                    row = new PlayerVisual { trailHp = p.hp, displayHp = p.hp }; playerVisuals.Add(p.id, row);
                }
                PlayerState previous = !baseline && oldState.players != null ? oldState.players.Find(x => x.id == p.id) : null;
                if (previous == null) { row.trailHp = row.displayHp = p.hp; row.awaitingImpact = false; continue; }
                int income = p.gold - previous.gold;
                if (income > 0) {
                    feedbackCount++;
                    CoinPopup last = row.coins.Count == 0 ? null : row.coins[row.coins.Count - 1];
                    if (last != null && ((now - last.received < .18f && now - last.start < .3f) || row.coins.Count >= 4)) {
                        last.amount += income; last.received = now;
                    } else row.coins.Add(new CoinPopup { amount = income, received = now, start = last == null ? now : Mathf.Max(now, last.start + .34f) });
                }
                if (p.hp < previous.hp) {
                    row.trailHp = Mathf.Max(row.trailHp, previous.hp);
                    row.awaitingImpact = CommanderImpactPending != null && CommanderImpactPending(p.id);
                    if (!row.awaitingImpact) { row.displayHp = p.hp; row.damagedAt = now; }
                } else if (p.hp > previous.hp) { row.trailHp = row.displayHp = p.hp; row.awaitingImpact = false; }
            }
        }

        void AdvanceMotion()
        {
            if (motionFrame == Time.frameCount) return;
            motionFrame = Time.frameCount;
            marketMotion.Step(app.CanPrepare && app.ShowShop ? 1 : 0, 21, .64f);
            bool recruiting = RecruitVisible;
            if (recruiting && !wasRecruiting) { recruitMotion.value = recruitMotion.velocity = 0; }
            wasRecruiting = recruiting; recruitMotion.Step(recruiting ? 1 : 0, 17, .61f);
            if (app.State == null) return;
            foreach (PlayerState p in app.State.players) {
                PlayerVisual row;
                if (!playerVisuals.TryGetValue(p.id, out row)) { row = new PlayerVisual { trailHp = p.hp, displayHp = p.hp }; playerVisuals.Add(p.id, row); }
                if (row.awaitingImpact && (CommanderImpactPending == null || !CommanderImpactPending(p.id))) {
                    row.awaitingImpact = false; row.displayHp = p.hp; row.damagedAt = Time.unscaledTime;
                }
                if (!row.awaitingImpact && Time.unscaledTime - row.damagedAt > .38f)
                    row.trailHp = Mathf.Lerp(row.trailHp, row.displayHp, 1 - Mathf.Exp(-6 * Time.unscaledDeltaTime));
                for (int i = row.coins.Count - 1; i >= 0; i--) if (Time.unscaledTime - row.coins[i].start > 1.25f) row.coins.RemoveAt(i);
            }
        }

        void CoinFeedback(PlayerVisual row, Rect r)
        {
            foreach (CoinPopup coin in row.coins) {
                float age = Time.unscaledTime - coin.start;
                if (age < 0 || age > 1.25f) continue;
                float life = age / 1.25f, alpha = Mathf.Clamp01((1 - life) * 3);
                float bounce = 1 - Mathf.Exp(-age * 15) * Mathf.Cos(age * 27);
                float x = r.x + 9, y = r.y + 4 - life * 35 - Mathf.Sin(Mathf.Min(1, age * 3) * Mathf.PI) * 8;
                Fill(new Rect(x - 3, y - 2, 85, 28), new Color(.06f, .08f, .07f, .85f * alpha));
                float size = 21 * Mathf.Clamp(bounce, .5f, 1.15f);
                ArenaIcons.Draw(new Rect(x + (22-size)/2, y + (22-size)/2, size, size), "coin");
                Text(new Rect(x + 27, y - 2, 64, 29), "+" + coin.amount, label, new Color(1, .83f, .39f, alpha));
            }
        }
    }
}
