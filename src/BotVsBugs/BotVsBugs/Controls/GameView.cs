using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using BotVsBugs.Animation;
using BotVsBugs.Game;
using Nova.Avalonia.Foldable;
using WorldPoint = nkast.Aether.Physics2D.Common.Vector2;

namespace BotVsBugs.Controls;

/// <summary>
/// Draws a level and plays it: drag the bot back on the sling and let go, or, on a foldable, fold the
/// hinge to pull back and snap it open to fire.
/// </summary>
public sealed class GameView : Control
{
    public static readonly StyledProperty<GameWorld?> WorldProperty =
        AvaloniaProperty.Register<GameView, GameWorld?>(nameof(World));

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<GameView, bool>(nameof(IsPaused));

    /// <summary>
    /// The aim a level starts with: about 35° up.
    /// </summary>
    public const double DefaultAim = 0.6d;

    public const double MinAim = -0.6d;

    public const double MaxAim = 1.45d;

    // Below this a drag only aims; letting go doesn't fire.
    private const double MinDragPull = 0.12d;

    // How close to the bot, or the sling, a press must land to grab it.
    private const float GrabBot = 1.6f;
    private const float GrabSling = 2.2f;

    // The background art is 1024 px square, drawn this many meters wide, with the foot of its hills at the ground.
    private const float BackgroundSize = 20f;
    private const float BackgroundBottom = -5.94f;
    private const float TileSize = 0.7f;

    // The cropped dotnet-bot head is 121x142 px: a 121 px circle under its antenna.
    private const double BotArtWidth = 121d;
    private const double BotArtHeight = 142d;
    private const double BotArtCenterY = 81.5d;

    private static readonly IBrush SkyBrush = new SolidColorBrush(Color.Parse("#D8F5F8"));
    private static readonly IBrush PuffBrush = Brushes.White;
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#F2FFFFFF"));
    private static readonly IBrush PopupBrush = Brushes.White;
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFE45C"));
    private static readonly ImmutableSolidColorBrush OutlineBrush = new(Color.Parse("#2B1D4A"));
    private static readonly Color TrunkColor = Color.Parse("#7A4A26");
    private static readonly Color BackForkColor = Color.Parse("#5E391D");
    private static readonly Color FrontForkColor = Color.Parse("#8E5A2C");
    private static readonly Color BandColor = Color.Parse("#3B2314");
    private static readonly Typeface GameTypeface = new(new FontFamily("avares://BotVsBugs/Assets/Fonts#Lilita One"));

    // The sling: a trunk from the ground, forks spreading up to the tips the band is tied to.
    private static readonly WorldPoint SlingBase = new(3.4f, 0f);
    private static readonly WorldPoint SlingFork = new(3.4f, 1.7f);
    private static readonly WorldPoint BackTip = new(3f, 2.8f);
    private static readonly WorldPoint FrontTip = new(3.8f, 2.8f);

    private readonly FrameTicker _ticker;
    private readonly HingeTrigger _hinge = new();
    private readonly List<WorldPoint> _trajectory = [];
    private readonly Dictionary<(string Text, int Size), (Geometry Geometry, Size Size, IPen Outline)> _texts = new();

    // The sling's pens, made once per color and thickness at the current scale.
    private readonly Dictionary<(Color Color, float Thickness), IPen> _sticks = new();
    private TimeSpan _lastFrame;
    private GamePhase? _lastPhase;
    private double _scale = 1d;
    private Point _origin;
    // The pointer dragging the bot: only it moves the sling and lets go. A second finger, like a thumb
    // holding the phone, is ignored.
    private IPointer? _dragPointer;
    private double _dragPull;
    private double _aimAngle = DefaultAim;

    public GameView()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _ticker = new FrameTicker(this, OnFrame);
        _hinge.Fired += OnHingeFired;
    }

    public GameWorld? World
    {
        get => GetValue(WorldProperty);
        set => SetValue(WorldProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the level is frozen, as behind the pause menu.
    /// </summary>
    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    /// <summary>
    /// Gets the hinge slingshot, for the gauges that show it.
    /// </summary>
    public HingeTrigger Hinge => _hinge;

    /// <summary>
    /// Gets or sets the aim, in radians counterclockwise from pointing right. Dragging the bot sets it; a
    /// hinge shot uses it.
    /// </summary>
    public double AimAngle
    {
        get => _aimAngle;
        set
        {
            var aim = Math.Clamp(value, MinAim, MaxAim);
            if (aim == _aimAngle)
                return;

            _aimAngle = aim;
            AimChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets how far the band is pulled back right now, by a finger or by the hinge, from 0 to 1.
    /// </summary>
    public double Pull => IsDragging ? _dragPull : _hinge.IsArmed ? _hinge.Pull : 0d;

    /// <summary>
    /// Occurs when the hinge moves or arms, so the gauges can follow it.
    /// </summary>
    public event EventHandler? HingeChanged;

    public event EventHandler? AimChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTicker();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ticker.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Inherited from the top level, where Nova.Avalonia.Foldable publishes the fold.
        if (change.Property == Fold.StateProperty)
        {
            _hinge.Update(Fold.GetState(this));
            HingeChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (change.Property == WorldProperty)
        {
            _dragPointer = null;
            _lastPhase = null;
            _aimAngle = DefaultAim;
            AimChanged?.Invoke(this, EventArgs.Empty);
            SyncHinge();
            UpdateTicker();
            InvalidateVisual();
        }
        else if (change.Property == IsPausedProperty)
        {
            _dragPointer = null;
            SyncHinge();
            UpdateTicker();
            InvalidateVisual();
        }
    }

    // Frames only while the game runs: paused, it asks for none, so the device can rest. The pause itself
    // redraws once.
    private void UpdateTicker()
    {
        if (IsPaused || TopLevel.GetTopLevel(this) is null)
        {
            _ticker.Stop();
            return;
        }

        if (!_ticker.IsRunning)
        {
            // The ticker counts from zero again.
            _lastFrame = TimeSpan.Zero;
            _ticker.Start();
        }
    }

    private void OnFrame(TimeSpan elapsed)
    {
        // Never a step back in time, should the ticker's clock ever start over.
        var delta = elapsed >= _lastFrame ? elapsed - _lastFrame : TimeSpan.Zero;
        _lastFrame = elapsed;

        if (World is not { } world || IsPaused)
            return;

        world.Update(delta.TotalSeconds);

        if (world.Phase != _lastPhase)
            SyncHinge();

        InvalidateVisual();

        // The level is over and the dust has settled: nothing moves behind the result, so ask for no more frames.
        if (world.Phase is GamePhase.Won or GamePhase.Lost && world.Particles.Count == 0 && world.Popups.Count == 0)
            _ticker.Stop();
    }

    // The hinge only listens while a bot waits on the sling and the game runs.
    private void SyncHinge()
    {
        _lastPhase = World?.Phase;
        if (World is { CanLaunch: true } && !IsPaused)
            _hinge.Ready();
        else
            _hinge.Stop();

        HingeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHingeFired(double power)
    {
        if (World is { CanLaunch: true } world && !IsDragging && !IsPaused)
        {
            world.Launch(AimAngle, power);
            return;
        }

        // The shot was dropped, a finger is on the bot: the trigger stopped itself when it fired, and the
        // phase won't change to start it again, so listen for the next fold.
        SyncHinge();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (World is not { CanLaunch: true } || IsPaused || IsDragging)
            return;

        var point = ToWorld(e.GetPosition(this));
        var bot = GameWorld.PulledPosition(AimAngle, Pull);
        if ((point - bot).Length() > GrabBot && (point - GameWorld.SlingAnchor).Length() > GrabSling)
            return;

        _dragPointer = e.Pointer;
        _dragPull = Pull;
        e.Pointer.Capture(this);
        e.Handled = true;
        Drag(point);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (e.Pointer == _dragPointer)
            Drag(ToWorld(e.GetPosition(this)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.Pointer != _dragPointer)
            return;

        _dragPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_dragPull >= MinDragPull && World is { CanLaunch: true } world)
            world.Launch(AimAngle, _dragPull);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer == _dragPointer)
            _dragPointer = null;
    }

    private bool IsDragging => _dragPointer is not null;

    // The shot flies away from the finger, through the sling.
    private void Drag(WorldPoint point)
    {
        var offset = GameWorld.SlingAnchor - point;
        var length = offset.Length();
        if (length > 0.05f)
            AimAngle = Math.Atan2(offset.Y, offset.X);

        _dragPull = Math.Min(length, GameWorld.MaxPull) / GameWorld.MaxPull;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        UpdateCamera(size);

        context.FillRectangle(SkyBrush, new Rect(size));

        if (World is not { } world)
            return;

        DrawBackground(context, world, size);
        DrawGround(context, world, size);

        var isAiming = world.CanLaunch;
        var pull = isAiming && !IsPaused ? Pull : 0d;
        var waiting = GameWorld.PulledPosition(AimAngle, pull);

        DrawStick(context, SlingBase, SlingFork, 0.32f, TrunkColor);
        DrawStick(context, SlingFork, BackTip, 0.24f, BackForkColor);
        if (isAiming)
            DrawStick(context, BackTip, waiting, 0.14f, BandColor);

        foreach (var block in world.Blocks)
            DrawSprite(context, Sprites.Get(block.Sprite), block.Position, block.Width, block.Height, block.Rotation);

        foreach (var bug in world.Bugs)
            DrawBug(context, bug, world.Time);

        // The bots still to come wait their turn on the ground behind the sling.
        var queued = Math.Min(world.BotsLeft - (isAiming ? 1 : 0), GameWorld.QueueLength);
        for (var i = 0; i < queued; i++)
            DrawBot(context, GameWorld.QueuedBotPosition(i), 0f, 0.85f);

        if (world.Spent is { } spent)
            DrawBot(context, spent.Position, spent.Rotation, 1f);
        if (world.Shot is { } shot)
            DrawBot(context, shot.Position, shot.Rotation, 1f);
        if (isAiming)
            DrawBot(context, waiting, 0f, 1f);

        if (isAiming)
            DrawStick(context, FrontTip, waiting, 0.14f, BandColor);
        DrawStick(context, SlingFork, FrontTip, 0.24f, FrontForkColor);

        if (isAiming && pull > 0.02d)
            DrawTrajectory(context, pull);

        DrawParticles(context, world);
        DrawPopups(context, world);
    }

    // Fits the whole level, sling to structures, ground to sky; extra room becomes more sky and more ground.
    private void UpdateCamera(Size size)
    {
        var scale = Math.Max(0.01d, Math.Min(
            size.Width / GameWorld.ViewWidth,
            size.Height / (GameWorld.ViewTop + GameWorld.GroundDepth)));
        if (scale != _scale)
        {
            _scale = scale;
            _sticks.Clear();
        }

        _origin = new Point(
            (size.Width - (GameWorld.ViewWidth * _scale)) / 2d,
            size.Height - (GameWorld.GroundDepth * _scale));
    }

    private Point ToScreen(WorldPoint point) => new(_origin.X + (point.X * _scale), _origin.Y - (point.Y * _scale));

    private WorldPoint ToWorld(Point point) =>
        new((float)((point.X - _origin.X) / _scale), (float)((_origin.Y - point.Y) / _scale));

    private void DrawBackground(DrawingContext context, GameWorld world, Size size)
    {
        var art = Sprites.Get($"Backgrounds/colored_{world.Level.Background}.png");
        var tile = BackgroundSize * _scale;
        var top = ToScreen(new WorldPoint(0f, BackgroundBottom + BackgroundSize)).Y;

        // Tiles to the left of the view's origin too, out to the control's edges.
        var x = _origin.X - (Math.Ceiling(_origin.X / tile) * tile);
        for (; x < size.Width; x += tile)
            context.DrawImage(art, new Rect(x, top, tile + 1d, tile));
    }

    private void DrawGround(DrawingContext context, GameWorld world, Size size)
    {
        var surface = Sprites.Get(world.Level.GroundTile);
        var fill = Sprites.Get("Ui/dirt.png");
        var tile = TileSize * _scale;

        var x = _origin.X - (Math.Ceiling(_origin.X / tile) * tile);
        for (; x < size.Width; x += tile)
        {
            // A pixel of overlap keeps seams from showing between tiles.
            context.DrawImage(surface, new Rect(x, _origin.Y, tile + 1d, tile + 1d));
            for (var y = _origin.Y + tile; y < size.Height; y += tile)
                context.DrawImage(fill, new Rect(x, y, tile + 1d, tile + 1d));
        }
    }

    private void DrawStick(DrawingContext context, WorldPoint from, WorldPoint to, float thickness, Color color)
    {
        if (!_sticks.TryGetValue((color, thickness), out var pen))
        {
            pen = new ImmutablePen(new ImmutableSolidColorBrush(color), thickness * _scale, lineCap: PenLineCap.Round);
            _sticks[(color, thickness)] = pen;
        }

        context.DrawLine(pen, ToScreen(from), ToScreen(to));
    }

    // Draws a bitmap stretched over a width x height box in meters, centered on the point and turned with it.
    private void DrawSprite(DrawingContext context, Bitmap bitmap, WorldPoint center, float width, float height, float rotation)
    {
        var screen = ToScreen(center);
        var w = width * _scale;
        var h = height * _scale;
        using (context.PushTransform(Matrix.CreateRotation(-rotation) * Matrix.CreateTranslation(screen.X, screen.Y)))
            context.DrawImage(bitmap, new Rect(-w / 2d, -h / 2d, w, h));
    }

    private void DrawBot(DrawingContext context, WorldPoint center, float rotation, float size)
    {
        var screen = ToScreen(center);
        var k = 2d * GameWorld.BotRadius * size * _scale / BotArtWidth;
        using (context.PushTransform(Matrix.CreateRotation(-rotation) * Matrix.CreateTranslation(screen.X, screen.Y)))
        {
            context.DrawImage(
                Sprites.Get("Bot/bot_head.png"),
                new Rect(-BotArtWidth / 2d * k, -BotArtCenterY * k, BotArtWidth * k, BotArtHeight * k));
        }
    }

    private void DrawBug(DrawingContext context, Bug bug, double time)
    {
        var frames = BugInfo.Frames(bug.Kind);
        var rate = bug.IsHovering ? 10d : 2.5d;
        var frame = Sprites.Get(frames[(int)((time + bug.Phase) * rate) % frames.Length]);
        var size = BugInfo.FrameSize(bug.Kind);

        // Fliers are drawn centered on their body; the others stand on its bottom edge.
        var center = BugInfo.Flies(bug.Kind)
            ? bug.Position
            : bug.Position + new WorldPoint(0f, (size / 2f) - bug.Radius);
        DrawSprite(context, frame, center, size, size, 0f);
    }

    private void DrawTrajectory(DrawingContext context, double pull)
    {
        GameWorld.Trajectory(AimAngle, pull, _trajectory, 18, 0.07f);
        for (var i = 0; i < _trajectory.Count; i++)
        {
            var radius = Math.Max(1.5d, (0.11d - (i * 0.004d)) * _scale);
            context.DrawEllipse(DotBrush, null, ToScreen(_trajectory[i]), radius, radius);
        }
    }

    private void DrawParticles(DrawingContext context, GameWorld world)
    {
        foreach (var particle in world.Particles)
        {
            var fade = 1d - (particle.Age / particle.Life);
            using (context.PushOpacity(Math.Clamp(fade, 0d, 1d)))
            {
                if (particle.Sprite is { } sprite)
                {
                    DrawSprite(context, Sprites.Get(sprite), particle.Position, particle.Size, particle.Size * 0.875f, particle.Angle);
                }
                else
                {
                    // Smoke grows as it fades.
                    var radius = particle.Size * (0.6d + (particle.Age / particle.Life)) * _scale / 2d;
                    context.DrawEllipse(PuffBrush, null, ToScreen(particle.Position), radius, radius);
                }
            }
        }
    }

    private void DrawPopups(DrawingContext context, GameWorld world)
    {
        // Text and its outline are built once per text and size. Resizing keeps adding sizes, so the cache
        // starts over once it holds more than 64.
        if (_texts.Count > 64)
            _texts.Clear();

        foreach (var popup in world.Popups)
        {
            var pixels = Math.Max(8, (int)Math.Round(popup.Size * _scale));
            if (!_texts.TryGetValue((popup.Text, pixels), out var text))
            {
                var formatted = new FormattedText(
                    popup.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    GameTypeface,
                    pixels,
                    PopupBrush);
                var outline = new ImmutablePen(OutlineBrush, Math.Max(2d, pixels / 6d), lineJoin: PenLineJoin.Round);
                text = (formatted.BuildGeometry(default) ?? new RectangleGeometry(), new Size(formatted.Width, formatted.Height), outline);
                _texts[(popup.Text, pixels)] = text;
            }

            var fade = Math.Clamp((popup.Life - popup.Age) / 0.4d, 0d, 1d);
            var at = ToScreen(popup.Position);
            using (context.PushOpacity(fade))
            using (context.PushTransform(Matrix.CreateTranslation(at.X - (text.Size.Width / 2d), at.Y - (text.Size.Height / 2d))))
            {
                context.DrawGeometry(OutlineBrush, text.Outline, text.Geometry);
                context.DrawGeometry(popup.IsHighlight ? HighlightBrush : PopupBrush, null, text.Geometry);
            }
        }
    }
}
