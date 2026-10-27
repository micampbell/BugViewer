namespace BugViewer;

// Source collections are immutable after publication. Entries are bounded by estimated
// resident cost (including derived picking/upload data), rather than by frame count.
internal sealed class ScenePreparationCache(long capacity)
{
    private readonly Dictionary<object, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> lru = [];
    private long bytes;
    internal T Get<T>(object key, Func<T> create, Func<T, long> size)
    {
        if (entries.TryGetValue(key, out var node))
        {
            lru.Remove(node); lru.AddLast(node);
            return (T)node.Value.Value;
        }
        var value = create();
        var cost = Math.Max(1, size(value));
        if (cost <= capacity)
        {
            while (bytes + cost > capacity && lru.First is { } oldest)
            {
                bytes -= oldest.Value.Bytes; entries.Remove(oldest.Value.Key); lru.RemoveFirst();
            }
            entries.Add(key, lru.AddLast(new Entry(key, value!, cost))); bytes += cost;
        }
        return value;
    }
    internal void Clear() { entries.Clear(); lru.Clear(); bytes = 0; }
    internal static long MeshBytes(MeshData mesh) => 256L + mesh.Vertices.Count * 24L + mesh.Indices.Count * 256L;
    internal static long LineBytes(LineData? line) => line is null ? 1 : 256L + line.Vertices.Count * 112L;
    private sealed record Entry(object Key, object Value, long Bytes);
}
