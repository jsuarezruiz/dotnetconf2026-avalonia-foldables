using System;

namespace BotVsBugs.Game;

/// <summary>
/// What a block is made of.
/// </summary>
public enum Material
{
    Wood,
    Stone,
    Glass,
}

/// <summary>
/// The bugs to squash, each one a classic .NET bug.
/// </summary>
public enum BugKind
{
    Ladybug,
    Fly,
    Bee,
    Snail,
    Worm,
}

/// <summary>
/// How heavy, grippy and tough each material is, and what breaking it is worth.
/// </summary>
public static class MaterialInfo
{
    public static float Density(Material material) => material switch
    {
        Material.Stone => 2.4f,
        Material.Glass => 0.8f,
        _ => 1f,
    };

    public static float Friction(Material material) => material switch
    {
        Material.Stone => 0.8f,
        Material.Glass => 0.3f,
        _ => 0.7f,
    };

    // Damage a 70x70 block takes before it breaks; bigger blocks last longer.
    public static float Toughness(Material material, float width, float height) =>
        (material switch
        {
            Material.Stone => 14f,
            Material.Glass => 2.2f,
            _ => 6f,
        }) * MathF.Sqrt(width * height / 0.49f);

    public static int Points(Material material) => material switch
    {
        Material.Stone => 800,
        Material.Glass => 300,
        _ => 500,
    };

    public static string Key(Material material) => material switch
    {
        Material.Stone => "stone",
        Material.Glass => "glass",
        _ => "wood",
    };
}

/// <summary>
/// Each bug's size, health, sprites and the bug it stands for.
/// </summary>
public static class BugInfo
{
    public static string Label(BugKind kind) => kind switch
    {
        BugKind.Fly => "IndexOutOfRangeException",
        BugKind.Bee => "Race condition",
        BugKind.Snail => "Memory leak",
        BugKind.Worm => "Off-by-one error",
        _ => "NullReferenceException",
    };

    public static float Radius(BugKind kind) => kind switch
    {
        BugKind.Fly => 0.32f,
        BugKind.Bee => 0.36f,
        BugKind.Snail => 0.42f,
        BugKind.Worm => 0.3f,
        _ => 0.4f,
    };

    public static float Health(BugKind kind) => kind switch
    {
        BugKind.Fly => 1.2f,
        BugKind.Bee => 1.5f,
        BugKind.Snail => 6f,
        BugKind.Worm => 2f,
        _ => 2.5f,
    };

    /// <summary>
    /// Gets whether the bug hovers until something knocks it out of the air.
    /// </summary>
    public static bool Flies(BugKind kind) => kind is BugKind.Fly or BugKind.Bee;

    /// <summary>
    /// Gets how far a hovering bug patrols from home, side to side.
    /// </summary>
    public static float Patrol(BugKind kind) => kind == BugKind.Bee ? 1.2f : 0.35f;

    /// <summary>
    /// Gets the animation frames, all 128x128 with the bug facing left, toward the bot.
    /// </summary>
    public static string[] Frames(BugKind kind) => kind switch
    {
        BugKind.Fly => ["Bugs/fly_a.png", "Bugs/fly_b.png"],
        BugKind.Bee => ["Bugs/bee_a.png", "Bugs/bee_b.png"],
        BugKind.Snail => ["Bugs/snail_rest.png", "Bugs/snail_walk_a.png"],
        BugKind.Worm => ["Bugs/worm_normal_rest.png", "Bugs/worm_normal_move_a.png"],
        _ => ["Bugs/ladybug_walk_a.png", "Bugs/ladybug_walk_b.png"],
    };

    /// <summary>
    /// Gets the size, in meters, the 128x128 frame is drawn at.
    /// </summary>
    public static float FrameSize(BugKind kind) => kind switch
    {
        BugKind.Fly or BugKind.Bee => 0.9f,
        BugKind.Snail => 1.05f,
        BugKind.Worm => 0.95f,
        _ => 1f,
    };
}
