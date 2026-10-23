using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BlockArena.Core;
using BlockArena.Networking;

sealed class CommsCodec : IWireCodec
{
    readonly JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
    readonly int thread = Environment.CurrentManagedThreadId;
    public string ForgedId;
    public string ReplaceVoice;
    public int? ForceSequence;
    public int StatePackets;
    public string Encode(WirePacket packet)
    {
        if (thread != Environment.CurrentManagedThreadId) throw new Exception("Codec left main thread.");
        if (ForgedId != null) { packet.playerId = ForgedId; if (packet.voice != null) packet.voice.playerId = ForgedId; }
        if (packet.voice != null && ReplaceVoice != null) packet.voice.pcm = ReplaceVoice;
        if (packet.voice != null && ForceSequence.HasValue) packet.voice.sequence = ForceSequence.Value;
        return JsonSerializer.Serialize(packet, options);
    }
    public WirePacket Decode(string json)
    {
        if (thread != Environment.CurrentManagedThreadId) throw new Exception("Codec left main thread.");
        var packet = JsonSerializer.Deserialize<WirePacket>(json, options);
        if (packet != null && packet.type == "state") StatePackets++;
        return packet;
    }
}

sealed class CommunicationCheck : IDisposable
{
    readonly ArenaServer server;
    readonly string host;
    readonly int port;
    readonly ArenaClient[] clients = new ArenaClient[4];
    readonly CommsCodec[] codecs = new CommsCodec[4];
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<(int receiver, VoiceFrame frame)> voice = new List<(int, VoiceFrame)>();
    double last;
    int checks;
    public CommunicationCheck(string remote = null, int remotePort = 0)
    {
        if (remote == null) { server = new ArenaServer(new CommsCodec()); server.Start(0); Require(server.IsRunning, server.Error); host = "127.0.0.1"; port = server.Port; }
        else { host = remote; port = remotePort; }
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    void Pass(string label) { checks++; Console.WriteLine("PASS " + label); }
    void Pump(bool collect = true)
    {
        double now = clock.Elapsed.TotalSeconds;
        server?.Pump((float)(now - last)); last = now;
        foreach (var client in clients) client?.Pump();
        if (collect) for (int i=0; i<clients.Length; i++)
        {
            VoiceFrame frame;
            while (clients[i] != null && clients[i].TryDequeueVoice(out frame)) voice.Add((i, frame));
        }
        Thread.Sleep(2);
    }
    void Wait(Func<bool> condition, string label, int timeout = 6000)
    {
        double end = clock.Elapsed.TotalSeconds + timeout / 1000.0;
        while (!condition()) { if (clock.Elapsed.TotalSeconds > end) throw new TimeoutException(label); Pump(); }
    }
    void For(int milliseconds, bool collect = true)
    {
        double end = clock.Elapsed.TotalSeconds + milliseconds / 1000.0;
        while (clock.Elapsed.TotalSeconds < end) Pump(collect);
    }
    static byte[] Pcm(int seed)
    {
        var data = new byte[WireProtocol.VoiceBytesPerFrame];
        for (int i=0; i<data.Length / 2; i++)
        {
            short value = (short)Math.Round(Math.Sin((i + seed*13) * 2 * Math.PI * (220 + seed*30) / WireProtocol.VoiceSampleRate) * 14000);
            data[i*2] = (byte)value; data[i*2+1] = (byte)(value >> 8);
        }
        return data;
    }
    public void Run()
    {
        Require(WireProtocol.Version == 11, "Communication must share protocol v11.");
        for (int i=0; i<4; i++)
        {
            codecs[i] = new CommsCodec(); clients[i] = new ArenaClient(codecs[i]); clients[i].Connect(host, port, "指挥官" + i);
            int index = i; Wait(()=>clients[index].Connected, "four-client handshake");
        }
        Wait(()=>clients.All(c=>c.State.players.Count==4), "four-player roster");
        Pass("four authenticated TCP commanders join protocol v11");

        codecs[0].ForgedId = clients[1].PlayerId;
        clients[0].SendChat("你好 <color=red>军团</color>\n\t");
        Wait(()=>clients.All(c=>c.ChatHistory.Count==1), "chat broadcast");
        foreach (var client in clients)
        {
            ChatMessage message = client.ChatHistory[0];
            Require(message.playerId == clients[0].PlayerId && message.name == "指挥官0" && message.slot == 0, "Chat identity was forged.");
            Require(!message.text.Contains('<') && !message.text.Contains('>') && !message.text.Any(char.IsControl), "Rich text/control input was not sanitized.");
        }
        codecs[0].ForgedId = null;
        Pass("chat is broadcast once with socket-bound identity and literal safe text");

        clients[0].SendChat(new string('界', 200));
        Wait(()=>clients.All(c=>c.ChatHistory.Count==2), "long chat truncation");
        Require(clients[1].ChatHistory[1].text.Length == WireProtocol.ChatMaxCharacters, "Chat length was not bounded.");
        int before = clients[0].ChatHistory.Count, errors = clients[1].ErrorVersion;
        for (int i=0; i<5; i++) { clients[1].SendChat("limited-" + i); For(25); }
        Wait(()=>clients[1].ErrorVersion > errors, "chat rate rejection"); For(120);
        Require(clients[0].ChatHistory.Count - before <= 3 && clients.All(c=>c.Connected), "Chat rate limit damaged peers or allowed flooding.");
        Pass("chat character limit and independent sender token bucket are enforced");

        voice.Clear(); codecs[0].ForgedId = clients[1].PlayerId;
        byte[][] samples = Enumerable.Range(0,4).Select(Pcm).ToArray();
        double end = clock.Elapsed.TotalSeconds + .55, nextFrame = 0;
        while (clock.Elapsed.TotalSeconds < end)
        {
            if (clock.Elapsed.TotalSeconds >= nextFrame)
            {
                for (int i=0;i<4;i++) Require(clients[i].SendVoice(samples[i]), "Healthy voice enqueue failed.");
                nextFrame = clock.Elapsed.TotalSeconds + .04;
            }
            Pump();
        }
        For(180); codecs[0].ForgedId = null;
        for (int receiver=0; receiver<4; receiver++)
        {
            int destination = receiver;
            var received = voice.Where(v=>v.receiver == destination).Select(v=>v.frame).ToArray();
            Require(received.Length >= 12, "Too few voice frames reached a commander.");
            Require(received.Select(f=>f.playerId).Distinct().Count()==3 && received.All(f=>f.playerId != clients[destination].PlayerId), "Voice echoed or trusted a forged player ID.");
            foreach (var frame in received)
            {
                int sender = Array.FindIndex(clients,c=>c.PlayerId==frame.playerId);
                Require(sender>=0 && Convert.FromBase64String(frame.pcm).SequenceEqual(samples[sender]), "PCM samples changed during relay.");
            }
        }
        Pass("four simultaneous PCM streams reach the other three commanders without echo or identity spoofing");

        voice.Clear(); codecs[2].ReplaceVoice = new string('!', WireProtocol.VoiceBase64Length);
        clients[2].SendVoice(samples[2]); For(130);
        codecs[2].ReplaceVoice = Convert.ToBase64String(new byte[WireProtocol.VoiceBytesPerFrame+1]);
        clients[2].SendVoice(samples[2]); For(130); codecs[2].ReplaceVoice = null;
        Require(voice.Count==0 && clients.All(c=>c.Connected), "Invalid media reached playback or damaged a game session.");
        Require(!clients[2].SendVoice(new byte[2]), "Local media size validation missing.");
        Pass("invalid base64, decoded PCM size and local frame lengths are dropped safely");

        For(350); voice.Clear(); codecs[3].ForceSequence=10000;
        clients[3].SendVoice(samples[3]); For(70); clients[3].SendVoice(samples[3]); For(140); codecs[3].ForceSequence=null;
        Require(voice.Count==3 && voice.All(v=>v.frame.playerId==clients[3].PlayerId), "Duplicate sequence replay was relayed.");
        Pass("stale/replayed input sequence is rejected before fan-out");

        voice.Clear(); int snapshots = codecs[0].StatePackets; end=clock.Elapsed.TotalSeconds+1; nextFrame=0;
        while(clock.Elapsed.TotalSeconds<end)
        {
            if(clock.Elapsed.TotalSeconds>=nextFrame){clients[2].SendVoice(samples[2]);nextFrame=clock.Elapsed.TotalSeconds+.02;}
            Pump();
        }
        For(150);
        int receivedByHost=voice.Count(v=>v.receiver==0 && v.frame.playerId==clients[2].PlayerId);
        Require(receivedByHost>=15 && receivedByHost<=38 && codecs[0].StatePackets>=snapshots+5 && clients.All(c=>c.Connected), "Voice flooding escaped limit or blocked gameplay snapshots.");
        Pass("excess voice is rate-limited while gameplay snapshots and connections remain live");

        For(250); voice.Clear();
        for(int i=0;i<20;i++){clients[0].SendVoice(samples[0]);For(40,false);}
        int queued=0; VoiceFrame queuedFrame;
        while(clients[1].TryDequeueVoice(out queuedFrame))queued++;
        Require(queued>0 && queued<=WireProtocol.VoiceReceiveLimit, "Playback packet queue is not bounded.");
        for(int i=0;i<4;i++)while(clients[i].TryDequeueVoice(out queuedFrame)){}
        Pass("slow playback keeps a bounded recent voice window");

        string oldId=clients[2].PlayerId, token=clients[2].ResumeToken;
        clients[2].Dispose(); Require(!clients[2].TryDequeueVoice(out queuedFrame), "Disposed connection retained PCM.");
        codecs[2]=new CommsCodec(); clients[2]=new ArenaClient(codecs[2]); clients[2].Connect(host,port,"恢复",token);
        Wait(()=>clients[2].Connected,"resume commander");
        Require(clients[2].PlayerId==oldId,"Resume changed commander identity.");
        voice.Clear(); clients[2].SendVoice(samples[2]); Wait(()=>voice.Any(v=>v.receiver==0 && v.frame.playerId==oldId),"voice after resume");
        Require(voice.Where(v=>v.frame.playerId==oldId).All(v=>v.frame.sequence>1),"Server media sequence was reset by reconnect.");
        Pass("disconnect clears PCM and resumed sessions continue authoritative speaker sequence");

        using(var raw=new TcpClient())
        {
            raw.Connect(host,port); raw.ReceiveTimeout=3000;
            var codec=new CommsCodec();byte[] payload=Encoding.UTF8.GetBytes(codec.Encode(new WirePacket{type="voice",voice=new VoiceFrame{sequence=1,pcm=Convert.ToBase64String(samples[0])}}));
            byte[] header={(byte)(payload.Length>>24),(byte)(payload.Length>>16),(byte)(payload.Length>>8),(byte)payload.Length};
            raw.GetStream().Write(header);raw.GetStream().Write(payload);
            Wait(()=>raw.Available>=4,"pre-handshake voice rejection");
            byte[] responseHeader=Read(raw.GetStream(),4);int length=(responseHeader[0]<<24)|(responseHeader[1]<<16)|(responseHeader[2]<<8)|responseHeader[3];
            var response=codec.Decode(Encoding.UTF8.GetString(Read(raw.GetStream(),length)));
            Require(response.type=="error" && response.message.Contains("握手"),"Unauthenticated voice was accepted.");
        }
        Require(clients.All(c=>c.Connected),"Unauthorized peer damaged healthy sessions.");
        Pass("voice before handshake cannot enter an authenticated room");
        Console.WriteLine("PASS "+checks+" commander communication checks against "+(server==null?"external Unity server":"real in-process TCP server"));
    }
    static byte[] Read(NetworkStream stream,int count)
    {
        var bytes=new byte[count];int offset=0;
        while(offset<count){int read=stream.Read(bytes,offset,count-offset);if(read==0)throw new EndOfStreamException();offset+=read;}return bytes;
    }
    public void Dispose(){foreach(var client in clients)client?.Dispose();server?.Dispose();}
}

static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string host=null;int port=0;
            if(args.Length>0){if(args.Length!=3||args[0]!="--external"||!int.TryParse(args[2],out port))throw new ArgumentException("Usage: CommsChecks [--external host port]");host=args[1];}
            using(var check=new CommunicationCheck(host,port))check.Run();return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("FAIL "+ex);return 1;}
    }
}
