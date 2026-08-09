namespace Cogito;

using System;
using System.Collections.Generic;

public readonly record struct SemanticCASClass<TRep>(TRep Rep, int Members, int FirstCapture);

public readonly record struct SemanticCASAdmission<TCert, TRep>(
    TCert Cert,
    SemanticCASClass<TRep> Class,
    bool FirstCapture,
    bool RepresentativeChanged)
    where TCert : notnull;

/// Outcome-addressed class register: the caller owns the certificate semantics; this owns admission,
/// member census, first capture, and cheapest-representative competition.
public sealed class SemanticCAS<TCert, TRep>
    where TCert : notnull
{
    private readonly Dictionary<TCert, SemanticCASClass<TRep>> _classes = new();
    private readonly Comparison<TRep> _compareRepresentatives;

    public SemanticCAS(Comparison<TRep> compareRepresentatives)
        => _compareRepresentatives = compareRepresentatives ?? throw new ArgumentNullException(nameof(compareRepresentatives));

    public int Count => _classes.Count;
    public IReadOnlyDictionary<TCert, SemanticCASClass<TRep>> Classes => _classes;
    public IEnumerable<SemanticCASClass<TRep>> Values => _classes.Values;
    public SemanticCASClass<TRep> this[TCert cert] => _classes[cert];

    public bool Contains(TCert cert) => _classes.ContainsKey(cert);
    public void Clear() => _classes.Clear();

    internal bool Remove(TCert cert) => _classes.Remove(cert);

    internal void Set(TCert cert, SemanticCASClass<TRep> value) => _classes[cert] = value;

    public SemanticCASAdmission<TCert, TRep> Admit(TCert cert, TRep rep, int captureIdx)
    {
        if (!_classes.TryGetValue(cert, out SemanticCASClass<TRep> cls))
        {
            SemanticCASClass<TRep> opened = new(rep, 1, captureIdx);
            _classes[cert] = opened;
            return new SemanticCASAdmission<TCert, TRep>(cert, opened, true, true);
        }

        bool representativeChanged = _compareRepresentatives(rep, cls.Rep) < 0;
        SemanticCASClass<TRep> updated = cls with
        {
            Rep = representativeChanged ? rep : cls.Rep,
            Members = cls.Members + 1,
        };
        _classes[cert] = updated;
        return new SemanticCASAdmission<TCert, TRep>(cert, updated, false, representativeChanged);
    }
}
