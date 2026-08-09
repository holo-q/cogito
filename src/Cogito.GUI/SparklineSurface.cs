using System.Numerics;

using Bob.Config;
using Bob.Render;
using Bob.Widgets;

using Mathtek;

namespace Cogito.GUI;

/// The curve sparkline surface: a vertical stack of lanes, one per key curve
/// column, each drawn as a live `Sparkline` that scrolls as the run grows. Columns
/// are resolved by NAME against whatever schema the tailed run carries (rich trunk vs 10-col
/// mesh), so a column the run doesn't have is simply skipped — the same surface reads both.
///
/// It reads the tail buffers straight inside `Paint` (the ISurface contract: the surface owns domain
/// state + style; gl/atlas/cam/bounds arrive with the frame). No per-frame allocation past a single
/// reused scratch buffer that linearizes each column's ring into the sparkline's `ReadOnlySpan`.
public sealed class SparklineSurface : ISurface
{
	private readonly CurveTailReader _reader;
	// Fully qualified: our own namespace `Cogito.GUI` shadows the bare `GUI` type from Bob.Widgets.
	private readonly Bob.Widgets.GUI _gui    = new();
	private readonly WidgetStyle     _wstyle = WidgetStyle.Default();
	private readonly Vector4         _stroke = new(0.45f, 0.78f, 0.95f, 0.92f);
	private readonly float[]         _scratch = new float[CurveTailReader.Capacity];

	/// The ranked columns to surface, richest-signal first. Resolved by name at paint time; the ones
	/// the current run's header carries get a lane, the rest are silently absent. Spans both schemas:
	/// the trunk names (coverage/maxspan/…) and the mesh names (real/dream/vest_n0/…) both appear,
	/// and a run shows whichever subset it wrote.
	private static readonly string[] Lanes =
	[
		// — trunk rich schema —
		"coverage", "maxspan", "cvz", "meanz", "vest_rate", "vest_peer", "vest_n0",
		"dreams_peer", "rules", "compressed", "churn", "births",
		// — mesh schema (distinct names) —
		"real", "dream", "vested", "vest_total",
	];

	public SparklineSurface(CurveTailReader reader) => _reader = reader;

	public string SurfaceName => "curve";

	public void Paint(BobFrame ctx)
	{
		Rect       area  = ctx.bounds;
		GlRenderer gl    = ctx.gl;
		var        cam   = ctx.cam;
		GlyphAtlas atlas = ctx.atlas;

		// Header line: run path + row count (the "am I live?" readout).
		float pad     = 16f;
		float x       = area.x + pad;
		float top     = area.y + pad;
		string status = _reader.HasHeader
			? $"{Path.GetFileName(_reader.RunPath.TrimEnd('/'))}   rows {_reader.RowCount}   cols {_reader.ColumnNames.Length}"
			: $"waiting for {_reader.RunPath} …";
		gl.DrawText(cam, x, top + 12f, status, new Vector4(0.85f, 0.90f, 1.00f, 1f), atlas);

		// Which lanes does THIS run actually carry?
		Span<int> present = stackalloc int[Lanes.Length];
		int laneCount = 0;
		for (int i = 0; i < Lanes.Length; i++)
			if (_reader.Column(Lanes[i]) is not null) present[laneCount++] = i;
		if (laneCount == 0) return;

		// Lay lanes out as a single column of row strips filling the area below the header.
		float gridTop    = top + 34f;
		float gridBottom = area.y + area.h - pad;
		float gridH      = MathF.Max(gridBottom - gridTop, 1f);
		float rowH       = gridH / laneCount;
		float labelW     = 190f;                      // gutter for "name   value"
		float plotX      = x + labelW;
		float plotW      = MathF.Max(area.x + area.w - pad - plotX, 8f);

		var ui = new WidgetUi(_gui, gl, ctx.theme, atlas, ctx.cam, _wstyle);

		for (int li = 0; li < laneCount; li++)
		{
			string       name = Lanes[present[li]];
			CurveColumn  col  = _reader.Column(name)!;
			float        laneY = gridTop + li * rowH;
			float        plotH = MathF.Max(rowH - 10f, 6f);
			var          plot  = Rect.Sized(plotX, laneY + 5f, plotW, plotH);

			// Lane backdrop — a faint tray so the lanes read as a stacked instrument panel.
			gl.FillRect(cam, plotX, laneY + 3f, plotW, plotH + 4f, new Vector4(1f, 1f, 1f, 0.03f));

			// Label + live latest value in the gutter.
			float value = col.Latest;
			string vtext = float.IsNaN(value) ? "—" : FormatValue(name, value);
			gl.DrawText(cam, x, laneY + rowH * 0.5f + 4f, name, new Vector4(0.72f, 0.78f, 0.92f, 1f), atlas);
			gl.DrawText(cam, x + labelW - 70f, laneY + rowH * 0.5f + 4f, vtext, new Vector4(0.95f, 0.85f, 0.55f, 1f), atlas);

			// The sparkline itself — linearize the ring into the reused scratch, then stroke it.
			int n = col.CopyTo(_scratch.AsSpan(0, (int)MathF.Min(plotW, CurveTailReader.Capacity)));
			if (n >= 2)
				Sparkline.Draw(gl, cam, plot, _scratch.AsSpan(0, n), _stroke);
		}

		ui.Flush();
	}

	/// Present-tense value formatting keyed by column magnitude: fractions (coverage, vest_rate) read
	/// as decimals, counts read as integers. Purely cosmetic — the sparkline carries the shape, this
	/// carries the current number.
	private static string FormatValue(string name, float v)
		=> name is "coverage" or "vest_rate" or "cvz" or "meanz"
			? v.ToString("F3")
			: MathF.Abs(v) >= 1000f ? v.ToString("F0") : v.ToString("0.##");
}
