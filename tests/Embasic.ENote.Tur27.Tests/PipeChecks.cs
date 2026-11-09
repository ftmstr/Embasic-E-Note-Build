using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static async Task PipeChecks()
    {
        await CheckAsync("separate pipe host signs observation and rejects a reader-key publication", async () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            string pipe = NewPipe();
            var client = new VaultAuthorityPipeClient(pipe, f.PublicKey);
            using (var host = f.Host(pipe))
            {
                Equal(f.Source!.Stamp, await client.ObserveAsync(f.Workspace, ReaderKey));
                Equal(0, await WaitChild(host));
            }
            using (var host = f.Host(pipe))
            {
                await ThrowsAsync<VaultGenerationOutcomeUnknownException>(() => client.PublishAsync(next.Stamp, ReaderKey));
                Equal(0, await WaitChild(host));
            }
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            Equal<VaultRetryIntent?>(null, f.Provider.ReadTransition(f.Workspace).Pending);
        });
        await CheckAsync("separate pipe host cannot commit an unregistered candidate even with publisher role", async () =>
        {
            using var f = new Fixture();
            string pipe = NewPipe();
            var client = new VaultAuthorityPipeClient(pipe, f.PublicKey);
            var fake = new VaultGenerationStamp(f.Workspace, 2, new string('B', 64));
            using var host = f.Host(pipe);
            await ThrowsAsync<VaultGenerationOutcomeUnknownException>(() => client.PublishAsync(fake, PublisherKey));
            Equal(0, await WaitChild(host));
            Equal(f.Original.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            Equal<VaultRetryIntent?>(null, f.Provider.ReadTransition(f.Workspace).Pending);
        });
        await CheckAsync("different service signing key is rejected despite a valid reader role", async () =>
        {
            using var f = new Fixture();
            using var imposter = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllBytes(Path.Combine(f.Home, "imposter.key"), imposter.ExportPkcs8PrivateKey());
            string pipe = NewPipe();
            using var host = f.Host(pipe, "imposter");
            var client = new VaultAuthorityPipeClient(pipe, f.PublicKey);
            await ThrowsAsync<CryptographicException>(() => client.ObserveAsync(f.Workspace, ReaderKey));
            Equal(0, await WaitChild(host));
        });
        await CheckAsync("captured IPC request cannot replay under a fresh server challenge", async () =>
        {
            using var f = new Fixture();
            string pipe = NewPipe();
            byte[] captured = new byte[122], firstChallenge = new byte[32];
            using (var host = f.Host(pipe))
            {
                using var client = await ConnectRaw(pipe);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await client.ReadExactlyAsync(firstChallenge, timeout.Token);
                captured[0] = 1; captured[1] = 1;
                f.Workspace.TryWriteBytes(captured.AsSpan(2, 16));
                RandomNumberGenerator.Fill(captured.AsSpan(58, 32));
                byte[] binding = [.. "EVRQ1"u8, .. firstChallenge, .. captured.AsSpan(0, 90)];
                HMACSHA256.HashData(ReaderKey, binding).CopyTo(captured, 90);
                await client.WriteAsync(captured, timeout.Token);
                await client.ReadExactlyAsync(new byte[105], timeout.Token);
                Equal(0, await WaitChild(host));
            }
            using (var host = f.Host(pipe))
            {
                using var client = await ConnectRaw(pipe);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[] newChallenge = new byte[32];
                await client.ReadExactlyAsync(newChallenge, timeout.Token);
                Equal(false, firstChallenge.SequenceEqual(newChallenge));
                await client.WriteAsync(captured, timeout.Token);
                Equal(0, await client.ReadAsync(new byte[105], timeout.Token));
                Equal(0, await WaitChild(host));
            }
        });
        await CheckAsync("host exit after reservation resumes full publication in a new process", async () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            string pipe = NewPipe();
            var client = new VaultAuthorityPipeClient(pipe, f.PublicKey);
            using (var host = f.Host(pipe, "before"))
            {
                await ThrowsAsync<VaultGenerationOutcomeUnknownException>(() => client.PublishAsync(next.Stamp, PublisherKey));
                Equal(71, await WaitChild(host));
            }
            Equal(new VaultRetryIntent(f.Source!.Stamp, next.Stamp), f.Provider.ReadTransition(f.Workspace).Pending);
            using (var host = f.Host(pipe))
            {
                Equal(VaultGenerationAdvanceState.Committed, await client.PublishAsync(next.Stamp, PublisherKey));
                Equal(0, await WaitChild(host));
            }
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        await CheckAsync("host exit after commit resolves exact receipt and refuses missing published content", async () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            string pipe = NewPipe();
            var client = new VaultAuthorityPipeClient(pipe, f.PublicKey);
            using (var host = f.Host(pipe, "after"))
            {
                await ThrowsAsync<VaultGenerationOutcomeUnknownException>(() => client.PublishAsync(next.Stamp, PublisherKey));
                Equal(72, await WaitChild(host));
            }
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            using (var host = f.Host(pipe))
            {
                Equal(VaultGenerationAdvanceState.Committed, await client.PublishAsync(next.Stamp, PublisherKey));
                Equal(0, await WaitChild(host));
            }
            File.Delete(Directory.EnumerateFiles(f.Published, "*.elh").Single());
            using (var host = f.Host(pipe))
            {
                await ThrowsAsync<VaultGenerationOutcomeUnknownException>(() => client.PublishAsync(next.Stamp, PublisherKey));
                Equal(0, await WaitChild(host));
            }
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
    }

    private static async Task ProcessChecks()
    {
        await CheckAsync("separate process reopens the durable plan after interruption before commit", async () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            using (var interrupted = f.ResumeChild(next.Stamp, "before"))
                Equal(71, await WaitChild(interrupted));
            Equal(new VaultRetryIntent(f.Source!.Stamp, next.Stamp), f.Provider.ReadTransition(f.Workspace).Pending);
            using (var restarted = f.ResumeChild(next.Stamp))
                Equal(0, await WaitChild(restarted));
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        await CheckAsync("separate process reconciles a receipt after interruption immediately after commit", async () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            using (var interrupted = f.ResumeChild(next.Stamp, "after"))
                Equal(72, await WaitChild(interrupted));
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            using (var restarted = f.ResumeChild(next.Stamp))
                Equal(0, await WaitChild(restarted));
            Equal(next.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        await CheckAsync("RISK independent same-user process with raw files and secrets bypasses IPC and reads legacy", async () =>
        {
            using var f = new Fixture();
            using var process = f.LegacyClient();
            Equal(0, await WaitChild(process));
            Equal(new string('C', 64), f.Provider.ReadTransition(f.Workspace).Current.ManifestSha256);
            Throws<CryptographicException>(() => f.Recovery.ReadCommitted(f.Workspace));
        });
    }

    private static bool NativePipesAvailable()
    {
        if (!OperatingSystem.IsLinux()) return true;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            return true;
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.AccessDenied)
        {
            return false;
        }
    }

    private static string NewPipe() => "enote-tur27-" + Guid.NewGuid().ToString("N");

    private static async Task<NamedPipeClientStream> ConnectRaw(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(timeout.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
