using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BotVsBugs.Game;

/// <summary>
/// The best stars earned on each level, kept in a small text file in the app's data folder.
/// </summary>
public static class Progress
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BotVsBugs",
        "progress.txt");

    private static int[]? _stars;

    private static int[] Stars => _stars ??= Load();

    public static int StarsFor(int level) => level >= 0 && level < Stars.Length ? Stars[level] : 0;

    /// <summary>
    /// Gets whether the level can be played: the first one always, the others once the one before is won.
    /// </summary>
    public static bool IsUnlocked(int level) => level == 0 || StarsFor(level - 1) > 0;

    /// <summary>
    /// Gets the first level not won yet, or the first level once every one is.
    /// </summary>
    public static int NextLevel
    {
        get
        {
            var next = Array.IndexOf(Stars, 0);
            return next < 0 ? 0 : next;
        }
    }

    public static int TotalStars => Stars.Sum();

    /// <summary>
    /// Records a win, keeping the best stars for the level.
    /// </summary>
    public static void Record(int level, int stars)
    {
        if (level < 0 || level >= Stars.Length || stars <= Stars[level])
            return;

        Stars[level] = stars;
        Save();
    }

    private static int[] Load()
    {
        var stars = new int[Levels.All.Count];
        try
        {
            if (!File.Exists(FilePath))
                return stars;

            // One "level stars" pair per line.
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var parts = line.Split(' ');
                if (parts.Length == 2
                    && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                    && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                    && level >= 0 && level < stars.Length)
                {
                    stars[level] = Math.Clamp(count, 0, 3);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return stars;
    }

    // Progress is nice to keep, not worth failing over: a write that fails is dropped.
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(
                FilePath,
                Stars.Select((count, level) => string.Create(CultureInfo.InvariantCulture, $"{level} {count}")));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
