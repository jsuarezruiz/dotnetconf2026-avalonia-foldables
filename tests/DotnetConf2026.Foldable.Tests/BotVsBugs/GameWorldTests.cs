using BotVsBugs.Game;
using Xunit;

namespace DotnetConf2026.Foldable.Tests.BotVsBugs;

public class GameWorldTests
{
    // Frames of three and six physics steps (20 and 10 fps), so the step that decides the level isn't the
    // last of its frame and more steps run after it.
    [Theory]
    [InlineData(0.05)]
    [InlineData(0.1)]
    public void Spare_bots_score_once_however_many_steps_a_frame_runs(double frame)
    {
        // No bugs: the level is won as soon as the dust settles, with all three bots spare.
        var world = new GameWorld(new LevelSpec("Empty", "", "grass", 3, [], []), 0);

        for (var i = 0; i < 1000 && world.Phase != GamePhase.Won; i++)
            world.Update(frame);

        Assert.Equal(GamePhase.Won, world.Phase);
        Assert.Equal(3 * GameWorld.SpareBotPoints, world.Score);
        Assert.Equal(3, world.Stars);
    }
}
