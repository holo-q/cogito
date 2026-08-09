namespace Cogito.Cli;

using System.CommandLine;

internal static class ProcessConstantCommands
{
    internal static Command Report()
    {
        Option<long?> fuel = new("--fuel") { Description = "exact terms in each base certificate (default 12)" };
        Option<long?> liftFuel = new("--lift-fuel") { Description = "additional exact terms in each monotone lift (default 12)" };
        Command command = new("process-constants", "certify Catalan and zeta(3) from resumable exact-rational processes")
        {
            fuel,
            liftFuel,
        };
        command.SetAction(parse => EmlProcessConstantAssay.Run(
            parse.GetValue(fuel) ?? 12,
            parse.GetValue(liftFuel) ?? 12));
        return command;
    }
}
