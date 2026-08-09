namespace Cogito;

using System.Text;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE SELF-STREAM — cogito models ITSELF from its own reads (the MODEL phase)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

public enum MetaGrammarPredictionArms : byte { None, Grammar, Bigram, Unigram }

internal enum SelfStreamChannels : byte { Excursion, Thought }

/// One append-only self-model observation.  The token is the replay input;
/// the remaining fields are the typed corroboration captured at the fold boundary.
/// Thought batches mark their final token so replay preserves force-induction.
internal readonly record struct SelfStreamTokenReceipt(
    SelfStreamChannels Channel,
    string Token,
    string Prediction,
    string Actual,
    bool Hit,
    char Arm,
    int Minted,
    bool BatchEnd);

internal readonly record struct MetaGrammarStateReplacement(
    int Events,
    int Minted,
    int GramHits,
    int FallHits,
    string LastPrediction,
    string LastActual,
    bool LastHit,
    char LastArm,
    int LastInduceAt,
    int MetaTokenAlphabetSize,
    int[] MintWindow);

internal readonly record struct SelfStreamStateReplacement(
    MetaGrammarStateReplacement Excursion,
    MetaGrammarStateReplacement Thought);

internal readonly record struct SelfStreamCheckpointDelta(
    int Cursor,
    SelfStreamTokenReceipt[] Events,
    SelfStreamStateReplacement State)
{
    internal bool IsEmpty => (Events?.Length ?? 0) == 0;
}

/// A pure prediction read. Grammar predictions carry the content-addressed production that corroborated the
/// continuation and its accumulated support. UniqueWinner is false when another token tied the winning support.
public readonly struct MetaGrammarPrediction(
    string token,
    MetaGrammarPredictionArms arm,
    bool uniqueWinner,
    bool hasProduction,
    RuleID productionID,
    int support,
    int productionSupport)
{
    public string Token { get; } = token;
    public MetaGrammarPredictionArms Arm { get; } = arm;
    public bool UniqueWinner { get; } = uniqueWinner;
    public bool HasProduction { get; } = hasProduction;
    public RuleID ProductionID { get; } = productionID;
    public int Support { get; } = support;
    public int ProductionSupport { get; } = productionSupport;
}

/// A META-GRAMMAR of one self-signal stream — cogito modelling the grammar of its OWN dynamics, and PREDICTING
/// itself from it (Home.cs's named upgrade: "the richest home is the meta-grammar's own prediction, minting on the
/// residual"). It accretes a stream of descriptor TOKENS (HomeWatch excursions, or merge-event thought-tokens),
/// periodically induces a meta-grammar over the token-ATOM tape (Re-Pair at token resolution — each token is one
/// symbol, so the rules are recurring token SUBSEQUENCES = the routines in the dynamics), and predicts the next
/// token by LONGEST-CONTEXT MATCH over those rule motifs (the order-1 count-table is the fallback arm — Cli.meta's
/// localBg). Mint is on the RESIDUAL only: a miss is a genuine surprise (minted, residual 1); a predicted token
/// goes free (residual 0). As the meta-grammar accretes the motifs, prediction sharpens and the mint-rate DECAYS —
/// the home closing on itself. Deterministic: argmax ties break by token-id, induction cadence is token-count-fixed,
/// no RNG (same stream ⇒ same curve, the Vow). The gram/fallback hit split is the proof that the meta-grammar
/// predicts BEYOND the count-table — the point of the named upgrade.
public sealed class MetaGrammar
{
    private readonly struct MotifSupport(int support, int productionSupport, RuleID productionID)
    {
        public int Support { get; } = support;
        public int ProductionSupport { get; } = productionSupport;
        public RuleID ProductionID { get; } = productionID;
    }

    /// A ≤MaxOrder-symbol motif context, right-aligned with −1 padding (D = most recent; token ids are ≥0, so
    /// padding can never collide and contexts of different lengths stay distinct). The struct key replaces the
    /// per-rule×position `string.Join` keys RebuildMotifs allocated — same grouping, zero string churn.
    private readonly record struct MotifKey(int A, int B, int C, int D);

    private static MotifKey ContextKey(List<int> seq, int start, int len)
        => new(
            len > 3 ? seq[start + len - 4] : -1,
            len > 2 ? seq[start + len - 3] : -1,
            len > 1 ? seq[start + len - 2] : -1,
            seq[start + len - 1]);

    // ── the token stream, densely id'd (first-seen order — deterministic; the meta-grammar's terminal alphabet) ──
    private readonly Dictionary<string, int> _idOf = new();
    private readonly List<string> _vocab = new();               // id → token (the meta-grammar's terminal vocabulary)
    private readonly List<int> _hist = new();                   // the id sequence — the meta-grammar's corpus

    // ── the count-table FALLBACK (Cli.meta's localBg): order-1 prev→next + order-0 counts, maintained incrementally ──
    private readonly Dictionary<int, Dictionary<int, int>> _bigram = new();
    private readonly Dictionary<int, int> _unigram = new();

    // ── the meta-grammar's motif predictor: order-1..K context→next counts, rebuilt on re-induction from the RULE
    //    EXPANSIONS (the recurring routines the grammar discovered — the primary, long-context arm) ──
    private readonly Dictionary<MotifKey, Dictionary<int, MotifSupport>> _motif = new();
    private RePairResult _meta;
    private int _lastInduceAt = -1;
    private readonly List<int> _recordEnds = new();
    private bool _nextStartsRecord;
    private int _metaTokenAlphabetSize;

    // ── residual (mint) + accuracy accounting: a rolling window (the volatile self-signal) + the cumulative split ──
    private readonly Queue<int> _mintWin = new();               // last MintWin mint flags — the rolling rate
    private readonly List<int> _mintTrail = new();              // every mint flag in order — the land-time decay curve
    private int _events, _minted, _gramHits, _fallHits;         // _minted + _gramHits + _fallHits == _events (the identity)
    private string _lastPred = "", _lastActual = ""; private bool _lastHit; private char _lastArm = 'x';

    private const int MaxOrder = 4;      // longest context the motif predictor keys on
    private const int MinInduce = 4;     // below this many tokens there is nothing for a grammar to find
    private const int InduceEvery = 3;   // re-induce the meta-grammar every N new tokens (cheap — the token tape is tiny)
    private const int MaxHist = 4000;    // bound the history (a thought trace appends a full re-induction's merges per stride)
    private const int MintWin = 16;      // the rolling mint-rate window — the WeightController self-signal
    private const int MaxMintTrail = 65536;   // bound the decay trail — it fed only DecayCurve yet grew forever into every full Save; the curve reads the most recent window (trimmed in chunks so the RemoveRange is amortized)
    private const int MintTrailTrimChunk = 4096;

    /// Fold one token: PREDICT from the current model (past tokens only — no leakage), compare, mint on the
    /// residual, then LEARN (update the count-table + append). Re-induction is the caller's call (MaybeInduce) —
    /// a sparse channel re-induces per event, a batched thought channel once per trace. Returns the mint flag.
    public int Fold(string token)
        => FoldToken(token, _nextStartsRecord);

    /// Fold one complete record. Re-induction inserts a distinct terminal after every boundary, so no repeated
    /// digram can include a boundary and no production can span two records. Boundaries never enter the vocabulary.
    public void FoldRecord(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0) throw new ArgumentException("a meta-grammar record requires at least one token", nameof(tokens));
        for (int i = 0; i < tokens.Count; i++) FoldToken(tokens[i], i == 0);
        _recordEnds.Add(_hist.Count);
        _nextStartsRecord = true;
    }

    private int FoldToken(string token, bool startsRecord)
    {
        string pred = PredictCurrent(out char arm);   // "" = the model has nothing yet (cold start → a guaranteed miss)
        bool hit = pred.Length > 0 && pred == token;
        _lastPred = pred.Length == 0 ? "·" : pred; _lastActual = token; _lastHit = hit; _lastArm = hit ? arm : 'x';

        int mintFlag = hit ? 0 : 1;
        _events++;
        if (hit) { if (arm == 'g') _gramHits++; else _fallHits++; } else _minted++;
        _mintWin.Enqueue(mintFlag); while (_mintWin.Count > MintWin) _mintWin.Dequeue();
        _mintTrail.Add(mintFlag);
        if (_mintTrail.Count >= MaxMintTrail + MintTrailTrimChunk) _mintTrail.RemoveRange(0, _mintTrail.Count - MaxMintTrail);

        Learn(Intern(token), startsRecord);
        _nextStartsRecord = false;
        return mintFlag;
    }

    /// The STANDING forecast — the predicted NEXT token given everything folded so far (the very Predict the
    /// next Fold will resolve; a pure read, no state moves). 's sharpest finding: this forecast was
    /// computed and thrown away every step, the controller keeping only its error rate — this is the wire that
    /// keeps it (the Homeostat's Predict tier navigates it). "" = cold start (no forecast yet).
    public string Forecast(out char arm) => PredictCurrent(out arm);

    /// Predict the token following `token` without changing history or accounting. Policy distillation uses the
    /// typed arm to reject fallback proposals and certifies the production/support corroboration in the candidate.
    public MetaGrammarPrediction PredictFollowing(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        int tokenID;
        if (_idOf.TryGetValue(token, out tokenID))
        {
            Dictionary<int, MotifSupport>? motif;
            if (_motif.TryGetValue(new MotifKey(-1, -1, -1, tokenID), out motif) && motif.Count > 0)
                return ChooseMotifPrediction(motif);

            Dictionary<int, int>? bigram;
            if (_bigram.TryGetValue(tokenID, out bigram) && bigram.Count > 0)
                return ChooseCountPrediction(bigram, MetaGrammarPredictionArms.Bigram);
        }
        if (_unigram.Count > 0) return ChooseCountPrediction(_unigram, MetaGrammarPredictionArms.Unigram);
        return new MetaGrammarPrediction("", MetaGrammarPredictionArms.None, false, false, default, 0, 0);
    }

    // PREDICT the next token: longest-context match over the meta-grammar's motifs (arm 'g' — the primary), else
    // the order-1 count-table, else the most-frequent token (arm 'f' — the fallback), else "" (cold start).
    private string PredictCurrent(out char arm)
    {
        int recordStart = _recordEnds.Count == 0 ? 0 : _recordEnds[^1];
        arm = 'g';
        for (int L = Math.Min(MaxOrder, _hist.Count - recordStart); L >= 1; L--)
        {
            Dictionary<int, MotifSupport>? motif;
            if (_motif.TryGetValue(ContextKey(_hist, _hist.Count - L, L), out motif) && motif.Count > 0)
                return ChooseMotifPrediction(motif).Token;
        }
        arm = 'f';
        Dictionary<int, int>? bigram;
        if (_hist.Count > recordStart && _bigram.TryGetValue(_hist[^1], out bigram) && bigram.Count > 0)
            return _vocab[ArgMax(bigram)];
        if (_unigram.Count > 0) return _vocab[ArgMax(_unigram)];
        return "";
    }

    private void Learn(int actual, bool startsRecord)
    {
        if (_hist.Count > 0 && !startsRecord)
        {
            var b = _bigram.TryGetValue(_hist[^1], out var bb) ? bb : (_bigram[_hist[^1]] = new());
            b[actual] = b.GetValueOrDefault(actual) + 1;
        }
        _unigram[actual] = _unigram.GetValueOrDefault(actual) + 1;
        _hist.Add(actual);
        if (_hist.Count > MaxHist) TrimHistory(_hist.Count - MaxHist);
    }

    /// Re-induce the meta-grammar over the token-atom tape + rebuild the motif VOMM from its rule expansions.
    /// `force` overrides the cadence gate (a fresh thought trace warrants a re-induce). Cheap — the atoms are the
    /// distinct descriptor tokens (tens–thousands), and Re-Pair over them is the same O(n) induction, tiny n.
    public void MaybeInduce(bool force)
    {
        if (_hist.Count < MinInduce || (!force && _hist.Count - _lastInduceAt < InduceEvery)) return;
        _lastInduceAt = _hist.Count;

        Symbol[] tape = new Symbol[_hist.Count + _recordEnds.Count];
        int tapeAt = 0;
        int boundaryAt = 0;
        for (int i = 0; i < _hist.Count; i++)
        {
            tape[tapeAt++] = new Symbol((uint)_hist[i]);
            if (boundaryAt < _recordEnds.Count && _recordEnds[boundaryAt] == i + 1)
            {
                tape[tapeAt++] = new Symbol((uint)(_vocab.Count + boundaryAt));
                boundaryAt++;
            }
        }
        _metaTokenAlphabetSize = _vocab.Count;
        _meta = new RePair().Induce(tape, Mbits.Zero, (uint)(_vocab.Count + _recordEnds.Count));
        RebuildMotifs();
    }

    // the motif VOMM is a PURE function of _meta — rebuilt after every re-induction AND on checkpoint Load (the
    // serialized state is _meta; the derived table never hits the disk).
    private void RebuildMotifs()
    {
        _motif.Clear();
        List<int> exp = new();
        int[] uses = Engine.RuleUses(_meta);
        for (int i = 0; i < _meta.Rules.Length; i++)
        {
            exp.Clear();
            ExpandInto(new Symbol(_meta.AlphabetSize + (uint)i), exp);
            bool containsBarrier = false;
            for (int e = 0; e < exp.Count; e++) containsBarrier |= exp[e] >= _metaTokenAlphabetSize;
            if (containsBarrier) continue;
            int support = Math.Max(1, uses[i]);
            RuleID productionID = _meta.Rules[i].Id;
            for (int p = 1; p < exp.Count; p++)                        // each (context → next) inside a motif is a routine-continuation vote
                for (int L = 1; L <= MaxOrder && p - L >= 0; L++)
                {
                    MotifKey key = ContextKey(exp, p - L, L);
                    Dictionary<int, MotifSupport>? existing;
                    Dictionary<int, MotifSupport> motif = _motif.TryGetValue(key, out existing)
                        ? existing
                        : (_motif[key] = new Dictionary<int, MotifSupport>());
                    MotifSupport prior;
                    if (motif.TryGetValue(exp[p], out prior))
                    {
                        bool replaceCorroboration = support > prior.ProductionSupport
                                              || (support == prior.ProductionSupport
                                                  && CompareRuleIDs(productionID, prior.ProductionID) < 0);
                        RuleID corroboration = replaceCorroboration ? productionID : prior.ProductionID;
                        int corroborationSupport = replaceCorroboration ? support : prior.ProductionSupport;
                        motif[exp[p]] = new MotifSupport(prior.Support + support, corroborationSupport, corroboration);
                    }
                    else motif[exp[p]] = new MotifSupport(support, support, productionID);
                }
        }
    }

    /// CHECKPOINT — the meta-grammar's whole memory. The count-tables are serialized (NOT rebuilt from _hist —
    /// the history is TRIMMED at MaxHist while the counts accumulate over every event ever folded, so a
    /// hist-replay would silently forget the pre-trim evidence); the motif table is rebuilt from the serialized
    /// _meta grammar (a trim can also leave _meta induced over a hist that no longer exists, so the grammar
    /// itself is the only faithful source). Dictionaries key-sorted; ArgMax is iteration-order-independent, so
    /// the reload's different insertion order can never reach a prediction.
    public void Save(CkptWriter w)
    {
        w.I32(_vocab.Count);
        foreach (var t in _vocab) w.Str(t);
        w.I32(_hist.Count);
        foreach (var h in _hist) w.I32(h);
        w.I32(_recordEnds.Count);
        foreach (int recordEnd in _recordEnds) w.I32(recordEnd);
        w.Bool(_nextStartsRecord);
        w.I32(_unigram.Count);
        foreach (var k in _unigram.Keys.Order()) { w.I32(k); w.I32(_unigram[k]); }
        w.I32(_bigram.Count);
        foreach (var k in _bigram.Keys.Order())
        {
            w.I32(k);
            var b = _bigram[k];
            w.I32(b.Count);
            foreach (var k2 in b.Keys.Order()) { w.I32(k2); w.I32(b[k2]); }
        }
        Checkpoint.WriteQueue(w, _mintWin);
        w.I32(_mintTrail.Count);
        foreach (var f in _mintTrail) w.U8((byte)f);
        w.I32(_events); w.I32(_minted); w.I32(_gramHits); w.I32(_fallHits);
        w.Str(_lastPred); w.Str(_lastActual); w.Bool(_lastHit); w.U8((byte)_lastArm);
        w.I32(_lastInduceAt);
        w.I32(_metaTokenAlphabetSize);
        bool hasMeta = _meta.Rules is not null;
        w.Bool(hasMeta);
        if (hasMeta) Checkpoint.WriteGrammar(w, _meta);
    }

    public void Load(CkptReader r)
    {
        _vocab.Clear(); _idOf.Clear();
        int nv = r.I32();
        for (int i = 0; i < nv; i++) { var t = r.Str(); _idOf[t] = _vocab.Count; _vocab.Add(t); }
        _hist.Clear();
        int nh = r.I32();
        for (int i = 0; i < nh; i++) _hist.Add(r.I32());
        _recordEnds.Clear();
        int nr = r.I32();
        int priorRecordEnd = 0;
        for (int i = 0; i < nr; i++)
        {
            int recordEnd = r.I32();
            if (recordEnd <= priorRecordEnd || recordEnd > _hist.Count)
                throw new InvalidDataException($"invalid meta-grammar record boundary {recordEnd}");
            _recordEnds.Add(recordEnd);
            priorRecordEnd = recordEnd;
        }
        _nextStartsRecord = r.Bool();
        _unigram.Clear();
        int nu = r.I32();
        for (int i = 0; i < nu; i++) { int k = r.I32(); _unigram[k] = r.I32(); }
        _bigram.Clear();
        int nb = r.I32();
        for (int i = 0; i < nb; i++)
        {
            int k = r.I32();
            var b = _bigram[k] = new Dictionary<int, int>();
            int nb2 = r.I32();
            for (int j = 0; j < nb2; j++) { int k2 = r.I32(); b[k2] = r.I32(); }
        }
        Checkpoint.ReadQueue(r, _mintWin);
        _mintTrail.Clear();
        int nt = r.I32();
        for (int i = 0; i < nt; i++) _mintTrail.Add(r.U8());
        _events = r.I32(); _minted = r.I32(); _gramHits = r.I32(); _fallHits = r.I32();
        _lastPred = r.Str(); _lastActual = r.Str(); _lastHit = r.Bool(); _lastArm = (char)r.U8();
        _lastInduceAt = r.I32();
        _metaTokenAlphabetSize = r.I32();
        if (r.Bool()) { _meta = Checkpoint.ReadGrammar(r); RebuildMotifs(); }
    }

    private void ExpandInto(Symbol s, List<int> into)
    {
        if (s.Value < _meta.AlphabetSize) { into.Add((int)s.Value); return; }
        foreach (var p in _meta.Rules[(int)(s.Value - _meta.AlphabetSize)].Pattern) ExpandInto(p, into);
    }

    private int Intern(string token)
    {
        if (!_idOf.TryGetValue(token, out var id)) { id = _vocab.Count; _idOf[token] = id; _vocab.Add(token); }
        return id;
    }

    private void TrimHistory(int removeCount)
    {
        _hist.RemoveRange(0, removeCount);
        int writeAt = 0;
        for (int i = 0; i < _recordEnds.Count; i++)
        {
            int shifted = _recordEnds[i] - removeCount;
            if (shifted <= 0) continue;
            _recordEnds[writeAt++] = shifted;
        }
        if (writeAt < _recordEnds.Count) _recordEnds.RemoveRange(writeAt, _recordEnds.Count - writeAt);
        _lastInduceAt = -1;
    }

    private MetaGrammarPrediction ChooseMotifPrediction(Dictionary<int, MotifSupport> candidates)
    {
        int bestToken = -1;
        int bestSupport = -1;
        bool unique = true;
        RuleID productionID = default;
        foreach (KeyValuePair<int, MotifSupport> candidate in candidates)
        {
            int tokenID = candidate.Key;
            MotifSupport support = candidate.Value;
            if (support.Support > bestSupport || (support.Support == bestSupport && tokenID < bestToken))
            {
                unique = support.Support != bestSupport;
                bestToken = tokenID;
                bestSupport = support.Support;
                productionID = support.ProductionID;
            }
            else if (support.Support == bestSupport) unique = false;
        }
        return new MetaGrammarPrediction(
            _vocab[bestToken], MetaGrammarPredictionArms.Grammar, unique, true, productionID, bestSupport,
            candidates[bestToken].ProductionSupport);
    }

    private MetaGrammarPrediction ChooseCountPrediction(
        Dictionary<int, int> candidates,
        MetaGrammarPredictionArms arm)
    {
        int bestToken = -1;
        int bestSupport = -1;
        bool unique = true;
        foreach (KeyValuePair<int, int> candidate in candidates)
        {
            if (candidate.Value > bestSupport || (candidate.Value == bestSupport && candidate.Key < bestToken))
            {
                unique = candidate.Value != bestSupport;
                bestToken = candidate.Key;
                bestSupport = candidate.Value;
            }
            else if (candidate.Value == bestSupport) unique = false;
        }
        return new MetaGrammarPrediction(_vocab[bestToken], arm, unique, false, default, bestSupport, 0);
    }

    private static int CompareRuleIDs(RuleID left, RuleID right)
        => left.Hash.AsSpan().SequenceCompareTo(right.Hash.AsSpan());

    // deterministic argmax — highest count, tie-break smallest id (result independent of Dictionary iteration order).
    private static int ArgMax(Dictionary<int, int> m)
    {
        int best = -1, bestC = -1;
        foreach (var (k, c) in m) if (best < 0 || c > bestC || (c == bestC && k < best)) { bestC = c; best = k; }
        return best;
    }

    // ── the reads ──
    public double MintRate => _mintWin.Count == 0 ? 0.0 : (double)_mintWin.Sum() / _mintWin.Count;   // rolling — the self-signal
    public double HitRate  => _events == 0 ? 0.0 : (double)(_gramHits + _fallHits) / _events;          // cumulative predictor accuracy
    public int Events => _events;
    public int Minted => _minted;
    public int GramHits => _gramHits;
    public int FallHits => _fallHits;
    public int RuleCount => _meta.Rules?.Length ?? 0;
    public string LastPred => _lastPred;
    public string LastActual => _lastActual.Length == 0 ? "·" : _lastActual;
    public bool LastHit => _lastHit;
    public char LastArm => _lastArm;

    internal MetaGrammarStateReplacement CaptureCheckpointState()
        => new(_events, _minted, _gramHits, _fallHits, _lastPred, _lastActual, _lastHit, _lastArm,
            _lastInduceAt, _metaTokenAlphabetSize, _mintWin.ToArray());

    internal void ValidateCheckpointState(in MetaGrammarStateReplacement expected)
    {
        MetaGrammarStateReplacement actual = CaptureCheckpointState();
        if (actual.Events != expected.Events || actual.Minted != expected.Minted
            || actual.GramHits != expected.GramHits || actual.FallHits != expected.FallHits
            || !string.Equals(actual.LastPrediction, expected.LastPrediction, StringComparison.Ordinal)
            || !string.Equals(actual.LastActual, expected.LastActual, StringComparison.Ordinal)
            || actual.LastHit != expected.LastHit || actual.LastArm != expected.LastArm
            || actual.LastInduceAt != expected.LastInduceAt
            || actual.MetaTokenAlphabetSize != expected.MetaTokenAlphabetSize
            || !actual.MintWindow.AsSpan().SequenceEqual(expected.MintWindow))
            throw new InvalidDataException("self-stream checkpoint replay diverged from its typed state replacement");
    }

    /// The mint-rate decay across the run, windowed into buckets — the KILL-LINE readout (must fall as the
    /// meta-grammar learns; a flat 100% is a broken predictor). Each bucket = the residual fraction over its slice.
    public string DecayCurve(int buckets = 8)
    {
        if (_mintTrail.Count == 0) return "(no events)";
        int per = Math.Max(1, _mintTrail.Count / buckets);
        var sb = new StringBuilder();
        for (int lo = 0; lo < _mintTrail.Count; lo += per)
        {
            int hi = Math.Min(_mintTrail.Count, lo + per), mint = 0;
            for (int i = lo; i < hi; i++) mint += _mintTrail[i];
            sb.Append($"{100 * mint / (hi - lo),3}% ");
        }
        return sb.ToString().TrimEnd();
    }

    /// The top motif-rules by usage, rendered in cogito's own descriptor vocabulary — the machine's dynamics
    /// vocabulary (the recurring routines it discovered in its own excursions / cognition).
    public IEnumerable<string> TopMotifs(int n)
    {
        if (_meta.Rules is null || _meta.Rules.Length == 0) yield break;
        var uses = Engine.RuleUses(_meta);
        string[] vocab = new string[_meta.AlphabetSize];
        for (int i = 0; i < _metaTokenAlphabetSize; i++) vocab[i] = _vocab[i];
        for (int i = _metaTokenAlphabetSize; i < vocab.Length; i++) vocab[i] = "|";
        foreach (var i in Enumerable.Range(0, _meta.Rules.Length).OrderByDescending(i => uses[i]).ThenBy(i => i).Take(n))
            yield return $"\"{MergeTrace.Render(_meta, i, vocab)}\" ×{uses[i]}";
    }
}

/// THE SELF-STREAM (MODEL phase) — cogito modelling its OWN dynamics from its reads and PREDICTING itself, minting
/// only the residual (the genuine surprises). It runs TWO channels, each its own MetaGrammar with its own mint-rate
///: the EXCURSION channel over HomeWatch's tokens (which probes left their
/// homeostatic comfort zone — performance/surprise dynamics) and the THOUGHT channel over the INDUCE phase's
/// merge-event stream (MergeTrace.EncodeEvents — cogito's cognition, the strange loop). The excursion mint-rate is the
/// self-signal the WeightController reads.
public sealed class SelfStream
{
    private readonly MetaGrammar _exc = new();   // the excursion channel — HomeWatch performance/surprise dynamics
    private readonly MetaGrammar _tht = new();   // the thought channel — merge-event cognition (the strange loop)
    private const int MaxCheckpointEvents = 1_000_000;
    private const int MaxTokenBytes = 64 * 1024;
    private readonly List<SelfStreamTokenReceipt> _checkpointEvents = new();
    private int _checkpointEventCursor;

    /// Fold this step's excursion token; returns the residual (1 = a genuine surprise the model missed, minted; 0 =
    /// predicted/free/quiet). "" = a quiet step (no probe left home) — no dynamical event, no fold.
    public double Observe(string excursionToken)
    {
        if (excursionToken.Length == 0) return 0.0;
        int mint = _exc.Fold(excursionToken);
        _exc.MaybeInduce(force: false);
        AppendCheckpointEvent(new(SelfStreamChannels.Excursion, excursionToken, _exc.LastPred, _exc.LastActual,
            _exc.LastHit, _exc.LastArm, mint, false));
        return mint;
    }

    /// Fold the INDUCE phase's merge-event thought-tokens (the second self-signal channel). A whole trace arrives
    /// per re-induction, so fold the batch then re-induce the thought meta-grammar once. `alphabetSize` is the tape's
    /// terminal boundary (256 for the byte tape) so MergeTrace.EncodeEvents reads the climb-class of each merge.
    public int ObserveThought(IReadOnlyList<MergeEvent> events, uint alphabetSize)
    {
        if (events.Count == 0) return 0;
        int minted = 0;
        var tokens = MergeTrace.EncodeEvents(events, alphabetSize).GetEnumerator();
        if (!tokens.MoveNext()) return 0;
        while (true)
        {
            string tok = (string)tokens.Current!;
            bool batchEnd = !tokens.MoveNext();
            int mint = _tht.Fold(tok);
            minted += mint;
            AppendCheckpointEvent(new(SelfStreamChannels.Thought, tok, _tht.LastPred, _tht.LastActual,
                _tht.LastHit, _tht.LastArm, mint, batchEnd));
            if (batchEnd) break;
        }
        _tht.MaybeInduce(force: true);
        return minted;
    }

    /// The self-signal the WeightController reads: the excursion channel's rolling mint-rate — rising = the
    /// machine surprising itself = an explore signal. Exposed only; 2a owns the homeostat law that consumes it.
    public double MintRate => _exc.MintRate;

    /// The excursion channel's cumulative predictor accuracy — the homeostat's ExcHit sense (face 1: the wire
    /// that un-darkens the self-model into the controller).
    public double ExcHitRate => _exc.HitRate;

    /// The thought channel's rolling mint-rate — the homeostat's ThtMint sense (Sealed reads thought-mint→0
    /// inside a converged dream loop: the machine no longer surprises itself about its own cognition).
    public double ThoughtMintRate => _tht.MintRate;

    /// The excursion channel's STANDING next-excursion forecast (+ the arm that produced it: 'g' motif /
    /// 'f' fallback) — the L3 output the homeostat's Predict tier consumes as its L4 feed.
    public string ExcForecast(out char arm) => _exc.Forecast(out arm);

    // ── the sparkline columns (appended to LossReading's row/header at the drive's write site — Reads.cs owns the
    //    base reading; these are the MODEL phase's own, known only after Observe) ──
    public const string HeaderCols = "\texc_pred\texc_hit\texc_mint\texc_ph\ttht_mint\ttht_ph";
    public string RowCols() =>
        $"\t{_exc.LastPred}\t{(_exc.Events == 0 ? "·" : _exc.LastHit ? _exc.LastArm.ToString() : "0")}"
      + $"\t{_exc.MintRate:F3}\t{_exc.HitRate:F3}\t{_tht.MintRate:F3}\t{_tht.HitRate:F3}";

    /// The per-step self-signal telegraph (throttled with the READ line): predicted→actual, hit-arm/miss, the two
    /// rolling mint-rates. "" until either channel has an event (no noise before the self-stream has anything to say).
    public string Line()
    {
        if (_exc.Events == 0 && _tht.Events == 0) return "";
        string exc = _exc.Events == 0 ? "exc —"
            : $"exc {_exc.LastPred}→{_exc.LastActual} {(_exc.LastHit ? _exc.LastArm : 'x')} mint {_exc.MintRate:P0}";
        return $"   ⟨self · {exc} | tht mint {_tht.MintRate:P0} ({_tht.Events} thoughts)⟩";
    }

    // checkpoint — both channels whole (each MetaGrammar owns its own encoding).
    public void Save(CkptWriter w)
    {
        _exc.Save(w); _tht.Save(w);
        // Snapshot materialization is observational. The pending receipts belong
        // to the mutation rail and are consumed only after its durable append by
        // CommitCheckpointDelta; clearing them here loses the next typed delta
        // when a policy/fork path materializes a checkpoint before SaveMutation.
    }

    public void Load(CkptReader r)
    {
        _exc.Load(r); _tht.Load(r);
        _checkpointEvents.Clear();
        _checkpointEventCursor = 0;
    }

    internal SelfStreamCheckpointDelta CaptureCheckpointDelta()
    {
        ValidateCheckpointCursor(_checkpointEventCursor, _checkpointEvents.Count);
        SelfStreamTokenReceipt[] events = _checkpointEvents.Count == _checkpointEventCursor
            ? Array.Empty<SelfStreamTokenReceipt>()
            : _checkpointEvents.GetRange(_checkpointEventCursor, _checkpointEvents.Count - _checkpointEventCursor).ToArray();
        return new(_checkpointEventCursor, events, CaptureCheckpointState());
    }

    internal void CommitCheckpointDelta()
    {
        _checkpointEvents.Clear();
        _checkpointEventCursor = 0;
    }

    internal void ApplyCheckpointDelta(in SelfStreamCheckpointDelta delta)
    {
        ValidateCheckpointCursor(delta.Cursor, _checkpointEvents.Count);
        if (delta.Events is null || delta.Events.Length > MaxCheckpointEvents)
            throw new InvalidDataException($"self-stream checkpoint event journal exceeds {MaxCheckpointEvents} events");
        if (delta.Cursor != _checkpointEvents.Count)
            throw new InvalidDataException($"self-stream checkpoint cursor gap: expected {_checkpointEvents.Count}, got {delta.Cursor}");

        for (int i = 0; i < delta.Events.Length; i++)
        {
            SelfStreamTokenReceipt receipt = delta.Events[i];
            ValidateToken(receipt.Token);
            int minted;
            if (receipt.Channel == SelfStreamChannels.Excursion)
            {
                minted = _exc.Fold(receipt.Token);
                _exc.MaybeInduce(force: false);
                ValidateReceipt(receipt, minted, _exc);
            }
            else if (receipt.Channel == SelfStreamChannels.Thought)
            {
                minted = _tht.Fold(receipt.Token);
                if (receipt.BatchEnd) _tht.MaybeInduce(force: true);
                ValidateReceipt(receipt, minted, _tht);
            }
            else throw new InvalidDataException($"unknown self-stream channel {(byte)receipt.Channel}");
            _checkpointEvents.Add(receipt);
        }
        _checkpointEventCursor = _checkpointEvents.Count;
        MetaGrammarStateReplacement excursionState = delta.State.Excursion;
        MetaGrammarStateReplacement thoughtState = delta.State.Thought;
        _exc.ValidateCheckpointState(in excursionState);
        _tht.ValidateCheckpointState(in thoughtState);
        // The event receipts are a mutation-local append buffer.  Live commit
        // clears it after the durable write; replay must mirror that boundary
        // or the next record's zero-based cursor is compared against stale
        // events from the previous mutation.
        _checkpointEvents.Clear();
        _checkpointEventCursor = 0;
    }

    internal SelfStreamStateReplacement CaptureCheckpointState()
        => new(_exc.CaptureCheckpointState(), _tht.CaptureCheckpointState());

    internal static void WriteCheckpointDelta(CkptWriter writer, in SelfStreamCheckpointDelta delta)
    {
        writer.U8(1);
        writer.I32(delta.Cursor);
        if (delta.Events is null || delta.Events.Length > MaxCheckpointEvents)
            throw new InvalidDataException($"self-stream checkpoint event journal exceeds {MaxCheckpointEvents} events");
        writer.I32(delta.Events.Length);
        foreach (SelfStreamTokenReceipt receipt in delta.Events)
        {
            ValidateToken(receipt.Token);
            writer.U8((byte)receipt.Channel); writer.Str(receipt.Token); writer.Str(receipt.Prediction); writer.Str(receipt.Actual);
            writer.Bool(receipt.Hit); writer.U8((byte)receipt.Arm); writer.I32(receipt.Minted); writer.Bool(receipt.BatchEnd);
        }
        MetaGrammarStateReplacement excursionState = delta.State.Excursion;
        MetaGrammarStateReplacement thoughtState = delta.State.Thought;
        WriteState(writer, in excursionState);
        WriteState(writer, in thoughtState);
    }

    internal static SelfStreamCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown self-stream checkpoint delta version");
        int cursor = reader.I32();
        int count = reader.I32();
        if (cursor < 0 || count < 0 || count > MaxCheckpointEvents)
            throw new InvalidDataException("self-stream checkpoint delta cursor or event count is invalid");
        SelfStreamTokenReceipt[] events = new SelfStreamTokenReceipt[count];
        for (int i = 0; i < count; i++)
        {
            SelfStreamChannels channel = (SelfStreamChannels)reader.U8();
            string token = reader.Str(); string prediction = reader.Str(); string actual = reader.Str();
            ValidateToken(token); ValidateToken(prediction); ValidateToken(actual);
            events[i] = new(channel, token, prediction, actual, reader.Bool(), (char)reader.U8(), reader.I32(), reader.Bool());
        }
        return new(cursor, events, new(ReadState(reader), ReadState(reader)));
    }

    internal static bool VerifyCheckpointObservationFixture(TextWriter output)
    {
        SelfStream source = new();
        source.Observe("excursion-a");
        source.Observe("excursion-b");
        source.ObserveThought(
            [new MergeEvent(0, new Symbol(1), new Symbol(2), new Symbol(3), 2, 1, 2, Mbits.Zero)],
            alphabetSize: 256);

        SelfStreamCheckpointDelta expected = source.CaptureCheckpointDelta();
        using MemoryStream image = new();
        using (CkptWriter writer = new(image)) source.Save(writer);
        byte[] firstImage = image.ToArray();

        using (MemoryStream second = new())
        {
            using CkptWriter writer = new(second);
            source.Save(writer);
            if (!firstImage.AsSpan().SequenceEqual(second.ToArray()))
            {
                output.WriteLine("  self-stream observation · repeated Save changed the materialized image");
                return false;
            }
        }

        SelfStreamCheckpointDelta retained = source.CaptureCheckpointDelta();
        bool retainedExact = retained.Cursor == expected.Cursor
            && retained.Events.AsSpan().SequenceEqual(expected.Events)
            && StatesEqual(retained.State, expected.State);

        SelfStream replay = new();
        replay.ApplyCheckpointDelta(in retained);
        SelfStreamCheckpointDelta replayState = replay.CaptureCheckpointDelta();
        bool replayExact = replayState.Events.Length == 0 && StatesEqual(replayState.State, retained.State);
        source.CommitCheckpointDelta();
        bool commitClears = source.CaptureCheckpointDelta().Events.Length == 0;
        bool passed = retainedExact && replayExact && commitClears;
        output.WriteLine($"  self-stream observation · pending={(retainedExact ? "retained" : "LOST")} replay={(replayExact ? "exact" : "DIVERGED")} commit={(commitClears ? "cleared" : "PRESENT")} · {(passed ? "PASS" : "FAIL")}");
        return passed;

        static bool StatesEqual(SelfStreamStateReplacement left, SelfStreamStateReplacement right)
            => StateEqual(left.Excursion, right.Excursion) && StateEqual(left.Thought, right.Thought);

        static bool StateEqual(MetaGrammarStateReplacement left, MetaGrammarStateReplacement right)
            => left.Events == right.Events && left.Minted == right.Minted
            && left.GramHits == right.GramHits && left.FallHits == right.FallHits
            && string.Equals(left.LastPrediction, right.LastPrediction, StringComparison.Ordinal)
            && string.Equals(left.LastActual, right.LastActual, StringComparison.Ordinal)
            && left.LastHit == right.LastHit && left.LastArm == right.LastArm
            && left.LastInduceAt == right.LastInduceAt
            && left.MetaTokenAlphabetSize == right.MetaTokenAlphabetSize
            && left.MintWindow.AsSpan().SequenceEqual(right.MintWindow);
    }

    private void AppendCheckpointEvent(in SelfStreamTokenReceipt receipt)
    {
        ValidateToken(receipt.Token);
        if (_checkpointEvents.Count == MaxCheckpointEvents)
            throw new InvalidDataException($"self-stream checkpoint event journal exceeds {MaxCheckpointEvents} events");
        _checkpointEvents.Add(receipt);
    }

    private static void ValidateReceipt(in SelfStreamTokenReceipt receipt, int minted, MetaGrammar grammar)
    {
        if (minted != receipt.Minted || !string.Equals(receipt.Prediction, grammar.LastPred, StringComparison.Ordinal)
            || !string.Equals(receipt.Actual, grammar.LastActual, StringComparison.Ordinal)
            || receipt.Hit != grammar.LastHit || receipt.Arm != grammar.LastArm)
            throw new InvalidDataException("self-stream checkpoint token receipt diverged during replay");
    }

    private static void WriteState(CkptWriter writer, in MetaGrammarStateReplacement state)
    {
        writer.I32(state.Events); writer.I32(state.Minted); writer.I32(state.GramHits); writer.I32(state.FallHits);
        writer.Str(state.LastPrediction); writer.Str(state.LastActual); writer.Bool(state.LastHit); writer.U8((byte)state.LastArm);
        writer.I32(state.LastInduceAt); writer.I32(state.MetaTokenAlphabetSize);
        if (state.MintWindow is null || state.MintWindow.Length > 16) throw new InvalidDataException("self-stream mint window exceeds bound");
        writer.I32(state.MintWindow.Length); foreach (int value in state.MintWindow) writer.I32(value);
    }

    private static MetaGrammarStateReplacement ReadState(CkptReader reader)
    {
        int events = reader.I32(); int minted = reader.I32(); int gramHits = reader.I32(); int fallHits = reader.I32();
        string prediction = reader.Str(); string actual = reader.Str(); bool hit = reader.Bool(); char arm = (char)reader.U8();
        int induce = reader.I32(); int alphabet = reader.I32(); int windowCount = reader.I32();
        if (windowCount < 0 || windowCount > 16) throw new InvalidDataException("self-stream mint window exceeds bound");
        int[] window = new int[windowCount]; for (int i = 0; i < window.Length; i++) window[i] = reader.I32();
        return new(events, minted, gramHits, fallHits, prediction, actual, hit, arm, induce, alphabet, window);
    }

    private static void ValidateToken(string token)
    {
        if (token is null || Encoding.UTF8.GetByteCount(token) > MaxTokenBytes)
            throw new InvalidDataException("self-stream token exceeds its bounded replay size");
    }

    private static void ValidateCheckpointCursor(int cursor, int count)
    {
        if (cursor < 0 || cursor > count)
            throw new InvalidDataException($"self-stream checkpoint cursor {cursor} is outside {count} events");
    }

    /// The land-time report — both channels' mint-rate decay curve (the kill-line readout), the gram/fallback hit
    /// split (the meta-grammar's contribution over the count-table), and the top motif-rules (cogito's dynamics
    /// vocabulary). Written to selfstream.txt and telegraphed at drive end.
    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("── SELF-STREAM · the recurrent self-signal (MODEL phase) — cogito predicting its own dynamics, minting the residual ──");
        ReportChannel(sb, "excursion (HomeWatch dynamics)", _exc);
        ReportChannel(sb, "thought (merge-event cognition)", _tht);
        return sb.ToString();
    }

    private static void ReportChannel(StringBuilder sb, string name, MetaGrammar mg)
    {
        int residual = mg.Events == 0 ? 0 : 100 * mg.Minted / mg.Events;
        sb.AppendLine($"  {name}: {mg.Events} events · {mg.Minted} minted ({residual}% residual) · predictor hit {mg.HitRate:P1} "
                    + $"(grammar {mg.GramHits} · fallback {mg.FallHits} · miss {mg.Minted})");
        sb.AppendLine($"    mint-rate decay ({mg.Events} events → buckets):  {mg.DecayCurve()}");
        sb.AppendLine($"    meta-grammar: {mg.RuleCount} motif-rules · top motifs (the machine's dynamics vocabulary):");
        foreach (var m in mg.TopMotifs(6)) sb.AppendLine($"      {m}");
    }
}
