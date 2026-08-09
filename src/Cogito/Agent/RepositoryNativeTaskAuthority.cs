namespace Cogito;

/// The optional native task mount is persisted beside the repository world
/// authority.  The prompt is the only part that crosses into the organism;
/// these fields are runner/adjudicator custody and never become tape input.
internal static class RepositoryNativeTaskAuthority
{
    internal static void Write(Dictionary<string, string> authority, RepositoryLoopClosureRegistration registration)
    {
        byte[] encoded = registration.Encode();
        authority["registration_sha256"] = registration.RegistrationSHA256;
        authority["registration_document_sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(encoded));
        authority["registration_ron_base64"] = Convert.ToBase64String(encoded);
    }

    internal static bool TryRead(IReadOnlyDictionary<string, string> authority, out RepositoryLoopClosureRegistration? registration)
    {
        registration = null;
        bool hasDigest = authority.ContainsKey("registration_sha256");
        bool hasDocumentDigest = authority.ContainsKey("registration_document_sha256");
        bool hasBytes = authority.ContainsKey("registration_ron_base64");
        if (!hasDigest && !hasDocumentDigest && !hasBytes) return false;
        if (!hasDigest || !hasDocumentDigest || !hasBytes)
            throw new InvalidDataException("native task authority has a partial registration mount");
        try
        {
            byte[] encoded = Convert.FromBase64String(Read(authority, "registration_ron_base64"));
            if (Read(authority, "registration_document_sha256")
                != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(encoded)))
                throw new InvalidDataException("native registration document digest diverges on resume");
            registration = RepositoryLoopClosureRegistration.Decode(encoded);
            if (Read(authority, "registration_sha256") != registration.RegistrationSHA256)
                throw new InvalidDataException("native registration authority digest diverges on resume");
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or InvalidDataException)
        {
            throw new InvalidDataException("native repository task authority is malformed", error);
        }
    }

    private static string Read(IReadOnlyDictionary<string, string> authority, string key)
        => authority.TryGetValue(key, out string? value) ? value : throw new InvalidDataException($"native task authority is missing {key}");
}
