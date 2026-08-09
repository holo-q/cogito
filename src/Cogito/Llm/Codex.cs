namespace Cogito;

using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// The codex-exec backend for ILlm — spawns `codex exec --json` (a frontier model: gpt-5.5 / low
// reasoning effort, no server), closes stdin, drains both pipes (deadlock-free), parses the JSONL for
// the last agent_message. Ported lean from Errloom's CodexTextModel (the org's tested dispatch shape)
// so cogito stays decoupled from Errloom's whole tree. Any failure → empty string (non-fatal to the gate).
public sealed class CodexLlm(string model = "gpt-5.5", string effort = "low", int timeoutSeconds = 600) : ILlm
{
    /// The one instance the ~9 verb sites share — gpt-5.5 / low effort, the org's standing dispatch config.
    /// Stateless (each Complete spawns a fresh `codex exec`), so a singleton is safe and saves re-constructing
    /// the identical config at every call site. The public ctor stays for the rare non-default (a longer timeout).
    public static readonly CodexLlm Default = new(model: "gpt-5.5", effort: "low");

    public string Complete(string system, string user) => CompleteAsync(system, user).GetAwaiter().GetResult();

    /// Batched generation: fan the prompts across up to `parallelism` concurrent codex subprocesses,
    /// results returned in INPUT ORDER. cogito's grammar induction stays SERIAL by design — only the
    /// LLM sample generation (doc2query / gendev / curriculum) fans out, because that subprocess wall is
    /// the training-data bottleneck: a training push that needs N samples drops from N·~5s to ⌈N/p⌉·~5s.
    /// A single sample's failure → "" in its slot (same non-fatal contract as Complete).
    public string[] CompleteBatch(IReadOnlyList<(string system, string user)> prompts, int parallelism = 8)
    {
        var results = new string[prompts.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var tasks = new Task[prompts.Count];
        for (int i = 0; i < prompts.Count; i++)
        {
            int idx = i;
            tasks[idx] = Task.Run(async () =>
            {
                await gate.WaitAsync();
                try { results[idx] = await CompleteAsync(prompts[idx].system, prompts[idx].user); }
                finally { gate.Release(); }
            });
        }
        Task.WaitAll(tasks);
        return results;
    }

    /// The async core — one codex subprocess, pipes drained concurrently, timeout via cancellation so a
    /// batch of these can overlap on the thread pool (each awaits the process, never blocks a worker thread).
    public async Task<string> CompleteAsync(string system, string user)
    {
        using var p = new Process { StartInfo = BuildPsi(system, user) };
        try { if (!p.Start()) return ""; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return ""; }

        p.StandardInput.Close();                                  // </dev/null — never block on input
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();           // drained so a chatty stderr can't deadlock stdout
        using var cts = new CancellationTokenSource(timeoutSeconds * 1000);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { /* already gone */ } return ""; }
        await Task.WhenAll(outTask, errTask);
        return ParseJsonl(outTask.Result);
    }

    private ProcessStartInfo BuildPsi(string system, string user)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "codex",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
        psi.ArgumentList.Add("--skip-git-repo-check");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"model_reasoning_effort={effort}");
        if (!string.IsNullOrWhiteSpace(model))
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"model={model}");
        }
        psi.ArgumentList.Add($"{system}\n\n{user}");
        return psi;
    }

    /// The codex JSONL contract: the last `item.completed` whose `item.type == "agent_message"` carries
    /// the completion in `.text`. A stream with none → empty (non-fatal).
    private static string ParseJsonl(string stdout)
    {
        string? last = null;
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '{') continue;
            JsonElement ev;
            try { using var doc = JsonDocument.Parse(line); ev = doc.RootElement.Clone(); }
            catch (JsonException) { continue; }

            if (ev.TryGetProperty("type", out var t) && t.GetString() == "item.completed"
                && ev.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var it) && it.GetString() == "agent_message"
                && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                last = text.GetString();
        }
        return last ?? "";
    }
}
