using Bob.Render;

namespace Cogito.GUI;

/// The dashboard's persistent state across frames: the live tail reader and the surface set it feeds.
/// It currently holds the sparkline surface; additional surfaces can register here when the dashboard
/// grows tab navigation for vest-by-source, criticality, metabolism, and the grammar graph.
///
/// Parameterless-constructible per the `App<TWorkspace,TFrame>` contract; the real wiring (which run
/// dir to tail) is injected in `CogitoApp.OnStartup` via `Bind`, since the app knows the run path and
/// the workspace is `new()`'d before that path is known.
public sealed class CogitoWorkspace
{
	public CurveTailReader? Reader  { get; private set; }
	public ISurface?        Active  { get; private set; }

	public void Bind(CurveTailReader reader)
	{
		Reader = reader;
		Active = new SparklineSurface(reader);
	}

	/// Drain the tail once per frame, before the active surface paints from it.
	public void Tick() => Reader?.Poll();
}
