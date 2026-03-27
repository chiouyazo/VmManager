using System.Buffers;
using System.IO.Pipelines;

namespace VmManager.Agent.Services;

public sealed class DuplexPipeStream : Stream
{
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;

    public DuplexPipeStream(PipeReader reader, PipeWriter writer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        _reader = reader;
        _writer = writer;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        ReadResult result = await _reader.ReadAsync(cancellationToken);
        ReadOnlySequence<byte> sequence = result.Buffer;

        if (result.IsCompleted && sequence.Length == 0)
            return 0;

        int bytesToCopy = (int)Math.Min(count, sequence.Length);
        sequence.Slice(0, bytesToCopy).CopyTo(buffer.AsSpan(offset, bytesToCopy));
        _reader.AdvanceTo(sequence.GetPosition(bytesToCopy));

        return bytesToCopy;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        ReadResult result = await _reader.ReadAsync(cancellationToken);
        ReadOnlySequence<byte> sequence = result.Buffer;

        if (result.IsCompleted && sequence.Length == 0)
            return 0;

        int bytesToCopy = (int)Math.Min(buffer.Length, sequence.Length);
        sequence.Slice(0, bytesToCopy).CopyTo(buffer.Span[..bytesToCopy]);
        _reader.AdvanceTo(sequence.GetPosition(bytesToCopy));

        return bytesToCopy;
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        FlushResult result = await _writer.WriteAsync(
            new ReadOnlyMemory<byte>(buffer, offset, count),
            cancellationToken
        );
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        FlushResult result = await _writer.WriteAsync(buffer, cancellationToken);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _writer.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use ReadAsync");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use WriteAsync");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Flush() { }
}
