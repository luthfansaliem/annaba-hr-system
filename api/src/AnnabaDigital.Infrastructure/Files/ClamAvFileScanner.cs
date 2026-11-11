using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Files;

public sealed class AntivirusOptions
{
    /// <summary>Off by default (development). In production point it at a clamd instance; when on, a file that cannot be scanned is refused.</summary>
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Scans an upload with ClamAV over clamd's INSTREAM command (the bytes are streamed to the daemon; nothing is written to disk).
/// Protocol: "zINSTREAM\0", then chunks of [4-byte big-endian length][data], a zero-length chunk, and the daemon answers "stream: OK" or "stream: &lt;signature&gt; FOUND".
/// Any other answer, a timeout or a refused connection throws: the caller treats "could not scan" as "do not store".
/// </summary>
public sealed class ClamAvFileScanner(IOptions<AntivirusOptions> options) : IFileScanner
{
    private readonly AntivirusOptions _o = options.Value;
    public bool Enabled => _o.Enabled;

    public async Task<ScanResult> ScanAsync(byte[] content, CancellationToken ct = default)
    {
        if (!_o.Enabled) return new ScanResult(ScanVerdict.NotScanned);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_o.TimeoutSeconds, 1, 120)));
        using var tcp = new TcpClient(); await tcp.ConnectAsync(_o.Host, _o.Port, timeout.Token);
        await using var s = tcp.GetStream();
        await s.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
        var len = new byte[4];
        for (var at = 0; at < content.Length; at += 65536)
        {
            var n = Math.Min(65536, content.Length - at);
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)n); await s.WriteAsync(len, timeout.Token); await s.WriteAsync(content.AsMemory(at, n), timeout.Token);
        }
        BinaryPrimitives.WriteUInt32BigEndian(len, 0); await s.WriteAsync(len, timeout.Token); await s.FlushAsync(timeout.Token);

        var reply = new MemoryStream(); var buf = new byte[512];
        while (reply.Length < 4096)
        {
            var read = await s.ReadAsync(buf, timeout.Token); if (read == 0) break;
            reply.Write(buf, 0, read); if (Array.IndexOf(buf, (byte)0, 0, read) >= 0) break;
        }
        var text = Encoding.ASCII.GetString(reply.ToArray()).TrimEnd('\0', '\n', ' ');
        if (text.EndsWith("OK", StringComparison.Ordinal)) return new ScanResult(ScanVerdict.Clean);
        if (text.EndsWith("FOUND", StringComparison.Ordinal))
        {
            var sig = text[..^5].Trim(); var colon = sig.IndexOf(':'); if (colon >= 0) sig = sig[(colon + 1)..].Trim();
            return new ScanResult(ScanVerdict.Infected, sig.Length > 100 ? sig[..100] : sig);
        }
        throw new InvalidOperationException($"Unexpected answer from the virus scanner: '{(text.Length > 80 ? text[..80] : text)}'.");
    }
}
