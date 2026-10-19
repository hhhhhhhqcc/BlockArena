using System;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    private static void RitualChecks()
    {
        Check("ordinary crystal attacks retain their short cadence", () =>
        {
            CommanderRitualTimeline.Shot first = CommanderRitualTimeline.Next(false, null);
            CommanderRitualTimeline.Shot second = CommanderRitualTimeline.Next(false, first);
            CommanderRitualTimeline.Shot third = CommanderRitualTimeline.Next(false, second);
            Near(1.2f, first.chargeAt); Near(1.95f, first.hitAt);
            Near(.32f, second.chargeAt - first.chargeAt); Near(.32f, third.hitAt - second.hitAt);
            Near(-1, first.cameraInAt); Near(4.5f, CommanderRitualTimeline.EndAt(third));
        });
        Check("next normal attack waits for the execution soul and camera return", () =>
        {
            CommanderRitualTimeline.Shot execution = CommanderRitualTimeline.Next(true, null);
            CommanderRitualTimeline.Shot normal = CommanderRitualTimeline.Next(false, execution);
            Assert(execution.cameraOutAt >= execution.hitAt + 1.5f - .001f);
            Assert(execution.cameraWideAt >= execution.cameraOutAt + .65f - .001f);
            Assert(normal.chargeAt >= execution.cameraWideAt + .25f - .001f);
            Assert(normal.launchAt > execution.cameraWideAt);
            Assert(CommanderRitualTimeline.EndAt(normal) > execution.cameraWideAt);
        });
        Check("execution camera waits until the previous normal attack has landed", () =>
        {
            CommanderRitualTimeline.Shot normal = CommanderRitualTimeline.Next(false, null);
            CommanderRitualTimeline.Shot execution = CommanderRitualTimeline.Next(true, normal);
            Assert(execution.cameraInAt >= normal.hitAt + .25f - .001f);
            Assert(execution.chargeAt >= execution.cameraInAt + .65f - .001f);
            Assert(execution.launchAt > execution.chargeAt);
        });
        Check("four executions each finish and return wide before the next zoom begins", () =>
        {
            CommanderRitualTimeline.Shot? previous = null;
            for (int i = 0; i < 4; i++)
            {
                CommanderRitualTimeline.Shot current = CommanderRitualTimeline.Next(true, previous);
                if (previous.HasValue)
                {
                    Assert(current.cameraInAt >= previous.Value.cameraWideAt + .25f - .001f);
                    Assert(current.launchAt > previous.Value.hitAt + 1.5f);
                }
                previous = current;
            }
            float end = CommanderRitualTimeline.EndAt(previous.Value);
            Assert(end >= previous.Value.cameraWideAt + .25f - .001f);
            Near(end, CommanderRitualTimeline.Duration(new[] { true, true, true, true }));
            Assert(end > ArenaGame.ResultsSeconds);
        });
        Check("mixed execution results wait long enough before recruiting the next round", () =>
        {
            ArenaGame game = new ArenaGame("a", "甲", 305);
            foreach (string id in new[] { "b", "c", "d" }) game.AddPlayer(id, id);
            game.Apply("a", new GameCommand { type = "start", bots = false }); ChooseAll(game);
            game.State.players[1].hp = 1; Battle(game);
            foreach (FighterState fighter in game.State.fighters)
            { fighter.hp = (4 - fighter.slot) * 10; fighter.attackCooldown = 100; }
            game.State.timeLeft = .001f; game.Tick(.05f);
            Equal("results", game.State.phase); Assert(game.State.players[1].eliminated);
            Assert(game.State.players.Skip(2).All(p => !p.eliminated));
            float expectedDuration = CommanderRitualTimeline.Duration(new[] { true, false, false });
            Near(expectedDuration, CommanderRitualTimeline.DurationFor(game.State));
            Near(expectedDuration + CommanderRitualTimeline.NetworkMarginSeconds, game.State.timeLeft);
            float wait = game.State.timeLeft;
            Advance(game, ArenaGame.ResultsSeconds + .05f);
            Equal("results", game.State.phase); Equal(1, game.State.round);
            Advance(game, wait - ArenaGame.ResultsSeconds + .05f);
            Equal("recruit", game.State.phase); Equal(2, game.State.round);
        });
        Check("final results preserve the complete queued execution window", () =>
        {
            ArenaGame game = new ArenaGame("a", "甲", 307);
            foreach (string id in new[] { "b", "c", "d" }) game.AddPlayer(id, id);
            game.Apply("a", new GameCommand { type = "start", bots = false }); ChooseAll(game);
            foreach (PlayerState player in game.State.players.Skip(1)) player.hp = 1;
            Battle(game);
            foreach (FighterState fighter in game.State.fighters)
            { fighter.hp = (4 - fighter.slot) * 10; fighter.attackCooldown = 100; }
            game.State.timeLeft = .001f; game.Tick(.05f);
            Equal("finished", game.State.phase); Equal("a", game.State.winnerId);
            float expectedDuration = CommanderRitualTimeline.Duration(new[] { true, true, true });
            Near(expectedDuration + CommanderRitualTimeline.NetworkMarginSeconds, game.State.timeLeft);
            Assert(game.State.timeLeft > ArenaGame.ResultsSeconds);
            Advance(game, game.State.timeLeft + 1);
            Equal("finished", game.State.phase); Equal(1, game.State.round);
        });
    }
}
