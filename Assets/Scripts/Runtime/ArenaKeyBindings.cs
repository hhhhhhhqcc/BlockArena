using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Local preferences only; no key binding ever changes the host's game rules.
    public sealed class ArenaKeyBindings
    {
        public static readonly string[] Actions = { "forward", "backward", "left", "right", "cast", "rotate-left", "rotate-right", "shop", "bestiary", "reset-view", "talk" };
        static readonly string[] names = { "向前移动", "向后移动", "向左移动", "向右移动", "指挥官技能", "视角左转 90°", "视角右转 90°", "技能商店", "生物图鉴", "恢复视角", "按住说话" };
        static readonly KeyCode[] defaults = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.R, KeyCode.Q, KeyCode.E, KeyCode.M, KeyCode.B, KeyCode.F, KeyCode.V };
        readonly Dictionary<string, KeyCode> bindings = new Dictionary<string, KeyCode>();
        readonly Func<bool> canPersist;
        public int Writes { get; private set; }
        public ArenaKeyBindings(Func<bool> persistenceAllowed, bool loadPreferences = true)
        {
            canPersist = persistenceAllowed;
            ResetDefaults(false);
            if (!loadPreferences) return;
            // A malformed or duplicated saved configuration falls back as one atomic set.
            var loaded = new Dictionary<string, KeyCode>();
            var used = new HashSet<KeyCode>();
            for (int i = 0; i < Actions.Length; i++) {
                var key = (KeyCode)PlayerPrefs.GetInt("arena.key." + Actions[i], (int)defaults[i]);
                if (!Allowed(key) || !used.Add(key)) return;
                loaded[Actions[i]] = key;
            }
            foreach (var pair in loaded) bindings[pair.Key] = pair.Value;
        }
        public KeyCode Key(string action) { KeyCode key; return bindings.TryGetValue(action, out key) ? key : KeyCode.None; }
        public string Label(string action) { return KeyLabel(Key(action)); }
        public bool Down(string action) { return Input.GetKeyDown(Key(action)); }
        public bool Held(string action) { return Input.GetKey(Key(action)); }
        public static string Name(string action) { int index = Array.IndexOf(Actions, action); return index < 0 ? action : names[index]; }
        public static bool Allowed(KeyCode key)
        {
            return key >= KeyCode.A && key <= KeyCode.Z || key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ||
                key >= KeyCode.F1 && key <= KeyCode.F12 || key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9 ||
                key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow ||
                key == KeyCode.Space || key == KeyCode.Tab || key == KeyCode.BackQuote || key == KeyCode.Minus || key == KeyCode.Equals ||
                key == KeyCode.LeftBracket || key == KeyCode.RightBracket || key == KeyCode.Semicolon || key == KeyCode.Quote || key == KeyCode.Comma || key == KeyCode.Period || key == KeyCode.Slash;
        }
        public bool TryBind(string action, KeyCode key, out string feedback)
        {
            if (!bindings.ContainsKey(action)) { feedback = "未知操作"; return false; }
            if (!Allowed(key)) { feedback = "请选择字母、数字、方向键或 F1–F12；Enter 与 Esc 保留"; return false; }
            foreach (var pair in bindings) if (pair.Key != action && pair.Value == key) {
                feedback = KeyLabel(key) + " 已用于「" + Name(pair.Key) + "」"; return false;
            }
            if (bindings[action] == key) { feedback = "按键未改变"; return true; }
            bindings[action] = key; Save(); feedback = Name(action) + "  ·  " + KeyLabel(key); return true;
        }
        public void ResetDefaults(bool save = true)
        { for (int i = 0; i < Actions.Length; i++) bindings[Actions[i]] = defaults[i]; if (save) Save(); }
        void Save()
        {
            if (canPersist != null && !canPersist()) return;
            foreach (var pair in bindings) PlayerPrefs.SetInt("arena.key." + pair.Key, (int)pair.Value);
            PlayerPrefs.Save(); Writes++;
        }
        public static string KeyLabel(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + ((int)key - (int)KeyCode.Keypad0);
            switch (key) { case KeyCode.UpArrow: return "↑"; case KeyCode.DownArrow: return "↓"; case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→"; case KeyCode.Space: return "Space"; case KeyCode.BackQuote: return "`"; default: return key.ToString(); }
        }
    }
}
