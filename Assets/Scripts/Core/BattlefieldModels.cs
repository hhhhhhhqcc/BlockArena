using System;
using System.Collections.Generic;

namespace BlockArena.Core
{
    [Serializable] public sealed class BattlefieldState
    {
        public string mapId, weather = "clear";
        public int seed;
        public float elapsed, revealAge;
        public List<TerrainState> terrain = new List<TerrainState>();
        public List<ObjectiveState> objectives = new List<ObjectiveState>();
        public List<HazardState> hazards = new List<HazardState>();
        public List<CommandZoneState> zones = new List<CommandZoneState>();
    }
    [Serializable] public sealed class TerrainState
    {
        public string id, kind;
        public float x,y,radius,halfWidth,halfHeight,height,hp,maxHp,burnTime,freeze;
    }
    [Serializable] public sealed class ObjectiveState
    {
        public string id,kind,name,ownerId,contenderId;
        public float x,y,radius=50,capture;
        public int reward,slot=-1;
        public bool claimed;
    }
    [Serializable] public sealed class HazardState
    {
        public string id,kind,ownerId;
        public float x,y,radius,delay,remaining;
        public bool triggered;
    }
    [Serializable] public sealed class CommandZoneState
    {
        public string id,kind,ownerId,targetId;
        public int slot;
        public float x,y,radius,remaining;
    }
    [Serializable] public sealed class TacticNode
    {
        public string action="move", condition="always", targetId;
        public float x,y,seconds=3,threshold=40;
    }
    [Serializable] public sealed class TacticPlanState
    {
        public string id;
        public string[] unitIds=new string[0];
        public List<TacticNode> nodes=new List<TacticNode>();
    }
    public sealed class MapDefinition
    {
        public string id,name,description,color;
        public string[] weathers;
    }
    public sealed class CommanderDefinition
    {
        public string id,name,description,icon;
        public float duration,radius;
    }
    public sealed class WeatherDefinition
    {
        public string id,name,description,color;
    }
}
