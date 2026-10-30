using System;
using System.Collections.Generic;
using nkast.Aether.Physics2D.Common;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;

namespace BotVsBugs.Game;

/// <summary>
/// Where a level stands.
/// </summary>
public enum GamePhase
{
    /// <summary>
    /// A bot waits on the sling.
    /// </summary>
    Ready,

    /// <summary>
    /// A bot is in the air, or rolling to a stop.
    /// </summary>
    Flying,

    Won,

    Lost,
}

/// <summary>
/// One level in play: the physics, the bugs left, the bots left and the score. Units are meters and
/// seconds, with y up and the ground at y = 0.
/// </summary>
public sealed class GameWorld
{
    /// <summary>
    /// The width of the world the camera always shows.
    /// </summary>
    public const float ViewWidth = 24f;

    /// <summary>
    /// The top of the world the camera always shows.
    /// </summary>
    public const float ViewTop = 15.5f;

    /// <summary>
    /// How much ground the camera shows below y = 0.
    /// </summary>
    public const float GroundDepth = 1f;

    public const float BotRadius = 0.45f;

    /// <summary>
    /// The most spare bots shown waiting on the ground behind the sling.
    /// </summary>
    public const int QueueLength = 4;

    /// <summary>
    /// How far back the band stretches at full pull.
    /// </summary>
    public const float MaxPull = 2f;

    /// <summary>
    /// The launch speed at full pull.
    /// </summary>
    public const float MaxSpeed = 19f;

    public const float Gravity = -10f;

    public const int BugPoints = 5000;

    public const int SpareBotPoints = 10000;

    /// <summary>
    /// Where the band meets, between the sling's forks.
    /// </summary>
    public static readonly Vector2 SlingAnchor = new(3.4f, 2.6f);

    private const float StepSeconds = 1f / 60f;

    // A freshly built level settles for this long before anything can break.
    private const double SettleTime = 1d;

    // A shot is over once the bot has been close to still this long, or after the timeout.
    private const double StillTime = 1d;
    private const float StillSpeed = 0.3f;
    private const double ShotTimeout = 9d;

    // Time for the last pieces to fall before the level is decided.
    private const double EndDelay = 1.5d;

    // A hovering bug drops once a hit this strong knocks it.
    private const float KnockImpulse = 0.25f;

    private readonly World _physics = new(new Vector2(0f, Gravity));
    private readonly List<Block> _blocks = [];
    private readonly List<Bug> _bugs = [];
    private readonly List<Particle> _particles = [];
    private readonly List<Popup> _popups = [];
    private readonly Random _random = new(7);
    private double _accumulator;
    private double _endAt = -1d;
    private bool _changed;

    /// <summary>
    /// Builds <paramref name="level"/>, ready for the first shot.
    /// </summary>
    public GameWorld(LevelSpec level, int index)
    {
        Level = level;
        Index = index;
        BotsLeft = level.Bots;

        _physics.ContactManager.PostSolve += OnPostSolve;

        // Wide enough that whatever rolls off the view still lands on something.
        var ground = _physics.CreateBody(Vector2.Zero, 0f, BodyType.Static);
        ground.CreateRectangle(400f, 4f, 1f, new Vector2(0f, -2f)).Friction = 0.9f;

        foreach (var spec in level.Blocks)
        {
            var body = _physics.CreateBody(new Vector2(spec.X, spec.Y), 0f, BodyType.Dynamic);
            var fixture = body.CreateRectangle(
                spec.Width / 100f,
                spec.Height / 100f,
                MaterialInfo.Density(spec.Material),
                Vector2.Zero);
            fixture.Friction = MaterialInfo.Friction(spec.Material);
            fixture.Restitution = 0.05f;

            var block = new Block(body, spec.Material, spec.Width, spec.Height);
            body.Tag = block;
            _blocks.Add(block);
        }

        var phase = 0d;
        foreach (var spec in level.Bugs)
        {
            var home = new Vector2(spec.X, spec.Y);
            var body = _physics.CreateBody(home, 0f, BodyType.Dynamic);
            var fixture = body.CreateCircle(BugInfo.Radius(spec.Kind), 0.8f);
            fixture.Friction = 0.8f;
            fixture.Restitution = 0.1f;

            // Bugs slide and tumble off ledges, but stay the right way up.
            body.FixedRotation = true;

            var bug = new Bug(body, spec.Kind, home) { Phase = phase += 1.7d };
            body.IgnoreGravity = bug.IsHovering;
            body.Tag = bug;
            _bugs.Add(bug);
        }

        BugsLeft = _bugs.Count;
    }

    public LevelSpec Level { get; }

    public int Index { get; }

    public GamePhase Phase { get; private set; } = GamePhase.Ready;

    public int Score { get; private set; }

    /// <summary>
    /// Gets the bots not launched yet, the one on the sling included.
    /// </summary>
    public int BotsLeft { get; private set; }

    /// <summary>
    /// Gets the stars earned once the level is won: three with two bots to spare.
    /// </summary>
    public int Stars { get; private set; }

    public int BugsLeft { get; private set; }

    /// <summary>
    /// Gets the bot in the air, if any.
    /// </summary>
    public Bot? Shot { get; private set; }

    /// <summary>
    /// Gets the last bot launched, left where it landed until the next one goes.
    /// </summary>
    public Bot? Spent { get; private set; }

    public IReadOnlyList<Block> Blocks => _blocks;

    public IReadOnlyList<Bug> Bugs => _bugs;

    public IReadOnlyList<Particle> Particles => _particles;

    public IReadOnlyList<Popup> Popups => _popups;

    /// <summary>
    /// Gets the simulated time since the level started.
    /// </summary>
    public double Time { get; private set; }

    public bool CanLaunch => Phase == GamePhase.Ready && BotsLeft > 0 && _bugs.Count > 0;

    /// <summary>
    /// Occurs after an update that changed the score, the bots left or the phase.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the direction for an aim angle in radians, counterclockwise from pointing right.
    /// </summary>
    public static Vector2 Direction(double angle) => new((float)Math.Cos(angle), (float)Math.Sin(angle));

    /// <summary>
    /// Gets where the spare bot <paramref name="index"/> waits behind the sling, nearest first.
    /// </summary>
    public static Vector2 QueuedBotPosition(int index) => new(2.2f - (index * 0.95f), BotRadius * 0.85f);

    /// <summary>
    /// Gets where the bot sits with the band pulled back <paramref name="pull"/> (0 to 1) against the aim.
    /// </summary>
    public static Vector2 PulledPosition(double angle, double pull) =>
        SlingAnchor - (Direction(angle) * (float)(pull * MaxPull));

    /// <summary>
    /// Gets the velocity a shot leaves the sling with.
    /// </summary>
    public static Vector2 LaunchVelocity(double angle, double pull) =>
        Direction(angle) * (float)(pull * MaxSpeed);

    /// <summary>
    /// Fills <paramref name="points"/> with where a shot passes, every <paramref name="interval"/> seconds.
    /// Exact as long as it hits nothing: the bot has no drag.
    /// </summary>
    public static void Trajectory(double angle, double pull, List<Vector2> points, int count, float interval)
    {
        points.Clear();
        var start = PulledPosition(angle, pull);
        var velocity = LaunchVelocity(angle, pull);
        for (var i = 1; i <= count; i++)
        {
            var t = i * interval;
            var point = start + (velocity * t) + new Vector2(0f, 0.5f * Gravity * t * t);
            if (point.Y < 0f)
                break;
            points.Add(point);
        }
    }

    /// <summary>
    /// Launches the bot on the sling, aimed at <paramref name="angle"/> with the band pulled <paramref name="pull"/> (0 to 1).
    /// </summary>
    public void Launch(double angle, double pull)
    {
        if (!CanLaunch)
            return;

        if (Spent is { } spent)
        {
            Puff(spent.Position, 6, 0.5f);
            _physics.Remove(spent.Body);
            Spent = null;
        }

        var body = _physics.CreateBody(PulledPosition(angle, pull), 0f, BodyType.Dynamic);
        var fixture = body.CreateCircle(BotRadius, 4f);
        fixture.Friction = 0.6f;
        fixture.Restitution = 0.3f;
        body.IsBullet = true;
        body.AngularDamping = 0.6f;
        body.LinearVelocity = LaunchVelocity(angle, pull);

        Shot = new Bot(body, Time);
        body.Tag = Shot;
        BotsLeft--;
        Phase = GamePhase.Flying;
        _changed = true;
    }

    /// <summary>
    /// Advances the level by <paramref name="seconds"/> of real time, in fixed physics steps.
    /// </summary>
    public void Update(double seconds)
    {
        if (Phase is GamePhase.Won or GamePhase.Lost)
        {
            // The last debris still settles and fades behind the result.
            UpdateEffects((float)seconds);
            return;
        }

        // A long hitch, like returning from the background, isn't caught up all at once.
        _accumulator += Math.Min(seconds, 0.1d);
        while (_accumulator >= StepSeconds)
        {
            _accumulator -= StepSeconds;
            Step();
        }

        if (_changed)
        {
            _changed = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Step()
    {
        _physics.Step(StepSeconds);
        Time += StepSeconds;

        ApplyImpacts();
        Hover();
        RemoveStrays();
        UpdateShot();
        UpdateEffects(StepSeconds);
        UpdatePhase();
    }

    // Records each body's strongest hit of the step; the damage is applied after the step, when bodies
    // can be removed.
    private void OnPostSolve(Contact contact, ContactVelocityConstraint impulse)
    {
        if (Time < SettleTime)
            return;

        var strongest = 0f;
        for (var i = 0; i < impulse.pointCount; i++)
            strongest = Math.Max(strongest, impulse.points[i].normalImpulse);

        if (contact.FixtureA.Body.Tag is Entity a)
            a.Impact = Math.Max(a.Impact, strongest);
        if (contact.FixtureB.Body.Tag is Entity b)
            b.Impact = Math.Max(b.Impact, strongest);
    }

    private void ApplyImpacts()
    {
        for (var i = _blocks.Count - 1; i >= 0; i--)
        {
            var block = _blocks[i];
            if (TakeHit(block) && block.Health <= 0f)
                Break(block);
        }

        for (var i = _bugs.Count - 1; i >= 0; i--)
        {
            var bug = _bugs[i];
            var impact = bug.Impact;
            if (bug.IsHovering && impact >= KnockImpulse)
            {
                bug.IsHovering = false;
                bug.Body.IgnoreGravity = false;
            }

            if (TakeHit(bug) && bug.Health <= 0f)
                Squash(bug);
        }
    }

    // Applies the step's hit to the entity, and returns whether it did damage.
    private static bool TakeHit(Entity entity)
    {
        var damage = entity.Impact - entity.Threshold;
        entity.Impact = 0f;
        if (damage <= 0f)
            return false;

        entity.Health -= damage;
        return true;
    }

    private void Hover()
    {
        foreach (var bug in _bugs)
        {
            if (!bug.IsHovering)
                continue;

            var t = Time + bug.Phase;
            var target = bug.Home + new Vector2(
                (float)Math.Sin(t * 0.9d) * BugInfo.Patrol(bug.Kind),
                (float)Math.Sin(t * 2.1d) * 0.25f);
            bug.Body.Awake = true;
            bug.Body.LinearVelocity = (target - bug.Position) * 3f;
        }
    }

    // Anything that leaves the world is gone: blocks break, bugs count as squashed.
    private void RemoveStrays()
    {
        for (var i = _blocks.Count - 1; i >= 0; i--)
        {
            if (IsOutside(_blocks[i].Position))
                Break(_blocks[i]);
        }

        for (var i = _bugs.Count - 1; i >= 0; i--)
        {
            if (IsOutside(_bugs[i].Position))
                Squash(_bugs[i]);
        }
    }

    private static bool IsOutside(Vector2 position) =>
        position.Y < -4f || position.X < -8f || position.X > ViewWidth + 16f;

    private void UpdateShot()
    {
        if (Shot is not { } shot)
            return;

        shot.StillFor = shot.Body.LinearVelocity.Length() < StillSpeed ? shot.StillFor + StepSeconds : 0d;
        var isOver = shot.StillFor >= StillTime
            || Time - shot.LaunchedAt >= ShotTimeout
            || IsOutside(shot.Position);
        if (!isOver)
            return;

        Shot = null;
        if (IsOutside(shot.Position))
            _physics.Remove(shot.Body);
        else
            Spent = shot;

        if (BugsLeft > 0 && BotsLeft > 0)
        {
            Phase = GamePhase.Ready;
            _changed = true;
        }
    }

    // Decides the level once every bug is squashed, or the last bot has landed, and the dust settles.
    private void UpdatePhase()
    {
        BugsLeft = _bugs.Count;
        var isDecided = BugsLeft == 0 || (BotsLeft == 0 && Shot is null);
        if (!isDecided)
        {
            _endAt = -1d;
            return;
        }

        if (_endAt < 0d)
        {
            _endAt = Time + EndDelay;
            return;
        }

        if (Time < _endAt)
            return;

        if (BugsLeft == 0)
        {
            Score += SpareBotPoints * BotsLeft;
            Stars = BotsLeft >= 2 ? 3 : BotsLeft == 1 ? 2 : 1;
            for (var i = 0; i < Math.Min(BotsLeft, QueueLength); i++)
                _popups.Add(new Popup($"+{SpareBotPoints}", new Vector2(QueuedBotPosition(i).X, 1.6f), 0.7f, false));
            Phase = GamePhase.Won;
        }
        else
        {
            Phase = GamePhase.Lost;
        }

        _changed = true;
    }

    private void Break(Block block)
    {
        _blocks.Remove(block);
        _physics.Remove(block.Body);

        var points = MaterialInfo.Points(block.Material);
        Score += points;
        _changed = true;
        _popups.Add(new Popup($"+{points}", block.Position, 0.45f, false));

        var debris = $"Debris/debris{char.ToUpperInvariant(MaterialInfo.Key(block.Material)[0])}{MaterialInfo.Key(block.Material)[1..]}_";
        var pieces = 3 + (int)(block.Width * block.Height * 4f);
        for (var i = 0; i < Math.Min(pieces, 10); i++)
        {
            var offset = new Vector2(
                (float)(_random.NextDouble() - 0.5d) * block.Width,
                (float)(_random.NextDouble() - 0.5d) * block.Height);
            _particles.Add(new Particle
            {
                Position = block.Position + offset,
                Velocity = (offset * 3f) + new Vector2(0f, 2f + (float)_random.NextDouble() * 2f),
                Angle = (float)(_random.NextDouble() * Math.PI * 2d),
                Spin = (float)(_random.NextDouble() - 0.5d) * 10f,
                Size = 0.35f + ((float)_random.NextDouble() * 0.25f),
                Life = 1.2f,
                Sprite = $"{debris}{1 + _random.Next(3)}.png",
            });
        }
    }

    private void Squash(Bug bug)
    {
        _bugs.Remove(bug);
        _physics.Remove(bug.Body);

        Score += BugPoints;
        _changed = true;
        Puff(bug.Position, 10, 0.6f);

        var above = bug.Position + new Vector2(0f, bug.Radius + 0.4f);
        _popups.Add(new Popup($"{BugInfo.Label(bug.Kind)} fixed!", above + new Vector2(0f, 0.6f), 0.55f, true));
        _popups.Add(new Popup($"+{BugPoints}", above, 0.7f, true));
    }

    private void Puff(Vector2 position, int count, float size)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2d;
            _particles.Add(new Particle
            {
                Position = position,
                Velocity = Direction(angle) * (1f + (float)_random.NextDouble() * 2f),
                Size = size * (0.6f + (float)_random.NextDouble() * 0.6f),
                Life = 0.6f + (float)_random.NextDouble() * 0.3f,
            });
        }
    }

    private void UpdateEffects(float seconds)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Age += seconds;
            if (particle.Age >= particle.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            // Debris falls; smoke drifts and slows.
            if (particle.Sprite is not null)
                particle.Velocity += new Vector2(0f, Gravity * seconds);
            else
                particle.Velocity *= 1f - (3f * seconds);

            particle.Position += particle.Velocity * seconds;
            particle.Angle += particle.Spin * seconds;
        }

        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            var popup = _popups[i];
            popup.Age += seconds;
            popup.Position += new Vector2(0f, 0.7f * seconds);
            if (popup.Age >= popup.Life)
                _popups.RemoveAt(i);
        }
    }
}
