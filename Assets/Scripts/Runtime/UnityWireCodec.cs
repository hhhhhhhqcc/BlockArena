using BlockArena.Networking;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed class UnityWireCodec : IWireCodec
    {
        public string Encode(WirePacket packet) { return JsonUtility.ToJson(packet); }
        public WirePacket Decode(string json) { return JsonUtility.FromJson<WirePacket>(json); }
    }
}
