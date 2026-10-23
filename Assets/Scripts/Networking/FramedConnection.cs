using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BlockArena.Networking
{
    internal static class NetworkClock
    {
        public static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
    }

    // Transport workers see UTF-8 strings only. Codec and gameplay never execute
    // on these threads, so Unity's JsonUtility remains on the caller's thread.
    internal sealed class FramedConnection : IDisposable
    {
        private sealed class TextFrame
        {
            public string Text;
            public int Bytes;
            public double CreatedAt;
            public TextFrame(string text, int bytes) { Text = text; Bytes = bytes; CreatedAt = NetworkClock.Now; }
        }

        private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly int maxInbound, maxOutbound, inboundBudget, outboundBudget;
        private readonly double frameRate, byteRate;
        private readonly ConcurrentQueue<TextFrame> inbound = new ConcurrentQueue<TextFrame>();
        private readonly Queue<TextFrame> outbound = new Queue<TextFrame>();
        private readonly Queue<TextFrame> media = new Queue<TextFrame>();
        private readonly object sendGate = new object();
        private readonly AutoResetEvent sendWake = new AutoResetEvent(false);
        private readonly Thread reader, writer;
        private TextFrame latestSnapshot;
        private int inboundBytes, inboundCount, outboundBytes, closed;
        private string error;
        private bool drainAndClose;

        public bool IsOpen { get { return Volatile.Read(ref closed) == 0; } }
        public string Error { get { return Volatile.Read(ref error); } }

        public FramedConnection(TcpClient socket, bool serverSide)
        {
            client = socket ?? throw new ArgumentNullException(nameof(socket));
            client.NoDelay = true;
            client.SendTimeout = 5000;
            client.ReceiveTimeout = 0;
            stream = client.GetStream();
            maxInbound = serverSide ? WireProtocol.MaxCommandBytes : WireProtocol.MaxSnapshotBytes;
            maxOutbound = serverSide ? WireProtocol.MaxSnapshotBytes : WireProtocol.MaxCommandBytes;
            inboundBudget = serverSide ? 512 * 1024 : 16 * 1024 * 1024;
            outboundBudget = serverSide ? 8 * 1024 * 1024 : 256 * 1024;
            frameRate = serverSide ? 60 : 120;
            byteRate = serverSide ? 256 * 1024 : 64 * 1024 * 1024;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "Arena TCP receive" };
            writer = new Thread(WriteLoop) { IsBackground = true, Name = "Arena TCP send" };
            reader.Start();
            writer.Start();
        }

        public bool TryReceive(out string text)
        {
            TextFrame frame;
            if (!inbound.TryDequeue(out frame)) { text = null; return false; }
            Interlocked.Add(ref inboundBytes, -frame.Bytes);
            Interlocked.Decrement(ref inboundCount);
            text = frame.Text;
            return true;
        }

        public bool TrySend(string text, bool replaceSnapshot = false, bool discardableMedia = false)
        {
            if (!IsOpen || text == null) return false;
            int bytes;
            try { bytes = Encoding.GetByteCount(text); }
            catch (EncoderFallbackException) { Close("消息包含无效字符。"); return false; }
            if (bytes <= 0 || bytes > maxOutbound) { Close("消息超过传输大小限制。"); return false; }
            bool overflow = false;
            lock (sendGate)
            {
                if (!IsOpen || drainAndClose) return false;
                if (discardableMedia)
                {
                    // Audio may lose frames, gameplay may not. Keep less than a quarter-second
                    // of media, and always dispatch snapshots/actions before these packets.
                    while (media.Count >= 6)
                    {
                        TextFrame dropped = media.Dequeue(); outboundBytes -= dropped.Bytes;
                    }
                    if (bytes > 4096 || outboundBytes + bytes > outboundBudget) return false;
                    media.Enqueue(new TextFrame(text, bytes)); outboundBytes += bytes;
                    sendWake.Set(); return true;
                }
                int replacedBytes = replaceSnapshot && latestSnapshot != null ? latestSnapshot.Bytes : 0;
                if ((!replaceSnapshot && outbound.Count >= 64) || outboundBytes - replacedBytes + bytes > outboundBudget)
                    overflow = true;
                else
                {
                    var frame = new TextFrame(text, bytes);
                    if (replaceSnapshot) { latestSnapshot = frame; outboundBytes -= replacedBytes; }
                    else outbound.Enqueue(frame);
                    outboundBytes += bytes;
                }
            }
            if (overflow) { Close("连接发送过慢，待发送消息已超限。"); return false; }
            sendWake.Set();
            return true;
        }

        public void CloseAfterFlush()
        {
            lock (sendGate) drainAndClose = true;
            sendWake.Set();
        }

        public void Close(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason)) Interlocked.CompareExchange(ref error, reason, null);
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            try { client.Close(); } catch { }
            try { sendWake.Set(); } catch (ObjectDisposedException) { }
        }

        private void ReadLoop()
        {
            var header = new byte[4];
            double last = NetworkClock.Now, frameTokens = frameRate * 2, byteTokens = byteRate * 2;
            try
            {
                while (IsOpen)
                {
                    if (!ReadExact(header, 4)) { Close("对端已关闭连接。"); return; }
                    uint size = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                    if (size == 0 || size > maxInbound) throw new InvalidDataException("收到超出限制的消息长度。");
                    double now = NetworkClock.Now, elapsed = Math.Max(0, now - last);
                    last = now;
                    frameTokens = Math.Min(frameRate * 2, frameTokens + elapsed * frameRate);
                    byteTokens = Math.Min(byteRate * 2, byteTokens + elapsed * byteRate);
                    if (frameTokens < 1 || byteTokens < size) throw new InvalidDataException("消息发送频率超过限制。");
                    frameTokens -= 1;
                    byteTokens -= size;
                    var data = new byte[(int)size];
                    if (!ReadExact(data, data.Length)) { Close("消息传输中断。"); return; }
                    string text = Encoding.GetString(data);
                    int count = Interlocked.Increment(ref inboundCount);
                    int bytes = Interlocked.Add(ref inboundBytes, data.Length);
                    if (count > 128 || bytes > inboundBudget)
                    {
                        Interlocked.Decrement(ref inboundCount);
                        Interlocked.Add(ref inboundBytes, -data.Length);
                        throw new InvalidDataException("接收队列已满。");
                    }
                    inbound.Enqueue(new TextFrame(text, data.Length));
                }
            }
            catch (Exception ex)
            {
                if (IsOpen) Close(ex is InvalidDataException || ex is DecoderFallbackException ? ex.Message : "网络接收已中断。");
            }
        }

        private bool ReadExact(byte[] data, int count)
        {
            int offset = 0;
            while (offset < count && IsOpen)
            {
                int read = stream.Read(data, offset, count - offset);
                if (read == 0) return false;
                offset += read;
            }
            return offset == count;
        }

        private void WriteLoop()
        {
            try
            {
                while (IsOpen)
                {
                    TextFrame frame = null;
                    bool finish;
                    lock (sendGate)
                    {
                        if (outbound.Count > 0) frame = outbound.Dequeue();
                        else if (latestSnapshot != null) { frame = latestSnapshot; latestSnapshot = null; }
                        else
                        {
                            while (media.Count > 0)
                            {
                                TextFrame candidate = media.Dequeue();
                                if (NetworkClock.Now - candidate.CreatedAt <= .24) { frame = candidate; break; }
                                outboundBytes -= candidate.Bytes;
                            }
                        }
                        if (frame != null) outboundBytes -= frame.Bytes;
                        finish = frame == null && drainAndClose;
                    }
                    if (finish) { Close(); return; }
                    if (frame == null) { sendWake.WaitOne(250); continue; }
                    byte[] data = Encoding.GetBytes(frame.Text);
                    int length = data.Length;
                    byte[] header = { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
                    stream.Write(header, 0, header.Length);
                    stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception) { if (IsOpen) Close("网络发送已中断。"); }
        }

        public void Dispose()
        {
            Close();
            if (Thread.CurrentThread != reader && reader.IsAlive) reader.Join(50);
            if (Thread.CurrentThread != writer && writer.IsAlive) writer.Join(50);
            if (!reader.IsAlive && !writer.IsAlive) sendWake.Dispose();
        }
    }
}
