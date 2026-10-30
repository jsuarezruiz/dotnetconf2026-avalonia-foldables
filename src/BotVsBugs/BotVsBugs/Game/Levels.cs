using System.Collections.Generic;

namespace BotVsBugs.Game;

/// <summary>
/// A block in a level: its sprite size in pixels (70, 140 or 220; 100 px is a meter) and its center in meters.
/// </summary>
public sealed record BlockSpec(Material Material, int Width, int Height, float X, float Y);

/// <summary>
/// A bug in a level, by its center in meters.
/// </summary>
public sealed record BugSpec(BugKind Kind, float X, float Y);

/// <summary>
/// A level: what to build, which bugs to squash and how many bots to do it with.
/// </summary>
public sealed record LevelSpec(string Name, string Hint, string Background, int Bots, BlockSpec[] Blocks, BugSpec[] Bugs)
{
    /// <summary>
    /// Gets the tile under the grass line: sand in the desert, dirt elsewhere.
    /// </summary>
    public string GroundTile => Background == "desert" ? "Ui/sand.png" : "Ui/grass.png";
}

/// <summary>
/// The campaign. The slingshot sits on the left, the bugs' structures on the right, so on a book-style
/// foldable the fold runs between them.
/// </summary>
public static class Levels
{
    public static IReadOnlyList<LevelSpec> All { get; } =
    [
        HelloWorld(),
        NullCheck(),
        OffByOne(),
        GlassHouses(),
        RaceCondition(),
        MemoryLeak(),
        StackOverflow(),
        ShipIt(),
    ];

    private static LevelSpec HelloWorld()
    {
        var b = new LevelBuilder();
        var roof = b.Hut(Material.Wood, Material.Wood, 16f, 0f);
        b.Bug(BugKind.Ladybug, 16f, 0f);
        b.Bug(BugKind.Ladybug, 16f, roof);
        return b.Build("Hello, World!", "Drag the bot back and let go. On a foldable, fold to pull back and snap open to fire.", "grass", 3);
    }

    private static LevelSpec NullCheck()
    {
        var b = new LevelBuilder();
        b.Hut(Material.Wood, Material.Wood, 14.5f, 0f);
        var roof = b.Hut(Material.Wood, Material.Wood, 18.5f, 0f);
        var top = b.Block(Material.Wood, 140, 140, 18.5f, roof);
        b.Bug(BugKind.Ladybug, 14.5f, 0f);
        b.Bug(BugKind.Ladybug, 18.5f, 0f);
        b.Bug(BugKind.Ladybug, 18.5f, top);
        return b.Build("Null Check", "Bugs hide inside. Knock the walls down on them.", "grass", 3);
    }

    private static LevelSpec OffByOne()
    {
        var b = new LevelBuilder();
        var a = b.Block(Material.Stone, 140, 140, 14f, 0f);
        a = b.Block(Material.Wood, 140, 70, 14f, a);
        var c = b.Block(Material.Stone, 140, 220, 16.5f, 0f);
        c = b.Block(Material.Wood, 140, 140, 16.5f, c);
        var d = b.Block(Material.Stone, 140, 140, 19f, 0f);
        d = b.Block(Material.Glass, 140, 140, 19f, d);
        d = b.Block(Material.Wood, 140, 70, 19f, d);
        b.Bug(BugKind.Worm, 14f, a);
        b.Bug(BugKind.Worm, 16.5f, c);
        b.Bug(BugKind.Worm, 19f, d);
        return b.Build("Off by One", "Stone barely notices a hit. Aim for the wood and glass on top.", "desert", 4);
    }

    private static LevelSpec GlassHouses()
    {
        var b = new LevelBuilder();
        b.Hut(Material.Glass, Material.Glass, 15f, 0f);
        b.Hut(Material.Glass, Material.Glass, 19f, 0f);
        b.Bug(BugKind.Ladybug, 15f, 0f);
        b.Bug(BugKind.Ladybug, 19f, 0f);
        b.Bug(BugKind.Fly, 15f, 4.2f);
        b.Bug(BugKind.Fly, 19f, 4.6f);
        return b.Build("Glass Houses", "Flies hover until something knocks them down.", "land", 3);
    }

    private static LevelSpec RaceCondition()
    {
        var b = new LevelBuilder();
        var left = b.Hut(Material.Wood, Material.Wood, 14.5f, 0f);
        b.Hut(Material.Wood, Material.Stone, 14.5f, left);
        var right = b.Hut(Material.Wood, Material.Wood, 20f, 0f);
        right = b.Hut(Material.Wood, Material.Stone, 20f, right);
        b.Bug(BugKind.Ladybug, 14.5f, 0f);
        b.Bug(BugKind.Ladybug, 20f, right);
        b.Bug(BugKind.Bee, 17.25f, 3f);
        b.Bug(BugKind.Bee, 17.25f, 6f);
        return b.Build("Race Condition", "Bees never sit still. Time your shot.", "land", 4);
    }

    private static LevelSpec MemoryLeak()
    {
        var b = new LevelBuilder();
        var roof = b.Hut(Material.Stone, Material.Stone, 15f, 0f);
        b.Hut(Material.Stone, Material.Stone, 19f, 0f);
        var bridge = b.Block(Material.Stone, 220, 70, 17f, roof);
        b.Block(Material.Wood, 70, 70, 14.25f, roof);
        b.Block(Material.Wood, 70, 70, 19.75f, roof);
        b.Bug(BugKind.Snail, 15f, 0f);
        b.Bug(BugKind.Snail, 19f, 0f);
        b.Bug(BugKind.Snail, 17f, bridge);
        return b.Build("Memory Leak", "Snails are tough. Bring the roof down on them.", "shroom", 4);
    }

    private static LevelSpec StackOverflow()
    {
        var b = new LevelBuilder();
        var first = b.Hut(Material.Wood, Material.Wood, 17f, 0f);
        var second = b.Hut(Material.Wood, Material.Wood, 17f, first);
        b.Hut(Material.Glass, Material.Glass, 17f, second);
        b.Bug(BugKind.Ladybug, 17f, 0f);
        b.Bug(BugKind.Ladybug, 17f, first);
        b.Bug(BugKind.Worm, 17f, second);
        b.Bug(BugKind.Fly, 17f, 9.7f);
        return b.Build("Stack Overflow", "Topple the tower. Everything comes down eventually.", "grass", 4);
    }

    private static LevelSpec ShipIt()
    {
        var b = new LevelBuilder();
        var left = b.Hut(Material.Stone, Material.Stone, 14.4f, 0f);
        var middle = b.Hut(Material.Stone, Material.Stone, 16.8f, 0f);
        var right = b.Hut(Material.Stone, Material.Stone, 19.2f, 0f);
        b.Hut(Material.Wood, Material.Wood, 14.4f, left);
        b.Hut(Material.Wood, Material.Wood, 19.2f, right);
        var box = b.Block(Material.Glass, 140, 140, 16.8f, middle);
        b.Bug(BugKind.Ladybug, 14.4f, 0f);
        b.Bug(BugKind.Snail, 16.8f, 0f);
        b.Bug(BugKind.Ladybug, 19.2f, 0f);
        b.Bug(BugKind.Ladybug, 14.4f, left);
        b.Bug(BugKind.Worm, 16.8f, box);
        b.Bug(BugKind.Bee, 16.8f, 6.8f);
        b.Bug(BugKind.Fly, 19.2f, 6.9f);
        return b.Build("Ship It!", "Every last bug, before Friday's release.", "desert", 5);
    }

    private sealed class LevelBuilder
    {
        private readonly List<BlockSpec> _blocks = [];
        private readonly List<BugSpec> _bugs = [];

        // Adds a block centered on x with its bottom edge at y, and returns its top edge.
        public float Block(Material material, int width, int height, float x, float y)
        {
            _blocks.Add(new BlockSpec(material, width, height, x, y + (height / 200f)));
            return y + (height / 100f);
        }

        // Two 70x220 posts under a 220x70 roof, centered on x, with room for a bug between the posts.
        // Returns the top of the roof.
        public float Hut(Material walls, Material roof, float x, float y)
        {
            Block(walls, 70, 220, x - 0.78f, y);
            Block(walls, 70, 220, x + 0.78f, y);
            return Block(roof, 220, 70, x, y + 2.2f);
        }

        // A bug standing on the floor at y, or, for a flying one, hovering there.
        public void Bug(BugKind kind, float x, float y) =>
            _bugs.Add(new BugSpec(kind, x, BugInfo.Flies(kind) ? y : y + BugInfo.Radius(kind)));

        public LevelSpec Build(string name, string hint, string background, int bots) =>
            new(name, hint, background, bots, _blocks.ToArray(), _bugs.ToArray());
    }
}
