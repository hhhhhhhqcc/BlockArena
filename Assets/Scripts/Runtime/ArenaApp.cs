using System;
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
        public GameState State { get { return client != null ? client.State : null; } }
        public PlayerState Me
        {
            get
            {
                if (State == null || client == null) return null;
                return State.players.Find(p => p.id == client.PlayerId);
            }
        }
        public bool Connected { get { return client != null && client.Connected; } }
        public bool Hosting { get { return server != null; } }
        public bool HasSession { get { return client != null; } }
        public string PlayerId { get { return client == null ? null : client.PlayerId; } }
        public string PlayerName = "冒险家", Address = "127.0.0.1", PortText = "7777";
        public string Status = "", SelectedId;
        public bool ShowRoom, ShowHelp, ShowShop, ShowBestiary;
        public ArenaWorld World { get; private set; }
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
        ArenaServer server;
        ArenaClient client;
        ArenaHud hud;
        readonly PlacementPreview placements = new PlacementPreview();
        GameState previous;
        string dragId, notice = "", lastNetworkError;
        Vector2 dragStart;
        Point2 dragOffset, dragPoint;
        Point2? walkTarget;
        int lastErrorVersion;
        float nextMove, noticeUntil;
        bool wasMoving, quickStart, headless;
        internal float SnapshotPauseUntil;
        internal Vector2? VerificationPointer;
        internal bool VerificationInput;

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
                try { server = new ArenaServer(new UnityWireCodec()); server.Start(ReadPort()); Debug.Log("ARENA_SERVER_READY TCP " + server.Port); }
                catch (Exception e) { Debug.LogError(e.Message); Application.Quit(1); }
                return;
            }
            World = new GameObject("Arena World").AddComponent<ArenaWorld>();
            World.transform.SetParent(transform, false);
            World.Initialize();
            World.PlacementPosition = PreviewPosition;
            hud = new ArenaHud(this);
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
                client = new ArenaClient(new UnityWireCodec());
                client.Connect("127.0.0.1", port, PlayerName);
                quickStart = quick;
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

        void SavePreferences()
        {
            PlayerPrefs.SetString("arena.name", PlayerName.Trim());
            PlayerPrefs.SetString("arena.address", Address.Trim());
            PlayerPrefs.SetString("arena.port", PortText);
            PlayerPrefs.Save();
        }

        public void Reconnect()
        {
            if (client == null || string.IsNullOrEmpty(client.ResumeToken)) return;
            placements.Clear(); CancelDragging(); lastErrorVersion = 0;
            string token = client.ResumeToken;
            client.Dispose();
            client = new ArenaClient(new UnityWireCodec());
            try { client.Connect(Hosting ? "127.0.0.1" : Address.Trim(), ReadPort(), PlayerName, token); Status = "正在恢复原来的队伍…"; }
            catch (Exception e) { Status = e.Message; }
        }

        public void Leave()
        {
            StopNetwork();
            ShowRoom = ShowHelp = ShowShop = ShowBestiary = false;
            SelectedId = dragId = null;
            walkTarget = null;
            if (World != null) World.Clear();
            Status = "";
        }

        void StopNetwork()
        {
            if (client != null) { client.Dispose(); client = null; }
            if (server != null) { server.Dispose(); server = null; }
            previous = null; quickStart = false; lastNetworkError = null;
            placements.Clear(); CancelDragging(); lastErrorVersion = 0;
        }

        public void Send(GameCommand action)
        {
            if (!Connected) { Notify("尚未连接服务器。"); return; }
            client.Send(action);
        }

        public void Notify(string message) { notice = message; noticeUntil = Time.unscaledTime + 3.5f; }

        void Update()
        {
            if (server != null) server.Pump(Time.unscaledDeltaTime);
            if (client != null && Time.unscaledTime >= SnapshotPauseUntil) client.Pump();
            if (headless) return;
            if (!VerificationInput && Input.GetKeyDown(KeyCode.Escape))
            {
                if (ShowHelp) ShowHelp = false;
                else if (ShowBestiary) ShowBestiary = false;
                else if (ShowShop) ShowShop = false;
                else if (State != null) ShowRoom = !ShowRoom;
                SelectedId = null; walkTarget = null; CancelDragging();
            }
            if (!VerificationInput && Input.GetKeyDown(KeyCode.M) && CanPrepare && !ShowRoom && !ShowHelp && !ShowBestiary) ShowShop = !ShowShop;
            if (!VerificationInput && Input.GetKeyDown(KeyCode.B) && !ShowRoom && !ShowHelp) ShowBestiary = !ShowBestiary;
            if (!VerificationInput && !hud.Contains(Input.mousePosition))
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > .01f) World.Zoom(scroll, Input.mousePosition);
                if (Input.GetMouseButton(2)) World.Pan(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));
            }
            if (!VerificationInput && Input.GetKeyDown(KeyCode.F) && !ShowRoom && !ShowHelp && !ShowBestiary) World.ResetView();
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
                    if (State.phase != "lobby") ShowRoom = false;
                    // Other players finishing recruitment does not interrupt our ongoing placement.
                    if (previous == null || !IsPlacementPhase(previous.phase) || !IsPlacementPhase(State.phase))
                    {
                        SelectedId = null; walkTarget = null; wasMoving = false; CancelDragging();
                        placements.Clear();
                        ShowShop = false;
                    }
                    if (State.phase == "battle") Notify("四方开战 · 军团自动作战");
                }
                previous = State;
                World.SetState(State, client.PlayerId);
                if (quickStart && Connected && State.phase == "lobby")
                {
                    quickStart = false;
                    Send(new GameCommand { type = "start", bots = true });
                }
            }
            if (!Connected) placements.Clear();
            else if (placements.Reconcile(Me, Time.unscaledTime)) Notify("摆位确认超时，已同步服务器位置。");
            if (!CanPrepare || ShowRoom || ShowHelp || ShowBestiary)
            {
                CancelDragging();
            }
            else if (!VerificationInput)
            {
                ProcessPlacementPointer(Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
                HandleMovement();
            }
            World.SetSelectedUnit(SelectedId);
            InspectPointer(VerificationPointer ?? (Vector2)Input.mousePosition);
        }

        static bool IsPlacementPhase(string phase) { return phase == "prep" || phase == "recruit"; }
        Point2? PreviewPosition(string unitId) { Point2 position; return placements.TryGet(unitId, out position) ? (Point2?)position : null; }
        void CancelDragging() { dragId = null; if (World != null) World.SetDrag(null, null); }

        // Both physical mouse input and the opt-in native smoke driver use the same drag path.
        internal void ProcessPlacementPointer(Vector2 mouse, bool pressed, bool held, bool released)
        {
            if (!CanPrepare || ShowRoom || ShowHelp || ShowBestiary) return;
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
                }
                else
                {
                    SelectedId = null;
                    Point2 point;
                    if (World.TryGround(mouse, out point)) walkTarget = point;
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
                }
                CancelDragging();
            }
        }

        internal void InspectPointer(Vector2 mouse)
        {
            VoxelCreature visual; FighterState fighter;
            if (State == null || !Connected || ShowRoom || ShowHelp || ShowBestiary || dragId != null ||
                (!CanPrepare && State.phase != "battle") || hud.Contains(mouse) ||
                !World.TryInspectCreature(mouse, out visual, out fighter))
            {
                World.SetHovered(null); hud.ClearHover(); return;
            }
            World.SetHovered(visual);
            CreatureDefinition definition = CreatureCatalog.Get(visual.Kind);
            PlayerState owner = State.players.Find(p => p.slot == visual.Slot);
            UnitState unit = owner == null ? null : owner.units.Find(u => u.id == visual.UnitId);
            hud.SetInspection(definition, fighter, unit, owner, mouse);
        }

        Point2 ClampPlacement(Point2 point)
        {
            Point2 local = ArenaGame.WorldToLocal(Me.slot, point.x, point.y);
            return ArenaGame.LocalToWorld(Me.slot, Mathf.Clamp(local.x, -185, 185), Mathf.Clamp(local.y, 175, 355));
        }

        void HandleMovement()
        {
            if (Time.unscaledTime < nextMove) return;
            nextMove = Time.unscaledTime + .1f;
            Vector2 input = new Vector2(
                (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0));
            if (input.sqrMagnitude > 0) walkTarget = null;
            Vector2 direction = World.LocalMovementToWorld(input);
            if (walkTarget.HasValue)
            {
                Vector2 delta = new Vector2(walkTarget.Value.x - Me.steve.x, walkTarget.Value.y - Me.steve.y);
                if (delta.magnitude < 10) walkTarget = null;
                else direction = delta.normalized;
            }
            bool moving = direction.sqrMagnitude > .001f;
            if (moving || wasMoving) Send(new GameCommand { type = "move", dx = direction.x, dy = direction.y });
            wasMoving = moving;
        }

        void OnGUI()
        {
            if (headless || hud == null) return;
            if (VerificationInput && (Event.current.isMouse || Event.current.isKey)) Event.current.Use();
            hud.Draw();
        }
        void OnApplicationFocus(bool focus)
        {
            if (!focus) { walkTarget = null; CancelDragging(); }
        }
        void OnApplicationQuit() { StopNetwork(); }
        void OnDestroy() { if (Instance == this) { StopNetwork(); Instance = null; } }
    }
}
