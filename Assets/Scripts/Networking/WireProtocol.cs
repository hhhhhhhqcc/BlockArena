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
    }

    public static class WireProtocol
    {
        public const int Version = 7;
        public const int DefaultPort = 7777;
        public const int MaxCommandBytes = 64 * 1024;
        public const int MaxSnapshotBytes = 4 * 1024 * 1024;
        public const int MaxConnections = 16;
        public const float DisconnectGraceSeconds = 5f;
        public const float HandshakeTimeoutSeconds = 8f;
    }
}
