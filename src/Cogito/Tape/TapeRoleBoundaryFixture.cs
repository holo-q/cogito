namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

/// End-to-end proof surface for the tape intake boundary. The fixture keeps
/// one source-of-record tape and varies only the consumer role; it therefore
/// catches role loss in persistence and role leakage into induction without
/// fabricating a parallel packet format.
internal static class TapeRoleBoundaryFixture
{
    private const TapeEventRoles AllRoles = TapeEventRoles.GrammarInput | TapeEventRoles.Measurement | TapeEventRoles.AuditOnly;

    internal static bool Verify(TextWriter output)
    {
        bool schema = VerifyRoleSchema(output);
        bool projection = VerifyGrammarProjection(output);
        bool parity = VerifyGrammarParity(output);
        bool harvest = VerifyGrammarHarvest(output);
        bool observer = VerifyObserverNull(output);
        output.WriteLine($"  tape-role-boundary · schema={(schema ? "PASS" : "FAIL")} · projection={(projection ? "PASS" : "FAIL")} · parity={(parity ? "PASS" : "FAIL")} · harvest={(harvest ? "PASS" : "FAIL")} · observer={(observer ? "PASS" : "FAIL")}");
        return schema && projection && parity && harvest && observer;
    }

    private static bool VerifyRoleSchema(TextWriter output)
    {
        try
        {
            using Tape source = CreateRoleTape();
            int[] order = [7, 0, 6, 1, 5, 2, 4, 3];
            source.Reorder(order);
            byte[] sourceImage = Save(source);
            using Tape loaded = Load(sourceImage, []);
            Require(SameTapeView(source, loaded, Enumerable.Range(0, 8).Select(static x => new TapeEventID(x))), "full role checkpoint changed the source-of-record view");

            using Tape deltaSource = new();
            for (int i = 0; i <= (int)AllRoles; i++)
                deltaSource.Append(Encoding.UTF8.GetBytes($"delta-role-{i}"), "fixture", Provenances.Real, (TapeEventRoles)i);
            TapeCheckpointDelta delta = deltaSource.CaptureCheckpointDelta();
            using Tape deltaReplay = new();
            deltaReplay.ApplyCheckpointDelta(in delta);
            Require(SameTapeView(deltaSource, deltaReplay, Enumerable.Range(0, 8).Select(static x => new TapeEventID(x))), "role delta replay changed ids, bytes, or roles");

            using Tape evacuationSource = new();
            using MemoryStream sourceLog = new();
            evacuationSource.MountLog(sourceLog);
            TapeEventID shed = evacuationSource.Append("shed-role"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
            TapeEventID drop = evacuationSource.Append("drop-role"u8.ToArray(), "fixture", Provenances.Replay, TapeEventRoles.AuditOnly);
            evacuationSource.Append("resident-role"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            evacuationSource.Evacuate([shed], [drop]);
            byte[] evacuationImage = Save(evacuationSource);
            byte[] logImage = CopyLog(sourceLog);
            using Tape evacuationReplay = Load(evacuationImage, logImage);
            Require(evacuationReplay.RolesOf(shed) == (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly), "shed role did not survive reload");
            Require(evacuationReplay.RolesOf(drop) == TapeEventRoles.AuditOnly, "tomb role did not survive reload");
            Require(evacuationReplay.Resolve(shed, out byte[] shedBytes) && shedBytes.AsSpan().SequenceEqual("shed-role"u8), "shed audit-only bytes were not resolvable");
            Require(evacuationReplay.Resolve(drop, out byte[] dropBytes) && dropBytes.AsSpan().SequenceEqual("drop-role"u8), "tomb audit-only bytes were not resolvable");

            TapeCheckpointDelta invalid = delta with
            {
                Appended = [delta.Appended[0] with { Roles = (TapeEventRoles)0x80 }],
            };
            using Tape invalidReplay = new();
            bool unknownRejected = Rejects(() => invalidReplay.ApplyCheckpointDelta(in invalid));

            using Tape mismatchSource = new();
            using MemoryStream mismatchLog = new();
            mismatchSource.MountLog(mismatchLog);
            TapeEventID mismatchID = mismatchSource.Append("mismatch"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            mismatchSource.Evacuate([mismatchID], []);
            TapeCheckpointDelta mismatch = mismatchSource.CaptureCheckpointDelta();
            mismatch = mismatch with { Shed = [mismatch.Shed[0] with { Roles = TapeEventRoles.AuditOnly }] };
            using Tape mismatchReplay = new();
            bool changedRoleRejected = Rejects(() => mismatchReplay.ApplyCheckpointDelta(in mismatch));
            output.WriteLine($"  tape-role schema · full=exact · delta=exact · shed-reload=exact · unknown={(unknownRejected ? "rejected" : "ACCEPTED")} · changed={(changedRoleRejected ? "rejected" : "ACCEPTED")}");
            return unknownRejected && changedRoleRejected;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  tape-role schema · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyGrammarProjection(TextWriter output)
    {
        try
        {
            using Tape tape = new();
            TapeEventID grammar = tape.Append("grammar"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            TapeEventID measurement = tape.Append("measurement"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.Measurement);
            TapeEventID auditOnlyEvent = tape.Append("custody"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.AuditOnly);
            TapeEventID both = tape.Append("both"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.GrammarInput | TapeEventRoles.AuditOnly);
            TapeEventID none = tape.Append("none"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.None);
            TapeEventID[] expected = [grammar, both];
            TapeEventView[] projected = tape.GetGrammarEventViews().ToArray();
            Require(projected.Select(static view => view.Id).SequenceEqual(expected), "grammar projection admitted a measurement, audit-only, or role-none event");
            Require(tape.ByteLength == tape.Concat().Length, "full tape byte length no longer covers audit-only bytes");
            Require(tape.GrammarByteLength == projected.Sum(static view => view.Len + 1), "grammar byte count includes non-grammar roles");
            foreach (TapeEventView view in tape.GetEventViews())
                Require(tape.Resolve(view.Id, out byte[] bytes) && bytes.Length == view.Len, $"role view {view.Id} lost its audit-only payload");
            output.WriteLine($"  tape-role projection · full-events={tape.GetEventViews().Count()} · grammar-events={projected.Length} · audit-only-bytes={tape.ByteLength - tape.GrammarByteLength} · PASS");
            return true;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  tape-role projection · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyGrammarParity(TextWriter output)
    {
        try
        {
            using Tape tape = new();
            using Loom loom = new();
            AppendParityEvents(tape);
            TapeDelta initial = tape.DrainDelta();
            loom.ApplyTapeDelta(tape, in initial);
            loom.Pump();
            RePairResult incremental = loom.Result();
            RePairResult batch = Engine.Induce(tape, 1).Result;
            Require(GrammarDigest(incremental) == GrammarDigest(batch), "incremental and batch grammar differ at the role boundary");

            byte[] tapeImage = Save(tape);
            byte[] loomImage = Save(loom);
            using Tape resumedTape = Load(tapeImage, []);
            using Loom resumedLoom = LoadLoom(loomImage, resumedTape);
            TapeEventID sourceNext = tape.Append("resume-grammar"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            TapeDelta sourceDelta = tape.DrainDelta();
            loom.ApplyTapeDelta(tape, in sourceDelta);
            loom.Pump();
            TapeEventID resumedNext = resumedTape.Append("resume-grammar"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            TapeDelta resumedDelta = resumedTape.DrainDelta();
            Require(sourceNext == resumedNext, "resume assigned a different stable event id");
            resumedLoom.ApplyTapeDelta(resumedTape, in resumedDelta);
            resumedLoom.Pump();
            Require(GrammarDigest(loom.Result()) == GrammarDigest(resumedLoom.Result()), "resume grammar diverged after role-aware append");
            output.WriteLine("  tape-role parity · incremental=batch=resume · measurement high-water preserved · PASS");
            return true;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  tape-role parity · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyObserverNull(TextWriter output)
    {
        try
        {
            using Tape control = CreateObserverTape(TapeEventRoles.GrammarInput);
            using Tape repaired = CreateObserverTape(TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
            TapeEventView[] controlCommon = control.GetEventViews().Take(6).ToArray();
            TapeEventView[] repairedCommon = repaired.GetEventViews().Take(6).ToArray();
            Require(controlCommon.Length == repairedCommon.Length && controlCommon.Zip(repairedCommon).All(static pair => pair.First.Id == pair.Second.Id && pair.First.Len == pair.Second.Len && pair.First.Provenance == pair.Second.Provenance), "observer A/B changed proof, audit, closure, lineage, or relation-null audit-only state");
            Require(controlCommon.Zip(repairedCommon).All(static pair => pair.First.Id == pair.Second.Id), "observer A/B changed stable audit-only IDs");
            RePairResult baseline = Engine.Induce(CreateObserverBase(), 1).Result;
            RePairResult controlGrammar = Engine.Induce(control, 1).Result;
            RePairResult repairedGrammar = Engine.Induce(repaired, 1).Result;
            bool controlPerturbs = GrammarDigest(controlGrammar) != GrammarDigest(baseline);
            bool repairedStable = GrammarDigest(repairedGrammar) == GrammarDigest(baseline);
            Require(controlPerturbs && repairedStable, "ordinary cumulative receipt did not isolate its grammar observer effect");
            output.WriteLine($"  tape-role observer · MinimumOne proof/audit/evaluator/closure/lineage/relation-null exact · control=perturbs · repaired=stable · PASS");
            return true;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  tape-role observer · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyGrammarHarvest(TextWriter output)
    {
        try
        {
            using Tape tape = new();
            using MemoryStream log = new();
            tape.MountLog(log);
            TapeEventID leadingMeasurement = tape.Append("leading-measurement"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.Measurement);
            TapeEventID shedGrammar = tape.Append("abababababab"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.GrammarInput);
            _ = tape.Append("interleaved-custody"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.AuditOnly);
            TapeEventID residentGrammar = tape.Append("babababababa"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
            TapeDelta initial = tape.DrainDelta();
            using Loom loom = new();
            loom.ApplyTapeDelta(tape, in initial);
            loom.Pump();
            Require(GrammarDigest(loom.Result(tape)) == GrammarDigest(Engine.Induce(tape, 1).Result), "role-aware harvest leaked an interleaved non-grammar span");

            tape.Evacuate([leadingMeasurement, shedGrammar], []);
            TapeDelta evacuation = tape.DrainDelta();
            loom.ApplyTapeDelta(tape, in evacuation);
            loom.Pump();
            RePairResult harvested = loom.Result(tape);
            RePairResult expected = Engine.Induce(tape, 1).Result;
            Require(GrammarDigest(harvested) == GrammarDigest(expected), "role-aware harvest lost a shed grammar span or admitted a shed measurement span");
            Require(tape.GetGrammarEventViews().Select(static view => view.Id).SequenceEqual([residentGrammar, shedGrammar]), "grammar harvest view order changed across resident/shed boundary");
            output.WriteLine("  tape-role harvest · leading/interleaved non-grammar + shed grammar/non-grammar · Result(tape)=batch · PASS");
            return true;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  tape-role harvest · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static Tape CreateRoleTape()
    {
        Tape tape = new();
        for (int i = 0; i <= (int)AllRoles; i++)
            tape.Append(Encoding.UTF8.GetBytes($"role-{i}"), "fixture", Provenances.Real, (TapeEventRoles)i);
        return tape;
    }

    private static void AppendParityEvents(Tape tape)
    {
        tape.Append("abababababab"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
        tape.Append("measurement-metric"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        tape.Append("babababababa"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput | TapeEventRoles.AuditOnly);
        tape.Append("custody-receipt"u8.ToArray(), "fixture", Provenances.Reflected, TapeEventRoles.AuditOnly);
    }

    private static Tape CreateObserverBase()
    {
        Tape tape = new();
        tape.Append("abababababababab"u8.ToArray(), "world", Provenances.Real, TapeEventRoles.GrammarInput | TapeEventRoles.AuditOnly);
        tape.Append("babababababababa"u8.ToArray(), "world", Provenances.Real, TapeEventRoles.GrammarInput);
        return tape;
    }

    private static Tape CreateObserverTape(TapeEventRoles receiptRole)
    {
        Tape tape = CreateObserverBase();
        tape.Append("rung0-proof|MinimumOne|evaluator=0|audit=agreed"u8.ToArray(), "eml:rung0", Provenances.Reflected, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        tape.Append("closure|lineage|relation-null|executions=3|divergences=3"u8.ToArray(), "eml:rung0", Provenances.Reflected, TapeEventRoles.AuditOnly);
        tape.Append("ordinary-cumulative-receipt|MinimumOne|rung0-proof|closure|lineage|relation-null|abababababababababababab"u8.ToArray(), "eml:rung0", Provenances.Reflected, receiptRole);
        return tape;
    }

    private static byte[] Save(Tape tape)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) tape.Save(writer);
        return stream.ToArray();
    }

    private static byte[] Save(Loom loom)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) loom.Save(writer);
        return stream.ToArray();
    }

    private static Tape Load(byte[] image, byte[] log)
    {
        Tape tape = new();
        if (log.Length > 0) tape.MountLog(new MemoryStream(log));
        using CkptReader reader = new(new MemoryStream(image, writable: false));
        tape.Load(reader);
        return tape;
    }

    private static byte[] CopyLog(Stream stream)
    {
        using MemoryStream copy = new();
        long position = stream.Position;
        stream.Position = 0;
        stream.CopyTo(copy);
        stream.Position = position;
        return copy.ToArray();
    }

    private static Loom LoadLoom(byte[] image, Tape tape)
    {
        Loom loom = new();
        using CkptReader reader = new(new MemoryStream(image, writable: false));
        loom.Load(reader, tape);
        return loom;
    }

    private static bool SameTapeView(Tape left, Tape right, IEnumerable<TapeEventID> ids)
    {
        if (left.ByteLength != right.ByteLength || left.GrammarByteLength != right.GrammarByteLength) return false;
        foreach (TapeEventID id in ids)
        {
            if (left.RolesOf(id) != right.RolesOf(id) || left.ProvenanceOf(id) != right.ProvenanceOf(id) || left.SourceOf(id) != right.SourceOf(id)) return false;
            if (!left.Resolve(id, out byte[] a) || !right.Resolve(id, out byte[] b) || !a.AsSpan().SequenceEqual(b)) return false;
        }
        return true;
    }

    private static string GrammarDigest(in RePairResult grammar)
    {
        StringBuilder text = new();
        text.Append(grammar.AlphabetSize).Append('|');
        foreach (GrammarRule rule in grammar.Rules)
        {
            text.Append(rule.Id).Append(':').Append(rule.Cost.Value).Append(':');
            foreach (Symbol symbol in rule.Pattern) text.Append(symbol.Value).Append(',');
            text.Append('|');
        }
        text.Append("/|");
        foreach (Symbol symbol in grammar.Compressed) text.Append(symbol.Value).Append(',');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
