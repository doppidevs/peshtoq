using System.IO;
using System.Net;
using System.Text;

namespace Peshtoq.Services.Link;

/// <summary>
/// In-process loopback verification of the stage-0 Link foundation: two LinkService instances
/// with distinct ephemeral identities pair over localhost TLS and must agree on the SAS, pin each
/// other, skip the prompt on reconnect, and — as a pure check — produce a different SAS under a
/// simulated MITM. Run with "Peshtoq.exe --link-selftest"; results go to the given log file.
/// This is a test harness, not part of the shipping UI.
/// </summary>
public static class LinkSelfTest
{
    public static async Task RunAsync(string logPath)
    {
        var log = new StringBuilder();
        int passed = 0, failed = 0;
        void Check(string name, bool ok)
        {
            if (ok) { passed++; log.AppendLine($"  PASS  {name}"); }
            else { failed++; log.AppendLine($"  FAIL  {name}"); }
        }

        log.AppendLine($"Peshtoq Link self-test — {DateTime.Now:O}");

        LinkService? a = null, b = null;
        try
        {
            // 1. SAS symmetry and MITM divergence — pure crypto, no sockets.
            var idA = IdentityService.CreateEphemeral();
            var idB = IdentityService.CreateEphemeral();
            var idM = IdentityService.CreateEphemeral();
            string sasAB = LinkTransport.ComputeSas(idA.Certificate, idB.Certificate);
            string sasBA = LinkTransport.ComputeSas(idB.Certificate, idA.Certificate);
            Check("SAS is symmetric (sort order independent)", sasAB == sasBA);
            Check("SAS(A,M) differs from SAS(M,B) — MITM would mismatch",
                LinkTransport.ComputeSas(idA.Certificate, idM.Certificate) !=
                LinkTransport.ComputeSas(idM.Certificate, idB.Certificate));
            Check("distinct identities have distinct SPKI hashes", idA.SpkiHash != idB.SpkiHash);

            // 2. Live loopback pairing.
            var storeA = new PairedStore((string?)null); // in-memory
            var storeB = new PairedStore((string?)null);
            a = new LinkService(idA, storeA);
            b = new LinkService(idB, storeB);

            int promptsA = 0, promptsB = 0;
            string seenSasA = "", seenSasB = "";
            a.ConfirmPairing = p => { promptsA++; seenSasA = p.Sas; return Task.FromResult(true); };
            b.ConfirmPairing = p => { promptsB++; seenSasB = p.Sas; return Task.FromResult(true); };

            a.Start();
            b.Start();

            var peerB = new DiscoveredPeer { Id = idB.SpkiHash, Host = "B", Address = IPAddress.Loopback, Port = b.Port };
            _ = a.ConnectAsync(peerB); // runs until disconnect; don't await

            bool connected = await WaitUntil(() => a.State == LinkState.Connected && b.State == LinkState.Connected, 8000);
            Check("both sides reached Connected", connected);
            Check("both prompted exactly once on first pair", promptsA == 1 && promptsB == 1);
            Check("both screens showed the same SAS", seenSasA.Length > 0 && seenSasA == seenSasB);
            Check("A pinned B's key", storeA.IsPinned(idB.SpkiHash));
            Check("B pinned A's key", storeB.IsPinned(idA.SpkiHash));

            // 3. Reconnect — pinned, so no prompt.
            a.Disconnect();
            bool dropped = await WaitUntil(() => a.State == LinkState.Listening && b.State == LinkState.Listening, 6000);
            Check("both returned to Listening after disconnect", dropped);

            _ = a.ConnectAsync(peerB);
            bool reconnected = await WaitUntil(() => a.State == LinkState.Connected && b.State == LinkState.Connected, 8000);
            Check("reconnected while paired", reconnected);
            Check("no second prompt for a paired peer", promptsA == 1 && promptsB == 1);

            // 4. Clipboard text over the link (transport only — ApplyRemote needs a real STA clipboard).
            var gotAtB = new TaskCompletionSource<string>();
            var gotAtA = new TaskCompletionSource<string>();
            b.ClipTextReceived += t => gotAtB.TrySetResult(t);
            a.ClipTextReceived += t => gotAtA.TrySetResult(t);

            a.SendClipText("salom-A→B");
            bool abOk = await Task.WhenAny(gotAtB.Task, Task.Delay(4000)) == gotAtB.Task;
            Check("clipboard text crossed A->B", abOk && gotAtB.Task.Result == "salom-A→B");

            b.SendClipText("javob-B→A");
            bool baOk = await Task.WhenAny(gotAtA.Task, Task.Delay(4000)) == gotAtA.Task;
            Check("clipboard text crossed B->A", baOk && gotAtA.Task.Result == "javob-B→A");

            // 5. Input packets: ordered delivery + correct decode (hooks/injection tested on 2 machines).
            var inbox = new System.Collections.Concurrent.ConcurrentQueue<(PacketType, byte[])>();
            var count = new TaskCompletionSource();
            b.InputPacket += (t, p) => { inbox.Enqueue((t, p)); if (inbox.Count >= 3) count.TrySetResult(); };

            a.EnqueueSend(PacketType.ControlAcquire, new byte[] { 0 });
            a.EnqueueSend(PacketType.MouseMove, Protocol.MouseMove(12, -7));
            a.EnqueueSend(PacketType.Key, Protocol.Key(0x41, 0x1E, true, false));
            bool got3 = await Task.WhenAny(count.Task, Task.Delay(4000)) == count.Task;
            var arr = inbox.ToArray();
            Check("3 input packets arrived in order", got3 && arr.Length >= 3
                && arr[0].Item1 == PacketType.ControlAcquire
                && arr[1].Item1 == PacketType.MouseMove
                && arr[2].Item1 == PacketType.Key);
            if (arr.Length >= 3)
            {
                var (mdx, mdy) = Protocol.ReadMouseMove(arr[1].Item2);
                var (kvk, kscan, kdown, kext) = Protocol.ReadKey(arr[2].Item2);
                Check("MouseMove decoded (12,-7)", mdx == 12 && mdy == -7);
                Check("Key decoded (vk=0x41 scan=0x1E down)", kvk == 0x41 && kscan == 0x1E && kdown && !kext);
            }
        }
        catch (Exception ex)
        {
            failed++;
            log.AppendLine($"  FAIL  exception: {ex}");
        }
        finally
        {
            a?.Dispose();
            b?.Dispose();
        }

        log.AppendLine($"\n{passed} passed, {failed} failed — {(failed == 0 ? "OK" : "FAILURES")}");
        try { File.WriteAllText(logPath, log.ToString()); } catch { }
    }

    private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (cond()) return true;
            await Task.Delay(50).ConfigureAwait(false);
        }
        return cond();
    }
}
