namespace BugViewer;

internal sealed class SceneReadProgressStream(Stream inner, Func<long, Task> report) : Stream
{
    private long read;
    private long reportedAt;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await inner.ReadAsync(buffer, cancellationToken);
        read += count;
        if (count == 0 || Environment.TickCount64 - reportedAt >= 100)
        {
            reportedAt = Environment.TickCount64;
            await report(read);
            // Yield through the browser event loop so repaint and Cancel can run in WASM.
            await Task.Delay(1, cancellationToken);
        }
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => read; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    // The caller owns the underlying browser stream.
}
