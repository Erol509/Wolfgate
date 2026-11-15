using System.Numerics;

namespace Content.Client._WF.RadarLabels;

/// <summary>
/// Places radar tags. A tag sits by its contact while it has room; tags that would overlap share one stacked list
/// instead, which splits back into separate tags as the scope zooms in and makes room.
/// </summary>
public sealed class WFRadarTagLayout
{
    /// <summary>
    /// One tag, in pixels: its contact, where its first line sits alone, the size of its lines from there down, the
    /// height of any lines it stacks above that, and the size of its first line.
    /// </summary>
    public struct Tag
    {
        public Vector2 Anchor;
        public Vector2 Position;
        public Vector2 Size;
        public float Rise;
        public Vector2 HeadSize;
        public int Priority;
    }

    /// <summary>A tag on its own, or a stack of tags that would otherwise overlap.</summary>
    public sealed class Group
    {
        /// <summary>Every tag in the group.</summary>
        public readonly List<int> All = new();

        /// <summary>The tags shown, top to bottom; a stack past <see cref="MaxRows"/> leaves the lowest priorities out.</summary>
        public readonly List<int> Rows = new();

        /// <summary>Tags left out of a full stack.</summary>
        public int Hidden;

        public Vector2 Origin;
        public Box2 Box;
        public bool Alive;

        public bool Stacked => All.Count > 1;
    }

    /// <summary>Space kept clear between tags.</summary>
    public float Gap = 3f;

    /// <summary>The most rows a stack shows before it sums the rest up in one line.</summary>
    public int MaxRows = 10;

    /// <summary>Height of the line that sums up a full stack's hidden tags.</summary>
    public float MoreHeight = 12f;

    public readonly List<Group> Groups = new();
    private readonly List<Group> _pool = new();
    private readonly Stack<Group> _pending = new();
    private IReadOnlyList<Tag> _tags = Array.Empty<Tag>();
    private Vector2 _bounds;

    /// <summary>Groups the tags so none overlap, inside a view of <paramref name="bounds"/> pixels.</summary>
    public void Solve(IReadOnlyList<Tag> tags, Vector2 bounds)
    {
        _tags = tags;
        _bounds = bounds;
        foreach (var group in Groups)
        {
            _pool.Add(group);
        }

        Groups.Clear();
        _pending.Clear();
        for (var i = 0; i < tags.Count; i++)
        {
            var group = Rent();
            group.All.Add(i);
            group.Rows.Add(i);
            group.Origin = tags[i].Position;
            group.Box = Box2.FromDimensions(tags[i].Position - new Vector2(0f, tags[i].Rise),
                tags[i].Size + new Vector2(0f, tags[i].Rise));
            Groups.Add(group);
            _pending.Push(group);
        }

        // A group that takes another in grows, so it is checked again; every merge leaves one group fewer.
        while (_pending.TryPop(out var group))
        {
            if (!group.Alive)
                continue;

            var merged = false;
            foreach (var other in Groups)
            {
                if (other == group || !other.Alive || !Overlaps(group.Box, other.Box))
                    continue;

                group.All.AddRange(other.All);
                other.Alive = false;
                merged = true;
            }

            if (!merged)
                continue;

            Arrange(group);
            _pending.Push(group);
        }

        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Alive)
                continue;

            _pool.Add(Groups[i]);
            Groups.RemoveAt(i);
        }
    }

    private Group Rent()
    {
        Group group;
        if (_pool.Count > 0)
        {
            group = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            group = new Group();
        }

        group.All.Clear();
        group.Rows.Clear();
        group.Hidden = 0;
        group.Alive = true;
        return group;
    }

    private bool Overlaps(Box2 a, Box2 b)
    {
        return a.Left < b.Right + Gap && b.Left < a.Right + Gap && a.Bottom < b.Top + Gap && b.Bottom < a.Top + Gap;
    }

    /// <summary>
    /// Lays a stack out from the top-left of where its tags would sit alone, kept inside the view: the highest
    /// priorities up to the row cap, ordered top to bottom by their contacts so the leader lines don't cross.
    /// </summary>
    private void Arrange(Group group)
    {
        var tags = _tags;
        group.Rows.Clear();
        group.Rows.AddRange(group.All);
        if (group.Rows.Count > MaxRows)
        {
            group.Rows.Sort((a, b) => tags[b].Priority.CompareTo(tags[a].Priority));
            group.Rows.RemoveRange(MaxRows, group.Rows.Count - MaxRows);
        }

        group.Hidden = group.All.Count - group.Rows.Count;
        group.Rows.Sort((a, b) =>
        {
            var y = tags[a].Anchor.Y.CompareTo(tags[b].Anchor.Y);
            return y != 0 ? y : tags[a].Anchor.X.CompareTo(tags[b].Anchor.X);
        });

        var origin = new Vector2(float.MaxValue);
        foreach (var index in group.All)
        {
            origin = Vector2.Min(origin, tags[index].Position);
        }

        var size = Vector2.Zero;
        foreach (var index in group.Rows)
        {
            size.X = MathF.Max(size.X, tags[index].HeadSize.X);
            size.Y += tags[index].HeadSize.Y;
        }

        if (group.Hidden > 0)
            size.Y += MoreHeight;

        origin.X = MathF.Max(0f, MathF.Min(origin.X, _bounds.X - size.X));
        origin.Y = MathF.Max(0f, MathF.Min(origin.Y, _bounds.Y - size.Y));
        group.Origin = origin;
        group.Box = Box2.FromDimensions(origin, size);
    }
}
