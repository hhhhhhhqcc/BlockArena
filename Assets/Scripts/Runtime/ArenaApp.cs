using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using BlockArena.Networking;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed class ArenaApp : MonoBehaviour
    {
        public static ArenaApp Instance { get; private set; }
        public GameState State { get { return VerificationInput && VerificationState != null ? VerificationState : client != null ? client.State : null; } }
        public PlayerState Me
        {
            get
            {
                if (State == null || client == null) return null;
                return State.players.Find(p => p.id == client.PlayerId);
            }
        }
        public bool Connected { get { return client != null && client.Connected; } }
        public bool ConnectionPending { get { return client != null && client.Connecting; } }
        public bool Hosting { get { return server != null; } }
        public bool HasSession { get { return client != null; } }
        static readonly ChatMessage[] EmptyChat = new ChatMessage[0];
        public IList<ChatMessage> ChatMessages { get { return client == null ? EmptyChat : client.ChatHistory; } }
        public void SendChat(string text) { if (client != null) client.SendChat(text); }
        public string PlayerId { get { return client == null ? null : client.PlayerId; } }
        public string PlayerName = "冒险家", Address = "127.0.0.1", PortText = "7777";
        public string Status = "", SelectedId;
        public bool ShowRoom, ShowShop, ShowBestiary;
        bool showGameMenu;
        public bool ShowGameMenu
        {
            get { return showGameMenu; }
            set
            {
                bool open = value && State != null;
                if (showGameMenu == open) return;
                showGameMenu = open;
                if (Tactics != null) Tactics.SuspendInput();
                StopPlayerMovement(); CancelDragging();
                if (open)
                {
                    ShowRoom = ShowShop = ShowBestiary = false;
                    if (hud != null) { hud.ShowAudio = false; hud.ShowOwnedSkills = false; hud.CloseSkillDetails(); hud.ClosePinnedInspection(); hud.ClearHover(); }
                    if (World != null) World.SetHovered(null);
                }
            }
        }
        internal bool BattlefieldInputBlocked { get { return communicationHandledFrame == Time.frameCount || QuickStarting || ShowGameMenu || ShowRoom || ShowBestiary || hud != null && (hud.ShowSettings || hud.CommunicationInputActive); } }
        public bool QuickStarting { get { return quickStart; } }
        public ArenaDisplaySettings Display { get; private set; }
        public ArenaKeyBindings Keys { get; private set; }
        public ArenaWorld World { get; private set; }
        public ArenaAudio Sound { get; private set; }
        public ArenaAmbience Ambience { get; private set; }
        public ArenaVoice Voice { get; private set; }
        public ArenaTactics Tactics { get; private set; }
        public string LocalAddresses { get; private set; }
        public string Notice { get { return Time.unscaledTime < noticeUntil ? notice : ""; } }
        public bool CanPrepare
        {
            get
            {
                return Connected && Me != null && !Me.eliminated && !Me.recruitPending &&
                    (State.phase == "recruit" || State.phase == "prep");
            }
        }
        public bool CanArrangeCreatures
        {
            get
            {
                return Connected && State != null && Me != null && !Me.eliminated && !Me.ready &&
                    (State.phase == "prep" || State.phase == "tactics" ||
                     State.phase == "recruit" && (!Me.recruitPending || hud != null && !hud.RecruitVisible));
            }
        }
        ArenaServer server;
        ArenaClient client;
        ArenaHud hud;
        ArenaCursor pointer;
        internal ArenaHud Hud { get { return hud; } }
        internal Texture2D VerifyCursor() { return pointer.VerifyAndCreatePreview(); }
        readonly PlacementPreview placements = new PlacementPreview();
        readonly ArenaCountdown countdown = new ArenaCountdown();
        readonly ArenaRevealAudio revealAudio = new ArenaRevealAudio();
        internal ArenaRevealAudio RevealAudio { get { return revealAudio; } }
        GameState previous;
        string dragId, notice = "", lastNetworkError;
        Vector2 dragStart;
        Point2 dragOffset, dragPoint;
        Point2? walkTarget;
        int lastErrorVersion;
        int communicationHandledFrame = -1;
        float nextMove, noticeUntil;
        float outcomeSoundAt;
        string outcomeSound;
        bool wasMoving, quickStart, quickStartSent, headless, settingsInputSuspended;
        float quickStartDeadline;
        internal float SnapshotPauseUntil;
        internal Vector2? VerificationPointer;
        internal bool VerificationInput;
        internal GameState VerificationState;
        internal void RecordCommunicationInput(bool handled)
        { if (handled) communicationHandledFrame = Time.frameCount; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<ArenaApp>() == null)
                new GameObject("Block Arena").AddComponent<ArenaApp>();
        }

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 4;
            Time.fixedDeltaTime = .05f;
            PlayerName = PlayerPrefs.GetString("arena.name", "冒险家");
            Address = PlayerPrefs.GetString("arena.address", "127.0.0.1");
            PortText = PlayerPrefs.GetString("arena.port", "7777");
            LocalAddresses = FindLocalAddresses();
            string[] args = Environment.GetCommandLineArgs();
            headless = Array.IndexOf(args, "-arena-server") >= 0;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-port") PortText = args[i + 1];
                if (args[i] == "-name") PlayerName = args[i + 1];
                if (args[i] == "-join") Address = args[i + 1];
            }
            if (headless)
            {
                try
                {
                    server = new ArenaServer(new UnityWireCodec());
                    server.Start(ReadPort());
                    if (!server.IsRunning) throw new InvalidOperationException(server.Error ?? "服务器启动失败。");
                    Debug.Log("ARENA_SERVER_READY TCP " + server.Port);
                }
                catch (Exception e) { Debug.LogError(e.Message); Application.Quit(1); }
                return;
            }
            Keys = new ArenaKeyBindings(() => !VerificationInput, !Array.Exists(args, argument => argument.EndsWith("-test", StringComparison.Ordinal)));
            Display = new ArenaDisplaySettings(() => VerificationInput);
            World = new GameObject("Arena World").AddComponent<ArenaWorld>();
            World.transform.SetParent(transform, false);
            World.Initialize();
            World.PlacementPosition = PreviewPosition;
            Sound = ArenaAudio.Initialize();
            if (Sound != null) World.SoundCue = Sound.Play;
            hud = new ArenaHud(this);
            Voice = gameObject.AddComponent<ArenaVoice>();
            Voice.Initialize(() => client, () => State,
                () => hud.CommunicationInputActive || ShowGameMenu || hud.ShowSettings || ShowBestiary,
                () => VerificationInput);
            var ambienceObject = new GameObject("Arena environmental audio");
            ambienceObject.transform.SetParent(transform, false);
            Ambience = ambienceObject.AddComponent<ArenaAmbience>();
            Ambience.Initialize(this);
            World.ThunderCue = Ambience.Thunder;
            hud.ShowCommanderRange = PlayerPrefs.GetInt("arena.commanderRange", 1) != 0;
            hud.CommanderRangeChanged = enabled =>
            {
                if (VerificationInput) return;
                PlayerPrefs.SetInt("arena.commanderRange", enabled ? 1 : 0);
                PlayerPrefs.Save();
            };
            hud.ShowCreatureRange = PlayerPrefs.GetInt("arena.creatureRange", 1) != 0;
            hud.CreatureRangeChanged = enabled =>
            {
                if (VerificationInput) return;
                PlayerPrefs.SetInt("arena.creatureRange", enabled ? 1 : 0);
                PlayerPrefs.Save();
            };
            Tactics = new ArenaTactics();
            Tactics.Initialize(this);
            hud.CommanderImpactPending = id => World.CommanderImpactPending(id);
            if (Sound != null)
            {
                hud.SetAudioVolumes(Sound.Muted ? 0 : Sound.MusicVolume, Sound.Muted ? 0 : Sound.SfxVolume);
                hud.AudioVolumesChanged = (music, effects) => Sound.SetVolumes(music, effects);
                hud.SoundCue = cue => Sound.Play(cue, Vector3.zero, 1);
                Sound.SetPhase("menu");
            }
            pointer = new ArenaCursor();
            if (Array.IndexOf(args, "-quickplay") >= 0) Host(true);
            else if (Array.IndexOf(args, "-join") >= 0) Join();
        }

        static string FindLocalAddresses()
        {
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());
                var result = new System.Collections.Generic.List<string>();
                foreach (var ip in addresses)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        result.Add(ip.ToString());
                return result.Count == 0 ? "未检测到局域网 IPv4" : string.Join("  /  ", result.ToArray());
            }
            catch { return "请在 Windows 网络设置中查看本机 IPv4"; }
        }

        int ReadPort()
        {
            int port;
            if (!int.TryParse(PortText, out port) || port < 1 || port > 65535)
                throw new InvalidOperationException("端口请输入 1 到 65535 的整数。");
            return port;
        }

        public void Host(bool quick = false)
        {
            try
            {
                int port = ReadPort();
                StopNetwork();
                server = new ArenaServer(new UnityWireCodec());
                server.Start(port);
                if (!server.IsRunning) throw new InvalidOperationException(server.Error ?? "服务器启动失败。");
                client = new ArenaClient(new UnityWireCodec());
                client.Connect("127.0.0.1", port, PlayerName);
                quickStart = quick;
                quickStartSent = false;
                quickStartDeadline = Time.unscaledTime + 12;
                Status = "正在创建本机服务器…";
                ShowRoom = !quick;
                SavePreferences();
            }
            catch (Exception e) { StopNetwork(); Status = e.Message; Notify(e.Message); }
        }

        public void Join()
        {
            try
            {
                int port = ReadPort();
                if (string.IsNullOrWhiteSpace(Address)) throw new InvalidOperationException("请输入房主的 IP 或穿透域名。");
                StopNetwork();
                client = new ArenaClient(new UnityWireCodec());
                client.Connect(Address.Trim(), port, PlayerName);
                Status = "正在连接房主…";
                ShowRoom = true;
                SavePreferences();
            }
            catch (Exception e) { StopNetwork(); Status = e.Message; Notify(e.Message); }
        }

        public void CancelConnection()
        {
            if (Connected) return;
            string page = hud == null ? "main" : hud.FrontPage;
            Leave();
            if (hud != null) hud.FrontPage = page;
        }

        void SavePreferences()
        {
            if (VerificationInput) return;
            PlayerPrefs.SetString("arena.name", PlayerName.Trim());
            PlayerPrefs.SetString("arena.address", Address.Trim());
            PlayerPrefs.SetString("arena.port", PortText);
            PlayerPrefs.Save();
        }

        public void Reconnect()
        {
            if (client == null || string.IsNullOrEmpty(client.ResumeToken)) return;
            ResetMenuState(); StopPlayerMovement();
            placements.Clear(); CancelDragging(); lastErrorVersion = 0;
            previous = null; SelectedId = null;
            outcomeSound = null;
            countdown.Reset();
            revealAudio.Reset();
            if (hud != null) hud.ObserveState(null, null);
            if (World != null) World.Clear();
            string token = client.ResumeToken;
            client.Dispose();
            client = new ArenaClient(new UnityWireCodec());
            try { client.Connect(Hosting ? "127.0.0.1" : Address.Trim(), ReadPort(), PlayerName, token); Status = "正在恢复原来的队伍…"; }
            catch (Exception e) { Status = e.Message; }
        }

        public void Leave()
        {
            StopNetwork();
            ResetMenuState();
            SelectedId = dragId = null;
            walkTarget = null;
            if (World != null) World.Clear();
            Status = "";
            if (hud != null) hud.SetFrontPage("main");
        }

        void StopNetwork()
        {
            if (Voice != null) Voice.ResetSession();
            ResetMenuState();
            if (client != null) { client.Dispose(); client = null; }
            if (server != null) { server.Dispose(); server = null; }
            previous = null; quickStart = quickStartSent = false; lastNetworkError = null;
            outcomeSound = null;
            countdown.Reset();
            revealAudio.Reset();
            if (Sound != null) Sound.SetPhase("menu");
            if (hud != null) hud.ObserveState(null, null);
            placements.Clear(); CancelDragging(); lastErrorVersion = 0;
        }

        void ResetMenuState()
        {
            showGameMenu = false;
            settingsInputSuspended = false;
            ShowRoom = ShowShop = ShowBestiary = false;
            walkTarget = null; wasMoving = false;
            if (Tactics != null) Tactics.SuspendInput();
            if (hud != null) { hud.ResetCommunication(); hud.ShowAudio = false; hud.CloseSkillDetails(); hud.ShowOwnedSkills = false; hud.ClosePinnedInspection(); hud.ClearHover(); }
        }

        void StopPlayerMovement()
        {
            walkTarget = null;
            if (Connected && Me != null && !Me.eliminated && State != null &&
                (State.phase == "prep" || State.phase == "recruit" || State.phase == "tactics" || State.phase == "battle"))
                Send(new GameCommand { type = "move", dx = 0, dy = 0 });
            wasMoving = false;
        }

        // Physical Escape and the native regression driver share this exact menu route.
        public void HandleEscape()
        {
            bool changed = true, sounded = false;
            if (hud != null && hud.CancelKeyBinding()) { sounded = true; }
            else if (QuickStarting) Leave();
            else if (Tactics != null && Tactics.Targeting) { Tactics.CancelTargeting(); sounded = true; }
            else if (ShowBestiary) ShowBestiary = false;
            else if (ShowRoom && State != null && (State.phase != "lobby" || ShowGameMenu || hud != null && hud.ShowSettings)) ShowRoom = false;
            else if (hud != null && hud.ShowAudio) hud.ShowAudio = false;
            else if (hud != null && hud.HasSkillDetails) hud.CloseSkillDetails();
            else if (hud != null && hud.ShowOwnedSkills) hud.ShowOwnedSkills = false;
            else if (hud != null && !string.IsNullOrEmpty(hud.PinnedInspectionId)) hud.ClosePinnedInspection();
            else if (Tactics != null && Tactics.SkillPickerOpen) Tactics.SkillPickerOpen = false;
            else if (Tactics != null && Tactics.OptionsOpen) Tactics.OptionsOpen = false;
            else if (ShowShop) ShowShop = false;
            else if (State != null) ShowGameMenu = !ShowGameMenu;
            else if (ConnectionPending) CancelConnection();
            else changed = hud != null && hud.BackFrontPage();
            SelectedId = null; StopPlayerMovement(); CancelDragging();
            if (changed && !sounded && Sound != null) Sound.PlayUiClick();
        }

        public void Send(GameCommand action)
        {
            if (!Connected) { Notify("尚未连接服务器。"); return; }
            client.Send(action);
        }

        public void Notify(string message)
        {
            notice = message; noticeUntil = Time.unscaledTime + 3.5f;
            if (Sound != null && !string.IsNullOrEmpty(message)) Sound.Play("error", Vector3.zero, .6f);
        }

        void Update()
        {
            if (server != null) server.Pump(Time.unscaledDeltaTime);
            if (client != null && Time.unscaledTime >= SnapshotPauseUntil) client.Pump();
            if (headless) return;
            if (Display != null) Display.Tick();
            World.KeepFrontendForStateUpdates = hud.ShowSettings && !QuickStarting;
            if (hud.ShowSettings && !settingsInputSuspended)
            {
                StopPlayerMovement(); CancelDragging();
                if (Tactics != null) Tactics.SuspendInput();
                World.SetHovered(null); hud.ClearHover();
            }
            settingsInputSuspended = hud.ShowSettings;
            bool communicationHandled = !VerificationInput && hud.HandleCommunicationKeys();
            RecordCommunicationInput(communicationHandled);
            if (communicationHandled)
            {
                StopPlayerMovement(); CancelDragging();
                if (Tactics != null) Tactics.SuspendInput();
            }
            if (!VerificationInput && !communicationHandled && Input.GetKeyDown(KeyCode.Escape))
                HandleEscape();
            if (!VerificationInput && !BattlefieldInputBlocked && !hud.KeyboardCaptured && Keys.Down("shop") && CanPrepare)
            { ShowShop = !ShowShop; if (Sound != null) Sound.PlayUiClick(); }
            if (!VerificationInput && !BattlefieldInputBlocked && !hud.KeyboardCaptured && Keys.Down("bestiary"))
            { ShowBestiary = !ShowBestiary; if (Sound != null) Sound.PlayUiClick(); }
            if (!VerificationInput && !BattlefieldInputBlocked && !hud.KeyboardCaptured && !hud.PointerCaptured && !hud.Contains(Input.mousePosition))
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > .01f) World.Zoom(scroll, Input.mousePosition);
                if (Input.GetMouseButton(2)) World.Pan(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));
            }
            if (!VerificationInput && !BattlefieldInputBlocked && !hud.KeyboardCaptured && Keys.Down("reset-view")) World.ResetView();
            if (!VerificationInput && !BattlefieldInputBlocked && !hud.KeyboardCaptured)
            {
                if (Keys.Down("rotate-left")) World.RotateView(-1);
                if (Keys.Down("rotate-right")) World.RotateView(1);
            }
            if (client != null && client.ErrorVersion != lastErrorVersion)
            {
                lastErrorVersion = client.ErrorVersion;
                if (!string.IsNullOrEmpty(client.Error)) { Status = client.Error; Notify(client.Error); }
            }
            string rejectedPlacement;
            if (client != null) while (client.TryDequeueRejectedPlacement(out rejectedPlacement)) placements.Reject(rejectedPlacement);
            if (client != null && client.Error != lastNetworkError && !string.IsNullOrEmpty(client.Error))
            {
                lastNetworkError = client.Error; Status = client.Error; Notify(client.Error);
            }
            if (State != null && !ReferenceEquals(previous, State))
            {
                if (previous == null || previous.phase != State.phase)
                {
                    ShowRoom = State.phase == "lobby" && !QuickStarting;
                    // Other players finishing recruitment does not interrupt our ongoing placement.
                    if (previous == null || !IsPlacementPhase(previous.phase) || !IsPlacementPhase(State.phase))
                    {
                        SelectedId = null; walkTarget = null; wasMoving = false; CancelDragging();
                        placements.Clear();
                        ShowShop = false;
                    }
                }
                World.SetState(State, client.PlayerId);
                hud.ObserveState(previous, State);
                ObserveAudio(previous, State);
                previous = State;
                if (quickStart && Connected && State.phase == "lobby" && !quickStartSent)
                {
                    quickStartSent = true;
                    Send(new GameCommand { type = "start", bots = true });
                }
                if (quickStart && State.phase != "lobby") quickStart = quickStartSent = false;
            }
            if (quickStart && Time.unscaledTime > quickStartDeadline)
            {
                Leave(); Status = "进入战场失败，请重试。"; Notify(Status);
            }
            if (!Connected) placements.Clear();
            else if (placements.Reconcile(Me, Time.unscaledTime)) Notify("摆位确认超时，已同步服务器位置。");
            revealAudio.Tick(Time.unscaledTime, PlayInterfaceCue);
            if (Me != null && SelectedId != null && !Me.units.Exists(u => u.id == SelectedId)) { SelectedId = null; CancelDragging(); }
            bool tacticsConsumed = !VerificationInput && Tactics.ProcessInput();
            bool canWalk = Connected && Me != null && !Me.eliminated && State != null &&
                (CanArrangeCreatures || State.phase == "battle");
            if (!CanArrangeCreatures || BattlefieldInputBlocked || hud.PointerCaptured || hud.KeyboardCaptured)
            {
                CancelDragging();
                if (hud.PointerCaptured || BattlefieldInputBlocked || hud.KeyboardCaptured)
                {
                    walkTarget = null;
                    if (wasMoving) { Send(new GameCommand { type = "move", dx = 0, dy = 0 }); wasMoving = false; }
                }
            }
            else if (!VerificationInput && State.phase != "tactics")
            {
                ProcessPlacementPointer(Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
            }
            if (!VerificationInput && Connected && State != null && !CanArrangeCreatures && !tacticsConsumed &&
                !(canWalk && State.phase == "battle") && !BattlefieldInputBlocked && !hud.PointerCaptured && !hud.KeyboardCaptured &&
                (State.phase == "prep" || State.phase == "recruit" || State.phase == "tactics" || State.phase == "battle") &&
                Input.GetMouseButtonDown(0) && !hud.Contains(Input.mousePosition))
                PinCreatureAt(Input.mousePosition);
            if (!VerificationInput && canWalk && !BattlefieldInputBlocked && !hud.PointerCaptured && !hud.KeyboardCaptured && !tacticsConsumed)
            {
                if (!CanPrepare && State.phase == "battle" && Input.GetMouseButtonDown(0) && !hud.Contains(Input.mousePosition))
                {
                    Point2 at;
                    if (!PinCreatureAt(Input.mousePosition))
                    {
                        hud.ClosePinnedInspection();
                        if (World.TryGround(Input.mousePosition, out at)) MoveSteveTo(at);
                    }
                }
                HandleMovement();
            }
            else if (wasMoving && Connected)
            {
                Send(new GameCommand { type = "move", dx = 0, dy = 0 }); wasMoving = false;
            }
            World.SetSelectedUnit(Tactics.Active ? null : SelectedId);
            World.SetTacticalSelection(Tactics.Active ? Tactics.SelectedUnitIds : null);
            World.SetPinnedCreature(hud.PinnedInspectionId);
            World.ShowCommanderRange = hud.ShowCommanderRange;
            World.ShowCreatureRange = hud.ShowCreatureRange;
            Point2 commandPoint = new Point2();
            bool preview = !BattlefieldInputBlocked && Tactics.Targeting && Me != null && !hud.Contains(VerificationPointer ?? (Vector2)Input.mousePosition) &&
                Tactics.PointAt(VerificationPointer ?? (Vector2)Input.mousePosition, out commandPoint);
            World.SetCommandPreview(Me == null ? null : Me.commanderSkill, preview ? (Point2?)commandPoint : null, !BattlefieldInputBlocked && Tactics.Targeting);
            World.SetSkillHighlight(hud.HighlightedSkillId, PlayerId);
            if (outcomeSound != null && Time.unscaledTime >= outcomeSoundAt && (World == null || !World.RitualActive))
            {
                if (Sound != null) Sound.Play(outcomeSound, Vector3.zero, 1);
                outcomeSound = null;
            }
            InspectPointer(VerificationPointer ?? (Vector2)Input.mousePosition);
            string frontend = hud.MenuBackgroundMode;
            World.SetFrontend(frontend == "lobby_host" ? "host" : frontend == "lobby_guest" ? "join" : frontend);
            UpdateCursor();
        }

        void UpdateCursor()
        {
            if (pointer == null || VerificationInput) return;
            Vector2 mouse = Input.mousePosition;
            ArenaCursor.Kind kind = ArenaCursor.Kind.Arrow;
            if (hud.PointerCaptured || dragId != null) kind = ArenaCursor.Kind.Grabbing;
            else if (!BattlefieldInputBlocked && Input.GetMouseButton(2) && !hud.Contains(mouse)) kind = ArenaCursor.Kind.Move;
            else if (hud.WantsTextCursor(mouse)) kind = ArenaCursor.Kind.Text;
            else if (hud.WantsHandCursor(mouse)) kind = ArenaCursor.Kind.Hand;
            else if (World.HoveredCreature != null) kind = CanArrangeCreatures && World.HoveredCreature.Slot == Me.slot ? ArenaCursor.Kind.Grab : ArenaCursor.Kind.Inspect;
            pointer.Update(kind, mouse, Input.GetMouseButtonDown(0), Input.GetMouseButton(0));
        }

        static bool IsPlacementPhase(string phase) { return phase == "prep" || phase == "recruit"; }
        void ObserveAudio(GameState before, GameState next)
        {
            int warningSecond = countdown.Observe(next, PlayerId);
            revealAudio.Observe(next, Time.unscaledTime, PlayInterfaceCue);
            if (Sound == null) return;
            if (warningSecond > 0) Sound.Play(warningSecond <= 3 ? "countdown_urgent" : "countdown", Vector3.zero, .8f);
            if (next.phase != "finished") outcomeSound = null;
            Sound.SetPhase(next.phase);
            if (before == null) return;
            if (before.phase != next.phase)
            {
                if (next.phase == "battle") Sound.Play("battle_start", Vector3.zero, 1);
                if (next.phase == "recruit") Sound.Play("recruit", Vector3.zero, .75f);
                if (next.phase == "tactics") Sound.Play("tactics_start", Vector3.zero, .85f);
                if (next.phase == "finished")
                {
                    outcomeSound = Me != null && !Me.eliminated ? "victory" : "defeat";
                    outcomeSoundAt = Time.unscaledTime + 4.5f;
                }
            }
            PlayerState prior = before.players.Find(p => p.id == PlayerId);
            string confirmation = InterfaceConfirmation(prior, Me, before.round == next.round);
            if (confirmation != null) Sound.Play(confirmation, Vector3.zero, .62f);
            if (prior != null && Me != null && Me.gold > prior.gold)
                Sound.Play("coin", Vector3.zero, .65f);
        }
        // Confirmation sounds follow accepted snapshots. Rejected purchases and repeated packets stay quiet.
        internal static string InterfaceConfirmation(PlayerState prior, PlayerState current, bool sameRound)
        {
            if (prior == null || current == null || !sameRound) return null;
            if (prior.commanderSkill != current.commanderSkill) return "skill_select";
            if (current.upgrades != null) foreach (string skill in current.upgrades)
                if (prior.upgrades == null || !prior.upgrades.Contains(skill)) return "purchase";
            if (current.gold < prior.gold && current.units != null) foreach (UnitState unit in current.units)
                if (prior.units == null || !prior.units.Exists(old => old.id == unit.id)) return "purchase";
            return null;
        }
        void PlayInterfaceCue(string cue)
        { if (Sound != null) Sound.Play(cue, Vector3.zero, .8f); }
        Point2? PreviewPosition(string unitId) { Point2 position; return placements.TryGet(unitId, out position) ? (Point2?)position : null; }
        internal bool PlacementDragging { get { return dragId != null; } }
        internal void CancelDragging() { dragId = null; if (World != null) World.SetDrag(null, null); }

        // Both physical mouse input and the opt-in native smoke driver use the same drag path.
        internal void ProcessPlacementPointer(Vector2 mouse, bool pressed, bool held, bool released)
        {
            if (!CanArrangeCreatures || BattlefieldInputBlocked || hud.PointerCaptured || hud.KeyboardCaptured) return;
            if (pressed && !hud.Contains(mouse))
            {
                string id;
                if (World.TryPickUnit(mouse, out id))
                {
                    SelectedId = dragId = id; dragStart = mouse; walkTarget = null;
                    Point2 ground;
                    World.TryGetUnitPosition(id, out dragPoint);
                    dragOffset = World.TryGround(mouse, out ground) ? new Point2(dragPoint.x - ground.x, dragPoint.y - ground.y) : new Point2();
                    World.SetDrag(id, dragPoint);
                    if (Sound != null) Sound.Play("pickup", PlacementSoundPosition(dragPoint), .7f);
                }
                else
                {
                    SelectedId = null;
                    Point2 point;
                    if (!PinCreatureAt(mouse))
                    {
                        hud.ClosePinnedInspection();
                        if (World.TryGround(mouse, out point)) MoveSteveTo(point);
                    }
                }
            }
            if (dragId != null && held && (mouse - dragStart).sqrMagnitude > 9)
            {
                Point2 point;
                if (World.TryGround(mouse, out point))
                {
                    dragPoint = ClampPlacement(new Point2(point.x + dragOffset.x, point.y + dragOffset.y));
                    World.SetDrag(dragId, dragPoint);
                }
            }
            if (dragId != null && released)
            {
                Point2 point;
                if ((mouse - dragStart).sqrMagnitude > 9 && World.TryGround(mouse, out point))
                {
                    dragPoint = ClampPlacement(new Point2(point.x + dragOffset.x, point.y + dragOffset.y));
                    Send(placements.Submit(dragId, dragPoint, Time.unscaledTime));
                    if (Sound != null) Sound.Play("place", PlacementSoundPosition(dragPoint), .8f);
                }
                else if ((mouse - dragStart).sqrMagnitude <= 9) PinCreatureAt(mouse);
                CancelDragging();
            }
        }

        internal void InspectPointer(Vector2 mouse)
        {
            VoxelCreature visual; FighterState fighter;
            if (State == null || !Connected || BattlefieldInputBlocked || dragId != null || hud.PointerCaptured ||
                (State.phase != "prep" && State.phase != "recruit" && State.phase != "battle" && State.phase != "tactics") || hud.Contains(mouse))
            {
                World.SetHovered(null); hud.ClearHover(); return;
            }
            if(!World.TryInspectCreature(mouse,out visual,out fighter)){
                World.SetHovered(null);ObjectiveState objective;
                if(World.TryInspectObjective(mouse,out objective))hud.SetObjectiveInspection(objective,mouse);else hud.ClearHover();
                return;
            }
            World.SetHovered(visual);
            CreatureDefinition definition = CreatureCatalog.Get(visual.Kind);
            PlayerState owner = State.players.Find(p => p.slot == visual.Slot);
            UnitState unit = owner == null ? null : owner.units.Find(u => u.id == visual.UnitId);
            hud.SetInspection(definition, fighter, unit, owner, mouse);
        }

        internal bool PinCreatureAt(Vector2 mouse)
        {
            VoxelCreature visual; FighterState fighter;
            if (State == null || World == null || BattlefieldInputBlocked || !World.TryInspectCreature(mouse, out visual, out fighter)) return false;
            PlayerState owner = State.players.Find(p => p.slot == visual.Slot);
            UnitState unit = owner == null ? null : owner.units.Find(u => u.id == visual.UnitId);
            hud.PinInspection(CreatureCatalog.Get(visual.Kind), fighter, unit, owner, mouse);
            return true;
        }

        internal void MoveSteveTo(Point2 point)
        {
            if (!Connected || Me == null || Me.eliminated || BattlefieldInputBlocked || hud.KeyboardCaptured) return;
            walkTarget = null; wasMoving = false;
            Send(new GameCommand { type = "move_to", x = point.x, y = point.y });
        }

        static Vector3 PlacementSoundPosition(Point2 point) { return new Vector3(point.x / 25f, 0, point.y / 25f); }

        Point2 ClampPlacement(Point2 point)
        {
            Point2 local = ArenaGame.WorldToFormation(Me, point.x, point.y);
            return ArenaGame.FormationToWorld(Me, Mathf.Clamp(local.x, -185, 185), Mathf.Clamp(local.y, 175, 355));
        }

        void HandleMovement()
        {
            if (Time.unscaledTime < nextMove) return;
            nextMove = Time.unscaledTime + .1f;
            Vector2 input = new Vector2(
                (Keys.Held("right") ? 1 : 0) - (Keys.Held("left") ? 1 : 0),
                (Keys.Held("forward") ? 1 : 0) - (Keys.Held("backward") ? 1 : 0));
            if (input.sqrMagnitude > 0) walkTarget = null;
            Vector2 direction = World.LocalMovementToWorld(input);
            bool moving = direction.sqrMagnitude > .001f;
            if (moving || wasMoving) Send(new GameCommand { type = "move", dx = direction.x, dy = direction.y });
            wasMoving = moving;
        }

        // Screen-space keys use the rendered camera, including its rotation between player seats.
        public static Vector2 ScreenMovementToWorld(Vector2 screenInput, Quaternion cameraRotation)
        {
            Vector3 right = cameraRotation * Vector3.right, up = cameraRotation * Vector3.up;
            right.y = up.y = 0;
            if (right.sqrMagnitude < .000001f) right = Vector3.right;
            if (up.sqrMagnitude < .000001f) up = Vector3.Cross(right, Vector3.up);
            Vector3 direction = right.normalized * screenInput.x + up.normalized * screenInput.y;
            return Vector2.ClampMagnitude(new Vector2(direction.x, direction.z), 1);
        }

        void OnGUI()
        {
            if (headless || hud == null) return;
            if (VerificationInput && (Event.current.isMouse || Event.current.isKey)) Event.current.Use();
            hud.Draw();
            if (pointer != null) pointer.Draw();
        }
        void OnApplicationFocus(bool focus)
        {
            if (!focus) { StopPlayerMovement(); CancelDragging(); if (Tactics != null) Tactics.SuspendInput(); }
            if (!focus && pointer != null) pointer.Suspend();
        }
        void OnApplicationQuit() { StopNetwork(); }
        void OnDestroy() { if (Instance == this) { StopNetwork(); if (pointer != null) pointer.Dispose(); if (Sound != null) Destroy(Sound.gameObject); Instance = null; } }
    }
}
