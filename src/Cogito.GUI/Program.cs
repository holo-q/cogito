namespace Cogito.GUI;

/// Entrypoint. `cogito-gui [runDir]` opens the window tailing `<runDir>/curve.tsv`. With no arg it
/// picks the most-recently-written run under `./runs` (the run you're most likely watching). Accepts
/// either a run DIR or a curve.tsv path directly. The window stays live as the run grows; point it at
/// a finished run to replay its curve, or a running trunk/mesh drive to watch it think.
public static class Program
{
	public static int Main(string[] args)
	{
		string? target = args.Length > 0 ? args[0] : NewestRunDir();
		if (target is null)
		{
			Console.Error.WriteLine("cogito-gui: no run dir given and none found under ./runs");
			Console.Error.WriteLine("  usage: cogito-gui [runDir | curve.tsv]");
			return 2;
		}

		string curve = ResolveCurvePath(target);
		Console.WriteLine($"cogito-gui: tailing {curve}");
		new CogitoApp(curve).Run();
		return 0;
	}

	/// A run dir → its curve.tsv; a *.tsv path → itself.
	private static string ResolveCurvePath(string target)
		=> target.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
			? Path.GetFullPath(target)
			: Path.GetFullPath(Path.Combine(target, "curve.tsv"));

	/// The run subdir with the freshest curve.tsv under ./runs — the one being written right now, if
	/// any drive is live, else the last one that finished.
	private static string? NewestRunDir()
	{
		string runs = Path.Combine(Environment.CurrentDirectory, "runs");
		if (!Directory.Exists(runs)) return null;
		string? best = null;
		DateTime bestTime = DateTime.MinValue;
		foreach (string dir in Directory.EnumerateDirectories(runs))
		{
			string curve = Path.Combine(dir, "curve.tsv");
			if (!File.Exists(curve)) continue;
			DateTime t = File.GetLastWriteTimeUtc(curve);
			if (t > bestTime) { bestTime = t; best = dir; }
		}
		return best;
	}
}
