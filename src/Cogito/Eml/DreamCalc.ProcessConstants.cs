namespace Cogito;

using System.Numerics;

public sealed partial class ReplayCalc
{
    private const uint ProcessConstantTag = 0x53435250; // PRCS
    private EmlProcessConstantState _catalanProcess = EmlProcessConstants.CreateCatalanState();
    private EmlProcessConstantState _zeta3Process = EmlProcessConstants.CreateZeta3State();
    private int _processExactHighWater;

    internal void RefineProcessConstants(Tape tape, Journal journal, int step, int exactClasses)
    {
        if (exactClasses <= _processExactHighWater) return;
        long fuel = exactClasses - _processExactHighWater;
        EmlProcessConstantCertificate? previousCatalan = ReadCertificate(_catalanProcess);
        EmlProcessConstantCertificate? previousZeta3 = ReadCertificate(_zeta3Process);
        EmlProcessConstantState catalan = EmlProcessConstants.Advance(in _catalanProcess, fuel);
        EmlProcessConstantState zeta3 = EmlProcessConstants.Advance(in _zeta3Process, fuel);
        EmlProcessConstantCertificate catalanCertificate = EmlProcessConstants.Certify(in catalan);
        EmlProcessConstantCertificate zeta3Certificate = EmlProcessConstants.Certify(in zeta3);
        ValidateCertificate(in catalanCertificate, previousCatalan);
        ValidateCertificate(in zeta3Certificate, previousZeta3);
        TapePacketCreator.AppendEmlProcessConstant(tape, journal, step, in catalanCertificate);
        TapePacketCreator.AppendEmlProcessConstant(tape, journal, step, in zeta3Certificate);
        _catalanProcess = catalan;
        _zeta3Process = zeta3;
        _processExactHighWater = exactClasses;
    }

    internal long GetResidualProcessFuel()
        => Math.Clamp(Math.Max(_catalanProcess.Terms, _zeta3Process.Terms), 32, 256);

    private static EmlProcessConstantCertificate? ReadCertificate(EmlProcessConstantState state)
        => state.Terms > 0 ? EmlProcessConstants.Certify(in state) : null;

    private static void ValidateCertificate(
        in EmlProcessConstantCertificate certificate,
        EmlProcessConstantCertificate? previous)
    {
        EmlProcessConstantCheck check = EmlProcessConstantChecker.Check(in certificate);
        if (!check.Accepted) throw new InvalidDataException($"process-constant certificate failed: {check.Detail}");
        if (previous is null) return;
        EmlProcessConstantCertificate previousValue = previous.Value;
        EmlProcessConstantCheck monotone = EmlProcessConstantChecker.ValidateMonotoneLift(in previousValue, in certificate);
        if (!monotone.Accepted) throw new InvalidDataException($"process-constant lift failed: {monotone.Detail}");
    }

    private void SaveProcessConstantState(CkptWriter writer)
    {
        writer.Section(ProcessConstantTag);
        writer.I32(_processExactHighWater);
        WriteProcessState(writer, in _catalanProcess);
        WriteProcessState(writer, in _zeta3Process);
    }

    private void LoadProcessConstantState(CkptReader reader)
    {
        if (!reader.TryExpect(ProcessConstantTag))
        {
            _processExactHighWater = 0;
            _catalanProcess = EmlProcessConstants.CreateCatalanState();
            _zeta3Process = EmlProcessConstants.CreateZeta3State();
            return;
        }
        _processExactHighWater = reader.I32();
        _catalanProcess = ReadProcessState(reader);
        _zeta3Process = ReadProcessState(reader);
        ValidateLoadedState(_catalanProcess);
        ValidateLoadedState(_zeta3Process);
    }

    private static void WriteProcessState(CkptWriter writer, in EmlProcessConstantState state)
    {
        writer.I32((int)state.Algorithm);
        writer.I32(state.Version);
        writer.I64(state.Terms);
        writer.I64(state.FuelSpent);
        writer.Str(state.PartialSum.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.Str(state.PartialSum.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static EmlProcessConstantState ReadProcessState(CkptReader reader)
    {
        EmlProcessConstantAlgorithms algorithm = (EmlProcessConstantAlgorithms)reader.I32();
        int version = reader.I32();
        long terms = reader.I64();
        long fuel = reader.I64();
        BigInteger numerator = BigInteger.Parse(reader.Str(), System.Globalization.CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.Parse(reader.Str(), System.Globalization.CultureInfo.InvariantCulture);
        return new EmlProcessConstantState(algorithm, version, terms, fuel, new EmlExactRational(numerator, denominator));
    }

    private static void ValidateLoadedState(EmlProcessConstantState state)
    {
        if (state.Terms == 0) return;
        EmlProcessConstantCertificate certificate = EmlProcessConstants.Certify(in state);
        EmlProcessConstantCheck check = EmlProcessConstantChecker.Check(in certificate);
        if (!check.Accepted) throw new InvalidDataException($"loaded process-constant state failed: {check.Detail}");
    }
}
