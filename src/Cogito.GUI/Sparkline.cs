using System.Numerics;

using Bob.Render;

using Mathtek;

namespace Cogito.GUI;

/// The sparkline itself. Bob.GUI carried a MiniGraphWidget once; it does not any more, and it
/// should not — a curve lane is cogito's instrument, not a general widget, and owning it here
/// means the dashboard's chart can follow the curve schema instead of a shared widget's taste.
///
/// Drawn as one thin filled column per sample against the renderer's rect primitive: no line
/// primitive is needed, nothing allocates, and a run whose column is flat still reads as a line
/// rather than vanishing.
internal static class Sparkline
{
	/// A sample column narrower than this would alias into gaps at wide plots.
	private const float MinimumStrokeWidth = 1.5f;

	internal static void Draw(GlRenderer gl, in CameraState cam, Rect plot, ReadOnlySpan<float> samples, Vector4 color)
	{
		if (samples.Length < 2 || plot.w <= 0f || plot.h <= 0f) return;

		// The lane autoscales to what it currently holds: a curve's absolute range is unknown
		// (coverage is a fraction, meanz is a z-score, real/dream are counts), so a fixed scale
		// would render most lanes as a flat line at the floor.
		float low = float.PositiveInfinity, high = float.NegativeInfinity;
		foreach (float sample in samples)
		{
			if (float.IsNaN(sample)) continue;
			if (sample < low) low = sample;
			if (sample > high) high = sample;
		}
		if (float.IsInfinity(low) || float.IsInfinity(high)) return;
		float span = high - low;
		if (span <= float.Epsilon) span = 1f;                 // a flat run draws mid-lane, not at the floor

		float stride = plot.w / (samples.Length - 1);
		float stroke = MathF.Max(stride, MinimumStrokeWidth);
		for (int index = 0; index < samples.Length; index++)
		{
			float sample = samples[index];
			if (float.IsNaN(sample)) continue;
			float normalized = (sample - low) / span;
			float height = MathF.Max(normalized * plot.h, 1f);
			gl.FillRect(cam, plot.x + index * stride, plot.y + plot.h - height, stroke, height, color);
		}
	}
}
