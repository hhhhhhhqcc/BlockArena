using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using BlockArena.Core;

namespace BlockArena.Networking
{
    /// <summary>Nonblocking connect; Pump performs all codec work on the caller's thread.</summary>
    public sealed class ArenaClient : IDisposable
    {
        private sealed class ConnectResult
        {
            public int Attempt;
            public TcpClient Socket;
            public string Error;
        }
        private readonly IWireCodec codec;
        private readonly ConcurrentQueue<ConnectResult> results = new ConcurrentQueue<ConnectResult>();
        private readonly Queue<string> rejectedPlacements = new Queue<string>();
        private readonly object connectGate = new object();
        private TcpClient connectingSocket;
        private FramedConnection wire;
        private int attempt, connecting, disposed;
        private bool welcomed;
        private readonly List<ChatMessage> chats = new List<ChatMessage>();
        private readonly IList<ChatMessage> readOnlyChats;
        private readonly Queue<VoiceFrame> voiceFrames = new Queue<VoiceFrame>();
        private int voiceSequence;
        private string pendingName, pendingToken;
        private double helloAt, lastPacketAt;

        public GameState State { get; private set; }
        public string PlayerId { get; private set; }
        public string ResumeToken { get; private set; }
        public string Error { get; private set; }
        public int ErrorVersion { get; private set; }
        public string RejectedPlacementId { get; private set; }
        public bool Connected { get { return welcomed && wire != null && wire.IsOpen; } }
        public bool Connecting { get { return Volatile.Read(ref connecting) != 0; } }
        public IList<ChatMessage> ChatHistory { get { return readOnlyChats; } }

        public ArenaClient(IWireCodec codec)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            readOnlyChats = chats.AsReadOnly();
        }

        public void Connect(string host, int port, string name, string resumeToken = null)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ArenaClient));
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
            {
                Error = "请输入有效的服务器地址和端口。"; return;
            }
            int currentAttempt = Interlocked.Increment(ref attempt);
            lock (connectGate)
            {
                if (connectingSocket != null) { connectingSocket.Close(); connectingSocket = null; }
            }
            if (wire != null) { wire.Dispose(); wire = null; }
            welcomed = false;
            chats.Clear(); voiceFrames.Clear(); voiceSequence = 0;
            Error = null;
            RejectedPlacementId = null;
            rejectedPlacements.Clear();
            pendingName = string.IsNullOrWhiteSpace(name) ? "冒险家" : name.Trim();
            pendingToken = resumeToken;
            if (string.IsNullOrEmpty(resumeToken)) { State = null; PlayerId = null; ResumeToken = null; }
            else ResumeToken = resumeToken;
            Volatile.Write(ref connecting, 1);
            var socket = new TcpClient();
            lock (connectGate) connectingSocket = socket;
            string address = host.Trim();
            var thread = new Thread(() => ConnectWorker(socket, address, port, currentAttempt))
            {
                IsBackground = true, Name = "Arena TCP connect"
            };
            thread.Start();
        }

        private void ConnectWorker(TcpClient socket, string host, int port, int currentAttempt)
        {
            string failure = null;
            try
            {
                var operation = socket.ConnectAsync(host, port);
                if (!operation.Wait(6000)) throw new TimeoutException("连接超时。");
                if (!socket.Connected) throw new SocketException();
                socket.NoDelay = true;
            }
            catch (Exception ex)
            {
                failure = ex is TimeoutException ? "连接超时，请检查服务器地址、端口映射和防火墙。" : "无法连接服务器，请确认服务器正在运行，地址与端口正确。";
                socket.Close();
            }
            lock (connectGate)
            {
                if (ReferenceEquals(connectingSocket, socket)) connectingSocket = null;
                if (Volatile.Read(ref disposed) != 0 || currentAttempt != Volatile.Read(ref attempt))
                {
                    socket.Close(); return;
                }
                results.Enqueue(new ConnectResult { Attempt = currentAttempt, Socket = failure == null ? socket : null, Error = failure });
            }
        }

        public void Pump()
        {
            if (Volatile.Read(ref disposed) != 0) return;
            ConnectResult result;
            while (results.TryDequeue(out result))
            {
                if (result.Attempt != Volatile.Read(ref attempt))
                {
                    if (result.Socket != null) result.Socket.Close();
                    continue;
                }
                if (result.Error != null)
                {
                    Error = result.Error; Volatile.Write(ref connecting, 0); continue;
                }
                try
                {
                    wire = new FramedConnection(result.Socket, false);
                    helloAt = lastPacketAt = NetworkClock.Now;
                    SendPacket(new WirePacket { type = "hello", name = pendingName, token = pendingToken });
                }
                catch (Exception)
                {
                    result.Socket.Close(); Error = "连接初始化失败，请重试。"; Volatile.Write(ref connecting, 0);
                }
            }
            if (wire == null) return;
            string json;
            int count = 0;
            while (count++ < 128 && wire.TryReceive(out json))
            {
                WirePacket packet;
                try { packet = codec.Decode(json); }
                catch (Exception) { Fail("服务器返回了无法读取的消息。"); break; }
                if (packet == null || packet.protocol != WireProtocol.Version) { Fail("游戏协议版本不一致。"); break; }
                lastPacketAt = NetworkClock.Now;
                if (packet.type == "welcome")
                {
                    if (string.IsNullOrEmpty(packet.playerId) || string.IsNullOrEmpty(packet.token) || packet.state == null)
                    {
                        Fail("服务器握手信息不完整。"); break;
                    }
                    PlayerId = packet.playerId; ResumeToken = packet.token; State = packet.state;
                    welcomed = true; Volatile.Write(ref connecting, 0); Error = null;
                }
                else if (packet.type == "state" && welcomed && packet.state != null) State = packet.state;
                else if (packet.type == "chat" && welcomed && packet.chat != null && !string.IsNullOrEmpty(packet.chat.playerId) && packet.chat.text != null && packet.chat.text.Length <= WireProtocol.ChatMaxCharacters)
                {
                    if (chats.Count == WireProtocol.ChatHistoryLimit) chats.RemoveAt(0);
                    chats.Add(packet.chat);
                }
                else if (packet.type == "voice" && welcomed && packet.voice != null && packet.voice.playerId != PlayerId &&
                    !string.IsNullOrEmpty(packet.voice.playerId) && packet.voice.pcm != null && packet.voice.pcm.Length == WireProtocol.VoiceBase64Length)
                {
                    while (voiceFrames.Count >= WireProtocol.VoiceReceiveLimit) voiceFrames.Dequeue();
                    voiceFrames.Enqueue(packet.voice);
                }
                else if (packet.type == "error")
                {
                    Error = string.IsNullOrEmpty(packet.message) ? "服务器拒绝了这次操作。" : packet.message;
                    RejectedPlacementId = packet.placementId;
                    if (!string.IsNullOrEmpty(packet.placementId)) rejectedPlacements.Enqueue(packet.placementId);
                    ErrorVersion++;
                }
                else { Fail("收到不符合当前连接状态的消息。"); break; }
            }
            if (wire.IsOpen && !welcomed && NetworkClock.Now - helloAt > WireProtocol.HandshakeTimeoutSeconds) Fail("服务器握手超时，请重新连接。");
            else if (wire.IsOpen && welcomed && NetworkClock.Now - lastPacketAt > 12) Fail("服务器长时间没有响应，请重新连接。");
            if (!wire.IsOpen)
            {
                welcomed = false; Volatile.Write(ref connecting, 0);
                voiceFrames.Clear();
                if (string.IsNullOrEmpty(Error)) Error = string.IsNullOrEmpty(wire.Error) ? "连接已断开，可以重新连接恢复对局。" : wire.Error;
                wire.Dispose(); wire = null;
            }
        }

        public bool TryDequeueRejectedPlacement(out string placementId)
        {
            if (rejectedPlacements.Count > 0) { placementId = rejectedPlacements.Dequeue(); return true; }
            placementId = null; return false;
        }

        public void Send(GameCommand command)
        {
            if (!Connected) { Error = "尚未连接服务器。"; return; }
            if (command == null) { Error = "操作不能为空。"; return; }
            Error = null;
            SendPacket(new WirePacket { type = "action", command = command });
        }

        public void SendChat(string text)
        {
            if (!Connected || string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > WireProtocol.ChatMaxCharacters)
            {
                int length = WireProtocol.ChatMaxCharacters;
                if (char.IsHighSurrogate(text[length - 1])) length--;
                text = text.Substring(0, length);
            }
            SendPacket(new WirePacket { type = "chat", message = text });
        }

        public bool SendVoice(byte[] pcm)
        {
            if (!Connected || pcm == null || pcm.Length != WireProtocol.VoiceBytesPerFrame) return false;
            var frame = new VoiceFrame { sequence = voiceSequence++, pcm = Convert.ToBase64String(pcm) };
            try { return wire.TrySend(codec.Encode(new WirePacket { type = "voice", voice = frame }), false, true); }
            catch (Exception) { return false; }
        }

        public bool TryDequeueVoice(out VoiceFrame frame)
        {
            if (voiceFrames.Count > 0) { frame = voiceFrames.Dequeue(); return true; }
            frame = null; return false;
        }

        private void SendPacket(WirePacket packet)
        {
            try
            {
                if (wire == null || !wire.TrySend(codec.Encode(packet))) Error = "消息发送失败，连接可能已断开。";
            }
            catch (Exception) { Error = "无法编码网络消息。"; }
        }
        private void Fail(string message)
        {
            Error = message; welcomed = false; Volatile.Write(ref connecting, 0);
            if (wire != null) wire.Close(message);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Interlocked.Increment(ref attempt);
            Volatile.Write(ref connecting, 0); welcomed = false;
            voiceFrames.Clear(); chats.Clear();
            lock (connectGate) { if (connectingSocket != null) { connectingSocket.Close(); connectingSocket = null; } }
            if (wire != null) { wire.Dispose(); wire = null; }
            ConnectResult result;
            while (results.TryDequeue(out result)) if (result.Socket != null) result.Socket.Close();
        }
    }
}
