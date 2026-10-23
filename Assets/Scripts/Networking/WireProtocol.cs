using System;
using BlockArena.Core;

namespace BlockArena.Networking
{
    public interface IWireCodec
    {
        string Encode(WirePacket packet);
        WirePacket Decode(string json);
    }

    [Serializable]
    public sealed class WirePacket
    {
        public string type;
        public int protocol = WireProtocol.Version;
        public string name, playerId, token, message, placementId;
        public GameCommand command;
        public GameState state;
        public ChatMessage chat;
        public VoiceFrame voice;
    }

    [Serializable]
    public sealed class ChatMessage
    {
        public int id, slot;
        public string playerId, name, text;
    }

    [Serializable]
    public sealed class VoiceFrame
    {
        public string playerId, pcm;
        public int sequence;
    }

    public static class WireProtocol
    {
        public const int Version = 11;
        public const int ChatMaxCharacters = 160;
        public const int ChatHistoryLimit = 40;
        public const int VoiceSampleRate = 16000;
        public const int VoiceSamplesPerFrame = 640;
        public const int VoiceBytesPerFrame = VoiceSamplesPerFrame * 2;
        public const int VoiceBase64Length = (VoiceBytesPerFrame + 2) / 3 * 4;
        public const int VoiceReceiveLimit = 12;
        public const int DefaultPort = 7777;
        public const int MaxCommandBytes = 64 * 1024;
        public const int MaxSnapshotBytes = 4 * 1024 * 1024;
        public const int MaxConnections = 16;
        public const float DisconnectGraceSeconds = 5f;
        public const float HandshakeTimeoutSeconds = 8f;
    }
}
