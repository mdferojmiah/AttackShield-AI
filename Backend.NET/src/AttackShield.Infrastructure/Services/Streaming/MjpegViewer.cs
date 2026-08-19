namespace AttackShield.Infrastructure.Services.Streaming;

/// <summary>
/// One connected MJPEG HTTP client. Wraps the response body stream and serialises
/// writes so a single slow client can't interleave partial frames. Marked dead on
/// the first write failure so the stdout pump can drop it.
/// </summary>
internal sealed class MjpegViewer
{
    private readonly Stream _output;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MjpegViewer(Stream output) => _output = output;

    public volatile bool Dead;

    /// <summary>
    /// Writes a full multipart chunk (header + frame + trailing CRLF).
    /// Returns false without writing when the previous frame is still in flight,
    /// so one stalled client cannot hold up the fan-out for every other viewer.
    /// </summary>
    public async Task<bool> TryWriteFrameAsync(byte[] header, byte[] frame)
    {
        if (Dead) return false;

        // Non-blocking acquire: a client that has not drained the previous frame
        // simply skips this one rather than back-pressuring the stdout pump.
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return false;

        try
        {
            using var timeout = new CancellationTokenSource(WriteTimeout);
            await _output.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await _output.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            await _output.WriteAsync(MjpegTrailer, timeout.Token).ConfigureAwait(false);
            await _output.FlushAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            Dead = true;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private static readonly byte[] MjpegTrailer = { 0x0D, 0x0A }; // CRLF
}
