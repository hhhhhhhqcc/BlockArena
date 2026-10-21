using BlockArena.Networking;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed class UnityWireCodec : IWireCodec
    {
        public string Encode(WirePacket packet) { return JsonUtility.ToJson(packet); }
        public WirePacket Decode(string json)
        {
            var packet=JsonUtility.FromJson<WirePacket>(json);
            // JsonUtility materializes inline serializable classes even when the wire value is null.
            // An arena only exists after reveal; preserve that absence for preparation and lobby UI.
            if(packet!=null&&packet.state!=null&&packet.state.battlefield!=null&&string.IsNullOrEmpty(packet.state.battlefield.mapId))
                packet.state.battlefield=null;
            if(packet!=null&&packet.state!=null&&packet.state.roundEvent!=null&&string.IsNullOrEmpty(packet.state.roundEvent.id))
                packet.state.roundEvent=null;
            return packet;
        }
    }
}
