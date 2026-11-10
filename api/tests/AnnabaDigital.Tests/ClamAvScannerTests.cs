using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Infrastructure.Files;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>The ClamAV client against a tiny stand-in for clamd that speaks the INSTREAM protocol (no real scanner needed).</summary>
public sealed class ClamAvScannerTests
{
    /// <summary>Accepts one connection, reads an INSTREAM session and answers. Returns the bytes it was sent.</summary>
    private static (int Port, Task<byte[]> Received) FakeClamd(Func<byte[], string> answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var task = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(); listener.Stop(); await using var s = client.GetStream();
            var cmd = new byte[10]; await s.ReadExactlyAsync(cmd); Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(cmd));
            var data = new MemoryStream(); var len = new byte[4];
            while (true)
            {
                await s.ReadExactlyAsync(len); var n = (int)BinaryPrimitives.ReadUInt32BigEndian(len); if (n == 0) break;
                Assert.True(n <= 65536); var chunk = new byte[n]; await s.ReadExactlyAsync(chunk); data.Write(chunk);
            }
            await s.WriteAsync(Encoding.ASCII.GetBytes(answer(data.ToArray()) + "\0"));
            return data.ToArray();
        });
        return (port, task);
    }

    private static ClamAvFileScanner Scanner(int port, bool enabled = true) => new(Options.Create(new AntivirusOptions { Enabled = enabled, Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }));

    [Fact]
    public async Task A_clean_file_is_streamed_in_chunks_and_reported_clean()
    {
        var payload = new byte[150_000]; new Random(7).NextBytes(payload);                       // more than two 64 KB chunks
        var (port, received) = FakeClamd(_ => "stream: OK");
        var r = await Scanner(port).ScanAsync(payload);
        Assert.Equal(ScanVerdict.Clean, r.Verdict); Assert.Equal(payload, await received);       // every byte arrived, in order
    }

    [Fact]
    public async Task An_infected_file_is_reported_with_the_signature_name()
    {
        var (port, _) = FakeClamd(_ => "stream: Win.Test.EICAR_HDB-1 FOUND");
        var r = await Scanner(port).ScanAsync("anything"u8.ToArray());
        Assert.Equal(ScanVerdict.Infected, r.Verdict); Assert.Equal("Win.Test.EICAR_HDB-1", r.Signature);
    }

    [Fact]
    public async Task Anything_else_is_an_error_so_the_caller_refuses_the_file()
    {
        var (port, _) = FakeClamd(_ => "INSTREAM size limit exceeded. ERROR");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner(port).ScanAsync(new byte[10]));

        var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); var dead = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();   // nothing listens here any more
        await Assert.ThrowsAnyAsync<SocketException>(() => Scanner(dead).ScanAsync(new byte[10]));
    }

    [Fact]
    public async Task When_switched_off_nothing_is_sent_anywhere()
    {
        var s = Scanner(1, enabled: false); Assert.False(s.Enabled);
        Assert.Equal(ScanVerdict.NotScanned, (await s.ScanAsync(new byte[10])).Verdict);
    }
}
