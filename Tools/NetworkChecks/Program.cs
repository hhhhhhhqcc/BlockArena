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
        Require(WireProtocol.Version == 11, "Concealment provenance and host exposure timers require protocol v11.");
        if (server != null) { CheckFreshRooms(); CheckSharedPreparation(); CheckLobbyDisconnectSeats(); }
        using (var legacy = OpenRaw())
        {
            byte[] payload = Encoding.UTF8.GetBytes(rawCodec.Encode(new WirePacket { type = "hello", protocol = 10, name = "旧版客户端" }));
            byte[] header = Header(payload.Length);
            legacy.GetStream().Write(header, 0, header.Length); legacy.GetStream().Write(payload, 0, payload.Length);
            WirePacket response = ReadRawPacket(legacy);
            Require(response.type == "error" && response.message.Contains("协议版本不一致") && response.protocol == WireProtocol.Version,
                "A v8 client was not explicitly rejected before handshake.");
            Require(string.IsNullOrEmpty(response.placementId), "A handshake error carried a placement ID.");
        }
        Pass("protocol v10 explicitly rejected before joining the v11 game");

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

        CheckWorldAndCommanderSelection();

        clients[0].Send(new GameCommand { type = "start", bots = false });
        Wait(() => clients.All(c => c.State.phase == "recruit"), 8000, "start/recruit");
        for (int i = 0; i < 4; i++)
        {
            Require(Own(clients[i]).shop != null && Own(clients[i]).shop.Length == 3, "Own recruit choices missing.");
            Require(!string.IsNullOrEmpty(Own(clients[i]).recruitOfferId), "Own recruit revision missing.");
            Require(Own(clients[i]).units.All(u => u.paidPrice == 0), "Starter creatures incorrectly carry a refundable price.");
            Require(Own(clients[i]).market.Count == TradeCatalog.MarketSlots && Own(clients[i]).market.All(o => o.kind == "skill"),
                "A new market must contain only permanent skills.");
            Require(Own(clients[i]).market.Select(o => o.itemId).Distinct().Count() == Own(clients[i]).market.Count,
                "A new market contains duplicate skills.");
            foreach (var other in clients[i].State.players.Where(p => p.id != clients[i].PlayerId))
                Require((other.shop == null || other.shop.Length == 0) && other.market.Count == 0 && string.IsNullOrEmpty(other.recruitOfferId), "Another player's shop or recruit revision leaked.");
            Require(clients[i].State.roundEvent == null || string.IsNullOrEmpty(clients[i].State.roundEvent.id), "A retired central encounter appeared in a personalized snapshot.");
            Require(string.IsNullOrEmpty(clients[i].State.leaderId) || clients[i].State.players.Any(p => p.id == clients[i].State.leaderId), "Leader refers to an unknown player.");
            string snapshot = rawCodec.Encode(new WirePacket { type = "state", state = clients[i].State });
            foreach (var player in clients) Require(!snapshot.Contains(player.ResumeToken), "Resume token leaked in a snapshot.");
        }
        Pass("private choices and tokens excluded from shared snapshots");

        var refreshClient = clients[1];
        string oldRecruit = Own(refreshClient).recruitOfferId;
        string oldCandidates = string.Join(",", Own(refreshClient).shop.OrderBy(k => k));
        int refreshBalance = Own(refreshClient).gold;
        refreshClient.Send(new GameCommand { type = "recruit_refresh", offerId = oldRecruit });
        Wait(() => Own(refreshClient).recruitOfferId != oldRecruit, 4000, "revisioned recruit refresh");
        Require(Own(refreshClient).gold == refreshBalance - 2 && Own(refreshClient).recruitPending &&
            string.Join(",", Own(refreshClient).shop.OrderBy(k => k)) != oldCandidates, "Recruit refresh did not charge exactly two coins for a different candidate set.");
        string freshRecruit = Own(refreshClient).recruitOfferId;
        int refreshErrors = refreshClient.ErrorVersion;
        refreshClient.Send(new GameCommand { type = "recruit_refresh", offerId = oldRecruit });
        refreshClient.Send(new GameCommand { type = "choose", index = 0, offerId = oldRecruit });
        Wait(() => refreshClient.ErrorVersion == refreshErrors + 2, 4000, "stale recruit refresh and choice rejected");
        int recruitSnapshots = codecs[1].StatePackets;
        Wait(() => codecs[1].StatePackets > recruitSnapshots, 4000, "snapshot after stale recruitment commands");
        Require(Own(refreshClient).gold == refreshBalance - 2 && Own(refreshClient).recruitOfferId == freshRecruit && Own(refreshClient).units.Count == 2 && Own(refreshClient).recruitPending,
            "An obsolete recruit command spent gold, changed the revision or bought a creature.");
        Pass("TCP recruit refresh costs two coins, changes its candidate set and rejects repeated stale revisions without spending");

        string starter = Own(refreshClient).units[0].id;
        refreshClient.Send(new GameCommand { type = "dismiss", unitId = starter });
        Wait(() => Own(refreshClient).units.Count == 1, 4000, "starter dismissal during pending recruitment");
        Require(Own(refreshClient).gold == refreshBalance - 2 && Own(refreshClient).recruitPending,
            "Starter dismissal generated gold or consumed the pending recruit choice.");
        int dismissalErrors = refreshClient.ErrorVersion;
        string finalStarter = Own(refreshClient).units[0].id;
        string foreignStarter = Own(clients[0]).units[0].id;
        refreshClient.Send(new GameCommand { type = "dismiss", unitId = finalStarter });
        refreshClient.Send(new GameCommand { type = "dismiss", unitId = foreignStarter });
        Wait(() => refreshClient.ErrorVersion == dismissalErrors + 2, 4000, "last and foreign creature dismissals rejected");
        recruitSnapshots = codecs[1].StatePackets;
        Wait(() => codecs[1].StatePackets > recruitSnapshots, 4000, "snapshot after rejected dismissals");
        Require(Own(refreshClient).units.Count == 1 && Own(refreshClient).units[0].id == finalStarter && Own(refreshClient).gold == refreshBalance - 2 &&
            Own(clients[0]).units.Any(u => u.id == foreignStarter), "Rejected dismissal removed a last or foreign creature, or generated a refund.");
        Pass("TCP starter dismissal refunds zero, preserves recruitment and rejects the last unit and another player's unit");

        var expectedGold = new int[4]; var expectedUnits = new int[4];
        clients[0].Send(new GameCommand { type = "skip" });
        expectedGold[0] = Own(clients[0]).gold; expectedUnits[0] = Own(clients[0]).units.Count;
        for (int i = 1; i < 4; i++) {
            var own = Own(clients[i]);
            int affordable = Enumerable.Range(0, own.shop.Length).Where(n => CreatureCatalog.Get(own.shop[n]).price <= own.gold)
                .OrderBy(n => CreatureCatalog.Get(own.shop[n]).price).DefaultIfEmpty(-1).First();
            int price = affordable < 0 ? 0 : CreatureCatalog.Get(own.shop[affordable]).price;
            expectedGold[i] = own.gold - price; expectedUnits[i] = own.units.Count + (affordable < 0 ? 0 : 1);
            clients[i].Send(affordable < 0 ? new GameCommand { type = "skip" } : new GameCommand { type = "choose", index = affordable, offerId = own.recruitOfferId });
        }
        Wait(() => Enumerable.Range(0, 4).All(i => clients[i].State.phase == "prep" && Own(clients[i]).units.Count == expectedUnits[i]), 8000, "paid choices and saving by skipping");
        Require(clients.All(c => !Own(c).recruitPending), "Choice flags not cleared.");
        Require(Enumerable.Range(0, 4).All(i => Own(clients[i]).gold == expectedGold[i]), "Recruit did not charge the server price.");
        Require(clients.Where(c => !string.IsNullOrEmpty(Own(c).recruitChoice)).All(c => Own(c).units.Single(u => u.kind == Own(c).recruitChoice && u.paidPrice > 0).paidPrice == CreatureCatalog.Get(Own(c).recruitChoice).price),
            "Paid recruit price was not included in the authoritative army snapshot.");
        Pass("paid recruitment and saving by skipping synchronize authoritative gold");

        var refundClient = clients[2];
        var paidUnit = Own(refundClient).units.Single(u => u.paidPrice > 0);
        int refundBalance = Own(refundClient).gold, refundCount = Own(refundClient).units.Count;
        refundClient.Send(new GameCommand { type = "dismiss", unitId = paidUnit.id });
        Wait(() => Own(refundClient).units.Count == refundCount - 1, 4000, "paid creature dismissal");
        Require(Own(refundClient).units.All(u => u.id != paidUnit.id) && Own(refundClient).gold == refundBalance + paidUnit.paidPrice / 2,
            "Paid dismissal did not remove the purchased creature and refund half the actual paid price rounded down.");
        dismissalErrors = refundClient.ErrorVersion;
        refundClient.Send(new GameCommand { type = "dismiss", unitId = paidUnit.id });
        Wait(() => refundClient.ErrorVersion > dismissalErrors, 4000, "duplicate dismissal rejected");
        recruitSnapshots = codecs[2].StatePackets;
        Wait(() => codecs[2].StatePackets > recruitSnapshots, 4000, "snapshot after duplicate dismissal");
        Require(Own(refundClient).gold == refundBalance + paidUnit.paidPrice / 2 && Own(refundClient).units.Count == refundCount - 1, "Duplicate dismissal refunded twice.");
        Pass("TCP paid creature dismissal refunds floor(actual paid price / 2) once and snapshots paid prices");

        var buyer=clients[0];var buyerState=Own(buyer);
        string obsolete=buyerState.market[0].id;int moneyBefore=buyerState.gold;
        buyer.Send(new GameCommand{type="refresh"});
        Wait(()=>Own(buyer).market.All(o=>o.id!=obsolete),4000,"new market offer IDs");
        Require(Own(buyer).gold==moneyBefore-2,"Refresh did not deduct authoritative price.");
        int purchaseError=buyer.ErrorVersion;buyer.Send(new GameCommand{type="purchase",offerId=obsolete});
        Wait(()=>buyer.ErrorVersion>purchaseError,4000,"obsolete offer rejected");
        var skill=Own(buyer).market.First(o=>o.kind=="skill"&&o.price<=Own(buyer).gold);moneyBefore=Own(buyer).gold;
        buyer.Send(new GameCommand{type="purchase",offerId=skill.id});
        Wait(()=>Own(buyer).upgrades.Contains(skill.itemId),4000,"permanent skill purchase snapshot");
        Require(Own(buyer).gold==moneyBefore-skill.price,"Permanent skill price not deducted.");
        Require(Own(buyer).market.All(o=>o.itemId!=skill.itemId),"Learned skill remains for sale.");
        Require(Own(buyer).units.All(u=>u.equipment.Count==0),"A permanent purchase populated retired equipment.");
        purchaseError=buyer.ErrorVersion;
        buyer.Send(new GameCommand{type="purchase",offerId=skill.id});
        Wait(()=>buyer.ErrorVersion>purchaseError,4000,"duplicate permanent purchase rejected");
        Require(Own(buyer).gold==moneyBefore-skill.price,"Duplicate purchase charged twice.");
        Require(string.IsNullOrEmpty(buyer.RejectedPlacementId),"A purchase error was reported as a placement rejection.");
        Pass("TCP skill-only market, purchase without selected creature, exact deduction and stale offer rejection");

        if (server != null)
        {
            // Arrange a late-game inventory, then exercise the real TCP purchase/refresh path.
            var owner = server.State.players.Single(p => p.id == clients[3].PlayerId);
            string finalSkill = TradeCatalog.Ids.Last(); owner.upgrades.Clear(); owner.upgrades.AddRange(TradeCatalog.Ids.Where(id => id != finalSkill)); owner.gold = 100;
            clients[3].Send(new GameCommand { type = "refresh" });
            Wait(() => Own(clients[3]).market.Count == 1 && Own(clients[3]).market[0].itemId == finalSkill, 4000, "last unlearned skill offer");
            Require(Own(clients[3]).gold == 98, "Last available skill refresh charged the wrong amount.");
            clients[3].Send(new GameCommand { type = "purchase", offerId = Own(clients[3]).market[0].id });
            Wait(() => Own(clients[3]).market.Count == 0 && Own(clients[3]).upgrades.Count == TradeCatalog.Ids.Length, 4000, "all skills learned empty market");
            int exhaustedGold = Own(clients[3]).gold, errors = clients[3].ErrorVersion;
            clients[3].Send(new GameCommand { type = "refresh" });
            Wait(() => clients[3].ErrorVersion > errors, 4000, "exhausted market refresh rejected");
            int snapshots = codecs[3].StatePackets;
            Wait(() => codecs[3].StatePackets > snapshots, 4000, "exhausted market final balance");
            Require(Own(clients[3]).gold == exhaustedGold && Own(clients[3]).market.Count == 0, "Empty refresh spent gold or restored learned skills.");
            Pass("TCP last-skill purchase exposes an empty market and exhausted refresh never spends gold");
        }

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

        int rejectedCast = clients[0].ErrorVersion;
        clients[0].Send(new GameCommand { type="cast",x=Own(clients[0]).steve.x,y=Own(clients[0]).steve.y });
        Wait(()=>clients[0].ErrorVersion>rejectedCast,4000,"preparation cast rejected");
        Require(Own(clients[0]).commanderUses==2,"A preparation cast spent a combat use.");
        for (int i = 0; i < 4; i++) if (!Own(clients[i]).ready) clients[i].Send(new GameCommand { type = "ready" });
        Wait(() => clients.All(c => c.State.phase == "reveal"), 10000, "all-ready map reveal");
        Require(clients.All(c=>c.State.revealStage=="arena"&&(c.State.battlefield==null||string.IsNullOrEmpty(c.State.battlefield.mapId))),"Reveal did not first return to the normal arena.");
        Wait(()=>clients.All(c=>c.State.phase=="reveal"&&c.State.revealStage=="map"),5000,"shared map draw");
        Require(clients.All(c=>c.State.battlefield!=null&&c.State.battlefield.mapId=="courtyards"&&c.State.battlefield.weather=="clear"&&
            c.State.battlefield.terrain.Any(t=>t.kind=="chasm")&&c.State.battlefield.terrain.Count(t=>t.kind=="bridge")==4&&
            c.State.battlefield.objectives.Any(o=>o.kind=="altar")),"Map and terrain diverged or weather was revealed too early.");
        Require(clients.All(c=>Math.Abs(c.State.battlefield.terrain.Single(t=>t.id=="central-highland").height-1.65f)<.001f),"Authored central highland height did not survive TCP serialization.");
        Require(clients.All(c=>c.State.battlefield.objectives.Count(o=>o.kind=="supply")==4),"Designed outer-courtyard reward routes were not synchronized.");
        int revealSeed=clients[0].State.battlefield.seed;
        Require(clients.All(c=>c.State.battlefield.seed==revealSeed),"Clients received different battlefield seeds.");
        Wait(()=>clients.All(c=>c.State.phase=="reveal"&&c.State.revealStage=="weather"),8000,"terrain fully emerged before weather draw");
        Require(clients.All(c=>c.State.battlefield.revealAge>=ArenaGame.RevealTerrainSeconds-.01f&&c.State.battlefield.weather=="clear"),"Weather draw raced unfinished terrain.");
        Wait(()=>clients.All(c=>c.State.phase=="reveal"&&c.State.revealStage=="climate"&&c.State.battlefield.weather=="storm"),5000,"shared final weather draw");
        Pass("all clients share plain arena, map draw, full terrain emergence, weather draw and climate transition in order");
        Wait(()=>clients.All(c=>c.State.phase=="tactics"),5000,"reveal to tactical planning");
        Require(clients.All(c=>c.State.timeLeft>116&&c.State.timeLeft<=120),"Tactical planning is not 120 seconds.");
        CheckNavigationAndTacticalPlacement();
        CheckPrivateTacticalPlans();
        for(int i=0;i<4;i++)clients[i].Send(new GameCommand{type="tactics_ready"});
        Wait(() => clients.All(c => c.State.phase == "battle"), 8000, "all-ready tactics to battle");
        Require(clients.All(c=>c.State.timeLeft>116&&c.State.timeLeft<=120),"Battle limit is not synchronized at 120 seconds.");
        Require(clients.All(c => Own(c).units.All(u => c.State.fighters.Any(f => f.ownerId == c.PlayerId && f.unitId == u.id && !f.summoned))),
            "Every original friendly battle unit must remain in its owner's personalized snapshot.");
        Require(clients.All(c => c.State.fighters.Where(f => f.ownerId != c.PlayerId && f.hidden).All(enemy =>
            c.State.fighters.Any(ally => !ally.dead && ally.ownerId == c.PlayerId && BattlefieldVision.CanSee(c.State.battlefield,ally,enemy)))),
            "Personalized snapshots exposed an undiscovered concealed enemy.");
        Require(clients.All(c=>c.State.fighters.Count==c.State.players.Sum(p=>p.units.Count)),
            "Ordinary enemies must all be visible even beyond AI acquisition range on the clear courtyards fixture.");
        Require(clients.All(c => c.State.fighters.All(f => f.slowMultiplier > 0 && f.slowMultiplier <= 1)),
            "The authoritative movement slow multiplier did not survive TCP serialization.");
        Require(clients.All(c=>c.State.fighters.All(f=>f.attackRevealRemaining>=0&&f.attackRevealRemaining<=2&&f.concealmentSources>=0&&f.concealmentSources<=7)),
            "Concealment fields did not survive authoritative TCP serialization.");
        Pass("battle snapshots expose ordinary armies while preserving concealed-enemy discovery and synchronized concealment and movement fields");
        Require(clients.All(c => c.State.fighters.Where(f => !f.neutral).All(f => f.attack > 0 && f.attackSpeed > 0 && f.range > 0 && f.speed > 0)),
            "Actual combat stats were not included in network snapshots.");
        Require(clients.All(c => c.State.fighters.Where(f => f.neutral).All(f => f.ownerId == "neutral" && f.slot == -1 && f.maxHp > 0) &&
            c.State.players.All(p => p.id != "neutral" && p.killIncome >= 0 && p.killIncome <= 5 && p.bountyIncome >= 0 && p.bountyIncome <= 3 && p.eventIncome >= 0)),
            "Neutral event identity or bounded income fields were not preserved in personalized battle snapshots.");
        Require(clients.All(c=>!c.State.fighters.Any(f=>f.neutral)),"The retired central guardian still spawned.");
        CheckBattleCommanderCommands();
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
            // Finish only the authoritative fixture, then exercise normal public TCP commands.
            // This avoids waiting for an entire multi-round match merely to test the lobby transition.
            server.State.phase="finished";server.State.winnerId=clients[0].PlayerId;
            Wait(()=>clients.All(c=>c.State.phase=="finished"),4000,"finished fixture broadcasts");
            int restartErrors=clients[1].ErrorVersion;
            clients[1].Send(new GameCommand{type="restart"});
            Wait(()=>clients[1].ErrorVersion>restartErrors,4000,"non-host rematch rejected");
            string[] playerIds=clients.Select(c=>c.PlayerId).ToArray();
            clients[0].Send(new GameCommand{type="restart"});
            Wait(()=>clients.All(c=>c.State.phase=="lobby"),4000,"rematch returns all peers to lobby");
            Require(clients.All(c=>c.State.round==0&&c.State.players.Count==4&&c.State.fighters.Count==0&&
                c.State.players.All(p=>p.hp==20&&!p.eliminated&&!p.recruitPending&&p.units.Count==0&&p.tactics.Count==0)),"The lobby retained the previous match's state.");
            Require(clients.Select(c=>c.PlayerId).SequenceEqual(playerIds)&&clients.All(c=>c.Connected),"Returning to lobby replaced identities or disconnected a peer.");
            clients[0].Send(new GameCommand{type="world_options",mapId="frost",weather="snow"});
            Wait(()=>clients.All(c=>c.State.preferredMap=="frost"),4000,"returned lobby accepts new options");
            clients[0].Send(new GameCommand{type="start",bots=false});
            Wait(()=>clients.All(c=>c.State.phase=="recruit"&&c.State.round==1),4000,"explicit start begins the next match");
            Pass("TCP rematch preserves all four sessions in a clean lobby and waits for an explicit new start");
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

    private void CheckWorldAndCommanderSelection()
    {
        int errors=clients[1].ErrorVersion;
        string mapBefore=clients[0].State.preferredMap;
        clients[1].Send(new GameCommand{type="world_options",mapId="bog",weather="rain"});
        Wait(()=>clients[1].ErrorVersion>errors,4000,"non-host world selection rejected");
        Require(clients[0].State.preferredMap==mapBefore,"A non-host changed the battlefield selection.");
        errors=clients[0].ErrorVersion;
        clients[0].Send(new GameCommand{type="world_options",mapId="nonexistent-map"});
        Wait(()=>clients[0].ErrorVersion>errors,4000,"invalid map rejected");
        clients[0].Send(new GameCommand{type="world_options",mapId="courtyards",weather="storm"});
        Wait(()=>clients.All(c=>c.State.preferredMap=="courtyards"&&c.State.preferredWeather=="storm"),4000,"host map and weather selection");
        string[] skills={"drums","trap","focus","rift"};
        for(int i=0;i<4;i++)clients[i].Send(new GameCommand{type="commander",skillId=skills[i]});
        Wait(()=>Enumerable.Range(0,4).All(i=>Own(clients[i]).commanderSkill==skills[i]),4000,"per-player commander skill selection");
        Require(clients.All(c=>c.State.players.All(p=>p.commanderUses==2)),"Fresh commander charges did not synchronize.");
        Pass("host-only map/weather options, invalid catalog IDs and per-player commander selection use real TCP");
    }

    private void CheckNavigationAndTacticalPlacement()
    {
        var client=clients[0];var own=Own(client);var unit=own.units[0];
        var placement=ArenaGame.FormationToWorld(own,0,300);
        client.Send(new GameCommand{type="place",unitId=unit.id,x=placement.x,y=placement.y,placementId="tactics-drag"});
        Wait(()=>Own(client).units.Any(u=>u.id==unit.id&&u.placementId=="tactics-drag"&&Math.Abs(u.x-placement.x)<.01f&&Math.Abs(u.y-placement.y)<.01f),4000,"tactical placement acknowledgement");
        int errors=client.ErrorVersion;
        client.Send(new GameCommand{type="place",unitId=Own(clients[1]).units[0].id,x=placement.x,y=placement.y,placementId="foreign-drag"});
        Wait(()=>client.ErrorVersion>errors,4000,"foreign tactical drag denied");
        var destination=ArenaGame.FormationToWorld(own,100,300);
        client.Send(new GameCommand{type="move_to",x=destination.x,y=destination.y});
        Wait(()=>Own(client).steveNavigating||Math.Abs(Own(client).steve.x-destination.x)<2,4000,"server navigation started");
        Wait(()=>!Own(client).steveNavigating&&Math.Abs(Own(client).steve.x-destination.x)<2&&Math.Abs(Own(client).steve.y-destination.y)<2,5000,"server navigation completed");
        Require(clients.All(c=>{var steve=c.State.players.Single(p=>p.id==client.PlayerId).steve;return Math.Abs(steve.x-destination.x)<2&&Math.Abs(steve.y-destination.y)<2;}),"Navigation destination did not reach every client.");
        Pass("real TCP tactical dragging protects ownership and click navigation completes on the authoritative server");
    }

    private void CheckPrivateTacticalPlans()
    {
        for(int i=0;i<4;i++){
            var own=Own(clients[i]);var unit=own.units[0];
            var nodes=new[]{
                new TacticNode{action="wait",condition="always",x=unit.x,y=unit.y,seconds=4},
                new TacticNode{action="move",condition="after_time",x=0,y=i==0?263:0,seconds=4},
                new TacticNode{action="capture",condition="always",targetId="altar",x=0,y=0,seconds=5}
            };
            clients[i].Send(new GameCommand{type="tactics_set",unitIds=new[]{unit.id},nodes=nodes});
        }
        Wait(()=>clients.All(c=>Own(c).tactics.Count==1),4000,"private route assignments");
        foreach(var client in clients){
            var plan=Own(client).tactics.Single();
            Require(plan.nodes.Count==3&&plan.nodes[0].action=="wait"&&plan.nodes[0].seconds==4&&
                plan.nodes[1].condition=="after_time"&&plan.nodes[2].targetId=="altar","Route nodes or visual-program conditions changed in transit.");
            foreach(var other in client.State.players.Where(p=>p.id!=client.PlayerId))
                Require(other.tactics.Count==0,"An opponent's secret battle plan leaked in a snapshot.");
        }
        var owner=clients[0];string id=Own(owner).tactics.Single().id,unitId=Own(owner).units[0].id;
        string foreign=Own(clients[1]).units[0].id;
        int errors=owner.ErrorVersion;
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{foreign},nodes=new[]{new TacticNode{action="wait",seconds=1}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId,unitId},nodes=new[]{new TacticNode{action="wait",seconds=1}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId},nodes=new[]{new TacticNode{action="telefrag"}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId},nodes=new[]{new TacticNode{x=BattlefieldCatalog.Extent(owner.State.battlefield)+1}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId},nodes=new[]{new TacticNode{condition="unknown"}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId},nodes=new[]{new TacticNode{action="capture",targetId="fake-objective"}}});
        owner.Send(new GameCommand{type="tactics_set",unitIds=new[]{unitId},nodes=Enumerable.Range(0,11).Select(_=>new TacticNode()).ToArray()});
        Wait(()=>owner.ErrorVersion==errors+7,4000,"invalid routes rejected atomically");
        int states=codecs[0].StatePackets;Wait(()=>codecs[0].StatePackets>states,4000,"route snapshot after rejected edits");
        Require(Own(owner).tactics.Count==1&&Own(owner).tactics.Single().id==id&&Own(owner).tactics.Single().nodes.Count==3,
            "A rejected tactic request cleared or changed the prior accepted route.");
        Pass("private multi-node routes survive serialization; foreign units, duplicates, invalid conditions, bounds, targets and oversized plans reject atomically");

        // Clearing one own squad is an intentional edit, and must not touch an opponent.
        var clearClient=clients[3];string ownUnit=Own(clearClient).units[0].id;
        clearClient.Send(new GameCommand{type="tactics_set",unitIds=new[]{ownUnit},nodes=Array.Empty<TacticNode>()});
        Wait(()=>Own(clearClient).tactics.Count==0,4000,"own route clearing");
        Require(Own(clients[0]).tactics.Single().id==id,"Clearing another squad affected the host's route.");
        Pass("empty route nodes clear only the authenticated player's selected squad");
    }

    private void CheckBattleCommanderCommands()
    {
        var hostClient=clients[0];var own=Own(hostClient);var unit=own.units[0];
        var fighter=hostClient.State.fighters.Single(f=>f.unitId==unit.id&&!f.summoned);float beforeSpeed=fighter.attackSpeed;
        int errors=hostClient.ErrorVersion;
        hostClient.Send(new GameCommand{type="commander",skillId="ward"});
        hostClient.Send(new GameCommand{type="cast",x=999,y=0});
        hostClient.Send(new GameCommand{type="cast",x=-own.steve.x,y=-own.steve.y});
        Wait(()=>hostClient.ErrorVersion==errors+3,4000,"locked skill and illegal command targets rejected");
        Require(Own(hostClient).commanderSkill=="drums"&&Own(hostClient).commanderUses==2,"Rejected casts or a locked skill edit consumed a use.");
        hostClient.Send(new GameCommand{type="cast",x=unit.x,y=unit.y});
        Wait(()=>Own(hostClient).commanderUses==1&&clients.All(c=>c.State.battlefield.zones.Any(z=>z.kind=="drums"&&z.ownerId==hostClient.PlayerId)),4000,"authoritative command zone broadcast");
        Wait(()=>hostClient.State.fighters.Any(f=>f.unitId==unit.id&&!f.summoned&&f.attackSpeed>=beforeSpeed*1.19f),3000,"war drums modify actual attack speed");
        errors=hostClient.ErrorVersion;hostClient.Send(new GameCommand{type="cast",x=unit.x,y=unit.y});
        Wait(()=>hostClient.ErrorVersion>errors,4000,"commander cooldown enforced");
        Require(Own(hostClient).commanderUses==1,"Cooldown rejection consumed another skill use.");

        var trapper=clients[1];var trapperState=Own(trapper);
        trapper.Send(new GameCommand{type="cast",x=trapperState.steve.x,y=trapperState.steve.y});
        Wait(()=>Own(trapper).commanderUses==1&&trapper.State.battlefield.hazards.Any(h=>h.kind=="trap"&&h.ownerId==trapper.PlayerId&&!h.triggered),4000,"owner trap snapshot");
        string trapId=trapper.State.battlefield.hazards.Single(h=>h.kind=="trap"&&h.ownerId==trapper.PlayerId).id;
        Require(clients.Where(c=>c!=trapper).All(c=>!c.State.battlefield.hazards.Any(h=>h.id==trapId)),"A hidden enemy trap leaked to another client.");
        foreach(var client in clients.Where(c=>c!=trapper))Require(!rawCodec.Encode(new WirePacket{type="state",state=client.State}).Contains(trapId),"Hidden trap ID leaked in raw JSON.");

        var focus=clients[2];errors=focus.ErrorVersion;
        string friendly=focus.State.fighters.First(f=>f.ownerId==focus.PlayerId).id;
        focus.Send(new GameCommand{type="cast",targetId=friendly,x=Own(focus).steve.x,y=Own(focus).steve.y});
        Wait(()=>focus.ErrorVersion>errors,4000,"friendly focus target rejected");
        Require(Own(focus).commanderUses==2,"An illegal friendly focus target spent a cast.");

        var rift=clients[3];var riftState=Own(rift);Point2 start=riftState.steve;
        float length=(float)Math.Sqrt(start.x*start.x+start.y*start.y);
        float x=start.x*(1-65/Math.Max(65,length)),y=start.y*(1-65/Math.Max(65,length));
        rift.Send(new GameCommand{type="cast",x=x,y=y});
        Wait(()=>Own(rift).commanderUses==1&&rift.State.battlefield.hazards.Any(h=>h.kind=="rift"),4000,"rift wind-up warning");
        Require(Math.Abs(Own(rift).steve.x-start.x)<.1f&&Math.Abs(Own(rift).steve.y-start.y)<.1f,"Rift skipped its cast wind-up.");
        Wait(()=>Math.Abs(Own(rift).steve.x-x)<.1f&&Math.Abs(Own(rift).steve.y-y)<.1f,3000,"authoritative rift teleport");
        Require(Own(rift).commanderCooldown>0,"Teleport did not retain a server cooldown.");
        Pass("real TCP commander zones apply stats; casts enforce phase/range/skill lock/cooldown, private traps and delayed rift movement");

        // The route is now executing on the server rather than merely round-tripping JSON.
        Wait(()=>hostClient.State.fighters.Any(f=>f.unitId==unit.id&&!f.summoned&&(f.dead||Math.Abs(f.x-unit.x)>12||Math.Abs(f.y-unit.y)>12)),6500,"timed wait transitions into route movement");
        Require(Own(hostClient).tactics.Single().nodes[1].condition=="after_time","Runtime execution mutated the source visual-program route.");
        Pass("the server executes the wait/after-time route and retains the authored program");
    }

    private PlayerState Own(ArenaClient client) { return client.State.players.Single(p => p.id == client.PlayerId); }
    private void CheckFreshRooms()
    {
        var signatures = new HashSet<string>();
        for (int i = 0; i < 6; i++)
        {
            using (var freshServer = new ArenaServer(new JsonWireCodec()))
            using (var freshClient = new ArenaClient(new JsonWireCodec()))
            {
                freshServer.Start(0); Require(freshServer.IsRunning, "Fresh test room failed to listen.");
                freshClient.Connect("127.0.0.1", freshServer.Port, "随机种子验证");
                var timer = Stopwatch.StartNew(); bool started = false;
                while (timer.ElapsedMilliseconds < 8000)
                {
                    freshServer.Pump(.005f); freshClient.Pump();
                    if (freshClient.Connected && !started) { freshClient.Send(new GameCommand { type = "start" }); started = true; }
                    if (freshClient.State != null && freshClient.State.phase == "recruit") break;
                    Thread.Sleep(4);
                }
                Require(freshClient.State != null && freshClient.State.phase == "recruit", "Fresh TCP room failed to recruit.");
                signatures.Add(string.Join(",", Own(freshClient).shop));
            }
        }
        Require(signatures.Count > 1, "Default TCP rooms repeated one fixed first-round recruitment sequence.");
        Pass("six independently hosted TCP rooms produce varied first-round recruit choices");
    }
    private void CheckSharedPreparation()
    {
        using(var room=new ArenaServer(new JsonWireCodec()))
        using(var first=new ArenaClient(new JsonWireCodec()))
        using(var second=new ArenaClient(new JsonWireCodec())){
            room.Start(0);Require(room.IsRunning,"Shared preparation fixture failed to listen.");
            var timer=Stopwatch.StartNew();double last=timer.Elapsed.TotalSeconds;
            void WaitRoom(Func<bool> predicate,string label,int timeout=5000){
                long until=timer.ElapsedMilliseconds+timeout;
                while(timer.ElapsedMilliseconds<until){
                    double now=timer.Elapsed.TotalSeconds;room.Pump((float)Math.Min(.1,Math.Max(0,now-last)));last=now;
                    first.Pump();second.Pump();Thread.Sleep(4);if(predicate())return;
                }
                throw new Exception("Shared preparation TCP timeout: "+label+"; "+first.Error+" / "+second.Error);
            }
            first.Connect("127.0.0.1",room.Port,"先准备者");WaitRoom(()=>first.Connected,"host handshake");
            second.Connect("127.0.0.1",room.Port,"后选择者");WaitRoom(()=>second.Connected,"guest handshake");
            first.Send(new GameCommand{type="start",bots=false});WaitRoom(()=>first.State.phase=="recruit"&&second.State.phase=="recruit","shared preparation begins");
            Require(first.State.timeLeft>43&&first.State.timeLeft<=45,"Preparation did not start with one 45-second timer.");
            room.State.timeLeft=17;
            first.Send(new GameCommand{type="skip"});WaitRoom(()=>!Own(first).recruitPending&&Own(second).recruitPending,"first personal choice completes");
            var offer=Own(first).market.First(o=>o.price<=Own(first).gold);int balance=Own(first).gold;
            first.Send(new GameCommand{type="purchase",offerId=offer.id});
            first.Send(new GameCommand{type="place",unitId=Own(first).units[0].id,x=30,y=250,placementId="parallel-prep"});
            first.Send(new GameCommand{type="ready"});
            WaitRoom(()=>Own(first).ready&&Own(first).upgrades.Contains(offer.itemId)&&Own(first).units[0].placementId=="parallel-prep","individual purchase, placement and ready while another recruits");
            Require(first.State.phase=="recruit"&&Own(second).recruitPending&&Own(first).gold==balance-offer.price,"Parallel preparation waited for another player or mischarged the skill.");
            Pass("one 45-second TCP preparation window allows each completed player to buy skills, place and ready independently");
            float before=room.State.timeLeft;second.Send(new GameCommand{type="skip"});
            WaitRoom(()=>first.State.phase=="prep"&&second.State.phase=="prep","last reinforcement choice completes");
            Require(first.State.timeLeft<=before+.02f&&first.State.timeLeft>before-2&&Own(first).ready,"Last choice reset the timer or cleared an already prepared player.");
            second.Send(new GameCommand{type="ready"});WaitRoom(()=>first.State.phase=="reveal"&&second.State.phase=="reveal","all ready bypasses the remaining shared timer");
            Pass("the last TCP reinforcement choice preserves remaining time and earlier readiness; all-ready skips the rest");

            var owner=room.State.players.Single(p=>p.id==first.PlayerId);room.State.round=3;owner.ready=false;
            owner.commanderUses=1;owner.commanderCooldown=6.5f;
            string[] phases={"recruit","prep","tactics"};string[] skills={"trap","ward","cleanse"};
            for(int i=0;i<phases.Length;i++){
                room.State.phase=phases[i];room.State.timeLeft=100;owner.ready=false;owner.recruitPending=false;
                first.Send(new GameCommand{type="commander",skillId=skills[i]});int index=i;
                WaitRoom(()=>Own(first).commanderSkill==skills[index],"later-round "+phases[i]+" commander selection");
                Require(Own(first).commanderUses==1&&Math.Abs(Own(first).commanderCooldown-6.5f)<.001f,"Changing commander refreshed charges or cooldown.");
                first.Send(new GameCommand{type=phases[i]=="tactics"?"tactics_ready":"ready"});WaitRoom(()=>Own(first).ready,"ready locks commander selection");
                int errors=first.ErrorVersion;first.Send(new GameCommand{type="commander",skillId="smoke"});WaitRoom(()=>first.ErrorVersion>errors,"ready commander edit denied");
                Require(Own(first).commanderSkill==skills[i],"A prepared player changed the locked commander.");
                first.Send(new GameCommand{type=phases[i]=="tactics"?"tactics_ready":"ready"});WaitRoom(()=>!Own(first).ready,"cancel ready unlocks later selection");
            }
            Pass("later-round TCP commander choices work in recruit, preparation and tactics while ready locks the selection without refreshing uses");
            foreach(string phase in new[]{"reveal","battle","results","finished"}){
                room.State.phase=phase;room.State.timeLeft=100;int errors=first.ErrorVersion;
                first.Send(new GameCommand{type="commander",skillId="smoke"});WaitRoom(()=>first.ErrorVersion>errors,"locked phase "+phase);
                Require(Own(first).commanderSkill=="cleanse","A locked phase accepted commander replacement.");
            }
            room.State.phase="prep";owner.eliminated=true;int eliminatedErrors=first.ErrorVersion;
            first.Send(new GameCommand{type="commander",skillId="smoke"});WaitRoom(()=>first.ErrorVersion>eliminatedErrors,"eliminated commander replacement denied");
            Require(owner.commanderSkill=="cleanse"&&owner.commanderUses==1&&Math.Abs(owner.commanderCooldown-6.5f)<.001f,"Rejected selections changed commander combat resources.");
            Pass("TCP reveal, combat, results, finished and eliminated states reject commander replacement");

            owner.eliminated=false;owner.ready=true;owner.recruitPending=false;room.State.phase="recruit";room.State.timeLeft=.05f;
            var pending=room.State.players.Single(p=>p.id==second.PlayerId);pending.recruitPending=true;pending.ready=false;pending.gold=0;
            WaitRoom(()=>first.State.phase=="reveal"&&second.State.phase=="reveal","shared timeout directly enters reveal");
            Require(!Own(second).recruitPending&&first.State.timeLeft<=ArenaGame.RevealSeconds&&first.State.timeLeft>9,"Timeout granted a second preparation period or left recruitment pending.");
            Pass("TCP shared preparation expiry completes pending recruitment and enters reveal without an extra timer");
        }
    }

    private void CheckLobbyDisconnectSeats()
    {
        using(var room=new ArenaServer(new JsonWireCodec())){
            room.Start(0);Require(room.IsRunning,"Seat-reuse fixture failed to listen.");
            var peers=new List<ArenaClient>();var timer=Stopwatch.StartNew();double last=timer.Elapsed.TotalSeconds;
            void StepRoom(){
                double now=timer.Elapsed.TotalSeconds;room.Pump((float)Math.Min(.1,Math.Max(0,now-last)));last=now;
                foreach(var peer in peers)peer.Pump();Thread.Sleep(4);
            }
            void WaitRoom(Func<bool> predicate,string label,int timeout=8000){
                long until=timer.ElapsedMilliseconds+timeout;
                while(timer.ElapsedMilliseconds<until){StepRoom();if(predicate())return;}
                throw new Exception("Lobby seat regression timed out: "+label+"; "+string.Join(" / ",peers.Select(p=>p.Error)));
            }
            ArenaClient Join(string name,string token=null){
                var client=new ArenaClient(new JsonWireCodec());peers.Add(client);client.Connect("127.0.0.1",room.Port,name,token);return client;
            }
            try{
                var hostClient=Join("主机");WaitRoom(()=>hostClient.Connected,"host joins");
                var guest1=Join("朋友甲");WaitRoom(()=>guest1.Connected,"guest one joins");
                var guest2=Join("朋友乙");WaitRoom(()=>guest2.Connected,"guest two joins");
                var guest3=Join("朋友丙");WaitRoom(()=>guest3.Connected&&hostClient.State.players.Count==4,"four slots occupied");
                string reconnectId=guest2.PlayerId,reconnectToken=guest2.ResumeToken;
                guest2.Dispose();
                var early=Join("宽限期抢位");WaitRoom(()=>!early.Connecting&&!string.IsNullOrEmpty(early.Error),"grace period keeps occupied seat",3000);
                Require(!early.Connected&&early.Error.Contains("房间已满"),"A new player stole a recoverable grace-period seat.");
                var resumed=Join("恢复朋友乙",reconnectToken);WaitRoom(()=>resumed.Connected,"valid token resumes within grace",3000);
                Require(resumed.PlayerId==reconnectId&&room.State.players.Count==4,"Grace-period recovery changed identity or duplicated a slot.");
                Pass("lobby disconnect grace reserves the seat and valid recovery keeps the same identity");

                string vacantId=resumed.PlayerId,vacantToken=resumed.ResumeToken;int vacantSlot=Own(resumed).slot;
                // A disconnected participant remains recoverable while the match is active/finished.
                room.State.phase="finished";room.State.winnerId=hostClient.PlayerId;resumed.Dispose();
                WaitRoom(()=>room.State.players.Single(p=>p.id==vacantId).connected==false,"finished-match disconnect grace expires");
                Require(room.State.players.Count==4,"An active-match identity was prematurely expired.");
                hostClient.Send(new GameCommand{type="restart"});
                WaitRoom(()=>hostClient.State.phase=="lobby"&&hostClient.State.players.Count==3,"returning lobby releases the departed participant");
                var stale=Join("已失效恢复",vacantToken);WaitRoom(()=>!stale.Connecting&&!string.IsNullOrEmpty(stale.Error),"removed token rejected");
                Require(!stale.Connected&&stale.Error.Contains("恢复凭证已失效"),"Deleting a player left its old resume token valid.");
                var replacement=Join("新朋友");WaitRoom(()=>replacement.Connected&&hostClient.State.players.Count==4,"new friend uses the vacated slot");
                Require(Own(replacement).slot==vacantSlot&&replacement.PlayerId!=vacantId&&replacement.ResumeToken!=vacantToken,
                    "The replacement did not receive an independent identity in the freed slot.");
                Pass("returning to lobby expires departed participants and tokens so a new friend can reuse the slot");

                string idleId=guest1.PlayerId,idleToken=guest1.ResumeToken;guest1.Dispose();
                WaitRoom(()=>hostClient.State.players.Count==3&&!hostClient.State.players.Any(p=>p.id==idleId),"an ordinary lobby departure expires");
                var idleStale=Join("普通大厅过期恢复",idleToken);WaitRoom(()=>!idleStale.Connecting&&!string.IsNullOrEmpty(idleStale.Error),"ordinary lobby token expires with its slot");
                Require(!idleStale.Connected&&idleStale.Error.Contains("恢复凭证已失效"),"An ordinary lobby departure retained an orphan credential.");
                var lobbyReplacement=Join("大厅新朋友");WaitRoom(()=>lobbyReplacement.Connected&&hostClient.State.players.Count==4,"ordinary lobby slot accepts a fresh hello");
                Pass("ordinary lobby departures also free their seats and revoke their matching credentials after grace");

                string oldHostToken=hostClient.ResumeToken;
                foreach(var peer in peers)peer.Dispose();
                WaitRoom(()=>room.State.players.Count==0,"empty lobby expires every departed session");
                var newHost=Join("接班房主");WaitRoom(()=>newHost.Connected,"empty server accepts a new host");
                Require(newHost.State.hostId==newHost.PlayerId&&newHost.State.players.Count==1,"An empty dedicated server retained a missing host.");
                var staleHost=Join("旧房主凭证",oldHostToken);WaitRoom(()=>!staleHost.Connecting&&!string.IsNullOrEmpty(staleHost.Error),"old host token rejected");
                Require(!staleHost.Connected,"The departed host's token regained authority in the new room.");
                newHost.Send(new GameCommand{type="start",bots=true});WaitRoom(()=>newHost.State.phase=="recruit"&&newHost.State.players.Count==4,"new host can start with bots");
                Pass("an emptied dedicated lobby elects the next arrival as host and accepts a fresh bot-filled match");
            }finally{foreach(var peer in peers)peer.Dispose();}
        }
    }
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
