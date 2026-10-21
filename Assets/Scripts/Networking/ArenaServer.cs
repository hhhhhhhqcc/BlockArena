using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using BlockArena.Core;

namespace BlockArena.Networking
{
    /// <summary>Authoritative game. Call Pump on the main thread.</summary>
    public sealed class ArenaServer : IDisposable
    {
        private sealed class AcceptedSocket { public TcpClient Socket; public double AcceptedAt; }
        private sealed class Session
        {
            public string PlayerId, Token;
            public Peer Peer;
            public double DisconnectedAt = -1;
            public bool MarkedDisconnected;
        }
        private sealed class Peer
        {
            public FramedConnection Wire;
            public Session Session;
            public double AcceptedAt, RejectedAt;
            public bool Rejected;
        }
        private readonly IWireCodec codec;
        private readonly ConcurrentQueue<AcceptedSocket> accepted = new ConcurrentQueue<AcceptedSocket>();
        private readonly List<Peer> peers = new List<Peer>();
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private TcpListener listener;
        private Thread acceptThread;
        private ArenaGame game;
        private int running, connectionCount, disposed;
        private string error;
        private float simulationAccumulator, broadcastAccumulator;
        public int Port { get; private set; }
        public string Error { get { return Volatile.Read(ref error); } }
        public bool IsRunning { get { return Volatile.Read(ref running) != 0; } }
        public GameState State { get { return game == null ? null : game.State; } }

        public ArenaServer(IWireCodec codec) { this.codec = codec ?? throw new ArgumentNullException(nameof(codec)); }
        public void Start(int port)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ArenaServer));
            if (IsRunning) throw new InvalidOperationException("服务器已经启动。");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            error = null;
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start(WireProtocol.MaxConnections);
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Volatile.Write(ref running, 1);
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Arena TCP accept" };
                acceptThread.Start();
            }
            catch (Exception ex)
            {
                if (listener != null) listener.Stop();
                listener = null; Port = 0;
                error = ex is SocketException ? "无法监听端口，请确认端口未被占用，并允许游戏通过防火墙。" : "服务器启动失败：" + ex.Message;
            }
        }

        private void AcceptLoop()
        {
            double last = NetworkClock.Now, tokens = 24;
            while (IsRunning)
            {
                try
                {
                    TcpClient socket = listener.AcceptTcpClient();
                    double now = NetworkClock.Now;
                    tokens = Math.Min(24, tokens + Math.Max(0, now - last) * 12); last = now;
                    if (!IsRunning || tokens < 1) { socket.Close(); continue; }
                    tokens -= 1;
                    if (Interlocked.Increment(ref connectionCount) > WireProtocol.MaxConnections)
                    {
                        Interlocked.Decrement(ref connectionCount); socket.Close(); continue;
                    }
                    if (!IsRunning) { Interlocked.Decrement(ref connectionCount); socket.Close(); continue; }
                    accepted.Enqueue(new AcceptedSocket { Socket = socket, AcceptedAt = now });
                }
                catch (SocketException) { if (IsRunning) Interlocked.CompareExchange(ref error, "接收新连接时发生网络错误。", null); }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex) { if (IsRunning) Interlocked.CompareExchange(ref error, "连接监听错误：" + ex.Message, null); }
            }
        }

        public void Pump(float dt)
        {
            if (!IsRunning) return;
            double now = NetworkClock.Now;
            PruneDisconnectedLobbySessions(now);
            AcceptedSocket next;
            while (accepted.TryDequeue(out next))
            {
                try { peers.Add(new Peer { Wire = new FramedConnection(next.Socket, true), AcceptedAt = next.AcceptedAt }); }
                catch (Exception) { next.Socket.Close(); Interlocked.Decrement(ref connectionCount); }
            }
            foreach (Peer peer in peers)
            {
                if (!peer.Wire.IsOpen || peer.Rejected) continue;
                if (peer.Session == null && now - peer.AcceptedAt > WireProtocol.HandshakeTimeoutSeconds)
                {
                    Reject(peer, "连接握手超时，请重新连接。"); continue;
                }
                string json; int count = 0;
                while (peer.Wire.IsOpen && !peer.Rejected && count++ < 32 && peer.Wire.TryReceive(out json)) Process(peer, json);
            }
            for (int i = peers.Count - 1; i >= 0; i--)
            {
                Peer peer = peers[i];
                if (peer.Rejected && now - peer.RejectedAt > 1) peer.Wire.Close();
                if (peer.Wire.IsOpen) continue;
                Session session = peer.Session;
                // Closing a stale connection cannot disconnect its replacement.
                if (session != null && ReferenceEquals(session.Peer, peer))
                {
                    session.Peer = null; session.DisconnectedAt = now; session.MarkedDisconnected = false;
                }
                peer.Wire.Dispose(); peers.RemoveAt(i); Interlocked.Decrement(ref connectionCount);
            }
            if (game == null) return;
            foreach (Session session in sessions.Values)
            {
                if (session.Peer == null && !session.MarkedDisconnected && session.DisconnectedAt >= 0 &&
                    now - session.DisconnectedAt >= WireProtocol.DisconnectGraceSeconds)
                {
                    game.SetConnected(session.PlayerId, false); session.MarkedDisconnected = true;
                }
            }
            PruneDisconnectedLobbySessions(now);
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) dt = 0;
            dt = Math.Min(dt, .5f);
            simulationAccumulator += dt; broadcastAccumulator += dt;
            try
            {
                int ticks = 0;
                while (simulationAccumulator >= .05f && ticks++ < 10) { game.Tick(.05f); simulationAccumulator -= .05f; }
            }
            catch (Exception ex) { error = "游戏模拟错误：" + ex.Message; }
            if (broadcastAccumulator >= .1f)
            {
                broadcastAccumulator %= .1f;
                foreach (Peer peer in peers)
                {
                    if (peer.Session == null || peer.Rejected || !peer.Wire.IsOpen || !ReferenceEquals(peer.Session.Peer, peer)) continue;
                    Send(peer, new WirePacket { type = "state", state = game.SnapshotFor(peer.Session.PlayerId) }, true);
                }
            }
        }

        private void Process(Peer peer, string json)
        {
            WirePacket packet;
            try { packet = codec.Decode(json); }
            catch (Exception) { Reject(peer, "无法读取网络消息。"); return; }
            if (packet == null || packet.protocol != WireProtocol.Version)
            {
                Reject(peer, "游戏协议版本不一致，请使用相同版本的客户端。"); return;
            }
            if (peer.Session == null)
            {
                if (packet.type != "hello") { Reject(peer, "请先完成连接握手。"); return; }
                Hello(peer, packet); return;
            }
            if (!ReferenceEquals(peer.Session.Peer, peer)) { peer.Wire.Close("该会话已在其他连接恢复。"); return; }
            if (packet.type != "action" || packet.command == null) { SendError(peer, "不支持的网络操作。"); return; }
            string placementId = packet.command.type == "place" ? packet.command.placementId : null;
            try
            {
                // Sender identity is bound to the socket; claimed playerId/state are ignored.
                game.Apply(peer.Session.PlayerId, packet.command);
            }
            catch (InvalidOperationException ex) { SendError(peer, ex.Message, placementId); }
            catch (ArgumentException ex) { SendError(peer, ex.Message, placementId); }
            catch (Exception) { SendError(peer, "当前无法执行此操作。", placementId); }
        }

        private void Hello(Peer peer, WirePacket packet)
        {
            // A restart can return to the lobby earlier in this same Pump batch.
            // Expire vacant seats and their credentials before processing the next hello.
            PruneDisconnectedLobbySessions(NetworkClock.Now);
            if (!string.IsNullOrEmpty(packet.token))
            {
                Session resumed;
                if (!sessions.TryGetValue(packet.token, out resumed)) { Reject(peer, "恢复凭证已失效，请重新加入。"); return; }
                Peer oldPeer = resumed.Peer;
                resumed.Peer = peer; resumed.DisconnectedAt = -1; resumed.MarkedDisconnected = false; peer.Session = resumed;
                if (oldPeer != null && !ReferenceEquals(oldPeer, peer)) oldPeer.Wire.Close("该会话已在新的连接恢复。");
                game.SetConnected(resumed.PlayerId, true);
                Welcome(peer); return;
            }
            if (game != null && game.State.phase != "lobby")
            {
                Reject(peer, "对战已开始。已有玩家可以使用恢复凭证重新连接。"); return;
            }
            string id = "p_" + Guid.NewGuid().ToString("N"), name = CleanName(packet.name);
            try
            {
                if (game == null || game.State.players.Count==0) game = new ArenaGame(id, name);
                else game.AddPlayer(id, name);
            }
            catch (InvalidOperationException ex) { Reject(peer, ex.Message); return; }
            var session = new Session { PlayerId = id, Token = NewToken(), Peer = peer };
            sessions.Add(session.Token, session); peer.Session = session;
            Welcome(peer);
        }
        private void PruneDisconnectedLobbySessions(double now)
        {
            if(game==null||game.State.phase!="lobby")return;
            List<Session> expired=null;
            foreach(Session session in sessions.Values){
                if(session.Peer!=null||session.DisconnectedAt<0||now-session.DisconnectedAt<WireProtocol.DisconnectGraceSeconds)continue;
                if(expired==null)expired=new List<Session>();expired.Add(session);
            }
            if(expired==null)return;
            foreach(Session session in expired){
                game.RemovePlayer(session.PlayerId);
                sessions.Remove(session.Token);
            }
        }
        private void Welcome(Peer peer)
        {
            Send(peer, new WirePacket { type = "welcome", playerId = peer.Session.PlayerId, token = peer.Session.Token, state = game.SnapshotFor(peer.Session.PlayerId) });
        }
        private void Send(Peer peer, WirePacket packet, bool snapshot = false)
        {
            if (!peer.Wire.IsOpen) return;
            try { peer.Wire.TrySend(codec.Encode(packet), snapshot); }
            catch (Exception) { peer.Wire.Close("服务器无法编码消息。"); }
        }
        private void SendError(Peer peer, string message, string placementId = null)
        {
            Send(peer, new WirePacket { type = "error", message = message, placementId = placementId });
        }
        private void Reject(Peer peer, string message)
        {
            SendError(peer, message); peer.Rejected = true; peer.RejectedAt = NetworkClock.Now; peer.Wire.CloseAfterFlush();
        }
        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "冒险家";
            var result = new System.Text.StringBuilder();
            foreach (char ch in value.Trim())
            {
                if (char.IsControl(ch) || ch == '<' || ch == '>') continue;
                if (result.Length >= 16) break;
                result.Append(ch);
            }
            if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
            return result.Length == 0 ? "冒险家" : result.ToString();
        }
        private static string NewToken()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Volatile.Write(ref running, 0);
            if (listener != null) { try { listener.Stop(); } catch { } }
            if (acceptThread != null && Thread.CurrentThread != acceptThread && acceptThread.IsAlive) acceptThread.Join(250);
            AcceptedSocket pending;
            while (accepted.TryDequeue(out pending)) { pending.Socket.Close(); Interlocked.Decrement(ref connectionCount); }
            foreach (Peer peer in peers) { peer.Wire.Dispose(); Interlocked.Decrement(ref connectionCount); }
            peers.Clear(); sessions.Clear(); Port = 0;
        }
    }
}
