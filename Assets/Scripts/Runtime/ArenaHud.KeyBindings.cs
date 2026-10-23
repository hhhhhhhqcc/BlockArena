using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        string bindingAction, bindingFeedback = "";
        internal bool KeyBindingActive { get { return !string.IsNullOrEmpty(bindingAction); } }
        internal string BindingFeedback { get { return bindingFeedback; } }
        Rect BindingButtonRect(int index)
        {
            Rect r = SettingsContentRect; float column = (r.width - 62) / 2;
            return new Rect(r.x + 25 + (index % 2) * (column + 12) + column - 94, r.y + 78 + (index / 2) * 57, 86, 40);
        }
        internal Rect KeyBindingScreenRect(int index) { Metrics(); return ToScreenRect(BindingButtonRect(index)); }
        internal Rect KeyBindingsResetScreenRect { get { Metrics(); Rect r=SettingsContentRect;return ToScreenRect(new Rect(r.x+25,r.y+477,166,43)); } }
        internal void BeginKeyBinding(string action)
        {
            bindingAction = action; bindingFeedback = "按下新的按键 · Esc 取消"; ClearHover(); resetMenuFocus = true;
        }
        public bool CancelKeyBinding()
        {
            if (!KeyBindingActive) return false;
            bindingAction = null; bindingFeedback = "已取消"; return true;
        }
        internal bool SubmitKeyBinding(KeyCode key)
        {
            if (!KeyBindingActive || app.Keys == null) return false;
            bool accepted = app.Keys.TryBind(bindingAction, key, out bindingFeedback);
            if (accepted) { bindingAction = null; ClickFeedback("ui_click"); }
            return accepted;
        }
        void DrawKeyBindingsSettings()
        {
            Rect r = SettingsContentRect; SettingsCard(new Rect(r.x,r.y,r.width,545),"keys","按键设置");
            if (KeyBindingActive && Event.current.type == EventType.KeyDown) {
                if (Event.current.keyCode == KeyCode.Escape) CancelKeyBinding();
                else if (Event.current.keyCode != KeyCode.None) SubmitKeyBinding(Event.current.keyCode);
                Event.current.Use();
            }
            float column = (r.width - 62) / 2;
            for (int i=0;i<ArenaKeyBindings.Actions.Length;i++) {
                string action=ArenaKeyBindings.Actions[i];Rect key=BindingButtonRect(i);
                Rect row=new Rect(r.x+25+(i%2)*(column+12),key.y,column,44);
                RoundFill(row,new Color(.035f,.08f,.095f),7);
                Text(new Rect(row.x+12,row.y+9,row.width-116,29),ArenaKeyBindings.Name(action),small,white);
                if(Button(key,bindingAction==action?"…":app.Keys.Label(action),bindingAction==action?new Color(.22f,.39f,.32f):new Color(.10f,.21f,.23f),bindingAction==action?gold:mint))BeginKeyBinding(action);
            }
            Fill(new Rect(r.x+25,r.y+426,r.width-50,1),line);
            Text(new Rect(r.x+25,r.y+438,r.width-50,27),"Enter  打开 / 发送聊天     Esc  菜单 / 取消",micro,muted);
            if(Button(new Rect(r.x+25,r.y+477,166,43),"恢复默认按键",new Color(.075f,.15f,.17f),mint)){
                app.Keys.ResetDefaults();bindingAction=null;bindingFeedback="已恢复默认按键";
            }
            Text(new Rect(r.x+211,r.y+475,r.width-236,59),bindingFeedback,micro,KeyBindingActive?gold:mint);
        }
    }
}
