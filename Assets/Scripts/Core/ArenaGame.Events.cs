namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        // Central guardian/resource encounters were retired in v0.9.1.
        // ArenaEventState remains a wire DTO for older saved probe fixtures;
        // live matches generate resources only through their fixed map objectives.
    }
}
