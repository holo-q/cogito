namespace Cogito;

using System.Runtime.InteropServices;
using Cogito.Cas;
using Cogito.Grammar;
using Cogito.Induct;
using Cogito.Observe;
using Cogito.Codec;


// The reusable engine ops — tokenize, induce, build the event-sourced substrate, fingerprint the log.
// Shared by the gate (Pipeline) and the introspection verbs (Cli) so both drive the SAME deterministic
// engine — situational awareness reads exactly what the proof commits to, never a parallel reimplementation.

public static class Engine
{
    /// Standing order-1/order-2 model for repeated reads of one grammar. The old generation
    /// entry points rebuilt these successor tables on every call; action policies can ask for
    /// twelve samples against the same grammar, so keep the tables on the grammar identity and
    /// rebuild only when Re-Pair publishes a new rules/compressed pair.
    public sealed class MarkovModel
    {
        private GrammarRule[]? _rules;
        private Symbol[]? _compressed;
        private readonly List<Symbol> _sequence = new();
        private Dictionary<(uint, uint), List<uint>> _ctx2 = new();
        private Dictionary<uint, List<uint>> _ctx1 = new();
        private Dictionary<uint, Dictionary<uint, int>> _forward = new();
        private Dictionary<uint, int> _totals = new();

        public int Rebuilds { get; private set; }
        public int Hits { get; private set; }
        public int DeltaApplies { get; private set; }

        private void Ensure(in RePairResult grammar)
        {
            if (ReferenceEquals(_rules, grammar.Rules) && ReferenceEquals(_compressed, grammar.Compressed))
            {
                Hits++;
                return;
            }

            if (_compressed is not null)
            {
                ApplySequenceDelta(grammar.Compressed);
                _rules = grammar.Rules;
                _compressed = grammar.Compressed;
                DeltaApplies++;
                return;
            }

            _rules = grammar.Rules;
            _compressed = grammar.Compressed;
            _ctx2 = new Dictionary<(uint, uint), List<uint>>();
            _ctx1 = new Dictionary<uint, List<uint>>();
            _forward = new Dictionary<uint, Dictionary<uint, int>>();
            _totals = new Dictionary<uint, int>();
            Symbol[] sequence = grammar.Compressed;
            _sequence.AddRange(sequence);
            for (int i = 0; i + 1 < sequence.Length; i++)
            {
                uint a = sequence[i].Value;
                uint b = sequence[i + 1].Value;
                Add(_ctx1, a, b);
                if (i >= 1) Add(_ctx2, (sequence[i - 1].Value, a), b);

                if (!_forward.TryGetValue(a, out Dictionary<uint, int>? counts))
                    _forward[a] = counts = new();
                counts[b] = counts.GetValueOrDefault(b) + 1;
                _totals[a] = _totals.GetValueOrDefault(a) + 1;
            }
            Rebuilds++;
        }

        private void ApplySequenceDelta(Symbol[] next)
        {
            int prefix = 0;
            int common = Math.Min(_sequence.Count, next.Length);
            while (prefix < common && _sequence[prefix].Equals(next[prefix])) prefix++;
            int suffix = 0;
            while (suffix < _sequence.Count - prefix && suffix < next.Length - prefix
                && _sequence[_sequence.Count - 1 - suffix].Equals(next[next.Length - 1 - suffix])) suffix++;
            int removed = _sequence.Count - prefix - suffix;
            int inserted = next.Length - prefix - suffix;
            int oldEnd = prefix + removed;
            int firstOld = Math.Max(0, prefix - 2);
            int lastOld = Math.Min(_sequence.Count - 2, oldEnd);
            for (int i = firstOld; i <= lastOld; i++) RemoveTransition(i);
            _sequence.RemoveRange(prefix, removed);
            if (inserted > 0)
            {
                var replacement = new Symbol[inserted];
                Array.Copy(next, prefix, replacement, 0, inserted);
                _sequence.InsertRange(prefix, replacement);
            }
            int newEnd = prefix + inserted;
            int firstNew = Math.Max(0, prefix - 2);
            int lastNew = Math.Min(_sequence.Count - 2, newEnd);
            for (int i = firstNew; i <= lastNew; i++) AddTransition(i);
        }

        private void RemoveTransition(int i)
        {
            if ((uint)i >= (uint)(_sequence.Count - 1)) return;
            uint a = _sequence[i].Value, b = _sequence[i + 1].Value;
            Remove(_ctx1, a, b);
            if (i >= 1) Remove(_ctx2, (_sequence[i - 1].Value, a), b);
            if (_forward.TryGetValue(a, out Dictionary<uint, int>? counts))
            {
                if (counts[b] <= 1) counts.Remove(b); else counts[b]--;
                if (counts.Count == 0) _forward.Remove(a);
            }
            if (_totals[a] <= 1) _totals.Remove(a); else _totals[a]--;
        }

        private void AddTransition(int i)
        {
            if ((uint)i >= (uint)(_sequence.Count - 1)) return;
            uint a = _sequence[i].Value, b = _sequence[i + 1].Value;
            Add(_ctx1, a, b);
            if (i >= 1) Add(_ctx2, (_sequence[i - 1].Value, a), b);
            if (!_forward.TryGetValue(a, out Dictionary<uint, int>? counts)) _forward[a] = counts = new();
            counts[b] = counts.GetValueOrDefault(b) + 1;
            _totals[a] = _totals.GetValueOrDefault(a) + 1;
        }

        private static void Remove<TKey>(Dictionary<TKey, List<uint>> bags, TKey key, uint value) where TKey : notnull
        {
            if (!bags.TryGetValue(key, out List<uint>? list)) return;
            int at = list.IndexOf(value);
            if (at >= 0) list.RemoveAt(at);
            if (list.Count == 0) bags.Remove(key);
        }

        public byte[] GenerateFrom(in RePairResult grammar, int count, ulong seed, int seedIndex)
        {
            Ensure(in grammar);
            List<Symbol> sequence = _sequence;
            if (sequence.Count < 3) return Reconstruct.Expand(grammar.Rules, CollectionsMarshal.AsSpan(sequence));

            ulong rng = seed;
            uint Pick(List<uint> options)
            {
                rng = rng * 6364136223846793005UL + 1442695040888963407UL;
                return options[(int)((rng >> 33) % (ulong)options.Count)];
            }

            int j = Math.Clamp(seedIndex, 1, sequence.Count - 1);
            var symbols = new List<Symbol>(count + 2) { sequence[j - 1], sequence[j] };
            uint p1 = sequence[j - 1].Value, p0 = sequence[j].Value;
            for (int i = 0; i < count; i++)
            {
                uint next = _ctx2.TryGetValue((p1, p0), out List<uint>? o2) && o2.Count > 0 ? Pick(o2)
                    : _ctx1.TryGetValue(p0, out List<uint>? o1) && o1.Count > 0 ? Pick(o1)
                    : sequence[0].Value;
                symbols.Add(new Symbol(next));
                p1 = p0; p0 = next;
            }
            return Reconstruct.Expand(grammar.Rules, symbols.ToArray());
        }

        public byte[] GenerateMetabolic(in RePairResult grammar, int count, ulong seed, Metabolism metabolism)
        {
            Ensure(in grammar);
            List<Symbol> sequence = _sequence;
            if (sequence.Count < 3) return Reconstruct.Expand(grammar.Rules, CollectionsMarshal.AsSpan(sequence));

            ulong rng = seed;
            double NextDouble()
            {
                rng = rng * 6364136223846793005UL + 1442695040888963407UL;
                return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53);
            }
            uint Pick(List<uint> options)
            {
                double total = 0;
                for (int i = 0; i < options.Count; i++) total += metabolism.Weight(options[i]);
                if (total <= 0) { metabolism.Fired(options[0]); return options[0]; }
                double pick = NextDouble() * total;
                for (int i = 0; i < options.Count; i++)
                {
                    pick -= metabolism.Weight(options[i]);
                    if (pick <= 0) { metabolism.Fired(options[i]); return options[i]; }
                }
                uint last = options[^1]; metabolism.Fired(last); return last;
            }

            int j = 1;
            var symbols = new List<Symbol>(count + 2) { sequence[j - 1], sequence[j] };
            uint p1 = sequence[j - 1].Value, p0 = sequence[j].Value;
            for (int i = 0; i < count; i++)
            {
                uint next = _ctx2.TryGetValue((p1, p0), out List<uint>? o2) && o2.Count > 0 ? Pick(o2)
                    : _ctx1.TryGetValue(p0, out List<uint>? o1) && o1.Count > 0 ? Pick(o1)
                    : sequence[0].Value;
                symbols.Add(new Symbol(next));
                p1 = p0; p0 = next;
            }
            return Reconstruct.Expand(grammar.Rules, symbols.ToArray());
        }

        public byte[] GenerateMCMC(in RePairResult grammar, int length, int sweeps, ulong seed)
        {
            Ensure(in grammar);
            List<Symbol> sequence = _sequence;
            if (sequence.Count < 3) return Reconstruct.Expand(grammar.Rules, CollectionsMarshal.AsSpan(sequence));

            ulong rng = seed;
            double NextDouble()
            {
                rng = rng * 6364136223846793005UL + 1442695040888963407UL;
                return ((rng >> 11) & 0xFFFFFFFFFFFFFUL) / (double)(1UL << 52);
            }
            uint SampleForward(uint current)
            {
                if (!_forward.TryGetValue(current, out Dictionary<uint, int>? options) || options.Count == 0)
                    return sequence[0].Value;
                double pick = NextDouble() * _totals[current];
                foreach ((uint value, int count) in options)
                {
                    pick -= count;
                    if (pick <= 0) return value;
                }
                return options.Keys.First();
            }

            uint[] values = new uint[length];
            values[0] = sequence[0].Value;
            for (int i = 1; i < length; i++) values[i] = SampleForward(values[i - 1]);
            for (int sweep = 0; sweep < sweeps; sweep++)
                for (int i = 1; i < length; i++)
                {
                    if (!_forward.TryGetValue(values[i - 1], out Dictionary<uint, int>? options) || options.Count == 0) continue;
                    var weighted = new List<(uint X, double W)>(options.Count);
                    double total = 0;
                    foreach ((uint x, int forwardCount) in options)
                    {
                        double backward = i + 1 >= length
                            ? 1.0
                            : (_forward.TryGetValue(x, out Dictionary<uint, int>? next) && next.TryGetValue(values[i + 1], out int backwardCount) && _totals[x] > 0
                                ? (double)backwardCount / _totals[x]
                                : 0.001);
                        double weight = forwardCount * backward;
                        weighted.Add((x, weight));
                        total += weight;
                    }
                    if (total <= 0) continue;
                    double pick = NextDouble() * total;
                    foreach ((uint x, double weight) in weighted)
                    {
                        pick -= weight;
                        if (pick <= 0) { values[i] = x; break; }
                    }
                }

            var result = new Symbol[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = new Symbol(values[i]);
            return Reconstruct.Expand(grammar.Rules, result);
        }

        private static void Add<TKey>(Dictionary<TKey, List<uint>> bags, TKey key, uint value) where TKey : notnull
        {
            if (!bags.TryGetValue(key, out List<uint>? list)) bags[key] = list = new();
            list.Add(value);
        }
    }

    /// Byte-tokenize the corpus and run Re-Pair to fixpoint. Returns the tape (sized to `n`) and the result.
    /// The span barrier is ARMED on '\n': corpora reach here as newline-joined spans (Tape.Concat) or raw
    /// line-structured text, so the newline is the event boundary — no rule may straddle it (RePair's law).
    /// Straddling rules were the world-run's maxSpan monsters: cross-boundary coincidences that blew the
    /// memory budget (no single tape span covers them → undemotable rent) and self-blocked the greedy cover.
    /// Runs on a FRESH LOOM (splice-all + pump ≡ the linear RePair byte-for-byte — `verify-loom`'s
    /// batch-identity arm gates it against the RePair oracle): one merge kernel, batch = its degenerate use.
    public static (Symbol[] Tape, int N, RePairResult Result) Induce(byte[] corpus)
    {
        var tok = ByteTokenizer.Instance;
        var tape = new Symbol[tok.MaxSymbols(corpus.Length)];
        int n = tok.Tokenize(corpus, tape);
        return (tape, n, LoomBatch(corpus, default, 1, null));
    }

    /// The ONE batch induction body — a fresh Loom spliced whole (barrier-free runs as segments, '\n' runs as
    /// their trailing barrier counts — inert in the kernel, re-emitted in place at harvest) and pumped once.
    /// Segment weights are span-constant by the WeightsFor contract; barrier positions' weights are never read
    /// (barrier digrams are never counted), so `weights[start]` prices every segment exactly.
    private static RePairResult LoomBatch(byte[] corpus, ReadOnlySpan<byte> weights, int wScale, List<MergeEvent>? events)
    {
        using var loom = new Loom(256, '\n', wScale);
        long pseudo = 0;
        int i = 0;
        while (i < corpus.Length)
        {
            int start = i;
            while (i < corpus.Length && corpus[i] != (byte)'\n') i++;
            int runLen = i - start;
            int bars = 0;
            while (i < corpus.Length && corpus[i] == (byte)'\n') { bars++; i++; }
            loom.SpliceEvent(corpus.AsSpan(start, runLen), pseudo++, weights.IsEmpty ? (byte)wScale : weights[start], bars);
        }
        loom.Pump(events, Mbits.Zero);
        return loom.Result();
    }

    /// Induce like Induce(), but with the per-merge THOUGHT STREAM captured — every rule-birth appends a
    /// MergeEvent (the greedy salience + RG scale + Δmdl of each merge, in decision order). This is the
    /// introspection entry that ARMS the event seam Induce() leaves dark on the gate's hot path; deterministic
    /// induction ⟹ the thought stream replays byte-for-byte. Feeds `thoughtstream` and the self-model cluster.
    public static (Symbol[] Tape, int N, RePairResult Result, List<MergeEvent> Events) InduceTraced(byte[] corpus)
    {
        var tok = ByteTokenizer.Instance;
        var tape = new Symbol[tok.MaxSymbols(corpus.Length)];
        int n = tok.Tokenize(corpus, tape);
        var events = new List<MergeEvent>();
        var result = LoomBatch(corpus, default, 1, events);
        return (tape, n, result, events);
    }

    /// Induce over the TAPE under the provenance-weighted count measure: rent the per-byte weights
    /// (evidence wScale, unvested Replay 1 — Tape.WeightsFor), thread them into Re-Pair, return the buffer to
    /// the pool. wScale=1 takes the EXACT unweighted path (the degenerate arm — byte-identical to today by
    /// construction, not by arithmetic coincidence).
    public static (Symbol[] Tape, int N, RePairResult Result) Induce(Tape tape, int wScale)
    {
        Symbol[] symbols = BuildSymbolTape(tape, out int count);
        RePairResult result = LoomBatch(tape, wScale, null);
        return (symbols, count, result);
    }

    /// Induce the grammar intake view with the unweighted count measure.  The
    /// tape's custody view may contain measurement or custody-only packets;
    /// those remain available to validators but never become grammar symbols.
    public static (Symbol[] Tape, int N, RePairResult Result) Induce(Tape tape)
        => Induce(tape, 1);

    /// The traced twin of Induce(Tape, wScale) — the weighted count measure WITH the thought stream (the trunk's
    /// induce sites are all traced; the weighted grammar must feed the same self-model channel the unweighted did).
    public static (Symbol[] Tape, int N, RePairResult Result, List<MergeEvent> Events) InduceTraced(Tape tape, int wScale)
    {
        Symbol[] symbols = BuildSymbolTape(tape, out int count);
        List<MergeEvent> events = new();
        RePairResult result = LoomBatch(tape, wScale, events);
        return (symbols, count, result, events);
    }

    /// Induce directly over canonical tape events. Event boundaries come from Tape itself, never from scanning
    /// payload bytes for newline sentinels; binary packet bodies therefore cannot counterfeit a span boundary.
    private static RePairResult LoomBatch(Tape tape, int wScale, List<MergeEvent>? events)
    {
        using Loom loom = new(256, '\n', wScale);
        foreach (TapeEventView view in tape.GetGrammarEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] eventBytes))
                throw new InvalidDataException($"tape event {view.Id} did not resolve during batch induction");
            byte weight = view.Evidence ? (byte)wScale : (byte)1;
            loom.SpliceEvent(eventBytes, view.Id.Value, weight);
        }
        loom.Pump(events, Mbits.Zero);
        return loom.Result();
    }

    private static Symbol[] BuildSymbolTape(Tape tape, out int count)
    {
        if (tape.GrammarByteLength > int.MaxValue)
            throw new InvalidOperationException($"tape grammar view is {tape.GrammarByteLength}B, past the int-indexed induction ceiling");
        Symbol[] symbols = new Symbol[(int)tape.GrammarByteLength];
        count = 0;
        foreach (TapeEventView view in tape.GetGrammarEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] eventBytes))
                throw new InvalidDataException($"tape event {view.Id} did not resolve while building its symbol view");
            for (int index = 0; index < eventBytes.Length; index++) symbols[count++] = Symbol.Terminal(eventBytes[index]);
            symbols[count++] = Symbol.Terminal((byte)'\n');
        }
        return symbols;
    }

    /// Induce at TOKEN resolution: remap the corpus's distinct token-IDs to a dense 0..K-1 alphabet (the working
    /// vocabulary — an LLM tokenizer's sub-word "blur" over bytes) and run Re-Pair with that terminal boundary.
    /// The atoms are now sub-words, not bytes; the grammar abstracts over a coarser, pre-structured alphabet.
    /// NO span barrier here — a flat token stream carries no boundary info; a caller with span structure must
    /// pass the remapped ID of its boundary token as `barrier` to RePair itself.
    public static (int N, RePairResult Result) InduceTokens(uint[] tokens)
    {
        var map = new Dictionary<uint, uint>();
        var tape = new Symbol[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!map.TryGetValue(tokens[i], out var id)) { id = (uint)map.Count; map[tokens[i]] = id; }
            tape[i] = new Symbol(id);
        }
        uint k = (uint)map.Count;
        return (tokens.Length, new RePair().Induce(tape, Mbits.Zero, k));
    }

    /// Observe (one ObsTextEvent per line) → consolidate (one GrammarVersionEvent) over a fresh substrate.
    /// Returns the live store + log + the mutation packet (null at homeostasis). Deterministic ⟹ repeatable.
    public static (ContentStore Store, EventLog Log, GrammarVersionEvent? Gve) BuildLog(byte[] corpus)
    {
        var store = new ContentStore();
        var log = new EventLog(store);
        var tok = ByteTokenizer.Instance;

        foreach (var line in SplitLines(corpus))
        {
            var blob = TextBlob.Normalize(line.Span);
            var textRef = store.Put(blob.ToEnvelope());
            log.Append(new ObsTextEvent("corpus", textRef).ToEnvelope());
        }

        var gve = new Consolidator(store, tok).Consolidate(log, GrammarSpec.Null, Mbits.Zero);
        if (gve is { } e) log.Append(e.ToEnvelope());                    // the mutation packet, back into the log
        return (store, log, gve);
    }

    /// Fold every event's H_event into one digest — the log's replay fingerprint.
    public static Hash256 LogFingerprint(EventLog log)
    {
        var all = new byte[(int)log.Count * 32];
        long idx = 0;
        for (var id = EventID.Zero; idx < log.Count; id = id.Next, idx++)
            log.HashOf(id).AsSpan().CopyTo(all.AsSpan((int)idx * 32));
        return Hash.Domain("cogito/log_fingerprint/"u8, all);
    }

    /// Split a corpus on '\n' into per-line observations (the newline itself dropped; empty lines skipped).
    public static IEnumerable<ReadOnlyMemory<byte>> SplitLines(byte[] corpus)
    {
        int start = 0;
        for (int i = 0; i < corpus.Length; i++)
            if (corpus[i] == (byte)'\n')
            {
                if (i > start) yield return new ReadOnlyMemory<byte>(corpus, start, i - start);
                start = i + 1;
            }
        if (start < corpus.Length) yield return new ReadOnlyMemory<byte>(corpus, start, corpus.Length - start);
    }

    public static IEnumerable<ReadOnlyMemory<byte>> SplitLines(ReadOnlyMemory<byte> corpus)
    {
        int start = 0;
        for (int i = 0; i < corpus.Length; i++)
            if (corpus.Span[i] == (byte)'\n')
            {
                if (i > start) yield return corpus.Slice(start, i - start);
                start = i + 1;
            }
        if (start < corpus.Length) yield return corpus.Slice(start);
    }

    /// A grammar's COVER BASIS built ONCE — its rule expansions (len≥2) sorted longest-first, the greedy maximal
    /// cover order. The load-bearing perf seam: covering/parsing text against a grammar re-expands EVERY rule and
    /// re-sorts on each call, so a per-round FrontierPick over thousands of spans (or a held-out sweep over hundreds
    /// of lines) paid that O(rules·expansion) rebuild thousands of times. Build one GrammarCover, reuse it
    /// across the whole batch: the basis is built once, each text is
    /// only the greedy scan. Byte-identical to the old per-call statics (same expansions, same sort, same greedy
    /// rule); the statics below delegate here so there is one impl and callers-in-a-loop hoist the build.
    public sealed class GrammarCover
    {
        private readonly GrammarSequence? _shared;
        private readonly GrammarShape? _shape;

        /// maxExps > 0 keeps only the N LONGEST expansions — a faithful large-scale approximation for the frontier
        /// residual (coverage is dominated by the long, deep rules; the thousands of short rules barely change the
        /// span RANKING but blow scoring up to O(pool·span·rules)). 0 = keep all (the byte-exact intake path).
        public GrammarCover(GrammarRule[] rules, int maxExps = 0)
        {
            _shared = new GrammarSequence(rules, [], 256, maxExps);
        }

        /// Bind the cover to the publication plane owned by Cortex. The shape owns the
        /// sequence lifetime; after a reset it swaps that sequence in place, so every
        /// reader observes the same revision without rebuilding the expansion basis.
        public GrammarCover(GrammarShape shape)
        {
            _shape = shape ?? throw new ArgumentNullException(nameof(shape));
        }

        private GrammarSequence Sequence => _shape?.Sequence ?? _shared!;

        /// The cover BASIS — the (length desc, bytes asc)-sorted expansions the greedy cover scans. The
        /// FrontierIndex's candidate gather reads it (a span can score >0 ONLY by containing one of these);
        /// identity-stable per build, so callers key per-stride caches on the array reference. Never mutate.
        public byte[][] Expansions => Sequence.Expansions;

        /// The per-byte covered mask — which bytes of `text` the grammar explains (greedy longest-first cover).
        /// The UNCOVERED bytes are cogito's structural known-unknowns: what it has no rule for, read straight off
        /// the grammar — self-knowledge tied to capability (the same rules that generate), not a separate memo.
        public bool[] CoverMask(byte[] text)
            => Sequence.BuildCoverMask(text);

        /// The fraction of `text`'s bytes the grammar covers (greedy longest-first). The held-out version is the
        /// generalization signal: a grammar that learned the domain's STRUCTURE covers fresh domain text's
        /// scaffolding while its specific identifiers stay literal. Rising over rounds = generalizing, not memorizing.
        public double Coverage(byte[] text) => Sequence.ComputeCoverage(text);

        /// Greedy longest-match parse SIZE — the number of symbols `text` compresses to (each maximal rule match = 1
        /// symbol, each uncovered byte = 1). LOWER = deeper = more structure learned. The depth read byte-COVERAGE
        /// cannot give: a family's WORDS cover a held-out line's bytes (coverage ≈ 100%), but only DEEP
        /// (phrase/template) rules shrink its SYMBOL count. The MDL-native metric.
        public int ParsedSize(ReadOnlySpan<byte> text) => Sequence.ComputeParsedSize(text);

        /// ParsedSize normalized per byte — the depth read on a common scale (1.0 = no rule covered any of it →
        /// pure bytes; →0 = the text parsed into deep named subroutines). The held-out compression metric: a
        /// deeper-developed grammar parses the SAME held-out corpus into fewer symbols/byte. Empty text → 1.0
        /// (nothing to compress, maximally "shallow" by convention).
        public double ParsedSizePerByte(byte[] text) => text.Length == 0 ? 1.0 : (double)ParsedSize(text) / text.Length;
    }

    // Thin one-shot delegates — correct for a single text; a caller in a LOOP over one grammar must build a
    // GrammarCover once and reuse it (Radula.FrontierPick, CritLock) or it re-pays the O(rules·expansion) build.
    public static double CoverageOf(GrammarRule[] rules, byte[] text) => new GrammarCover(rules).Coverage(text);
    public static bool[] CoverMask(GrammarRule[] rules, byte[] text) => new GrammarCover(rules).CoverMask(text);
    public static int ParsedSize(GrammarRule[] rules, byte[] text) => new GrammarCover(rules).ParsedSize(text);

    /// Grammar CONCENTRATION — the Gini coefficient of the compressed sequence's chunk frequencies (0 = every
    /// chunk used equally, 1 = one chunk dominates). HIGH concentration = a few chunks dominate = the healthy
    /// Zipfian structure of a language; a DROP signals COLLAPSE toward uniform chunk-soup (verified on heal:
    /// the crash de-concentrates 0.38→0.20, the heal re-concentrates to 0.42). Moves WITH Zipf — both measure
    /// the distribution's inequality — so it confirms collapse, it does not lead it. A probe, never consensus.
    public static double ConcentrationOf(RePairResult r)
        => GrammarShape.ComputeConcentration(in r);

    // ── grammar-shape stats — the grok read, shared by every introspection verb ────────────────────────────
    // (abstraction depth + criticality exponent + scale-invariance). One impl here so the grok/renorm/intake
    // verbs all read the SAME numbers off the SAME engine, never a parallel reimplementation.

    /// Per-rule usage count — how many times each nonterminal is referenced (in the compressed tape + in other
    /// rules' patterns). The load-bearing read: a rule used once is dead weight, a rule used often is a concept.
    public static int[] RuleUses(RePairResult r) => GrammarShape.ComputeUses(r);

    /// Zipf slope of an explicit frequency list (log freq vs log rank) — the per-scale criticality exponent.
    public static double ZipfOf(IEnumerable<int> freqs)
    {
        var f = freqs.Where(x => x > 0).OrderByDescending(x => x).ToList();
        int k = f.Count;
        if (k < 3) return double.NaN;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < k; i++) { double x = Math.Log(i + 1), y = Math.Log(f[i]); sx += x; sy += y; sxx += x * x; sxy += x * y; }
        return (k * sxy - sx * sy) / (k * sxx - sx * sx);
    }

    /// The GROK read's shape. Scales = RG coarse-graining depth (how many levels of composition the grammar
    /// reached); MeanZ = the per-scale Zipf slope averaged (the −0.70 universality); CvZ = its variation across
    /// scales (LOW ⟹ the SAME power-law at every scale = a critical RG fixed point = the grok); MaxSpan = the
    /// deepest rule's byte extent (the correlation length); KZ = how many per-scale slopes ENTERED CvZ (only
    /// levels with ≥4 rules yield a slope, so KZ ≤ Scales — trunk_0106: Scales≈31, KZ≈12). KZ is the sample
    /// size of the CvZ estimate: its sampling sd is CV·√(1/(2k)+CV²/k), so any threshold on CvZ must be
    /// k-aware (RESULTS "correction": the flat 0.20 grok bar was this formula's k≈12 special case).
    public readonly record struct RenormStat(int Scales, double MeanZ, double CvZ, double MaxSpan, int KZ);

    /// Per-rule EXPANSION LENGTHS by child recurrence — O(rules + Σ|pattern|), ZERO byte materialization. The
    /// shape Pearl.Audit, Gc, and BuildIndex each re-derived by expanding every rule (O(Σ expansion) per night);
    /// hoisted to ONE verb so a length-only consumer never materializes a byte. Slot rules measure their
    /// REPRESENTATIVE (Pattern[0] — exactly what Reconstruct's tape-less read produces); demoted rules keep their
    /// Pattern and measure identically; a forward reference (defensive — emission order forbids it) counts 1.
    public static long[] ExpLens(GrammarRule[] rules, uint alphabetSize)
    {
        int n = rules.Length;
        var len = new long[n];
        for (int i = 0; i < n; i++)
        {
            var rule = rules[i];
            if (rule.IsSlot)
            {
                var m = rule.Pattern[0];
                len[i] = m.Value < alphabetSize || (int)(m.Value - alphabetSize) >= i ? 1 : len[(int)(m.Value - alphabetSize)];
            }
            else
            {
                long s = 0;
                foreach (var sym in rule.Pattern)
                    s += sym.Value < alphabetSize || (int)(sym.Value - alphabetSize) >= i ? 1 : len[(int)(sym.Value - alphabetSize)];
                len[i] = s;
            }
        }
        return len;
    }

    /// Per-rule composition depth + terminal span by the same recurrence RenormStats reads. Centralizing it keeps
    /// every depth-facing probe on one ruler instead of cloning the tower walk.
    public static (int[] Depth, int[] Span) RuleDepthSpan(in RePairResult g)
        => RuleDepthSpan(g.Rules, g.AlphabetSize);

    public static (int[] Depth, int[] Span) RuleDepthSpan(GrammarRule[] rules, uint alphabetSize)
        => GrammarShape.ComputeDepthSpan(rules, alphabetSize);

    /// Deterministic in-place Fisher-Yates permutation — the org's ONE shuffle primitive (integer PCG-LCG,
    /// `>> 33` mod-reduce). Every seeded permutation folds here: the null-model byte-shuffle (Scoreboard, `compress`'s
    /// permutation test) and the domain-order null (GrokBell / CritLock kill-lines). The seed is taken ALREADY-DERIVED
    /// — a caller XORs its own salt before the call, so the byte stream is the call site's to own (byte-exact with the
    /// pre-fold per-site loops — the Vow).
    public static void Shuffle<T>(T[] a, ulong seed)
    {
        ulong rng = seed;
        for (int i = a.Length - 1; i > 0; i--)
        {
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            int j = (int)((rng >> 33) % (ulong)(i + 1));
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    /// A shuffled COPY — the null-model shuffles permute a corpus they must not mutate, so they clone first.
    public static T[] Shuffled<T>(T[] src, ulong seed) { var a = (T[])src.Clone(); Shuffle(a, seed); return a; }

    /// The GROK read: a deep, scale-invariant grammar is the "grokked" state; a shallow one is a hoard.
    public static RenormStat RenormStats(RePairResult r) => GrammarShape.ComputeRenorm(r);

    /// Computes the same maximum terminal span published by RenormStats without building the other renormalization planes.
    public static int ComputeMaxSpan(in RePairResult g)
    {
        var spans = new int[g.Rules.Length];
        int maxSpan = 0;
        for (int i = 0; i < g.Rules.Length; i++)
        {
            int totalSpan = 0;
            foreach (var symbol in g.Rules[i].Pattern)
                if (symbol.Value >= g.AlphabetSize && symbol.Value - g.AlphabetSize < (uint)i)
                    totalSpan += spans[(int)(symbol.Value - g.AlphabetSize)];
                else totalSpan++;
            spans[i] = totalSpan;
            maxSpan = Math.Max(maxSpan, totalSpan);
        }
        return maxSpan;
    }

    /// Generate text from a grammar — a variable-order (order-2 → order-1 backoff) context walk over the
    /// compressed chunk-sequence, expanded to terminals. The grammar IS a generative model; this samples it.
    /// Deterministic (seeded LCG, integer-only). Unconditioned: the walk opens at the corpus start (seedIdx 1).
    public static byte[] Generate(RePairResult r, int count, ulong seed) => GenerateFrom(r, count, seed, 1);

    /// Generate CONDITIONED on a seed position — the chatter's response mechanism. Identical Markov walk to
    /// Generate, but the walk STARTS from chunk `seedIdx` of the compressed sequence instead of the opening.
    /// Append the prompt to the corpus and seed from the tail: the continuation then follows what typically
    /// comes AFTER the prompt's context (over the whole corpus), not the corpus's first line. The conditioning
    /// is purely WHERE the walk starts — the same grammar, entered at the prompt instead of the beginning.
    public static byte[] GenerateFrom(RePairResult r, int count, ulong seed, int seedIdx)
    {
        var s = r.Compressed;
        if (s.Length < 3) return Reconstruct.Expand(r.Rules, s);

        var ctx2 = new Dictionary<(uint, uint), List<uint>>();
        var ctx1 = new Dictionary<uint, List<uint>>();
        for (int i = 0; i + 1 < s.Length; i++)
        {
            Bag(ctx1, s[i].Value).Add(s[i + 1].Value);
            if (i >= 1) Bag(ctx2, (s[i - 1].Value, s[i].Value)).Add(s[i + 1].Value);
        }

        ulong rng = seed;
        uint Pick(List<uint> opts)
        {
            rng = rng * 6364136223846793005UL + 1442695040888963407UL;
            return opts[(int)((rng >> 33) % (ulong)opts.Count)];
        }

        int j = Math.Clamp(seedIdx, 1, s.Length - 1);                  // p0 = s[j], p1 = s[j-1]
        var seq = new List<Symbol>(count + 2) { s[j - 1], s[j] };
        uint p1 = s[j - 1].Value, p0 = s[j].Value;
        for (int i = 0; i < count; i++)
        {
            uint next = ctx2.TryGetValue((p1, p0), out var o2) && o2.Count > 0 ? Pick(o2)
                      : ctx1.TryGetValue(p0, out var o1) && o1.Count > 0 ? Pick(o1)
                      : s[0].Value;
            seq.Add(new Symbol(next));
            p1 = p0; p0 = next;
        }
        return Reconstruct.Expand(r.Rules, seq.ToArray());
    }

    /// Coherent generation by GIBBS sampling over the chunk-sequence — the energy-landscape upgrade of the
    /// greedy walk. Init with a forward walk, then sweep: resample each position from its full conditional
    /// P(x | left)·P(right | x), so every chunk must fit BOTH neighbors, not just the forward one. That
    /// bidirectional pressure is what buys global coherence (the greedy walk is forward-myopic → degenerates).
    /// Floats live only here (a sampler; deterministic + reproducible via the seed), never near consensus.
    public static byte[] GenerateMCMC(RePairResult r, int length, int sweeps, ulong seed)
    {
        var s = r.Compressed;
        if (s.Length < 3) return Reconstruct.Expand(r.Rules, s);

        var fwd = new Dictionary<uint, Dictionary<uint, int>>();
        var tot = new Dictionary<uint, int>();
        for (int i = 0; i + 1 < s.Length; i++)
        {
            uint a = s[i].Value, b = s[i + 1].Value;
            if (!fwd.TryGetValue(a, out var m)) fwd[a] = m = new();
            m[b] = m.GetValueOrDefault(b) + 1;
            tot[a] = tot.GetValueOrDefault(a) + 1;
        }

        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0xFFFFFFFFFFFFFUL) / (double)(1UL << 52); }
        uint SampleFwd(uint cur)
        {
            if (!fwd.TryGetValue(cur, out var m) || m.Count == 0) return s[0].Value;
            double pick = U() * tot[cur];
            foreach (var (k, c) in m) { pick -= c; if (pick <= 0) return k; }
            return m.Keys.First();
        }

        var seq = new uint[length];
        seq[0] = s[0].Value;
        for (int i = 1; i < length; i++) seq[i] = SampleFwd(seq[i - 1]);   // init: forward walk

        for (int sweep = 0; sweep < sweeps; sweep++)
            for (int i = 1; i < length; i++)
            {
                if (!fwd.TryGetValue(seq[i - 1], out var opts) || opts.Count == 0) continue;
                var ws = new List<(uint X, double W)>(opts.Count);
                double total = 0;
                foreach (var (x, fc) in opts)
                {
                    double bw = i + 1 >= length ? 1.0
                              : (fwd.TryGetValue(x, out var xm) && xm.TryGetValue(seq[i + 1], out var bc) && tot[x] > 0) ? (double)bc / tot[x] : 0.001;
                    double w = fc * bw; ws.Add((x, w)); total += w;
                }
                if (total <= 0) continue;
                double pick = U() * total;
                foreach (var (x, w) in ws) { pick -= w; if (pick <= 0) { seq[i] = x; break; } }
            }

        return Reconstruct.Expand(r.Rules, seq.Select(v => new Symbol(v)).ToArray());
    }

    private static List<uint> Bag<TKey>(Dictionary<TKey, List<uint>> d, TKey k) where TKey : notnull
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = new();
        return l;
    }
}
