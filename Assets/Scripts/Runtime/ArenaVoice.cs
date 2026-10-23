using System;
using System.Collections.Generic;
using BlockArena.Core;
using BlockArena.Networking;
using UnityEngine;

namespace BlockArena.Runtime
{
    // LAN voice uses the existing authenticated TCP session. Microphone capture never
    // starts until the player holds V or explicitly enables the open microphone.
    public sealed class ArenaVoice : MonoBehaviour
    {
        sealed class Speaker
        {
            readonly object gate = new object();
            readonly float[] buffer = new float[WireProtocol.VoiceSampleRate / 2];
            int read, count;
            int playedNonzero;
            double readyAt;
            public readonly GameObject Root;
            public readonly AudioSource Source;
            public readonly AudioClip Clip;
            public int Sequence = -1;
            public float LastReceived = -10;

            public Speaker(Transform parent, string id)
            {
                Root = new GameObject("Commander voice " + id);
                Root.transform.SetParent(parent, false);
                Source = Root.AddComponent<AudioSource>();
                Source.playOnAwake = false; Source.loop = true; Source.spatialBlend = 0;
                Source.priority = 40; Source.ignoreListenerPause = false;
                Clip = AudioClip.Create("Commander stream", WireProtocol.VoiceSampleRate, 1, WireProtocol.VoiceSampleRate, true, ReadAudio);
                Source.clip = Clip; Source.Play();
            }

            public void Append(byte[] pcm)
            {
                lock (gate)
                {
                    if (count == 0) readyAt = NetworkClock.Now + .055;
                    // Cap latency even if a sound device temporarily stops requesting samples.
                    int incoming = pcm.Length / 2;
                    if (count + incoming > WireProtocol.VoiceSamplesPerFrame * 6)
                    {
                        int discard = count + incoming - WireProtocol.VoiceSamplesPerFrame * 3;
                        discard = Math.Min(count, Math.Max(0, discard));
                        read = (read + discard) % buffer.Length; count -= discard;
                    }
                    for (int i = 0; i < incoming; i++)
                    {
                        short sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                        buffer[(read + count) % buffer.Length] = sample / 32768f;
                        count++;
                    }
                }
            }

            void ReadAudio(float[] data)
            {
                lock (gate)
                {
                    if (NetworkClock.Now < readyAt) { Array.Clear(data, 0, data.Length); return; }
                    int take = Math.Min(data.Length, count);
                    for (int i = 0; i < take; i++) { data[i] = buffer[read]; if (Math.Abs(data[i]) > .0001f) playedNonzero++; read = (read + 1) % buffer.Length; }
                    for (int i = take; i < data.Length; i++) data[i] = 0;
                    count -= take;
                }
            }

            public void Clear() { lock (gate) { read = count = 0; readyAt = 0; } }
            public int PlayedNonzeroSamples { get { lock (gate) { return playedNonzero; } } }
        }

        readonly Dictionary<string, Speaker> speakers = new Dictionary<string, Speaker>(StringComparer.Ordinal);
        Func<ArenaClient> connection;
        Func<GameState> state;
        Func<bool> inputSuppressed;
        Func<bool> verification;
        ArenaClient previousClient;
        AudioClip microphone;
        string microphoneDevice;
        int captureRead;
        float[] capture;
        readonly byte[] encoded = new byte[WireProtocol.VoiceBytesPerFrame];
        bool openMic, muted, wasConnected;
        float volume = .9f, lastSpeech = -10, lastSent = -10, nextDeviceAttempt, captureStarted;
        string status = "按住 V 说话";
        public string Status { get { return status=="按住 V 说话" ? "按住 "+(ArenaApp.Instance!=null&&ArenaApp.Instance.Keys!=null?ArenaApp.Instance.Keys.Label("talk"):"V")+" 说话" : status; } private set { status=value; } }
        public bool OpenMic { get { return openMic; } }
        public bool Muted { get { return muted; } }
        public bool Capturing { get { return microphone != null; } }
        public bool Talking { get { return Time.unscaledTime - lastSent < .22f; } }
        public float Volume { get { return volume; } }
        public int ReceivedFrames { get; private set; }
        public int SentFrames { get; private set; }
        internal int StreamingSpeakerCount { get { return speakers.Count; } }
        internal int PlayedNonzeroSamples
        {
            get { int count = 0; foreach (Speaker speaker in speakers.Values) count += speaker.PlayedNonzeroSamples; return count; }
        }

        public void Initialize(Func<ArenaClient> client, Func<GameState> currentState, Func<bool> blocked, Func<bool> verifying)
        {
            connection = client; state = currentState; inputSuppressed = blocked; verification = verifying;
            muted = PlayerPrefs.GetInt("arena.voiceMuted", 0) != 0;
            volume = Mathf.Clamp01(PlayerPrefs.GetFloat("arena.voiceVolume", .9f));
        }

        public void SetOpenMic(bool enabled)
        {
            openMic = enabled;
            if (!enabled) StopCapture();
            else { nextDeviceAttempt = 0; Status = "麦克风已开启"; }
        }

        public void SetMuted(bool enabled)
        {
            muted = enabled;
            foreach (Speaker speaker in speakers.Values) { speaker.Source.volume = muted ? 0 : volume; if (muted) speaker.Clear(); }
            if (verification == null || !verification()) { PlayerPrefs.SetInt("arena.voiceMuted", muted ? 1 : 0); PlayerPrefs.Save(); }
        }

        public void SetVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            volume = Mathf.Clamp01(value);
            foreach (Speaker speaker in speakers.Values) speaker.Source.volume = muted ? 0 : volume;
            if (verification == null || !verification()) { PlayerPrefs.SetFloat("arena.voiceVolume", volume); PlayerPrefs.Save(); }
        }

        public bool IsSpeaking(string playerId)
        {
            ArenaClient client = connection == null ? null : connection();
            if (client != null && client.PlayerId == playerId) return Talking;
            Speaker speaker;
            return playerId != null && speakers.TryGetValue(playerId, out speaker) && Time.unscaledTime - speaker.LastReceived < .22f;
        }

        void Update()
        {
            ArenaClient client = connection == null ? null : connection();
            bool connected = client != null && client.Connected;
            if (!ReferenceEquals(previousClient, client) || wasConnected && !connected)
            {
                ResetSession(); previousClient = client;
            }
            wasConnected = connected;
            if (!connected) { StopCapture(); return; }
            VoiceFrame frame;
            int limit = 0;
            while (limit++ < WireProtocol.VoiceReceiveLimit && client.TryDequeueVoice(out frame)) Receive(client, frame);
            GameState current = state == null ? null : state();
            if (current != null)
            {
                var removed = new List<string>();
                foreach (var pair in speakers)
                    if (!current.players.Exists(p => p.id == pair.Key && p.connected)) removed.Add(pair.Key);
                foreach (string id in removed) RemoveSpeaker(id);
            }
            bool suppressed = verification != null && verification() || inputSuppressed != null && inputSuppressed();
            bool talk = Application.isFocused && !suppressed && (openMic || ArenaApp.Instance != null && ArenaApp.Instance.Keys != null && ArenaApp.Instance.Keys.Held("talk"));
            if (!talk) { StopCapture(); return; }
            if (microphone == null && Time.unscaledTime >= nextDeviceAttempt) StartCapture();
            if (microphone != null) Capture(client);
        }

        void Receive(ArenaClient client, VoiceFrame frame)
        {
            GameState current = state == null ? null : state();
            if (frame == null || frame.playerId == client.PlayerId || current == null ||
                !current.players.Exists(p => p.id == frame.playerId && p.connected)) return;
            byte[] pcm;
            try { pcm = Convert.FromBase64String(frame.pcm); } catch (Exception) { return; }
            if (pcm.Length != WireProtocol.VoiceBytesPerFrame) return;
            Speaker speaker;
            if (!speakers.TryGetValue(frame.playerId, out speaker))
            {
                if (speakers.Count >= 3) return;
                speaker = new Speaker(transform, frame.playerId); speakers.Add(frame.playerId, speaker);
                speaker.Source.volume = muted ? 0 : volume;
            }
            if (frame.sequence <= speaker.Sequence) return;
            speaker.Sequence = frame.sequence; speaker.LastReceived = Time.unscaledTime; ReceivedFrames++;
            if (!muted) speaker.Append(pcm);
        }

        void StartCapture()
        {
            nextDeviceAttempt = Time.unscaledTime + 3;
            try
            {
                string[] devices = Microphone.devices;
                if (devices == null || devices.Length == 0) { Status = "未检测到麦克风"; return; }
                microphoneDevice = devices[0];
                microphone = Microphone.Start(microphoneDevice, true, 1, WireProtocol.VoiceSampleRate);
                if (microphone == null || microphone.channels < 1 || microphone.channels > 8) { StopCapture(); Status = "麦克风不可用"; return; }
                capture = new float[WireProtocol.VoiceSamplesPerFrame * microphone.channels];
                captureRead = 0; lastSpeech = -10; captureStarted = Time.unscaledTime;
                Status = openMic ? "麦克风已开启" : "按住 V 说话";
            }
            catch (Exception) { StopCapture(); Status = "无法使用麦克风，请检查系统权限"; }
        }

        void Capture(ArenaClient client)
        {
            int position;
            try { position = Microphone.GetPosition(microphoneDevice); } catch (Exception) { StopCapture(); return; }
            if (position < 0 || microphone == null) { StopCapture(); return; }
            if (position == 0 && Time.unscaledTime - captureStarted > 2)
            {
                StopCapture(); openMic = false; Status = "麦克风没有输入，请检查系统权限"; return;
            }
            int available = (position - captureRead + microphone.samples) % microphone.samples;
            if (available > WireProtocol.VoiceSamplesPerFrame * 6)
            {
                captureRead = (position - WireProtocol.VoiceSamplesPerFrame * 2 + microphone.samples) % microphone.samples;
                available = WireProtocol.VoiceSamplesPerFrame * 2;
            }
            int budget = 0;
            while (available >= WireProtocol.VoiceSamplesPerFrame && budget++ < 3)
            {
                if (!microphone.GetData(capture, captureRead)) { StopCapture(); return; }
                float square = 0;
                for (int i = 0; i < WireProtocol.VoiceSamplesPerFrame; i++)
                {
                    float sample = 0;
                    for (int channel = 0; channel < microphone.channels; channel++) sample += capture[i * microphone.channels + channel];
                    sample = Mathf.Clamp(sample / microphone.channels, -1, 1); square += sample * sample;
                    short pcm = (short)Mathf.RoundToInt(sample * 32767);
                    encoded[i * 2] = (byte)pcm; encoded[i * 2 + 1] = (byte)(pcm >> 8);
                }
                if (Mathf.Sqrt(square / WireProtocol.VoiceSamplesPerFrame) > .008f) lastSpeech = Time.unscaledTime;
                if (Time.unscaledTime - lastSpeech < .22f && client.SendVoice(encoded)) { lastSent = Time.unscaledTime; SentFrames++; }
                captureRead = (captureRead + WireProtocol.VoiceSamplesPerFrame) % microphone.samples;
                available -= WireProtocol.VoiceSamplesPerFrame;
            }
        }

        void StopCapture()
        {
            if (microphone == null) return;
            try { Microphone.End(microphoneDevice); } catch (Exception) { }
            Destroy(microphone); microphone = null; capture = null; captureRead = 0; lastSent = -10;
            if (status == "麦克风已开启" || status == "按住 V 说话") Status = openMic ? "麦克风已开启" : "按住 V 说话";
        }

        void RemoveSpeaker(string id)
        {
            Speaker speaker;
            if (!speakers.TryGetValue(id, out speaker)) return;
            speaker.Clear(); speaker.Source.Stop(); Destroy(speaker.Root); Destroy(speaker.Clip); speakers.Remove(id);
        }

        public void ResetSession()
        {
            openMic = false; StopCapture();
            var ids = new List<string>(speakers.Keys);
            foreach (string id in ids) RemoveSpeaker(id);
            Status = "按住 V 说话";
        }

        void OnApplicationFocus(bool focused) { if (!focused) StopCapture(); }
        void OnDisable() { ResetSession(); }
        void OnDestroy() { ResetSession(); }
    }
}
