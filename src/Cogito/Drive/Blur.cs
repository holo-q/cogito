namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE BLUR (Whorl B) — probabilistic merges at TOKEN grain ──
//
// Re-Pair is EXACT-match; the world is NEAR-match. The blur is the faculty that generalizes an exact-surface
// grammar into slots — merging things whose ROLE agrees even when their bytes differ. It decomposes into three
// mechanically distinct detectors, in COST order:
//
//   (1) COUNT-SLOTS  (CountSlots.cs) — the doubling-tower scan: a slot over N (a loop's repetition count). The
//       knot substrate. Cheapest, ships first.
//   (2) LITERAL-ALTERNATION — anti-unify over rule yields (AntiUnify.Edges/GrowthLoop): fillers that interchange in
//       the same frame are slot-mates ("cat"/"dog" in "the ___ ran"). MDL-gated (mint iff ΔMDL pays).
//   (3) TRANSFORM-SLOTS (v0 op = {offset}) — members related by a constant-structure Delta. "every byte +k" in
//       Delta space ANNOUNCES the offset op; nothing is enumerated. The transform-slot IS the PredictiveCodec's
//       (description, residual) at rule grain (Predict.cs): description = (offset, k), residual = the base member.
//       A slot over pitch-offset IS transposition invariance.
//
// GRAIN DISCIPLINE (thesis-load-bearing): every detector reads a GENERIC word/punctuation tokenization — the SAME
// organ for code, prose, and codebook streams. The blur does NOT know what a "function" is. If def/call paradigms
// are real, they must EMERGE as the highest-diversity slot frames; if they don't, we WANT to know. Nothing is
// hand-tuned into existence; the `blur` probe reports what emerges VERBATIM.
//
// TIER 1 is purely ADDITIVE (H3): the detectors read a grammar + corpus and produce slot data + a report. They
// never enter the reconstruction path, so verify-induct/verify-loom stay byte-identical. Tier 1.5 (slot-aware
// reflection) is the behavior change — it lives in Pearl.Audit behind a toggle, not here.

public static class Blur
{
    public enum SlotSources { Unknown = 0, StimulusRead = 1, PriorObservation = 2, GrammarPrior = 3 }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE GENERIC TOKENIZER — word-runs vs single-char punctuation; whitespace separates. Code/prose/codebook
    //  agnostic. `def foo(bar):` → [def, foo, (bar, ), :]; `the cat, sat.` → [the, cat, ,, sat,.]. It knows
    //  only "word byte vs not" — the frame `def ___ (` becomes visible without the tokenizer knowing what def is.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// Split a corpus into per-line token sentences. A token is a maximal run of WORD bytes ([A-Za-z0-9_] or any
    /// ≥0x80 UTF-8 continuation — unicode words stay whole) OR a single non-word, non-whitespace byte (punctuation).
    /// Whitespace separates and is dropped. Newline ends a sentence (the barrier). Lines with <2 tokens are skipped.
    public static List<string[]> Tokenize(byte[] corpus)
    {
        var sents = new List<string[]>();
        var toks = new List<string>();
        int i = 0, n = corpus.Length;
        while (i < n)
        {
            byte b = corpus[i];
            if (b == (byte)'\n') { if (toks.Count >= 2) sents.Add(toks.ToArray()); toks.Clear(); i++; continue; }
            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r') { i++; continue; }
            if (IsWord(b)) { int j = i + 1; while (j < n && IsWord(corpus[j])) j++; toks.Add(Encoding.UTF8.GetString(corpus, i, j - i)); i = j; }
            else { toks.Add(((char)b).ToString()); i++; }
        }
        if (toks.Count >= 2) sents.Add(toks.ToArray());
        return sents;
    }

    private static bool IsWord(byte b) =>
        (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z')
        || (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'_' || b >= 0x80;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  FRAME CENSUS — the raw def/call emergence read. For each interior token, its trigram frame (left, right);
    //  a frame's fillers are the words that occupy its centre. Frames with the most DISTINCT fillers ARE the
    //  paradigmatic slots — `def ___ (` tops a code corpus, `the ___` tops prose — VISIBLE, never hand-coded.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// One discovered paradigmatic frame: `left ___ right`, its distinct fillers, and how often it fired.
    public readonly record struct Frame(string Left, string Right, string[] Fillers, int Fires)
    {
        public int Diversity => Fillers.Length;
    }

    /// Census the (left, right) trigram frames and their filler sets. Returns frames with ≥`minFillers` distinct
    /// fillers, sorted by diversity (then fire count) descending — the highest-diversity frames first. Deterministic.
    public static List<Frame> FrameCensus(IReadOnlyList<string[]> corpus, int minFillers = 2)
    {
        var frames = new Dictionary<(string, string), (Dictionary<string, int> Fillers, int Fires)>();
        foreach (var s in corpus)
            for (int p = 1; p + 1 < s.Length; p++)
            {
                var key = (s[p - 1], s[p + 1]);
                if (!frames.TryGetValue(key, out var e)) frames[key] = e = (new Dictionary<string, int>(StringComparer.Ordinal), 0);
                e.Fillers[s[p]] = e.Fillers.GetValueOrDefault(s[p]) + 1;
                frames[key] = (e.Fillers, e.Fires + 1);
            }
        var outp = new List<Frame>();
        foreach (var ((l, r), (fillers, fires)) in frames)
        {
            if (fillers.Count < minFillers) continue;
            var fs = fillers.Keys.ToArray(); Array.Sort(fs, StringComparer.Ordinal);
            outp.Add(new Frame(l, r, fs, fires));
        }
        outp.Sort((a, b) => a.Diversity != b.Diversity ? b.Diversity - a.Diversity
                          : a.Fires != b.Fires ? b.Fires - a.Fires
                          : string.CompareOrdinal(a.Left + "\u001f" + a.Right, b.Left + "\u001f" + b.Right));
        return outp;
    }

    /// One file's token sentences. The byte-induction path still reads LoadSource's concatenated bytes; this is the
    /// additive file-aware path for probes that need source identity after tokenization.
    public readonly record struct TokenFile(string File, string Repo, string[][] Sentences, int Bytes);

    /// One filler in a file-aware frame: the same centre token plus the files where it occupied that frame.
    public readonly record struct FileFrameFiller(string Token, FileCount[] Files, int Fires);

    /// A count carried by one source file.
    public readonly record struct FileCount(string File, int Count);

    /// A frame with the file axis preserved: `left ___ right`, filler→file sets, family-level file counts, and fires.
    public readonly record struct FileFrame(string Left, string Right, FileFrameFiller[] Fillers, FileCount[] Files, int Fires)
    {
        public int Diversity => Fillers.Length;
    }

    /// Load and tokenize a source FILE or DIR while preserving the file labels. Directory sources recurse, skip
    /// generated/binary dead weight, and keep labels relative to the requested root.
    public static List<TokenFile> TokenizeSourceFiles(string source, int maxBytes = int.MaxValue)
    {
        var outp = new List<TokenFile>();
        if (File.Exists(source))
        {
            var bytes = ReadPrefix(source, maxBytes);
            if (bytes.Length > 0) outp.Add(new TokenFile(Path.GetFileName(source), RepoLabel(Path.GetDirectoryName(Path.GetFullPath(source)) ?? "."), Tokenize(bytes).ToArray(), bytes.Length));
            return outp;
        }

        if (!Directory.Exists(source)) return outp;
        string root = Path.GetFullPath(source);
        string repo = RepoLabel(root);
        int used = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsProbeTextFile)
                     .OrderBy(f => Path.GetRelativePath(root, f), StringComparer.Ordinal))
        {
            if (used >= maxBytes) break;
            var bytes = ReadPrefix(file, maxBytes - used);
            used += bytes.Length;
            if (bytes.Length == 0) continue;
            string label = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var sents = Tokenize(bytes).ToArray();
            if (sents.Length > 0) outp.Add(new TokenFile(label, repo, sents, bytes.Length));
        }
        return outp;
    }

    /// Census trigram frames with file identity intact. The output is sorted like FrameCensus, but each filler carries
    /// the files where it fired so cross-file co-instantiation can be measured instead of inferred.
    public static List<FileFrame> FrameCensusByFile(IReadOnlyList<TokenFile> files, int minFillers = 2)
    {
        var frames = new Dictionary<(string, string), FileFrameEntry>();
        foreach (var file in files)
            foreach (var s in file.Sentences)
                for (int p = 1; p + 1 < s.Length; p++)
                {
                    var key = (s[p - 1], s[p + 1]);
                    if (!frames.TryGetValue(key, out var e)) frames[key] = e = new FileFrameEntry();
                    e.Add(s[p], file.File);
                }

        var outp = new List<FileFrame>();
        foreach (var ((l, r), e) in frames)
        {
            if (e.Fillers.Count < minFillers) continue;
            var fillers = e.Fillers
                .Select(kv => new FileFrameFiller(kv.Key, Counts(kv.Value.Files), kv.Value.Fires))
                .OrderBy(f => f.Token, StringComparer.Ordinal)
                .ToArray();
            outp.Add(new FileFrame(l, r, fillers, Counts(e.Files), e.Fires));
        }
        outp.Sort((a, b) => a.Diversity != b.Diversity ? b.Diversity - a.Diversity
                          : a.Fires != b.Fires ? b.Fires - a.Fires
                          : string.CompareOrdinal(a.Left + "\u001f" + a.Right, b.Left + "\u001f" + b.Right));
        return outp;
    }

    private sealed class FileFrameEntry
    {
        public readonly Dictionary<string, FileFrameFillerEntry> Fillers = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Files = new(StringComparer.Ordinal);
        public int Fires;

        public void Add(string filler, string file)
        {
            if (!Fillers.TryGetValue(filler, out var e)) Fillers[filler] = e = new FileFrameFillerEntry();
            e.Fires++;
            e.Files[file] = e.Files.GetValueOrDefault(file) + 1;
            Files[file] = Files.GetValueOrDefault(file) + 1;
            Fires++;
        }
    }

    private sealed class FileFrameFillerEntry
    {
        public readonly Dictionary<string, int> Files = new(StringComparer.Ordinal);
        public int Fires;
    }

    private static FileCount[] Counts(Dictionary<string, int> counts)
        => counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new FileCount(kv.Key, kv.Value)).ToArray();

    private static byte[] ReadPrefix(string path, int maxBytes)
    {
        if (maxBytes <= 0) return [];
        var bytes = File.ReadAllBytes(path);
        return bytes.Length > maxBytes ? bytes[..maxBytes] : bytes;
    }

    private static string RepoLabel(string path)
        => Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static bool IsProbeTextFile(string path)
    {
        foreach (var part in Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (part is ".git" or "bin" or "obj" or "tmp" or "runs") return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cs" or ".csproj" or ".props" or ".targets" or ".md" or ".txt" or ".toml" or ".json" or ".jsonl" or ".ron" or ".slnx";
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  TRANSFORM-SLOTS (v0 op = {offset}) — members related by a constant byte offset (the group action). The op
    //  is DERIVED, not enumerated: normalize each member by subtracting its first byte → the offset-invariant SHAPE;
    //  members with the same shape are one orbit (b[i] = a[i]+k ∀i ⟺ same shape). The (description, residual) code:
    //  description = the offset per member, residual = the base member's bytes. Pays when the orbit compresses.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// One transform-slot under the offset op: `Base` (the canonical member — the residual), `Members` (the orbit),
    /// `Offsets` (each member's signed byte offset from Base — the description), `SavedBytes` (enumerate N surfaces
    /// vs base + N offsets). Only orbits of ≥2 members and ≥2-byte surfaces are reported (a 1-byte offset-orbit is
    /// trivial — every byte is an offset of every other).
    public readonly record struct OffsetSlot(string Base, string[] Members, int[] Offsets, long SavedBytes);

    public static List<OffsetSlot> OffsetSlots(IEnumerable<string> members)
    {
        var byLen = new Dictionary<int, List<(string S, byte[] B)>>();
        foreach (var m in members.Distinct(StringComparer.Ordinal))
        {
            var b = Encoding.UTF8.GetBytes(m);
            if (b.Length < 2) continue;                                   // byte-grain offset over 1-byte members is degenerate
            (byLen.TryGetValue(b.Length, out var l) ? l : byLen[b.Length] = new()).Add((m, b));
        }
        var slots = new List<OffsetSlot>();
        foreach (var (len, group) in byLen.OrderBy(kv => kv.Key))
        {
            var byShape = new Dictionary<string, List<(string S, byte[] B)>>(StringComparer.Ordinal);
            foreach (var (s, b) in group)
            {
                var shape = new byte[len]; byte b0 = b[0];
                for (int k = 0; k < len; k++) shape[k] = (byte)(b[k] - b0);   // shape[0]=0; identical shape ⟺ constant offset apart
                string key = Convert.ToHexStringLower(shape);
                (byShape.TryGetValue(key, out var l) ? l : byShape[key] = new()).Add((s, b));
            }
            foreach (var orbit in byShape.Values)
            {
                if (orbit.Count < 2) continue;
                orbit.Sort((x, y) => x.B.AsSpan().SequenceCompareTo(y.B));
                var baseB = orbit[0].B;
                var offsets = orbit.Select(o => (int)(sbyte)(o.B[0] - baseB[0])).ToArray();
                var mem = orbit.Select(o => o.S).ToArray();
                long saved = (long)orbit.Count * len - (len + orbit.Count);   // enumerate vs (residual + N descriptions)
                slots.Add(new OffsetSlot(orbit[0].S, mem, offsets, saved));
            }
        }
        slots.Sort((a, b) => b.SavedBytes != a.SavedBytes ? b.SavedBytes.CompareTo(a.SavedBytes) : string.CompareOrdinal(a.Base, b.Base));
        return slots;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  RULE SLOTS — the TIER 1.5 substrate: slot-classes over BYTE-GRAMMAR RULE indices. Two rules are slot-mates
    //  iff they instantiate the same slot-PATTERN (anti-unify at one token position within a RICH frame — the
    //  ≥minContext-context gate blocks the `the ___` over-merge that would fake reflection everywhere). Pearl.Audit
    //  pools JewelSources across each rule's mates so a DEEP rule reflects when a peer exercises a slot-mate (the
    //  depth cure). Produced here, consumed audit-side; nothing enters reconstruction.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// The per-rule slot-class: `Mates[r]` = the sorted rule indices that share r's slot-pattern (including r), or
    /// null if r has no slot-mates. `Classes` = the number of distinct slot-classes. Only rules with
    /// expLen ≥ `floorBytes` (the reflect floor — a rule too short to reflect can't pool) participate. Deterministic
    /// in CONTENT (the sorted mate arrays are independent of union order).
    ///
    /// Two rules are slot-mates iff they share a blanked-one-position FRAME (differ at exactly one token) where the
    /// frame has ≥2 distinct fillers AND ≥`minContext` context tokens (a RICH skeleton). The richness gate is the
    /// over-merge guard: a bare `the ___` (one context token) would bridge every noun rule into one blob — fake
    /// reflection everywhere; a rich skeleton with a single varying position (`public async Task ___ (`,
    /// `the ___ slept … den`) is a genuine paradigm. Union-find then pools every rule that varies within the same
    /// rich frame, so a large SYMMETRIC filler-class (e.g. 16 fillers across two sources) is ONE class — mutual-kNN
    /// cannot do that (uniform edge weights collapse to an ordinal tie-break that never links the late fillers).
    public static (int[]?[] Mates, int Classes, SlotSources?[] Sources) DetectRuleSlots(GrammarRule[] rules, uint alpha, int floorBytes = 8, int minContext = 3)
    {
        int n = rules.Length;
        var mates = new int[]?[n];
        var sources = new SlotSources?[n];
        if (n == 0) return (mates, 0, sources);
        var expLen = Engine.ExpLens(rules, alpha);
        var yields = new Dictionary<int, string[]>();
        for (int r = 0; r < n; r++)
        {
            if (expLen[r] < floorBytes || rules[r].Kind == RuleBodyKind.SlotClass) continue;
            var t = TokensOf(Reconstruct.Expand(rules, [new Symbol(alpha + (uint)r)]));
            if (t.Length - 1 >= minContext) yields[r] = t;   // need ≥minContext context tokens around the blank
        }
        if (yields.Count < 2) return (mates, 0, sources);

        // frame (blanked one position) → the rules instantiating it + its distinct fillers. A frame with ≥2 rules
        // AND ≥2 fillers is a real slot; the ≥minContext richness gate (applied above) blocks the promiscuous merge.
        var frameRules = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var frameFillers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var frameSources = new Dictionary<string, SlotSources>(StringComparer.Ordinal);
        var mixedSourceFrames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (r, toks) in yields)
            for (int p = 0; p < toks.Length; p++)
            {
                if (IsProvenanceControlToken(toks[p])) continue;
                string key = FrameKey(toks, p);
                (frameRules.TryGetValue(key, out var l) ? l : frameRules[key] = new()).Add(r);
                (frameFillers.TryGetValue(key, out var f) ? f : frameFillers[key] = new(StringComparer.Ordinal)).Add(toks[p]);
                var src = SourceNear(toks, p);
                if (frameSources.TryGetValue(key, out var prev))
                {
                    if (prev != src) mixedSourceFrames.Add(key);
                }
                else frameSources[key] = src;
            }
        var uf = new UnionFind(n);
        var ruleSources = new Dictionary<int, HashSet<SlotSources>>();
        foreach (var (key, l) in frameRules)
            if (l.Count >= 2 && frameFillers[key].Count >= 2)
            {
                if (mixedSourceFrames.Contains(key)) continue;
                var src = frameSources.GetValueOrDefault(key, SlotSources.Unknown);
                for (int i = 1; i < l.Count; i++) uf.Union(l[0], l[i]);
                foreach (int r in l)
                    (ruleSources.TryGetValue(r, out var set) ? set : ruleSources[r] = new()).Add(src);
            }

        var group = new Dictionary<int, List<int>>();
        foreach (var (r, _) in yields) { int root = uf.Find(r); (group.TryGetValue(root, out var gg) ? gg : group[root] = new()).Add(r); }
        int classes = 0;
        foreach (var g in group.Values)
        {
            if (g.Count < 2) continue;
            var arr = g.ToArray(); Array.Sort(arr);
            var src = GroupSource(arr, ruleSources);
            foreach (int r in arr) mates[r] = arr;
            foreach (int r in arr) sources[r] = src;
            classes++;
        }
        return (mates, classes, sources);
    }

    private static bool IsProvenanceControlToken(string token)
        => token.StartsWith("src_", StringComparison.Ordinal) || token is "slot" or "filler";

    private static SlotSources GroupSource(int[] rules, Dictionary<int, HashSet<SlotSources>> ruleSources)
    {
        SlotSources source = SlotSources.Unknown;
        foreach (int r in rules)
        {
            if (!ruleSources.TryGetValue(r, out var set)) continue;
            foreach (var s in set)
            {
                if (s == SlotSources.Unknown) continue;
                if (source == SlotSources.Unknown) source = s;
                else if (source != s) return SlotSources.Unknown;
            }
        }
        return source;
    }

    private static SlotSources SourceNear(string[] toks, int p)
    {
        SlotSources source = SlotSources.Unknown;
        int best = int.MaxValue;
        for (int i = 0; i < toks.Length; i++)
        {
            var s = SourceToken(toks[i]);
            if (s == SlotSources.Unknown) continue;
            int d = Math.Abs(i - p);
            if (d < best) { source = s; best = d; }
        }
        return source;
    }

    public static SlotSources SourceToken(string token)
        => token switch
        {
            "src_stimulus_read" => SlotSources.StimulusRead,
            "src_prior_observation" => SlotSources.PriorObservation,
            "src_grammar_prior" => SlotSources.GrammarPrior,
            _ => SlotSources.Unknown,
        };

    public static string SourceToken(SlotSources source)
        => source switch
        {
            SlotSources.StimulusRead => "src_stimulus_read",
            SlotSources.PriorObservation => "src_prior_observation",
            SlotSources.GrammarPrior => "src_grammar_prior",
            _ => "src_unknown",
        };

    public static string SourceLabel(SlotSources source)
        => source switch
        {
            SlotSources.StimulusRead => "stimulus-read",
            SlotSources.PriorObservation => "prior-observation",
            SlotSources.GrammarPrior => "grammar-prior",
            _ => "unknown",
        };

    /// Tokenize ONE span (a rule expansion — barrier-free) into words/punctuation. The single-sentence twin of
    /// Tokenize (no newline splitting; a rule never straddles '\n', so its expansion is one sentence).
    public static string[] TokensOf(ReadOnlySpan<byte> span)
    {
        var toks = new List<string>();
        int i = 0, n = span.Length;
        while (i < n)
        {
            byte b = span[i];
            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') { i++; continue; }
            if (IsWord(b)) { int j = i + 1; while (j < n && IsWord(span[j])) j++; toks.Add(Encoding.UTF8.GetString(span[i..j])); i = j; }
            else { toks.Add(((char)b).ToString()); i++; }
        }
        return toks.ToArray();
    }

    /// The blanked-one-position frame key: (length, blank pos, prefix words, suffix words). Two yields collide here
    /// iff they differ ONLY at position p — the slot the anti-unification opens.
    private static string FrameKey(string[] y, int p)
    {
        var sb = new StringBuilder();
        sb.Append(y.Length).Append('\u001f').Append(p).Append('\u001f');
        for (int i = 0; i < p; i++) sb.Append(y[i]).Append('\u001f');
        sb.Append('\u001f');
        for (int i = p + 1; i < y.Length; i++) sb.Append(y[i]).Append('\u001f');
        return sb.ToString();
    }

    private sealed class UnionFind
    {
        private readonly int[] _p;
        public UnionFind(int n) { _p = new int[n]; for (int i = 0; i < n; i++) _p[i] = i; }
        public int Find(int x) { while (_p[x] != x) { _p[x] = _p[_p[x]]; x = _p[x]; } return x; }
        public void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) _p[Math.Max(ra, rb)] = Math.Min(ra, rb); }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE ANTI-GOODHART NULL — the graded survivor (monk-depth 2026-07-07): a slot counts as real iff its
    //  two-part-MDL pay beats a MARGINAL-FILLER null by the pay-floor. A Goodharted slot (arbitrary tokens grouped)
    //  pays no more than random tokens drawn from the same marginal; a genuine paradigm (interchangeable in a frame)
    //  pays because its members compress the SLOTTED stream. The retired productive-paradigm test is NOT used.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// For one minted slot, the real substitution's ΔMDL pay vs the mean pay of `draws` marginal-filler nulls of the
    /// same size (members re-sampled from the corpus token frequency). `Survives` = real beats the null mean by `floor`.
    public readonly record struct NullTest(string Slot, int Members, double RealPay, double NullPay, bool Survives);

    public static List<NullTest> MarginalFillerNull(
        IReadOnlyList<string[]> corpus, Paradigm model, ulong seed, int draws = 8, double floor = 1.0)
    {
        // the token marginal (frequency-weighted sampling table) — the null's filler source.
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in corpus) foreach (var t in s) freq[t] = freq.GetValueOrDefault(t) + 1;
        var vocab = freq.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var cum = new long[vocab.Length]; long acc = 0;
        for (int i = 0; i < vocab.Length; i++) { acc += freq[vocab[i]]; cum[i] = acc; }
        long total = acc;
        ulong rng = seed;
        string Draw() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; long r = (long)((rng >> 11) % (ulong)Math.Max(1, total)); int lo = 0, hi = vocab.Length - 1; while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] <= r) lo = mid + 1; else hi = mid; } return vocab[lo]; }

        double PayOf(IReadOnlyCollection<string> members, string name)
        {
            var set = new HashSet<string>(members, StringComparer.Ordinal);
            var slotted = new List<List<string>>(corpus.Count);
            foreach (var s in corpus) { var row = new List<string>(s.Length); foreach (var t in s) row.Add(set.Contains(t) ? name : t); slotted.Add(row); }
            var m2s = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var w in members) m2s[w] = name;
            double baseTotal = AntiUnify.TwoPartMdl(corpus, corpus, new Dictionary<string, string>(StringComparer.Ordinal)).Total;
            return baseTotal - AntiUnify.TwoPartMdl(corpus, slotted, m2s).Total;
        }

        var tests = new List<NullTest>();
        foreach (var (name, members) in model.SlotMembers.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // only LEAF-word slots (skip meta-slots whose members are other slots — the null has no surface to draw)
            if (members.Any(m => m.StartsWith('['))) continue;
            double real = PayOf(members, name);
            double nullSum = 0;
            for (int d = 0; d < draws; d++)
            {
                var nullMembers = new List<string>(members.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                int guard = 0;
                while (nullMembers.Count < members.Count && guard++ < members.Count * 20) { var t = Draw(); if (seen.Add(t)) nullMembers.Add(t); }
                nullSum += PayOf(nullMembers, name);
            }
            double nullPay = draws > 0 ? nullSum / draws : 0;
            // the graded survivor: the slot must actually SHRINK the residual (real > floor) AND beat the
            // marginal-filler null by the floor. A slot that pays negative in isolation "beats" a worse null but
            // shrinks nothing — it must not count as rule-exercise (Tier 1.5's gate is shrinkage, not mere edge).
            tests.Add(new NullTest(name, members.Count, real, nullPay, real > floor && real > nullPay + floor));
        }
        return tests;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  THE PROBE VERB — run all three detectors over a corpus and report what emerges, honestly.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// usage: blur <source> [--out DIR] [--max-bytes N] [--iter I] [--top T] [--seed HEX]
    ///   <source> = a corpus FILE, or a DIR (its files concatenated, '\n'-joined). The v0 blur at token grain:
    ///   (1) the doubling-tower census (knot substrate), (2) literal-alternation slots (anti-unify, MDL-gated) +
    ///   the frame census (the def/call emergence read), (3) transform-slots (offset op), + the marginal-filler
    ///   anti-Goodhart null. READ-ONLY: mints nothing into the reconstruction path (Tier 1, byte-identical).
    public static int Run(string[] args)
    {
        string source = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "";
        string outDir = Args.Str(args, "--out", "tmp/blur");
        int maxBytes = Args.Int(args, "--max-bytes", 300_000);
        int iter = Args.Int(args, "--iter", 6);
        int top = Args.Int(args, "--top", 20);
        ulong seed = Args.Seed(args, "--seed", 0xB1000B1UL);

        var (corpus, label) = LoadSource(source, maxBytes);
        if (corpus.Length == 0) { Console.Error.WriteLine("  blur: empty/missing source"); return 1; }

        var run = Cogito.Run.New("blur");
        Trace.Note($"blur · v0 blur at TOKEN grain · {label} · {corpus.Length}B · generic word tokenizer · no LLM · seed {seed:X}");
        Trace.Note("");

        // ── the byte grammar (for the count-slot DAG scan) + the token corpus (for detectors 2+3) ──
        var (_, _, g) = Engine.Induce(corpus);
        var tokens = Tokenize(corpus);
        Trace.Note($"  byte grammar: {g.Rules.Length} rules · {g.Compressed.Length} compressed symbols · token corpus: {tokens.Count} sentences");
        Trace.Note("");

        // ── (1) COUNT-SLOTS — the doubling-tower census (the knot substrate) ──
        var towers = CountSlots.Scan(g.Rules, g.AlphabetSize);
        var census = CountSlots.Summarize(towers);
        Trace.Note("  ── (1) COUNT-SLOTS — doubling-tower census (Whorl C knot substrate) ──");
        Trace.Note($"    {census.Towers} towers · max height {census.MaxHeight} · deepest unroll {census.DeepestSpan}B");
        if (census.Towers > 0)
        {
            var hist = new StringBuilder("    height histogram: ");
            for (int h = 1; h < census.HeightHistogram.Length; h++) if (census.HeightHistogram[h] > 0) hist.Append($"h{h}:{census.HeightHistogram[h]} ");
            Trace.Note(hist.ToString());
            Trace.Note("    top towers (by unroll span):");
            foreach (var t in towers.OrderByDescending(t => t.TopSpan).ThenByDescending(t => t.Height).Take(Math.Min(top, 12)))
                Trace.Note($"      BODY «{Escape(BaseSurface(g, t.Base), 32)}» ({t.BaseSpan}B) ×2^{t.Height} → {t.TopSpan}B");
        }
        Trace.Note("");

        // ── (2) LITERAL-ALTERNATION — anti-unify at TOKEN grain (MDL-gated growth loop) + the frame census ──
        Trace.Note("  ── (2) LITERAL-ALTERNATION — anti-unify over rule yields (MDL-gated), generic word tokenizer ──");
        int half = tokens.Count / 2;
        var train = tokens.Take(Math.Max(1, tokens.Count - half)).ToArray();
        var heldout = tokens.Skip(Math.Max(1, tokens.Count - half)).ToArray();
        var (rows, model) = AntiUnify.GrowthLoop(train, heldout, maxIter: iter);
        double pay = 0;
        if (model.SlotCount > 0)
        {
            var flat = AntiUnify.TwoPartMdl(train, train, new Dictionary<string, string>(StringComparer.Ordinal));
            var slottedCorpus = new List<List<string>>(train.Length);
            foreach (var s in train) slottedCorpus.Add(ApplySlots(s, model.MemberToSlot));
            var slotted = AntiUnify.TwoPartMdl(train, slottedCorpus, model.MemberToSlot);
            pay = flat.Total - slotted.Total;
        }
        double lastAbs = rows.Count > 0 ? rows[^1].HeldoutAbstract : 0;
        Trace.Note($"    {model.SlotCount} slots · tower depth {model.MaxDepth()} · minting pays {pay:F0} bits · held-out abstract-rule coverage {100 * lastAbs:F0}%");
        Trace.Note("    discovered slot families (by member count):");
        foreach (var (name, mem) in model.SlotMembers.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(top))
            Trace.Note($"      {name,-8} = {{{string.Join(" ", mem.Take(10).Select(m => Escape(m, 16)))}{(mem.Count > 10 ? " …" : "")}}} ({mem.Count})");
        Trace.Note("");

        // the frame census — the def/call emergence read (highest-diversity frames = the paradigmatic slots)
        var frames = FrameCensus(tokens);
        Trace.Note("    the def/call EMERGENCE read — highest-diversity trigram frames `left ___ right` (the blur does NOT know what a function is):");
        foreach (var f in frames.Take(top))
            Trace.Note($"      «{Escape(f.Left, 16)}» ___ «{Escape(f.Right, 16)}»  → {f.Diversity} fillers, {f.Fires} fires  e.g. {{{string.Join(" ", f.Fillers.Take(6).Select(m => Escape(m, 14)))}}}");
        Trace.Note("");

        // ── (3) TRANSFORM-SLOTS — the offset op over the slot members + the frame fillers ──
        var offMembers = new List<string>();
        foreach (var mem in model.SlotMembers.Values) offMembers.AddRange(mem.Where(m => !m.StartsWith('[')));
        foreach (var f in frames.Take(top * 2)) offMembers.AddRange(f.Fillers);
        var offsets = OffsetSlots(offMembers);
        Trace.Note("  ── (3) TRANSFORM-SLOTS (v0 op = {offset}) — members related by a constant byte offset (transposition invariance) ──");
        Trace.Note($"    {offsets.Count} offset-orbits over {offMembers.Distinct().Count()} candidate members");
        foreach (var o in offsets.Take(top))
            Trace.Note($"      base «{Escape(o.Base, 18)}» + offsets [{string.Join(",", o.Offsets)}] → {{{string.Join(" ", o.Members.Take(6).Select(m => Escape(m, 14)))}}} (saves {o.SavedBytes}B)");
        Trace.Note("");

        // ── the anti-Goodhart null — held-out ΔMDL residual-shrinkage vs the marginal-filler null, pay-floor-gated ──
        Trace.Note("  ── ANTI-GOODHART NULL — real ΔMDL pay vs the marginal-filler null (the graded survivor) ──");
        var nulls = MarginalFillerNull(train, model, seed);
        int survived = nulls.Count(t => t.Survives);
        foreach (var t in nulls.OrderByDescending(t => t.RealPay - t.NullPay).Take(top))
            Trace.Note($"      {t.Slot,-8}  real {t.RealPay,10:F0}  null {t.NullPay,10:F0}  Δ {t.RealPay - t.NullPay,10:F0}  {(t.Survives ? "SURVIVES" : "goodhart")}");
        Trace.Note($"    {survived}/{nulls.Count} slots beat their marginal-filler null (pay-floor gated) — these are the slots that count as rule-exercise in Tier 1.5");
        Trace.Note("");

        // ── TIER 1.5 — slot-aware reflection (the depth cure), the behavior change, gated on --reflect ──
        if (Args.Has(args, "--reflect")) ReflectDemo(seed);

        // ── land the report ──
        var sb = new StringBuilder();
        sb.AppendLine($"source\t{label}");
        sb.AppendLine($"bytes\t{corpus.Length}");
        sb.AppendLine($"byte_rules\t{g.Rules.Length}");
        sb.AppendLine($"towers\t{census.Towers}\tmax_height\t{census.MaxHeight}\tdeepest_span\t{census.DeepestSpan}");
        sb.AppendLine($"slots\t{model.SlotCount}\tpay_bits\t{pay:F0}\theldout_abstract\t{lastAbs:F4}");
        sb.AppendLine($"offset_orbits\t{offsets.Count}");
        sb.AppendLine($"null_survivors\t{survived}\tof\t{nulls.Count}");
        run.Write("blur.tsv", sb.ToString());

        Trace.Note("  ── VERDICT (report what emerged, honestly) ──");
        Trace.Note($"    (1) knot substrate : {census.Towers} doubling towers, deepest {census.DeepestSpan}B unroll (loops fossilized as O(log N) towers)");
        Trace.Note($"    (2) alternation    : {model.SlotCount} slots, pays {pay:F0} bits; top frame diversity {(frames.Count > 0 ? frames[0].Diversity : 0)} — the emergence read is above (def/call if the top frames are `def ___ (` shaped)");
        Trace.Note($"    (3) transform      : {offsets.Count} byte-offset orbits (transposition invariance; fires for byte-aligned codebooks, sparse for text)");
        Trace.Note($"    anti-Goodhart      : {survived}/{nulls.Count} slots beat the marginal-filler null");
        return 0;
    }

    /// Apply the discovered slots to a sentence (chase the tower to a fixpoint) — the local twin of AntiUnify's
    /// private ApplySlots (kept here so the probe needs no friend access to that internal).
    private static List<string> ApplySlots(IReadOnlyList<string> sentence, Dictionary<string, string> m2s)
    {
        var s = new List<string>(sentence);
        bool changed = true;
        while (changed) { changed = false; for (int i = 0; i < s.Count; i++) if (m2s.TryGetValue(s[i], out var slot)) { s[i] = slot; changed = true; } }
        return s;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  TIER 1.5 DEMONSTRATION — slot-aware reflection is the DEPTH CURE, proven on a CONTROLLED two-source mesh.
    //  node0 and node1 emit the SAME long deep frame with SOURCE-SPECIFIC fillers → cross-source SLOT-MATES (same
    //  skeleton, different filler, each rule single-source); plus source-private distractor deep phrases with NO
    //  cross-source mate. slots-OFF a mate reflects only if a peer re-derived its EXACT surface (never — the filler
    //  differs per source); slots-ON it reflects because a peer exercised its slot-PATTERN → the deep cure fires.
    //  Anti-Goodhart: the real slot structure must credit MORE deep rules than pooling RANDOM floor-rules (the null
    //  tests the detector's SELECTION, not just its grouping). A same-corpus arbitrary split CANNOT test this —
    //  "source" must be an INDEPENDENT generator; only this construction, or the exit gate's real corroborated-mesh
    //  P1/P2, provides it. Slots-OFF is byte-identical to the pre-6.2 audit by construction (SlotJewels null ⇒
    //  Corroborate's `?? JewelSources` fallback; verify-weighted's frozen baseline is unaffected).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static void ReflectDemo(ulong seed)
    {
        Trace.Note("  ── TIER 1.5 — slot-aware reflection (the DEPTH CURE): a CONTROLLED two-source proof ──");
        string[] fillA = { "aardvark", "alpaca", "antelope", "armadillo", "anteater", "albatross", "axolotl", "abalone" };
        string[] fillB = { "buffalo", "badger", "beaver", "bison", "boarhog", "buzzard", "barracuda", "bumblebee" };
        const string frame = "quietly slept right through the long cold winter night inside the sheltered";   // a long shared skeleton → deep rules
        string[] distA = { "quantum entanglement plainly defies every notion of strictly local hidden realism",
                           "the ancient riverside public library quietly burned down one grey overcast autumn",
                           "distant heavy thunderclouds slowly gathered over the abandoned coastal lighthouse before dawn" };
        string[] distB = { "photosynthesis steadily converts incoming sunlight into stored chemical energy within green chloroplasts",
                           "the wandering foreign merchant caravan finally crossed the endless shifting desert dunes at midnight",
                           "molten iron churns slowly and endlessly beneath the fractured drifting continental crust today" };

        var tape = new Tape();
        void Emit(string s, string src, int rep) { var by = Encoding.UTF8.GetBytes(s); for (int i = 0; i < rep; i++) tape.Append((byte[])by.Clone(), src, Provenances.Replay); }
        foreach (var an in fillA) Emit($"the {an} {frame} den", "node0", 6);   // node0's animal frames
        foreach (var bn in fillB) Emit($"the {bn} {frame} den", "node1", 6);   // node1's animal frames — SLOT-MATES of node0's
        foreach (var d in distA) Emit(d, "node0", 6);                          // node0-private distractors (no cross-source mate)
        foreach (var d in distB) Emit(d, "node1", 6);

        var g = Engine.Induce(tape).Result;
        uint alpha = g.AlphabetSize;
        int n = g.Rules.Length;
        var (mates, classes, _) = DetectRuleSlots(g.Rules, alpha);
        int pooled = 0, biggest = 0; for (int r = 0; r < n; r++) if (mates[r] is { } m) { pooled++; if (m.Length > biggest) biggest = m.Length; }

        var offAudit  = Pearl.Audit(tape, g, 1, crossReflect: true);
        var onAudit   = Pearl.Audit(tape, g, 1, crossReflect: true, slotMates: mates);
        var nullMates = RandomFloorMates(mates, g.Rules, alpha, n, seed ^ 0x51070000UL);
        var nullAudit = Pearl.Audit(tape, g, 1, crossReflect: true, slotMates: nullMates);

        // rule depth (RenormStats recurrence) + expLen for the reflect floor.
        var expLen = Engine.ExpLens(g.Rules, alpha);
        var depth = new int[n];
        for (int i = 0; i < n; i++) { int d = 0; foreach (var s in g.Rules[i].Pattern) if (s.Value >= alpha && (int)(s.Value - alpha) < i) d = Math.Max(d, depth[(int)(s.Value - alpha)]); depth[i] = d + 1; }

        static bool Reflects(HashSet<string>?[]? own, HashSet<string>?[]? slot, int r)
            => ((slot?[r]) ?? own?[r]) is { Count: >= 2 };   // ≥2 distinct sources = cross-corroborated (node0 ⊥ node1)

        int maxD = 1; for (int i = 0; i < n; i++) if (expLen[i] >= Pearl.ReflectFloorBytes && depth[i] > maxD) maxD = depth[i];
        Trace.Note($"    shared grammar {n} rules · slot-classes {classes} ({pooled} rules pooled, biggest class {biggest}) · node0 {fillA.Length} animal-frames + {distA.Length} distractors ⊥ node1 likewise");
        Trace.Note($"    depth |  floor-rules |  reflect OFF |  reflect ON  |  Δ (deep cure)");
        int totOff = 0, totOn = 0, deepGain = 0, nullDeepGain = 0;
        for (int d = 1; d <= maxD; d++)
        {
            int fr = 0, ro = 0, rn = 0;
            for (int r = 0; r < n; r++)
            {
                if (expLen[r] < Pearl.ReflectFloorBytes || depth[r] != d) continue;
                fr++;
                bool off = Reflects(offAudit.JewelSources, null, r);
                bool on  = Reflects(onAudit.JewelSources, onAudit.SlotJewels, r);
                if (off) ro++;
                if (on) rn++;
                if (on && !off && d >= 3) deepGain++;
                if (Reflects(nullAudit.JewelSources, nullAudit.SlotJewels, r) && !off && d >= 3) nullDeepGain++;
            }
            totOff += ro; totOn += rn;
            if (fr > 0) Trace.Note($"    {d,5} |  {fr,11} |  {ro,11} |  {rn,11} |  {rn - ro,+5}");
        }
        Trace.Note($"    TOTAL floor-rules reflected: OFF {totOff} → ON {totOn} (Δ {totOn - totOff:+0}); deep (depth≥3) newly-reflected: REAL {deepGain} vs RANDOM-floor null {nullDeepGain}");
        Trace.Note($"    parity: slots-OFF audit byte-identical to the pre-6.2 audit by construction (SlotJewels null ⇒ Corroborate's ?? JewelSources fallback; verify-weighted frozen baseline unaffected)");
        Trace.Note($"    anti-Goodhart (Tier 1.5): the detected slot structure must credit MORE deep rules than random-floor pooling — {(deepGain > nullDeepGain ? "PASS" : deepGain == 0 ? "no deep gain" : "SUSPECT (real ≤ null)")} (real {deepGain} vs null {nullDeepGain})");
        Trace.Note("    NOTE: a same-corpus split is NOT a valid test (source = an arbitrary label). The real-mesh P1/P2 re-fire (exit gate, w-depth checkpoints) is the field test; this is the ground-truth kill-line.");
        Trace.Note("");
    }

    /// The RANDOM-FLOOR null: same class-SIZE distribution as the detected slot structure, but members drawn
    /// uniformly WITHOUT replacement from ALL floor-rules. This tests the detector's SELECTION — does pooling the
    /// rules it CHOSE credit more deep cross-source rules than pooling an equal number of arbitrary floor-rules?
    /// If random pooling gains as much, the "slots" carry no signal beyond the base pooling-inflation rate.
    private static int[]?[] RandomFloorMates(int[]?[] mates, GrammarRule[] rules, uint alpha, int n, ulong seed)
    {
        var nullMates = new int[]?[n];
        var sizes = new List<int>();
        var seenRoot = new HashSet<int>();
        for (int r = 0; r < n; r++) if (mates[r] is { } m && seenRoot.Add(m[0])) sizes.Add(m.Length);
        if (sizes.Count == 0) return nullMates;
        var expLen = Engine.ExpLens(rules, alpha);
        var floorRules = new List<int>();
        for (int r = 0; r < n; r++) if (expLen[r] >= Pearl.ReflectFloorBytes) floorRules.Add(r);
        if (floorRules.Count < 2) return nullMates;
        var shuffled = Engine.Shuffled(floorRules.ToArray(), seed);
        int idx = 0;
        foreach (int sz in sizes)
        {
            if (idx + sz > shuffled.Length) break;
            var cls = new int[sz]; Array.Copy(shuffled, idx, cls, 0, sz); Array.Sort(cls); idx += sz;   // disjoint carve — no rule in two null classes
            foreach (int r in cls) nullMates[r] ??= cls;
        }
        return nullMates;
    }

    private static string BaseSurface(in RePairResult g, Symbol s)
        => Encoding.UTF8.GetString(Reconstruct.Expand(g.Rules, [s]));

    private static string Escape(string s, int cap)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(c == '\n' ? "\\n" : c == '\t' ? "\\t" : c == '\r' ? "\\r" : c.ToString());
        var r = sb.ToString();
        return r.Length > cap ? r[..cap] + "…" : r;
    }

    private static (byte[] Corpus, string Label) LoadSource(string source, int maxBytes)
    {
        byte[] bytes;
        string label;
        if (File.Exists(source)) { bytes = File.ReadAllBytes(source); label = Path.GetFileName(source); }
        else if (Directory.Exists(source))
        {
            var files = Directory.GetFiles(source).Where(f => !f.EndsWith(".bin") && !f.EndsWith(".sh")).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            using var ms = new MemoryStream();
            foreach (var f in files) { if (ms.Length >= maxBytes) break; ms.Write(File.ReadAllBytes(f)); ms.WriteByte((byte)'\n'); }
            bytes = ms.ToArray(); label = Path.GetFileName(source.TrimEnd('/')) + "/";
        }
        else { bytes = Encoding.UTF8.GetBytes(Builtin); label = "builtin"; }
        if (bytes.Length > maxBytes) bytes = bytes[..maxBytes];
        return (bytes, label);
    }

    // A repetitive builtin so `blur` runs with no source — code (def/call frames), a loop (a doubling tower), prose.
    private const string Builtin =
        "def add(a, b): return a + b\ndef mul(a, b): return a * b\ndef sub(a, b): return a - b\ndef div(a, b): return a / b\n" +
        "call add(1, 2)\ncall mul(3, 4)\ncall sub(5, 6)\ncall div(7, 8)\n" +
        "ababababababababababababababab\nababababababababababababababab\n" +
        "the quick brown fox jumps over the lazy dog\nthe slow green cat crawls under the busy log\n";
}
