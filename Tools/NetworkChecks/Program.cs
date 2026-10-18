using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BlockArena.Core;
using BlockArena.Networking;

sealed class JsonWireCodec : IWireCodec
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
    public string ClaimedPlayerId;
    public int StatePackets;
    public string Encode(WirePacket packet)
    {
        CheckThread();
        if (packet.type == "action" && ClaimedPlayerId != null) packet.playerId = ClaimedPlayerId;
        if (packet.type != "welcome" && packet.type != "hello" && !string.IsNullOrEmpty(packet.token))
            throw new Exception("A secret token escaped into a non-handshake packet.");
        return JsonSerializer.Serialize(packet, options);
    }
    public WirePacket Decode(string json)
    {
        CheckThread();
        var packet = JsonSerializer.Deserialize<WirePacket>(json, options);
        if (packet != null && packet.type == "state") StatePackets++;
        return packet;
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new Exception("Codec executed on an I/O thread instead of the Pump thread.");
    }
}

sealed class NetworkCheck : IDisposable
{
    public readonly List<string> Passed = new List<string>();
    private readonly ArenaClient[] clients = new ArenaClient[4];
    private readonly JsonWireCodec[] codecs = new JsonWireCodec[4];
    private readonly List<ArenaClient> extras = new List<ArenaClient>();
    private readonly JsonWireCodec rawCodec = new JsonWireCodec();
    private readonly ArenaServer server;
    private readonly string host;
    private readonly int port;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double previous;

    public NetworkCheck(string externalHost = null, int externalPort = 0)
    {
        if (externalHost == null)
        {
            server = new ArenaServer(new JsonWireCodec());
            server.Start(0);
            Require(server.IsRunning && server.Port > 0, server.Error ?? "Server failed to listen.");
            host = "127.0.0.1"; port = server.Port;
        }
        else { host = externalHost; port = externalPort; }
    }

    public void Run()
    {
        Require(WireProtocol.Version == 3, "Economy and equipment require protocol v3.");
        using (var legacy = OpenRaw())
        {
            byte[] payload = Encoding.UTF8.GetBytes(rawCodec.Encode(new WirePacket { type = "hello", protocol = 1, name = "旧版客户端" }));
            byte[] header = Header(payload.Length);
            legacy.GetStream().Write(header, 0, header.Length); legacy.GetStream().Write(payload, 0, payload.Length);
            WirePacket response = ReadRawPacket(legacy);
            Require(response.type == "error" && response.message.Contains("协议版本不一致") && response.protocol == 3,
                "A v1 client was not explicitly rejected before handshake.");
            Require(string.IsNullOrEmpty(response.placementId), "A handshake error carried a placement ID.");
        }
        Pass("protocol v1 explicitly rejected before joining the v3 game");

        for (int i = 0; i < 4; i++)
        {
            codecs[i] = new JsonWireCodec();
            clients[i] = new ArenaClient(codecs[i]);
            clients[i].Connect(host, port, "玩家" + (i + 1));
            Require(clients[i].Connecting, "Connect should return immediately in the connecting state.");
            int index = i;
            Wait(() => clients[index].Connected, 12000, "TCP hello/welcome " + i);
        }
        Wait(() => clients.All(c => c.State.players.Count == 4), 8000, "four-player snapshots");
        Require(clients.Select(c => c.PlayerId).Distinct().Count() == 4, "Four distinct player identities.");
        Require(clients.Select(c => Own(c).slot).OrderBy(i => i).SequenceEqual(new[] { 0, 1, 2, 3 }), "Four unique slots.");
        Pass("four real TCP clients / UTF-8 names / first hello is host");

        // A forged packet playerId must not grant another player's authority.
        codecs[1].ClaimedPlayerId = clients[0].PlayerId;
        clients[1].Send(new GameCommand { type = "start", bots = false });
        Wait(() => !string.IsNullOrEmpty(clients[1].Error), 4000, "non-host start rejected");
        Require(clients[0].State.phase == "lobby", "Forged playerId was incorrectly trusted.");
        codecs[1].ClaimedPlayerId = null;
        Pass("connection-bound identity rejects a forged host ID");

        clients[0].Send(new GameCommand { type = "start", bots = false });
        Wait(() => clients.All(c => c.State.phase == "recruit"), 8000, "start/recruit");
        for (int i = 0; i < 4; i++)
        {
            Require(Own(clients[i]).shop != null && Own(clients[i]).shop.Length == 3, "Own recruit choices missing.");
            foreach (var other in clients[i].State.players.Where(p => p.id != clients[i].PlayerId))
                Require((other.shop == null || other.shop.Length == 0) && other.market.Count == 0, "Another player's shop leaked.");
            string snapshot = rawCodec.Encode(new WirePacket { type = "state", state = clients[i].State });
            foreach (var player in clients) Require(!snapshot.Contains(player.ResumeToken), "Resume token leaked in a snapshot.");
        }
        Pass("private choices and tokens excluded from shared snapshots");

        clients[0].Send(new GameCommand { type = "skip" });
        for (int i = 1; i < 4; i++) clients[i].Send(new GameCommand { type = "choose", index = i % 3 });
        Wait(() => clients.All(c => c.State.phase == "prep" && Own(c).units.Count == (c==clients[0]?2:3)), 8000, "paid choices and saving by skipping");
        Require(clients.All(c => !Own(c).recruitPending), "Choice flags not cleared.");
        Require(clients.All(c => Own(c).gold == (string.IsNullOrEmpty(Own(c).recruitChoice)?12:12-CreatureCatalog.Get(Own(c).recruitChoice).price)), "Recruit did not charge the server price.");
        Pass("paid recruitment and saving by skipping synchronize authoritative gold");

        var buyer=clients[0];var buyerState=Own(buyer);
        var gear=buyerState.market.First(o=>o.kind=="equipment"&&o.price<=buyerState.gold);
        string gearUnit=buyerState.units[0].id;int moneyBefore=buyerState.gold;
        buyer.Send(new GameCommand{type="purchase",offerId=gear.id,unitId=gearUnit});
        Wait(()=>Own(buyer).units[0].equipment.Contains(gear.itemId),4000,"equipment purchase snapshot");
        Require(Own(buyer).gold==moneyBefore-gear.price,"Equipment price not deducted.");
        Require(Own(buyer).market.Single(o=>o.id==gear.id).sold,"Bought offer not sold.");
        int purchaseError=buyer.ErrorVersion;
        buyer.Send(new GameCommand{type="purchase",offerId=gear.id,unitId=gearUnit});
        Wait(()=>buyer.ErrorVersion>purchaseError,4000,"duplicate purchase rejected");
        Require(Own(buyer).gold==moneyBefore-gear.price,"Duplicate purchase charged twice.");
        string obsolete=Own(buyer).market[0].id;moneyBefore=Own(buyer).gold;
        buyer.Send(new GameCommand{type="refresh"});
        Wait(()=>Own(buyer).market.All(o=>o.id!=obsolete),4000,"new market offer IDs");
        Require(Own(buyer).gold==moneyBefore-2,"Refresh did not deduct authoritative price.");
        purchaseError=buyer.ErrorVersion;buyer.Send(new GameCommand{type="purchase",offerId=obsolete,unitId=gearUnit});
        Wait(()=>buyer.ErrorVersion>purchaseError,4000,"obsolete offer rejected");
        Pass("TCP equipment ownership, exact deduction, sold offers and stale market IDs");

        for (int i = 0; i < 4; i++)
        {
            var player = Own(clients[i]);
            double angle = player.slot * Math.PI / 2;
            float x = (float)(31.5 * Math.Cos(angle) - 245.25 * Math.Sin(angle));
            float y = (float)(31.5 * Math.Sin(angle) + 245.25 * Math.Cos(angle));
            string unitId = player.units[0].id;
            string placementId = "slot-" + i;
            clients[i].Send(new GameCommand { type = "place", unitId = unitId, x = x, y = y, placementId = placementId });
            int index = i;
            Wait(() => Math.Abs(Own(clients[index]).units[0].x - x) < .01f && Math.Abs(Own(clients[index]).units[0].y - y) < .01f &&
                Own(clients[index]).units[0].placementId == placementId, 4000, "continuous placement slot " + i);
        }
        string victimUnit = Own(clients[0]).units[0].id;
        float victimX = Own(clients[0]).units[0].x;
        int foreignErrorVersion = clients[1].ErrorVersion;
        clients[1].Send(new GameCommand { type = "place", unitId = victimUnit, x = 0, y = 200, placementId = "foreign-unit" });
        Wait(() => clients[1].ErrorVersion == foreignErrorVersion + 1, 4000, "foreign unit rejected");
        Require(clients[1].RejectedPlacementId == "foreign-unit", "Foreign placement rejection lost its correlation ID.");
        Require(clients[1].TryDequeueRejectedPlacement(out string rejectedId) && rejectedId == "foreign-unit", "Foreign rejection was not queued.");
        Require(Math.Abs(Own(clients[0]).units[0].x - victimX) < .01f, "Foreign unit was mutated.");
        Require(Own(clients[0]).units[0].placementId == "slot-0", "Foreign placement changed the last accepted ID.");
        Pass("four-direction continuous placement and ownership enforcement");

        var placementClient = clients[0];
        int beforePlacements = placementClient.ErrorVersion;
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 30, y = 230, placementId = "rapid-A-1" });
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 80, y = 280, placementId = "rapid-B-2" });
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 30, y = 230, placementId = "rapid-A-3" });
        Wait(() => clients.All(c => c.State.players.Single(p => p.id == placementClient.PlayerId).units[0].placementId == "rapid-A-3"),
            4000, "latest rapid placement ID on all clients");
        Require(placementClient.ErrorVersion == beforePlacements, "Valid consecutive placements were rejected.");
        Require(Math.Abs(Own(placementClient).units[0].x - 30) < .01f && Math.Abs(Own(placementClient).units[0].y - 230) < .01f,
            "Rapid A-B-A placement ended at the wrong coordinates.");
        Pass("rapid consecutive A-B-A placements echo only the final accepted ID and position");

        int beforeLongId = placementClient.ErrorVersion;
        string longId = new string('x', 65);
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 50, y = 260, placementId = longId });
        Wait(() => placementClient.ErrorVersion == beforeLongId + 1, 4000, "overlong placement ID rejected");
        Require(placementClient.RejectedPlacementId == longId, "Rejected overlong ID was not correlated exactly.");
        Require(placementClient.TryDequeueRejectedPlacement(out rejectedId) && rejectedId == longId, "Overlong ID rejection missing from queue.");
        int statesBeforeRejected = codecs[0].StatePackets;
        Wait(() => codecs[0].StatePackets > statesBeforeRejected, 4000, "snapshot after overlong ID rejection");
        Require(Own(placementClient).units[0].placementId == "rapid-A-3" && Math.Abs(Own(placementClient).units[0].x - 30) < .01f,
            "A rejected placement changed the last accepted position/ID.");
        Pass("invalid placement ID preserves prior authoritative coordinates and acknowledgement");

        int beforeTwoErrors = placementClient.ErrorVersion;
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 900, y = 230, placementId = "rejected-first" });
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 901, y = 230, placementId = "rejected-second" });
        placementClient.Send(new GameCommand { type = "place", unitId = victimUnit, x = 58, y = 263, placementId = "accepted-after-rejections" });
        Wait(() => placementClient.ErrorVersion == beforeTwoErrors + 2 && Own(placementClient).units[0].placementId == "accepted-after-rejections",
            4000, "two consecutive rejections followed by an accepted placement");
        Require(placementClient.RejectedPlacementId == "rejected-second", "Latest rejection correlation is stale.");
        Require(placementClient.TryDequeueRejectedPlacement(out rejectedId) && rejectedId == "rejected-first", "First rapid rejection was lost.");
        Require(placementClient.TryDequeueRejectedPlacement(out rejectedId) && rejectedId == "rejected-second", "Second rapid rejection was lost.");
        Require(!placementClient.TryDequeueRejectedPlacement(out rejectedId), "Rejection queue contains an unexpected entry.");
        Require(Math.Abs(Own(placementClient).units[0].x - 58) < .01f, "A rejected ID displaced a later valid placement.");
        Pass("consecutive errors increment the version and retain both correlated IDs in order");

        int beforeUnrelated = placementClient.ErrorVersion;
        placementClient.Send(new GameCommand { type = "choose", index = 0, placementId = "must-not-escape" });
        Wait(() => placementClient.ErrorVersion == beforeUnrelated + 1, 4000, "unrelated command rejection");
        Require(string.IsNullOrEmpty(placementClient.RejectedPlacementId), "A non-placement error retained or echoed a placement ID.");
        Require(!placementClient.TryDequeueRejectedPlacement(out rejectedId), "A non-placement rejection entered the placement queue.");
        Require(Own(placementClient).units[0].placementId == "accepted-after-rejections", "Unrelated error changed placement state.");
        Pass("unrelated errors clear the last rejection ID and never enter the placement queue");

        string disconnectedId = clients[2].PlayerId, disconnectedToken = clients[2].ResumeToken;
        string retainedUnit = Own(clients[2]).units[0].id;
        clients[2].Dispose();
        Wait(() => !clients[0].State.players.Single(p => p.id == disconnectedId).connected, 9000, "five-second disconnect grace");
        codecs[2] = new JsonWireCodec(); clients[2] = new ArenaClient(codecs[2]);
        clients[2].Connect(host, port, "恢复中的玩家", disconnectedToken);
        Wait(() => clients[2].Connected, 10000, "token reconnect after AI takeover");
        Require(clients[2].PlayerId == disconnectedId && Own(clients[2]).units[0].id == retainedUnit, "Reconnect changed identity/army.");
        Wait(() => clients[0].State.players.Single(p => p.id == disconnectedId).connected, 4000, "reconnected flag");
        Pass("disconnect grace, AI takeover, resume token retains identity and army");

        var oldHost = clients[0];
        string hostId = oldHost.PlayerId, hostToken = oldHost.ResumeToken;
        codecs[0] = new JsonWireCodec(); clients[0] = new ArenaClient(codecs[0]);
        clients[0].Connect(host, port, "恢复房主", hostToken);
        Wait(() => clients[0].Connected && !oldHost.Connected, 10000, "token takeover");
        oldHost.Dispose();
        Require(clients[0].PlayerId == hostId && clients[0].State.players.Count == 4, "Takeover duplicated a player.");
        Pass("resuming a live token replaces the old socket without a duplicate slot");

        for (int i = 0; i < 4; i++) if (!Own(clients[i]).ready) clients[i].Send(new GameCommand { type = "ready" });
        Wait(() => clients.All(c => c.State.phase == "battle"), 10000, "all-ready battle");
        Require(clients.All(c => c.State.fighters.Count >= 8), "Battle units were not broadcast.");
        Require(clients.All(c => c.State.fighters.All(f => f.attack > 0 && f.attackSpeed > 0 && f.range > 0 && f.speed > 0)),
            "Actual combat stats were not included in network snapshots.");
        Wait(() => clients[0].State.fighters.Any(f => f.hp < f.maxHp) || clients[0].State.effects.Count > 0, 12000, "authoritative battle changes");
        Require(clients.All(c => Own(c).connected), "Stale socket incorrectly disconnected a resumed player.");
        Pass("same automatic battle and live authoritative state on all clients");

        var late = new ArenaClient(new JsonWireCodec()); extras.Add(late);
        late.Connect(host, port, "迟到玩家");
        Wait(() => !late.Connecting && !string.IsNullOrEmpty(late.Error), 10000, "mid-game first join denied");
        Require(!late.Connected && clients[0].State.players.Count == 4, "Late join incorrectly admitted.");
        var badToken = new ArenaClient(new JsonWireCodec()); extras.Add(badToken);
        badToken.Connect(host, port, "伪造恢复", "not-a-valid-token");
        Wait(() => !badToken.Connecting && !string.IsNullOrEmpty(badToken.Error), 10000, "invalid token denied");
        Require(!badToken.Connected, "Invalid resume token admitted.");
        Pass("new mid-game players and invalid resume tokens are rejected");

        CheckBadFrame(new byte[] { 0, 1, 0, 1 }, null, "65KiB command frame");
        CheckBadFrame(new byte[] { 0, 0, 0, 0 }, null, "empty frame");
        CheckBadFrame(new byte[] { 255, 255, 255, 255 }, null, "overflow length frame");
        CheckBadFrame(new byte[] { 0, 0, 0, 2 }, new byte[] { 0xc3, 0x28 }, "invalid UTF-8");
        Pass("invalid frame lengths and UTF-8 disconnect before unsafe allocation");

        using (var fragmented = OpenRaw())
        {
            string hello = rawCodec.Encode(new WirePacket { type = "hello", name = "拆分报文" });
            byte[] bytes = Encoding.UTF8.GetBytes(hello);
            byte[] header = Header(bytes.Length);
            PumpFor(50);
            for (int i = 0; i < 4; i++) { fragmented.GetStream().Write(header, i, 1); PumpFor(5); }
            for (int i = 0; i < bytes.Length; i += 3)
                fragmented.GetStream().Write(bytes, i, Math.Min(3, bytes.Length - i));
            WirePacket response = ReadRawPacket(fragmented);
            Require(response.type == "error" && response.message.Contains("对战已开始"), "Fragmented UTF-8/length prefix was not reassembled.");
        }
        Pass("fragmented header and UTF-8 payload reassemble into one command");

        if (server != null)
        {
            using (var idle = OpenRaw())
            {
                Wait(() => DrainClosed(idle), 10500, "handshake timeout");
            }
            Pass("idle TCP connections expire without a handshake");
            int statesBefore = codecs[0].StatePackets;
            PumpFor(1100);
            int stateDelta = codecs[0].StatePackets - statesBefore;
            Require(stateDelta >= 7 && stateDelta <= 16, "Expected approximately 10 personalized snapshots/sec, got " + stateDelta);
            Pass("snapshot broadcast budget remains approximately 10Hz");
            int savedPort = port;
            server.Dispose();
            Wait(() => clients.All(c => !c.Connected), 5000, "stop server closes clients");
            using (var restarted = new ArenaServer(new JsonWireCodec()))
            {
                restarted.Start(savedPort);
                Require(restarted.IsRunning && restarted.Port == savedPort, "Stopped listener did not release its port.");
            }
            Pass("safe server shutdown releases clients and listen port");
        }
        Console.WriteLine("PASS " + Passed.Count + " network integration checks" + (server == null ? " against external Unity server" : ""));
    }

    private PlayerState Own(ArenaClient client) { return client.State.players.Single(p => p.id == client.PlayerId); }
    private void Pass(string message) { Passed.Add(message); Console.WriteLine("PASS " + message); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private void Step()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(.1, Math.Max(0, now - previous)); previous = now;
        server?.Pump(dt);
        foreach (var client in clients) client?.Pump();
        foreach (var client in extras) client.Pump();
        Thread.Sleep(4);
    }
    private void Wait(Func<bool> predicate, int timeoutMs, string message)
    {
        long until = clock.ElapsedMilliseconds + timeoutMs;
        while (clock.ElapsedMilliseconds < until)
        {
            Step();
            if (predicate()) return;
        }
        throw new Exception("Timeout: " + message + "; client errors: " + string.Join(" / ", clients.Select(c => c?.Error)));
    }
    private void PumpFor(int milliseconds)
    {
        long until = clock.ElapsedMilliseconds + milliseconds;
        while (clock.ElapsedMilliseconds < until) Step();
    }
    private TcpClient OpenRaw()
    {
        var client = new TcpClient();
        client.Connect(host, port); client.NoDelay = true; client.ReceiveTimeout = 3000;
        return client;
    }
    private static byte[] Header(int length) { return new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length }; }
    private void CheckBadFrame(byte[] header, byte[] data, string label)
    {
        using (var client = OpenRaw())
        {
            client.GetStream().Write(header, 0, header.Length);
            if (data != null) client.GetStream().Write(data, 0, data.Length);
            Wait(() => DrainClosed(client), 5000, label + " rejected");
        }
        Require(clients[0].Connected, "An invalid peer damaged healthy connections.");
    }
    private static bool DrainClosed(TcpClient client)
    {
        try
        {
            while (client.Available > 0)
            {
                byte[] data = new byte[Math.Min(8192, client.Available)];
                client.GetStream().Read(data, 0, data.Length);
            }
            return client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0;
        }
        catch (SocketException) { return true; }
        catch (IOException) { return true; }
    }
    private WirePacket ReadRawPacket(TcpClient client)
    {
        Wait(() => client.Available >= 4, 5000, "raw packet response");
        byte[] header = ReadExactly(client.GetStream(), 4);
        int count = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        Require(count > 0 && count <= WireProtocol.MaxSnapshotBytes, "Bad response frame size.");
        return rawCodec.Decode(Encoding.UTF8.GetString(ReadExactly(client.GetStream(), count)));
    }
    private static byte[] ReadExactly(NetworkStream stream, int count)
    {
        byte[] bytes = new byte[count]; int offset = 0;
        while (offset < count) { int read = stream.Read(bytes, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        return bytes;
    }
    public void Dispose()
    {
        foreach (var client in clients) client?.Dispose();
        foreach (var client in extras) client.Dispose();
        server?.Dispose();
    }
}

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string host = null; int port = 0;
            if (args.Length > 0)
            {
                if (args.Length != 3 || args[0] != "--external" || !int.TryParse(args[2], out port))
                    throw new ArgumentException("Usage: NetworkChecks [--external host port]");
                host = args[1];
            }
            using (var check = new NetworkCheck(host, port)) check.Run();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
    }
}
