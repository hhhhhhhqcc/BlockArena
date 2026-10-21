using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Resolution requests take effect at the end of a frame. Keep the draft separate
    // from Screen, and persist the accepted result rather than an unsupported request.
    public sealed class ArenaDisplaySettings
    {
        public const string WidthPreference = "arena.display.width";
        public const string HeightPreference = "arena.display.height";
        public const string ModePreference = "arena.display.mode";

        public struct ResolutionOption
        {
            public readonly int Width, Height;
            public string Label { get { return Width + " × " + Height; } }
            public ResolutionOption(int width, int height) { Width = width; Height = height; }
            public override string ToString() { return Label; }
        }

        public struct DisplayState
        {
            public int Width, Height;
            public FullScreenMode Mode;
            public DisplayState(int width, int height, FullScreenMode mode)
            { Width = width; Height = height; Mode = mode; }
        }

        readonly Func<bool> verificationMode;
        readonly bool verificationLaunch;
        readonly List<ResolutionOption> resolutions = new List<ResolutionOption>();
        readonly IReadOnlyList<ResolutionOption> resolutionView;
        DisplayState draft, requested, beforeApply;
        float applyStarted;
        int applyPolls, observedWidth, observedHeight;
        FullScreenMode observedMode;
        bool persistRequest;
        string status = "";

        public IReadOnlyList<ResolutionOption> Resolutions { get { return resolutionView; } }
        public int CurrentWidth { get { return Screen.width; } }
        public int CurrentHeight { get { return Screen.height; } }
        public FullScreenMode CurrentMode { get { return Screen.fullScreenMode; } }
        public int DraftWidth { get { return draft.Width; } }
        public int DraftHeight { get { return draft.Height; } }
        public FullScreenMode DraftMode { get { return draft.Mode; } }
        public int DesktopWidth { get; private set; }
        public int DesktopHeight { get; private set; }
        public int DraftResolutionIndex { get { return FindResolution(draft.Width, draft.Height); } }
        public bool Applying { get; private set; }
        public bool LastApplySucceeded { get; private set; }
        public bool CommandLineOverrides { get; private set; }
        public bool InitializedFromPreferences { get; private set; }
        public int PreferenceWriteCount { get; private set; }
        public bool PersistenceEnabled { get { return !verificationLaunch && (verificationMode == null || !verificationMode()); } }
        public bool HasChanges { get { return !Matches(Capture(), draft); } }
        public string CurrentLabel { get { return CurrentWidth + " × " + CurrentHeight + " · " + ModeLabel(CurrentMode); } }
        public string DraftLabel { get { return draft.Width + " × " + draft.Height + " · " + ModeLabel(draft.Mode); } }
        public string Status { get { return Applying ? "应用中…" : !string.IsNullOrEmpty(status) ? status : HasChanges ? "待应用" : ""; } }

        public ArenaDisplaySettings(Func<bool> verificationMode = null)
        {
            this.verificationMode = verificationMode;
            string[] arguments = Environment.GetCommandLineArgs();
            foreach (string argument in arguments)
            {
                if (argument.StartsWith("-screen-", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "-popupwindow", StringComparison.OrdinalIgnoreCase)) CommandLineOverrides = true;
                if ((argument.StartsWith("-", StringComparison.Ordinal) && argument.EndsWith("-test", StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(argument, "-arena-server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "-smoke-exit", StringComparison.OrdinalIgnoreCase)) verificationLaunch = true;
            }
            resolutionView = resolutions.AsReadOnly();
            RememberActual();
            RefreshResolutions();
            ResetDraft();
            // Native probes set VerificationInput after Awake; the launch flags must
            // already suppress preference loading and writing at construction time.
            if (!CommandLineOverrides && PersistenceEnabled) LoadPreferences();
        }

        public static string ModeLabel(FullScreenMode mode)
        { return mode == FullScreenMode.Windowed ? "窗口" : mode == FullScreenMode.ExclusiveFullScreen ? "独占全屏" : mode == FullScreenMode.MaximizedWindow ? "最大化窗口" : "无边框全屏"; }

        public DisplayState Capture() { return new DisplayState(CurrentWidth, CurrentHeight, CurrentMode); }

        public void ResetDraft()
        {
            if (Applying) return;
            draft = Capture();
            status = "";
        }

        public bool SetResolutionIndex(int index)
        {
            if (Applying || index < 0 || index >= resolutions.Count) return false;
            draft.Width = resolutions[index].Width; draft.Height = resolutions[index].Height;
            status = ""; return true;
        }

        public bool StepResolution(int direction)
        {
            if (Applying || resolutions.Count == 0 || direction == 0) return false;
            int current = DraftResolutionIndex;
            int next = current < 0 ? 0 : (current + (direction > 0 ? 1 : -1) + resolutions.Count) % resolutions.Count;
            return SetResolutionIndex(next);
        }

        public bool SetMode(FullScreenMode mode)
        {
            if (Applying || mode != FullScreenMode.Windowed && mode != FullScreenMode.FullScreenWindow) return false;
            draft.Mode = mode; status = ""; return true;
        }

        public bool ToggleMode()
        { return SetMode(draft.Mode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed); }

        // Returns whether Unity accepted a request for processing. Tick reports its
        // actual result after Unity has resized the native player window.
        public bool Apply()
        {
            if (Applying || FindResolution(draft.Width, draft.Height) < 0) return false;
            if (draft.Mode != FullScreenMode.Windowed && draft.Mode != FullScreenMode.FullScreenWindow) return false;
            return BeginApply(draft, true);
        }

        // Native probes capture their original state, apply real display changes,
        // then restore it. Restoration never writes preferences unless requested.
        public bool Restore(DisplayState state, bool persist = false)
        {
            if (Applying || state.Width <= 0 || state.Height <= 0 || !Enum.IsDefined(typeof(FullScreenMode), state.Mode)) return false;
            draft = state;
            return BeginApply(state, persist);
        }

        bool BeginApply(DisplayState target, bool persist)
        {
            requested = target; beforeApply = Capture();
            persistRequest = persist && PersistenceEnabled;
            LastApplySucceeded = false; applyPolls = 0;
            applyStarted = Time.realtimeSinceStartup;
            try
            {
                Screen.SetResolution(target.Width, target.Height, target.Mode);
                Applying = true; status = ""; return true;
            }
            catch (Exception)
            { Applying = false; status = "无法应用显示模式"; return false; }
        }

        public void Tick()
        {
            if (Applying)
            {
                applyPolls++;
                DisplayState actual = Capture();
                bool matched = Matches(actual, requested);
                if ((applyPolls >= 2 && matched) || Time.realtimeSinceStartup - applyStarted >= 2)
                {
                    Applying = false;
                    LastApplySucceeded = matched || !Matches(actual, beforeApply);
                    if (LastApplySucceeded)
                    {
                        draft = actual;
                        status = matched ? "已应用" : "已使用系统可用模式";
                        if (persistRequest && PersistenceEnabled) SavePreferences(actual);
                    }
                    else status = "系统未接受该显示模式";
                    persistRequest = false;
                    RefreshResolutions();
                    RememberActual();
                }
                return;
            }
            if (CurrentWidth == observedWidth && CurrentHeight == observedHeight && CurrentMode == observedMode) return;
            // A player may resize the window outside the settings page. Adopt it
            // only if no uncommitted selection exists; keep it as a recovery choice.
            bool editing = draft.Width != observedWidth || draft.Height != observedHeight || draft.Mode != observedMode;
            if (!editing) draft = Capture();
            RefreshResolutions();
            RememberActual();
        }

        public void RefreshResolutions()
        {
            int desktopWidth = Display.main != null ? Display.main.systemWidth : 0;
            int desktopHeight = Display.main != null ? Display.main.systemHeight : 0;
            Resolution desktop = Screen.currentResolution;
            if (desktopWidth <= 0 || desktopHeight <= 0) { desktopWidth = desktop.width; desktopHeight = desktop.height; }
            if (desktopWidth <= 0 || desktopHeight <= 0) { desktopWidth = CurrentWidth; desktopHeight = CurrentHeight; }
            DesktopWidth = desktopWidth; DesktopHeight = desktopHeight;
            resolutions.Clear();
            foreach (Resolution resolution in Screen.resolutions) AddResolution(resolution.width, resolution.height, false);
            AddResolution(1280, 720, false); AddResolution(1440, 900, false); AddResolution(1920, 1080, false);
            // Even unusual CLI/window sizes must remain available for restoration.
            AddResolution(CurrentWidth, CurrentHeight, true);
            resolutions.Sort((a, b) => { int area = ((long)a.Width * a.Height).CompareTo((long)b.Width * b.Height); return area != 0 ? area : a.Width.CompareTo(b.Width); });
        }

        void AddResolution(int width, int height, bool actual)
        {
            if (width <= 0 || height <= 0 || !actual && (width < 960 || height < 540 || width > DesktopWidth || height > DesktopHeight)) return;
            if (FindResolution(width, height) < 0) resolutions.Add(new ResolutionOption(width, height));
        }

        int FindResolution(int width, int height)
        {
            for (int i = 0; i < resolutions.Count; i++) if (resolutions[i].Width == width && resolutions[i].Height == height) return i;
            return -1;
        }

        void RememberActual()
        { observedWidth = CurrentWidth; observedHeight = CurrentHeight; observedMode = CurrentMode; }

        void LoadPreferences()
        {
            if (!PlayerPrefs.HasKey(WidthPreference) || !PlayerPrefs.HasKey(HeightPreference) || !PlayerPrefs.HasKey(ModePreference)) return;
            int width = PlayerPrefs.GetInt(WidthPreference), height = PlayerPrefs.GetInt(HeightPreference);
            FullScreenMode mode = (FullScreenMode)PlayerPrefs.GetInt(ModePreference);
            if (FindResolution(width, height) < 0 || mode != FullScreenMode.Windowed && mode != FullScreenMode.FullScreenWindow) return;
            draft = new DisplayState(width, height, mode);
            InitializedFromPreferences = BeginApply(draft, false);
        }

        void SavePreferences(DisplayState actual)
        {
            if (actual.Mode != FullScreenMode.Windowed && actual.Mode != FullScreenMode.FullScreenWindow) return;
            PlayerPrefs.SetInt(WidthPreference, actual.Width); PlayerPrefs.SetInt(HeightPreference, actual.Height);
            PlayerPrefs.SetInt(ModePreference, (int)actual.Mode); PlayerPrefs.Save(); PreferenceWriteCount++;
        }

        static bool Matches(DisplayState a, DisplayState b)
        { return a.Width == b.Width && a.Height == b.Height && a.Mode == b.Mode; }
    }
}
