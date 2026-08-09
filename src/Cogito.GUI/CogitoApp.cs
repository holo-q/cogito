using Bob.Render;

using Vibekit.Bob;
using Vibekit.Core;

namespace Cogito.GUI;

/// The Cogito.GUI application — a live dashboard of a grammar-induction run, read straight off the
/// run dir's `curve.tsv`. Mirrors sorsrs's `SorsrsApp` shape: an
/// `App<CogitoWorkspace, BobFrame>` whose `Run()` hands itself to a `BobWindow` (which owns the GLFW/
/// GL window, fonts, input, and the VSync frame loop). Every VSync tick, `Frame` drains the tail and
/// paints the active surface across the full window.
public sealed class CogitoApp : App<CogitoWorkspace, BobFrame>
{
	private readonly string _curvePath;

	public CogitoApp(string curveTsvPath) => _curvePath = curveTsvPath;

	public override string Title => "cogito-gui — reading the mind";

	public override void OnStartup()
		=> Workspace.Bind(new CurveTailReader(_curvePath));

	public override void OnShutdown() { }

	public override void Frame(in BobFrame frame)
	{
		Workspace.Tick();                                  // drain whatever the run appended since last frame
		Workspace.Active?.Paint(frame.WithBounds(Mathtek.Rect.Sized(0, 0, frame.width, frame.height)));
	}

	public void Run(int width = 1280, int height = 800)
	{
		using var window = new BobWindow(this);
		window.Run(width, height);
	}
}
