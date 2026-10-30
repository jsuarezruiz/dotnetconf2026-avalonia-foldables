using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics;

namespace BotVsBugs.Game;

/// <summary>
/// Something in the world with a physics body that hits can wear down.
/// </summary>
public abstract class Entity(Body body, float health, float threshold)
{
    public Body Body { get; } = body;

    public float Health { get; set; } = health;

    public float MaxHealth { get; } = health;

    /// <summary>
    /// Gets the impulse a hit needs before it does damage; anything less is the weight of a stack resting on it.
    /// </summary>
    public float Threshold { get; } = threshold;

    /// <summary>
    /// Gets or sets the strongest impulse taken during the current physics step.
    /// </summary>
    public float Impact { get; set; }

    public Vector2 Position => Body.Position;

    public float Rotation => Body.Rotation;
}

/// <summary>
/// A wood, stone or glass block, which cracks as it takes damage and breaks when it runs out.
/// </summary>
public sealed class Block(Body body, Material material, int widthPixels, int heightPixels)
    : Entity(body, MaterialInfo.Toughness(material, widthPixels / 100f, heightPixels / 100f), 1.2f)
{
    public Material Material { get; } = material;

    public float Width { get; } = widthPixels / 100f;

    public float Height { get; } = heightPixels / 100f;

    /// <summary>
    /// Gets the sprite for the block's state: intact, chipped, then cracked.
    /// </summary>
    public string Sprite
    {
        get
        {
            var stage = Health > MaxHealth * 0.66f ? 0 : Health > MaxHealth * 0.33f ? 1 : 2;
            return $"Blocks/{MaterialInfo.Key(Material)}_{(int)(Width * 100)}x{(int)(Height * 100)}_{stage}.png";
        }
    }
}

/// <summary>
/// A bug. Flying ones hover around home until a hit knocks them down.
/// </summary>
public sealed class Bug(Body body, BugKind kind, Vector2 home)
    : Entity(body, BugInfo.Health(kind), 0.5f)
{
    public BugKind Kind { get; } = kind;

    public Vector2 Home { get; } = home;

    public float Radius { get; } = BugInfo.Radius(kind);

    public bool IsHovering { get; set; } = BugInfo.Flies(kind);

    /// <summary>
    /// Gets a per-bug offset, so neighbors don't flap and bob in step.
    /// </summary>
    public double Phase { get; init; }
}

/// <summary>
/// A launched dotnet bot.
/// </summary>
public sealed class Bot(Body body, double launchedAt) : Entity(body, float.MaxValue, float.MaxValue)
{
    public double LaunchedAt { get; } = launchedAt;

    /// <summary>
    /// Gets or sets how long the bot has been close to still.
    /// </summary>
    public double StillFor { get; set; }
}

/// <summary>
/// A bit of debris or a puff of smoke, drawn but not simulated.
/// </summary>
public sealed class Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Angle;
    public float Spin;
    public float Size;
    public float Age;
    public float Life;

    /// <summary>
    /// The debris sprite, or null for a puff of smoke.
    /// </summary>
    public string? Sprite;
}

/// <summary>
/// Text that floats up from where something happened, like the points for a squashed bug.
/// </summary>
public sealed class Popup(string text, Vector2 position, float size, bool isHighlight)
{
    public string Text { get; } = text;

    public Vector2 Position { get; set; } = position;

    public float Size { get; } = size;

    public bool IsHighlight { get; } = isHighlight;

    public float Age { get; set; }

    public float Life { get; } = isHighlight ? 2.2f : 1.2f;
}
