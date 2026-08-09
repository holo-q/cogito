namespace Cogito;

using System.Security.Cryptography;

public sealed partial class ReplayCalc
{
    internal EmlIntensionalRematchBoundSource CaptureIntensionalRematchSource()
    {
        EmlRematchFixture fixture = EmlRematchFixture.CaptureBound(_sieve);
        byte[] lawImage = EmlIntensionalRematchRunner.SaveLawStoreImage(_lawStore);
        return new EmlIntensionalRematchBoundSource(
            _sieve.SignatureDigits,
            fixture.AdmissionImage,
            lawImage,
            fixture.Bindings.ToArray(),
            fixture.Obligations.ToArray(),
            Digest(fixture.AdmissionImage),
            Digest(lawImage));
    }

    private static string Digest(byte[] image)
        => Convert.ToHexStringLower(SHA256.HashData(image));
}
