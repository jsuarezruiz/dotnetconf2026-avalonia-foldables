using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BotVsBugs.Game;

/// <summary>
/// The game's bitmaps, loaded from the assets the first time they are drawn and kept.
/// </summary>
public static class Sprites
{
    private static readonly Dictionary<string, Bitmap> Cache = new();

    /// <summary>
    /// Gets the bitmap at <paramref name="path"/>, relative to the Assets folder.
    /// </summary>
    public static Bitmap Get(string path)
    {
        if (!Cache.TryGetValue(path, out var bitmap))
        {
            bitmap = new Bitmap(AssetLoader.Open(new Uri($"avares://BotVsBugs/Assets/{path}")));
            Cache[path] = bitmap;
        }

        return bitmap;
    }
}
