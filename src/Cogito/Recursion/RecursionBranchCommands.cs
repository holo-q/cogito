namespace Cogito.Cli;

using System.CommandLine;

internal static class RecursionBranchCommands
{
    internal static Command RenderBranches()
    {
        Option<string?> output = new("--output")
        {
            Description = "directory for recursion_branches.ron and recursion_branches.md"
        };
        Command command = new("render-branches", "write the canonical recursion verdict and downstream gate authority")
        {
            output
        };
        command.SetAction(parse =>
        {
            string outputDirectory = parse.GetValue(output) ?? Directory.GetCurrentDirectory();
            RecursionBranchAuthorityStore.WriteArtifacts(outputDirectory);
            RecursionBranchAuthority authority = RecursionBranchAuthorityStore.CreateCurrent();
            Console.WriteLine($"  recursion branches · {authority.Digest} · {outputDirectory}");
            return 0;
        });
        return command;
    }
}
