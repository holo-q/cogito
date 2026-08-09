namespace Cogito;

// Entry point — install the trace elevator (VTR stderr + journald sinks), then route to the
// System.CommandLine command tree (Cogito.Cli.CliRoot). The tree IS the CLI: it owns
// tokenize → bind → validate → dispatch across every verb, each verb's SetAction calling its
// body. No verb given ⇒ System.CommandLine renders the grouped help.
internal static class Program
{
    private static int Main(string[] args)
    {
        Trace.Init();                                   // wire the sinks before the first emit
        Trace.Note("cogito — the deterministic scribe");
        return Cogito.Cli.CliRoot.Run(args);
    }
}
