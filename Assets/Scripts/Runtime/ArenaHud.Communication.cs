using System;
using BlockArena.Networking;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        bool chatExpanded, chatInputActive, focusChat, drawingCommunication;
        string chatDraft = "";
        Vector2 chatScroll;
        GUIStyle chatText, chatName, chatEntry;
        int chatLastSeen, chatLastDrawn;
        int chatCompositionFrame = -100;
        float chatLastMessageAt = -10;
        int communicationDrawnSpeakers;
        internal bool CommunicationInputActive { get { return chatInputActive; } }
        internal bool CommunicationExpandedForVerification { get { return chatExpanded; } }
        internal int CommunicationDrawnSpeakers { get { return communicationDrawnSpeakers; } }
        internal Rect CommunicationPanelScreenRect { get { Metrics(); return ToScreenRect(ChatPanelRect); } }
        internal Rect CommunicationControlsScreenRect { get { Metrics(); return ToScreenRect(new Rect(24, height - 104, 148, 82)); } }
        internal void SetCommunicationDraftForVerification(string text) { chatDraft = text ?? ""; }
        Rect ChatButtonRect { get { return new Rect(24, height - 64, 44, 42); } }
        Rect MicrophoneButtonRect { get { return new Rect(76, height - 64, 44, 42); } }
        Rect SpeakerButtonRect { get { return new Rect(128, height - 64, 44, 42); } }
        Rect ChatPanelRect { get { return new Rect(24, height - 382, 348, 260); } }
        bool CommunicationVisible
        {
            get { return app.Connected && app.State != null && !app.QuickStarting && !app.ShowGameMenu && !ShowSettings && !app.ShowBestiary; }
        }

        public bool HandleCommunicationKeys()
        {
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            enter = AcceptCommunicationEnter(enter, Input.compositionString, Time.frameCount);
            return ProcessCommunicationInput(enter,
                Input.GetKeyDown(KeyCode.Escape), Input.GetMouseButtonDown(0), Input.mousePosition);
        }

        internal bool AcceptCommunicationEnter(bool enter, string composition, int frame)
        {
            // Enter used to confirm a Chinese IME candidate must not send the draft.
            if (!string.IsNullOrEmpty(composition)) chatCompositionFrame = frame;
            return enter && string.IsNullOrEmpty(composition) && frame > chatCompositionFrame + 1;
        }

        // Real keys and opt-in native verification share all state transitions.
        internal bool ProcessCommunicationInput(bool enter, bool escape, bool primaryPressed, Vector2 screenPointer)
        {
            if (!CommunicationVisible) { if (chatInputActive || focusChat) CloseCommunicationInput(); return false; }
            if (chatInputActive && escape) { CloseCommunicationInput(); chatExpanded = false; return true; }
            if (enter && (chatInputActive || !battlefieldKeyboardCaptured))
            {
                if (chatInputActive)
                {
                    if (!string.IsNullOrWhiteSpace(chatDraft)) app.SendChat(chatDraft);
                    chatDraft = ""; CloseCommunicationInput();
                }
                else OpenCommunicationInput();
                return true;
            }
            if (chatInputActive && primaryPressed)
            {
                Metrics();
                Vector2 p = new Vector2(screenPointer.x / scale, (Screen.height - screenPointer.y) / scale);
                if (!ChatPanelRect.Contains(p)) { CloseCommunicationInput(); return true; }
            }
            return false;
        }

        void CloseCommunicationInput() { chatInputActive = focusChat = false; resetMenuFocus = true; }
        void OpenCommunicationInput()
        { chatExpanded = chatInputActive = focusChat = true; ClearHover(); }
        internal void ToggleCommunicationPanel()
        {
            if(chatExpanded){chatExpanded=false;CloseCommunicationInput();}
            else OpenCommunicationInput();
        }
        public void ResetCommunication()
        {
            CloseCommunicationInput(); chatExpanded = false; chatDraft = ""; chatScroll = Vector2.zero;
            chatLastSeen = chatLastDrawn = 0; chatLastMessageAt = -10;
            chatCompositionFrame = -100;
        }

        bool CommunicationCovers(Vector2 point)
        {
            if (!CommunicationVisible) return false;
            return ChatButtonRect.Contains(point) || MicrophoneButtonRect.Contains(point) || SpeakerButtonRect.Contains(point) || chatExpanded && ChatPanelRect.Contains(point);
        }

        void DrawCommunication()
        {
            if (!CommunicationVisible) return;
            drawingCommunication = true;
            try { DrawCommunicationContents(); }
            finally { drawingCommunication = false; }
        }

        void DrawCommunicationContents()
        {
            if (chatText == null)
            {
                chatText = new GUIStyle(small) { richText = false, fontSize = 17, wordWrap = true };
                chatName = new GUIStyle(micro) { richText = false, fontSize = 15, wordWrap = false };
                chatEntry = new GUIStyle(field) { richText = false, fontSize = 17, padding = new RectOffset(10, 10, 8, 6) };
            }
            var history = app.ChatMessages;
            int latest = history == null || history.Count == 0 ? 0 : history[history.Count - 1].id;
            if (latest != chatLastDrawn) { chatLastDrawn = latest; chatLastMessageAt = Time.unscaledTime; chatScroll.y = 100000; }
            ArenaVoice voice = app.Voice;
            if (CommunicationButton(ChatButtonRect, chatExpanded))
            {
                ToggleCommunicationPanel();
                if(chatExpanded){chatLastSeen = latest; chatScroll.y = 100000;}
            }
            DrawCommunicationIcon(new Rect(ChatButtonRect.x + 11, ChatButtonRect.y + 10, 23, 23), "chat", chatExpanded ? mint : white, false);
            if (!chatExpanded && latest > chatLastSeen)
            {
                RoundFill(new Rect(ChatButtonRect.xMax - 10, ChatButtonRect.y - 4, 17, 17), gold, 8);
                Text(new Rect(ChatButtonRect.xMax - 10, ChatButtonRect.y - 5, 17, 18), Mathf.Min(9, latest - chatLastSeen).ToString(), centered, dark);
            }
            if (CommunicationButton(MicrophoneButtonRect, voice != null && (voice.OpenMic || voice.Capturing)))
                if (voice != null) voice.SetOpenMic(!voice.OpenMic);
            DrawCommunicationIcon(new Rect(MicrophoneButtonRect.x + 11, MicrophoneButtonRect.y + 10, 23, 23), "microphone", voice != null && voice.Talking ? mint : white, voice == null || !voice.OpenMic && !voice.Capturing);
            if (CommunicationButton(SpeakerButtonRect, voice != null && voice.Muted))
                if (voice != null) voice.SetMuted(!voice.Muted);
            DrawCommunicationIcon(new Rect(SpeakerButtonRect.x + 11, SpeakerButtonRect.y + 10, 23, 23), "speaker", white, voice != null && voice.Muted);
            if (Event.current.type == EventType.Repaint)
            {
                Vector2 pointer = Event.current.mousePosition;
                if (MicrophoneButtonRect.Contains(pointer)) SetHover("军团语音", voice == null ? "按住 "+app.Keys.Label("talk")+" 说话" : voice.Status + "\n点击切换开麦 · 按住 "+app.Keys.Label("talk")+" 临时说话", Input.mousePosition);
                if (SpeakerButtonRect.Contains(pointer)) SetHover("语音接收", voice != null && voice.Muted ? "点击恢复队友语音" : "点击静音其他指挥官", Input.mousePosition);
                if (ChatButtonRect.Contains(pointer)) SetHover("聊天", "Enter 输入 · Enter 发送 · Esc 收起", Input.mousePosition);
            }
            DrawSpeakingIndicators(voice);
            if (!chatExpanded)
            {
                if (history != null && history.Count > 0 && Time.unscaledTime - chatLastMessageAt < 5)
                {
                    ChatMessage message = history[history.Count - 1];
                    Rect preview = new Rect(24, height - 184, 348, 66);
                    RoundFill(preview, new Color(.025f, .07f, .075f, .78f), 10);
                    Text(new Rect(preview.x + 12, preview.y + 8, preview.width - 24, 20), message.name, chatName, CommunicationSlotColor(message.slot));
                    Text(new Rect(preview.x + 12, preview.y + 29, preview.width - 24, 30), Compact(message.text, 32), chatText, white);
                }
                return;
            }
            chatLastSeen = latest;
            Rect panel = ChatPanelRect;
            Panel(panel, new Color(.025f, .065f, .075f, .96f));
            Text(new Rect(panel.x + 16, panel.y + 12, 245, 27), "指挥频道", label, mint);
            if (CommunicationButton(new Rect(panel.xMax - 40, panel.y + 8, 30, 30), false)) { chatExpanded = false; CloseCommunicationInput(); }
            Text(new Rect(panel.xMax - 39, panel.y + 7, 28, 30), "×", centered, muted);
            Rect log = new Rect(panel.x + 12, panel.y + 48, panel.width - 24, 154);
            float contentHeight = 0;
            if (history != null) foreach (ChatMessage message in history)
                contentHeight += 23 + Mathf.Max(23, chatText.CalcHeight(new GUIContent(message.text), log.width - 28)) + 10;
            chatScroll = GUI.BeginScrollView(log, chatScroll, new Rect(0, 0, log.width - 18, Mathf.Max(log.height, contentHeight)), false, false);
            float y = 0;
            if (history == null || history.Count == 0) Text(new Rect(4, 30, log.width - 28, 60), "Enter 开始交流", chatText, muted);
            else foreach (ChatMessage message in history)
            {
                Text(new Rect(3, y, log.width - 28, 22), message.name, chatName, CommunicationSlotColor(message.slot));
                float body = Mathf.Max(23, chatText.CalcHeight(new GUIContent(message.text), log.width - 28));
                Text(new Rect(3, y + 23, log.width - 28, body), message.text, chatText, white);
                y += 23 + body + 10;
            }
            GUI.EndScrollView();
            Rect entry = new Rect(panel.x + 12, panel.yMax - 45, panel.width - 68, 33);
            bool enabled = GUI.enabled; GUI.enabled = true;
            GUI.SetNextControlName("arena_chat");
            Color entryBackground=GUI.backgroundColor;GUI.backgroundColor=new Color(.12f,.2f,.18f);
            string changed = GUI.TextField(entry, chatDraft, WireProtocol.ChatMaxCharacters, chatEntry);
            GUI.backgroundColor=entryBackground;
            if (changed != chatDraft || GUI.GetNameOfFocusedControl() == "arena_chat") chatInputActive = true;
            chatDraft = changed;
            if (focusChat) { GUI.FocusControl("arena_chat"); focusChat = false; }
            RegisterCommunicationPointer(entry, true);
            GUI.enabled = enabled;
            if (CommunicationButton(new Rect(panel.xMax - 48, panel.yMax - 45, 36, 33), false))
            {
                if (!string.IsNullOrWhiteSpace(chatDraft)) app.SendChat(chatDraft);
                chatDraft = ""; CloseCommunicationInput();
            }
            Text(new Rect(panel.xMax - 48, panel.yMax - 45, 36, 33), "↵", centered, mint);
            if(Event.current.type==EventType.Repaint&&new Rect(panel.xMax-48,panel.yMax-45,36,33).Contains(Event.current.mousePosition))
                SetHover("发送消息","回车发送 · Esc 收起",Input.mousePosition);
        }

        void DrawSpeakingIndicators(ArenaVoice voice)
        {
            communicationDrawnSpeakers = 0;
            if (app.State == null) return;
            int index = 0;
            foreach (var player in app.State.players)
            {
                if (player.bot) continue;
                communicationDrawnSpeakers++;
                bool talking = voice != null && voice.IsSpeaking(player.id);
                Rect r = new Rect(25 + index++ * 38, height - 104, 30, 24);
                RoundFill(r, talking ? CommunicationSlotColor(player.slot) : new Color(.06f, .12f, .13f, .68f), 8);
                Color color = talking ? dark : CommunicationSlotColor(player.slot);
                for (int n = 0; n < 3; n++)
                {
                    float h = talking ? 6 + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 11 + n * 1.6f)) * 10 : 4;
                    Fill(new Rect(r.x + 7 + n * 6, r.center.y - h / 2, 3, h), color);
                }
            }
        }

        static Color CommunicationSlotColor(int slot)
        {
            switch (slot) { case 1: return new Color(1, .74f, .28f); case 2: return new Color(.65f, .52f, 1); case 3: return new Color(1, .42f, .54f); default: return new Color(.38f, .87f, .73f); }
        }

        bool CommunicationButton(Rect r, bool active)
        {
            // These controls are an independent HUD layer, including in a connected lobby.
            bool enabled = GUI.enabled; GUI.enabled = true;
            bool hover = r.Contains(Event.current.mousePosition);
            RoundFill(r, active ? new Color(.1f, .3f, .28f, .95f) : new Color(.035f, .09f, .105f, .92f), 10);
            if (hover) RoundFill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(.18f, .29f, .28f, .65f), 9);
            RegisterCommunicationPointer(r, false);
            bool pressed = GUI.Button(r, "", button);
            if (pressed) ClickFeedback("ui_click");
            GUI.enabled = enabled; return pressed;
        }

        void RegisterCommunicationPointer(Rect r, bool textInput)
        {
            Vector2 a = GUIUtility.GUIToScreenPoint(r.position), b = GUIUtility.GUIToScreenPoint(new Vector2(r.xMax, r.yMax));
            Rect screen = new Rect(a.x, Screen.height - b.y, b.x - a.x, b.y - a.y);
            if (textInput) textRects.Add(screen); else handRects.Add(screen);
        }

        static void DrawCommunicationIcon(Rect r, string icon, Color color, bool slash)
        {
            float s = r.width / 24;
            Action<float, float, float, float> box = (x, y, w, h) => Fill(new Rect(r.x + x * s, r.y + y * s, w * s, h * s), color);
            if (icon == "chat") { box(2, 3, 20, 14); box(5, 17, 4, 5); box(9, 17, 4, 2); Fill(new Rect(r.x + 5*s, r.y + 7*s, 14*s, 2*s), new Color(.03f,.09f,.1f)); Fill(new Rect(r.x + 5*s,r.y + 12*s,10*s,2*s),new Color(.03f,.09f,.1f)); }
            else if (icon == "microphone") { box(9, 2, 6, 12); box(5, 8, 2, 7); box(17, 8, 2, 7); box(7, 15, 10, 2); box(11, 17, 2, 4); box(7, 21, 10, 2); }
            else { box(3, 9, 5, 8); box(8, 6, 3, 14); box(11, 3, 3, 20); box(17, 8, 2, 10); box(20, 5, 2, 16); }
            if (slash) for (int i = 1; i < 22; i++) Fill(new Rect(r.x + i*s, r.y + (23-i)*s, 2*s, 2*s), new Color(1,.5f,.4f));
        }
    }
}
