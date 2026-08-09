namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// Immutable runnable bytes captured beside a loop-closure registration. The
/// registration points at this directory, while the manifest seals every file
/// needed by the apphost (managed, native, runtimeconfig, and deps siblings).
public readonly record struct LoopClosureAuthorityBundleFile(string RelativePath, string SHA256)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(RelativePath)
        && !RelativePath.Contains('\\')
        && !Path.IsPathRooted(RelativePath)
        && RelativePath.Split('/').All(static part => part is not "" and not "." and not "..")
        && SHA256.Length == 64 && SHA256.All(Uri.IsHexDigit);
}

internal static class LoopClosureAuthorityBundleStore
{
    internal const string RelativePath = "bundle";

    internal static (string AppHostPath, string AssemblyPath, string CensusSHA256, IReadOnlyList<LoopClosureAuthorityBundleFile> Files) Capture(
        string registrationPath, RunAuthorityBinary binary)
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidDataException("process image path is unavailable");
        string sourceRoot = Path.GetDirectoryName(Path.GetFullPath(processPath))
            ?? throw new InvalidDataException("process image directory is unavailable");
        string destination = ResolveRoot(registrationPath);
        if (Path.GetFullPath(sourceRoot).Equals(destination, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure authority bundle cannot capture itself");

        string assemblyPath = typeof(Cortex).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath)
            && !IsInside(Path.GetFullPath(assemblyPath), sourceRoot))
            throw new InvalidDataException("loop-closure assembly is outside the apphost directory");

        List<string> sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsInside(Path.GetFullPath(path), destination))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();
        RequireRuntimeFiles(sourceFiles, sourceRoot);

        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"loop-closure authority bundle already exists: {destination}");

        string temporary = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (string source in sourceFiles)
            {
                string relative = Normalize(Path.GetRelativePath(sourceRoot, source));
                string target = Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar));
                string? parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(source, target, overwrite: false);
                CopyUnixMode(source, target);
            }
            Directory.Move(temporary, destination);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }

        List<LoopClosureAuthorityBundleFile> files = ReadManifest(destination);
        string appHostPath = Normalize(Path.GetRelativePath(sourceRoot, processPath));
        string assemblyRelativePath = string.IsNullOrWhiteSpace(assemblyPath)
            ? ""
            : Normalize(Path.GetRelativePath(sourceRoot, assemblyPath));
        if (!files.Any(item => item.RelativePath == appHostPath)
            || !string.IsNullOrWhiteSpace(assemblyRelativePath) && !files.Any(item => item.RelativePath == assemblyRelativePath))
            throw new InvalidDataException("loop-closure authority bundle omits its loaded apphost or assembly");
        string census = ComputeCensus(RelativePath, files);
        return (appHostPath, assemblyRelativePath, census, files);
    }

    internal static void Validate(
        string registrationPath,
        string bundlePath,
        string appHostPath,
        string assemblyPath,
        string appHostName,
        string appHostSHA256,
        string assemblyName,
        string assemblySHA256,
        string censusSHA256,
        IReadOnlyList<LoopClosureAuthorityBundleFile> expectedFiles)
    {
        string root = ResolveRoot(registrationPath, bundlePath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"loop-closure authority bundle is missing: {root}");
        List<LoopClosureAuthorityBundleFile> observed = ReadManifest(root);
        if (!observed.SequenceEqual(expectedFiles))
            throw new InvalidDataException("loop-closure authority bundle manifest differs from its registration");
        if (!string.Equals(ComputeCensus(bundlePath, observed), censusSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure authority bundle census differs from its registration");
        string appHostFile = Path.Combine(root, appHostPath.Replace('/', Path.DirectorySeparatorChar));
        RequireHash(appHostFile, appHostSHA256, "bundled apphost");
        if (!string.Equals(Path.GetFileName(appHostFile), appHostName, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure bundled apphost identity differs from its registration");
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            string assemblyFile = Path.Combine(root, assemblyPath.Replace('/', Path.DirectorySeparatorChar));
            RequireHash(assemblyFile, assemblySHA256, "bundled assembly");
            if (!string.Equals(Path.GetFileName(assemblyFile), assemblyName, StringComparison.Ordinal))
                throw new InvalidDataException("loop-closure bundled assembly identity differs from its registration");
        }
    }

    internal static string ResolveRoot(string registrationPath, string bundlePath = RelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        if (Path.IsPathRooted(bundlePath) || bundlePath.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException("loop-closure authority bundle path is not a safe registration-relative path");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(registrationPath)) ?? ".", bundlePath));
    }

    internal static string ComputeCensus(string bundlePath, IReadOnlyList<LoopClosureAuthorityBundleFile> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Normalize(bundlePath)}\n"));
        foreach (LoopClosureAuthorityBundleFile file in files.OrderBy(static item => item.RelativePath, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes($"{file.SHA256}  {file.RelativePath}\n"));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static bool VerifyFixture(TextWriter output)
    {
        string root = Run.HomePath($".loop-closure-authority-bundle-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string registrationPath = Path.Combine(root, "loop-closure-registration.ron");
        try
        {
            RunAuthorityBinary binary = RunAuthority.CurrentBinaryIdentity();
            (string appHostPath, string assemblyPath, string census, IReadOnlyList<LoopClosureAuthorityBundleFile> files)
                = Capture(registrationPath, binary);
            Validate(registrationPath, RelativePath, appHostPath, assemblyPath,
                binary.ProcessName, binary.ProcessSHA256, binary.AssemblyName, binary.AssemblySHA256, census, files);
            string bundleRoot = ResolveRoot(registrationPath);
            string appHost = Path.Combine(bundleRoot, appHostPath.Replace('/', Path.DirectorySeparatorChar));
            bool identities = Path.GetFileName(appHost) == binary.ProcessName
                && (!string.IsNullOrWhiteSpace(assemblyPath)
                    ? Path.GetFileName(assemblyPath) == binary.AssemblyName
                    : string.IsNullOrWhiteSpace(binary.AssemblyName));
            string tamperedPath = Path.Combine(bundleRoot, files[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] original = File.ReadAllBytes(tamperedPath);
            File.AppendAllText(tamperedPath, "tamper");
            bool tamperRejected;
            try
            {
                Validate(registrationPath, RelativePath, appHostPath, assemblyPath,
                    binary.ProcessName, binary.ProcessSHA256, binary.AssemblyName, binary.AssemblySHA256, census, files);
                tamperRejected = false;
            }
            catch (InvalidDataException) { tamperRejected = true; }
            File.WriteAllBytes(tamperedPath, original);
            output.WriteLine($"  registration bundle · files={files.Count} · identities={(identities ? "exact" : "DRIFT")} · tamper={(tamperRejected ? "rejected" : "ACCEPTED")} · {(identities && tamperRejected ? "PASS" : "FAIL")}");
            return identities && tamperRejected;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static List<LoopClosureAuthorityBundleFile> ReadManifest(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Normalize(Path.GetRelativePath(root, path)), StringComparer.Ordinal)
            .Select(path => new LoopClosureAuthorityBundleFile(
                Normalize(Path.GetRelativePath(root, path)),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToList();

    private static void RequireRuntimeFiles(IReadOnlyList<string> files, string sourceRoot)
    {
        if (!files.Any(path => Path.GetFileName(path).EndsWith(".runtimeconfig.json", StringComparison.Ordinal)))
            throw new FileNotFoundException("loop-closure authority bundle requires a runtimeconfig.json", sourceRoot);
        if (!files.Any(path => Path.GetFileName(path).EndsWith(".deps.json", StringComparison.Ordinal)))
            throw new FileNotFoundException("loop-closure authority bundle requires a deps.json", sourceRoot);
    }

    private static void RequireHash(string path, string expected, string role)
    {
        if (!File.Exists(path) || Directory.Exists(path)) throw new FileNotFoundException($"{role} is missing", path);
        string observed = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{role} bytes differ from the registered authority");
    }

    private static bool IsInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.Ordinal);
    }

    private static string Normalize(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static void CopyUnixMode(string source, string target)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try { File.SetUnixFileMode(target, File.GetUnixFileMode(source)); }
        catch (PlatformNotSupportedException) { }
    }
}
