namespace Cogito;

using System.Security.Cryptography;
using Cogito.Induct;
using System.Text;

/// G1's kill-line — the re-grep renormalization null.
///
/// The crawler's mouth is a tool. If a look over territory it already carries could still grow the
/// grammar diet, the organism would renormalize itself on its own echo: loop a grep, watch the tape
/// swell, and read the swelling as learning. So the standing claim is exactly two halves, and this
/// fixture is the line that kills the claim if either half fails:
///
///   custody is UNCONDITIONAL — every look lands as an event with its digest and journal row,
///                              whether or not the bytes taught anything (the loop-closure chain
///                              validates that evidence and must never depend on the verdict);
///   diet is MARGINAL         — the look carries GrammarInput only when the standing grammar cannot
///                              already generate it, so a re-look earns ZERO grammar bytes while a
///                              genuinely novel look earns its full length.
///
/// The disarmed arm (affirm cut &lt; 0) is the control: with the gate off, the identical re-look is
/// admitted and the diet grows twice. That is what proves the zero is caused by the organ and not by
/// something incidental about the second append.
///
/// What this fixture does NOT claim: it measures the intake MECHANISM at the tool seam, not the
/// lifelong criticality story. Whether meanz holds in the basin across a live crawl is G5's gate and
/// is not banked here.
internal static class RepositoryIntakeNull
{
    private const TapeEventRoles CustodyRoles = TapeEventRoles.Measurement | TapeEventRoles.AuditOnly;

    /// The re-look's residual must land under this to be affirmed. It is the fixture's own cut, not
    /// the drive's default: the claim under test is "the organ zeroes a re-look", and pinning the cut
    /// here keeps the fixture from silently passing or failing on a config edit elsewhere.
    private const double AffirmCut = 0.25;

    internal static bool Verify(TextWriter output)
    {
        bool armed = VerifyArmedArm(output);
        bool disarmed = VerifyDisarmedArm(output);
        output.WriteLine($"  repository-intake-null · armed={(armed ? "PASS" : "FAIL")} · disarmed-control={(disarmed ? "PASS" : "FAIL")}");
        return armed && disarmed;
    }

    /// The gate armed: first look feeds, identical re-look feeds nothing, novel look feeds again —
    /// and all three land as custody.
    private static bool VerifyArmedArm(TextWriter output)
    {
        try
        {
            byte[] known = BuildLookResult("known");
            byte[] novel = BuildLookResult("novel-and-unrelated");

            using Tape tape = new();
            Journal journal = new();

            long beforeFirst = tape.GrammarByteLength;
            TapeEventID first = AppendLook(tape, journal, step: 0, known, cover: null);
            long firstGain = tape.GrammarByteLength - beforeFirst;

            // The organism has now eaten the result once — induce over exactly what it ate, which is
            // what a crawl's re-induction does between looks.
            Engine.GrammarCover cover = BuildCover(known);
            Radula.Affirmation reLook = Radula.MeasureAffirmation(cover, known, AffirmCut);
            Radula.Affirmation novelLook = Radula.MeasureAffirmation(cover, novel, AffirmCut);

            long beforeSecond = tape.GrammarByteLength;
            TapeEventID second = AppendLook(tape, journal, step: 1, known, cover);
            long secondGain = tape.GrammarByteLength - beforeSecond;

            long beforeThird = tape.GrammarByteLength;
            TapeEventID third = AppendLook(tape, journal, step: 2, novel, cover);
            long thirdGain = tape.GrammarByteLength - beforeThird;

            output.WriteLine($"    armed · residual first→re-look {1.0:F3}→{reLook.Residual:F3} (novel {novelLook.Residual:F3}, cut {AffirmCut:F2})"
                           + $" · grammar bytes {firstGain}/{secondGain}/{thirdGain} (first/re-look/novel)");

            Require(firstGain > 0, "the first look over unknown territory earned no grammar diet");
            Require(secondGain == 0, "a re-look over known territory grew the grammar diet — the organism renormalizes on its own echo");
            Require(thirdGain > 0, "a genuinely novel look earned no grammar diet — the gate is refusing everything, not discriminating");
            Require(reLook.Affirmed && !novelLook.Affirmed, "the affirm measurement did not separate the re-look from the novel look");

            foreach (TapeEventID look in (TapeEventID[])[first, second, third])
            {
                Require((tape.RolesOf(look) & CustodyRoles) == CustodyRoles, "a look landed without its custody roles");
                Require(tape.Resolve(look, out byte[] _), "a look's custody bytes were not resolvable");
            }
            Require(tape.RolesOf(second) == CustodyRoles, "the rejected re-look still carries a diet role");
            Require(tape.Resolve(first, out byte[] firstBytes) && tape.Resolve(second, out byte[] secondBytes)
                && firstBytes.AsSpan().SequenceEqual(secondBytes), "the rejected re-look did not store the same custody bytes as the admitted look");
            return true;
        }
        catch (Exception failure)
        {
            output.WriteLine($"    armed · FAIL — {failure.Message}");
            return false;
        }
    }

    /// The control: with the cut disarmed the affirm measurement never fires, so the same re-look is
    /// admitted and the diet grows a second time. A disarmed arm that ALSO reads zero would mean the
    /// armed zero proved nothing.
    private static bool VerifyDisarmedArm(TextWriter output)
    {
        try
        {
            byte[] known = BuildLookResult("known");
            using Tape tape = new();
            Journal journal = new();

            AppendLook(tape, journal, step: 0, known, cover: null, affirmCut: -1);
            Engine.GrammarCover cover = BuildCover(known);
            long before = tape.GrammarByteLength;
            AppendLook(tape, journal, step: 1, known, cover, affirmCut: -1);
            long gain = tape.GrammarByteLength - before;

            output.WriteLine($"    disarmed · re-look grammar bytes {gain} (the gate off — the crawler eats its own echo, as designed for the control)");
            Require(gain > 0, "the disarmed control did not re-admit the known look — the armed zero is not attributable to the gate");
            return true;
        }
        catch (Exception failure)
        {
            output.WriteLine($"    disarmed · FAIL — {failure.Message}");
            return false;
        }
    }

    /// Drive one look through the real admissionPlan packet path, with the intake verdict deciding only
    /// the diet role. This is the same append the crawler's frontier calls, receipt validation and
    /// all — the fixture measures the shipped seam, not a parallel imitation of it.
    private static TapeEventID AppendLook(
        Tape tape, Journal journal, int step, byte[] result, Engine.GrammarCover? cover, double affirmCut = AffirmCut)
    {
        Radula.Affirmation measurement = Radula.MeasureAffirmation(cover, result, affirmCut);
        string evidence = Convert.ToHexStringLower(SHA256.HashData(result));
        RepositoryAdmissionReceipt receipt = RepositoryAdmissionReceipt.Create(
            step, new TapeEventID(tape.NextId + 1), Digest("world"), Digest("access"), Digest("call"),
            "src/fixture/look.cs", 1, evidence, step, Digest($"entry-{step}"));
        return TapePacketCreator.AppendRepositoryWorldEncounter(
            tape, journal, step, receipt, result, admitToGrammar: !measurement.Affirmed);
    }

    private static Engine.GrammarCover BuildCover(byte[] eaten)
    {
        byte[] corpus = new byte[eaten.Length + 1];
        eaten.CopyTo(corpus, 0);
        corpus[^1] = (byte)'\n';
        (_, _, RePairResult grammar) = Engine.Induce(corpus);
        return new Engine.GrammarCover(grammar.Rules);
    }

    /// A tool result shaped like one: repeated structured lines, which is what a grep or a read of a
    /// source file actually returns, and which is what makes a re-look genuinely generable.
    private static byte[] BuildLookResult(string tag)
    {
        StringBuilder text = new();
        for (int line = 0; line < 24; line++)
            text.Append($"src/{tag}/module{line % 4}.cs:{line + 1}: internal static void Handle{tag}Step{line % 4}(Cortex cortex) {{ }}\n");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidDataException(failure);
    }
}
