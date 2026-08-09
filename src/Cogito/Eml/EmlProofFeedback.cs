namespace Cogito;

using System.Security.Cryptography;
using System.Text;

internal enum EmlProofFeedbackStatuses
{
    DegradedSkip,
    Rejected,
    Accepted,
}

internal sealed class EmlAttachedProofTerm
{
    public EmlAttachedProofTerm(EmlLawClassID lawClass, string theorem, string term)
    {
        if (string.IsNullOrWhiteSpace(lawClass.Value)) throw new ArgumentException("proof attachment requires a law class", nameof(lawClass));
        if (string.IsNullOrWhiteSpace(theorem)) throw new ArgumentException("proof attachment requires a theorem", nameof(theorem));
        if (string.IsNullOrWhiteSpace(term)) throw new ArgumentException("proof attachment requires an actual proof term", nameof(term));
        LawClass = lawClass;
        Theorem = theorem;
        Term = term;
    }

    public EmlLawClassID LawClass { get; }
    public string Theorem { get; }
    public string Term { get; }
}

internal readonly record struct EmlProofOccurrenceCheck(
    bool Accepted,
    string Verifier,
    string ProofDigest,
    string Detail);

internal interface IEmlProofVerifier
{
    bool TryVerify(EmlAttachedProofTerm attachment, out EmlProofOccurrenceCheck verification);
}

internal readonly record struct EmlProofAssumption(
    EmlLawClassID LawClass,
    string Theorem,
    string Verifier,
    string ProofDigest);

internal readonly record struct EmlProofFeedbackReceipt(
    EmlProofFeedbackStatuses Status,
    EmlLawClassID LawClass,
    string Verifier,
    string ProofDigest,
    string Detail,
    TapeEventID EventID);

internal readonly record struct EmlProofFeedbackResult(
    EmlProofFeedbackStatuses Status,
    int Submitted,
    int Accepted,
    int Rejected,
    List<EmlProofAssumption> Assumptions,
    List<EmlProofFeedbackReceipt> Receipts);

internal static class EmlProofFeedbackGate
{
    public static EmlProofFeedbackResult ApplyProofFeedback(
        Cortex cortex,
        List<EmlAttachedProofTerm> attachments,
        IEmlProofVerifier? verifier)
    {
        ArgumentNullException.ThrowIfNull(cortex);
        ArgumentNullException.ThrowIfNull(attachments);
        List<EmlProofAssumption> assumptions = new();
        List<EmlProofFeedbackReceipt> receipts = new();
        if (verifier is null || attachments.Count == 0)
        {
            string detail = verifier is null
                ? "degraded-skip: no formal proof verifier is mounted"
                : "degraded-skip: no attached proof terms were supplied";
            receipts.Add(new EmlProofFeedbackReceipt(
                EmlProofFeedbackStatuses.DegradedSkip,
                default,
                "",
                "",
                detail,
                default));
            return new EmlProofFeedbackResult(
                EmlProofFeedbackStatuses.DegradedSkip,
                attachments.Count,
                0,
                0,
                assumptions,
                receipts);
        }

        int accepted = 0;
        int rejected = 0;
        for (int i = 0; i < attachments.Count; i++)
        {
            EmlAttachedProofTerm attachment = attachments[i];
            string termDigest = ComputeProofDigest(attachment.Term);
            if (!verifier.TryVerify(attachment, out EmlProofOccurrenceCheck verification)
                || !verification.Accepted
                || string.IsNullOrWhiteSpace(verification.Verifier)
                || !string.Equals(termDigest, verification.ProofDigest, StringComparison.OrdinalIgnoreCase))
            {
                rejected++;
                receipts.Add(new EmlProofFeedbackReceipt(
                    EmlProofFeedbackStatuses.Rejected,
                    attachment.LawClass,
                    verification.Verifier ?? "",
                    termDigest,
                    verification.Detail ?? "formal verifier rejected the attached proof term",
                    default));
                continue;
            }

            EmlProofAssumption assumption = new(
                attachment.LawClass,
                attachment.Theorem,
                verification.Verifier,
                termDigest);
            byte[] packet = EncodeProofPacket(in assumption, attachment.Term);
            TapeEventID eventID = cortex.AppendEvidence(packet, "proof:" + verification.Verifier);
            assumptions.Add(assumption);
            receipts.Add(new EmlProofFeedbackReceipt(
                EmlProofFeedbackStatuses.Accepted,
                attachment.LawClass,
                verification.Verifier,
                termDigest,
                verification.Detail,
                eventID));
            accepted++;
        }

        return new EmlProofFeedbackResult(
            accepted > 0 ? EmlProofFeedbackStatuses.Accepted : EmlProofFeedbackStatuses.Rejected,
            attachments.Count,
            accepted,
            rejected,
            assumptions,
            receipts);
    }

    public static string RenderProofFeedback(in EmlProofFeedbackResult result)
    {
        StringBuilder report = new("status\tlaw_class\tverifier\tproof_digest\tdetail\tevent_id\n");
        for (int i = 0; i < result.Receipts.Count; i++)
        {
            EmlProofFeedbackReceipt receipt = result.Receipts[i];
            report.Append(FormatStatus(receipt.Status)).Append('\t')
                .Append(receipt.LawClass.Value).Append('\t')
                .Append(FormatField(receipt.Verifier)).Append('\t')
                .Append(receipt.ProofDigest).Append('\t')
                .Append(FormatField(receipt.Detail)).Append('\t')
                .Append(receipt.EventID.Value).AppendLine();
        }
        return report.ToString();
    }

    private static byte[] EncodeProofPacket(in EmlProofAssumption assumption, string term)
    {
        StringBuilder packet = new();
        packet.Append("PROOF-ATTACHED\t")
            .Append(assumption.LawClass.Value).Append('\t')
            .Append(FormatField(assumption.Verifier)).Append('\t')
            .Append(assumption.ProofDigest).Append('\n')
            .Append("THEOREM\t").Append(FormatField(assumption.Theorem)).Append('\n')
            .Append("TERM\t").Append(FormatField(term));
        return Encoding.UTF8.GetBytes(packet.ToString());
    }

    private static string ComputeProofDigest(string term)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(term)));

    private static string FormatStatus(EmlProofFeedbackStatuses status)
        => status switch
        {
            EmlProofFeedbackStatuses.DegradedSkip => "degraded-skip",
            EmlProofFeedbackStatuses.Rejected => "rejected",
            EmlProofFeedbackStatuses.Accepted => "accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unknown proof-feedback status"),
        };

    private static string FormatField(string value)
    {
        StringBuilder result = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            result.Append(character is '\t' or '\r' or '\n' ? ' ' : character);
        }
        return result.ToString();
    }
}
