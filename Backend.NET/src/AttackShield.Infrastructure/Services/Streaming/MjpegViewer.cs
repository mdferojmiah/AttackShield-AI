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

    /// <summary>Writes a full multipart chunk (header + frame + trailing CRLF).</summary>
    public async Task WriteFrameAsync(byte[] header, byte[] frame)
    {
        if (Dead) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header).ConfigureAwait(false);
            await _output.WriteAsync(frame).ConfigureAwait(false);
            await _output.WriteAsync(MjpegTrailer).ConfigureAwait(false);
            await _output.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            Dead = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly byte[] MjpegTrailer = { 0x0D, 0x0A }; // CRLF
}
