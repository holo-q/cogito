namespace Cogito.GUI;

/// Tails a run's `curve.tsv` live, without perturbing the training engine. cogito opens the file
/// `FileShare.Read` + `AutoFlush` and appends one row per step; this reader holds its own read-only
/// handle at a byte offset, drains whatever whole lines have been flushed since last poll, and folds
/// each into per-column rolling buffers. Zero writes, no RNG, never checkpointed — invisible to the
/// determinism Vow.
///
/// HEADER-DRIVEN by contract: cogito writes two curve schemas — the trunk drive's rich 41–56-column
/// row (coverage/maxspan/cvz/vest_*/…) and the mesh driver's 10-column row (tape/real/dream/
/// vest_n0/…). The reader parses the FIRST line as the column names, builds name→index, and exposes
/// `Column(name)` — so one reader serves both schemas and survives the column list growing. A view
/// asks for the columns it wants by NAME and skips the ones this run doesn't carry.
public sealed class CurveTailReader
{
	private readonly string _path;
	private          long   _offset;                 // byte position we've consumed up to (whole lines only)
	private          string _partial = "";           // trailing bytes of an incompletely-flushed final line

	private readonly Dictionary<string, int> _colIndex = new();
	private          string[]                _colNames = [];
	private          CurveColumn[]           _cols     = [];
	private          int                     _rows;

	/// Rolling capacity per column (samples retained for the sparkline window). A curve is ~one row
	/// per training step; 2048 covers a long live view while staying a fixed, tiny footprint.
	public const int Capacity = 2048;

	public CurveTailReader(string curveTsvPath)
	{
		_path = curveTsvPath;
	}

	public string   RunPath   => _path;
	public bool     HasHeader => _colNames.Length > 0;
	public int      RowCount  => _rows;
	public string[] ColumnNames => _colNames;

	/// The rolling buffer for a named column, or null if this run's schema doesn't carry it.
	public CurveColumn? Column(string name)
		=> _colIndex.TryGetValue(name, out int i) ? _cols[i] : null;

	/// Drain everything appended since the last poll. Cheap when nothing changed (a length check + a
	/// short read); O(new lines) otherwise. Safe to call every frame — the file may not yet exist (the
	/// run hasn't started) or may be mid-write (we only fold COMPLETE lines, buffering the tail).
	public void Poll()
	{
		if (!File.Exists(_path)) return;
		FileStream fs;
		try
		{
			fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		} catch (IOException)
		{
			return; // transient (writer holds an exclusive moment) — try again next frame
		}
		using (fs)
		{
			long len = fs.Length;
			if (len < _offset) { Reset(); }        // file was truncated / replaced (a new run into the same path)
			if (len == _offset && _partial.Length == 0) return;

			fs.Seek(_offset, SeekOrigin.Begin);
			using var sr = new StreamReader(fs);
			string chunk = sr.ReadToEnd();
			_offset += System.Text.Encoding.UTF8.GetByteCount(chunk);

			string text = _partial + chunk;
			int start = 0;
			while (true)
			{
				int nl = text.IndexOf('\n', start);
				if (nl < 0) break;
				ReadOnlySpan<char> line = text.AsSpan(start, nl - start).TrimEnd('\r');
				Consume(line);
				start = nl + 1;
			}
			_partial = text[start..];              // hold the unterminated remainder for next poll
		}
	}

	private void Reset()
	{
		_offset  = 0;
		_partial = "";
		_rows    = 0;
		_colIndex.Clear();
		_colNames = [];
		_cols     = [];
	}

	private void Consume(ReadOnlySpan<char> line)
	{
		if (line.IsEmpty) return;
		if (_colNames.Length == 0) { ParseHeader(line); return; }
		FoldRow(line);
	}

	private void ParseHeader(ReadOnlySpan<char> line)
	{
		var names = new List<string>();
		foreach (Range seg in Split(line))
			names.Add(line[seg].ToString());
		_colNames = names.ToArray();
		_cols     = new CurveColumn[_colNames.Length];
		for (int i = 0; i < _colNames.Length; i++)
		{
			_colIndex[_colNames[i]] = i;
			_cols[i] = new CurveColumn(Capacity);
		}
	}

	private void FoldRow(ReadOnlySpan<char> line)
	{
		int col = 0;
		foreach (Range seg in Split(line))
		{
			if (col >= _cols.Length) break;
			ReadOnlySpan<char> cell = line[seg];
			// Non-numeric verdict cells (mom_band, refactor, loop, excursion) parse to NaN → the column
			// simply holds a flat 0 lane; numeric columns are the sparkline feed. Empty cells (reserved
			// off-path columns) also fall through to 0.
			_cols[col].Push(float.TryParse(cell, out float v) ? v : float.NaN);
			col++;
		}
		_rows++;
	}

	/// Split a tab-separated line into field ranges without allocating substrings per cell.
	private static IEnumerable<Range> Split(ReadOnlySpan<char> line)
	{
		// ReadOnlySpan can't cross an iterator boundary, so materialize ranges eagerly into a list.
		// A curve row is ≤56 cells — trivially cheap.
		string s = line.ToString();
		var ranges = new List<Range>();
		int start = 0;
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '\t') { ranges.Add(start..i); start = i + 1; }
		}
		ranges.Add(start..s.Length);
		return ranges;
	}
}

/// A fixed-capacity rolling window of one curve column's samples. Overwrites oldest on overflow;
/// linearizes into a caller-owned scratch span for the sparkline draw (oldest→newest, left→right).
public sealed class CurveColumn
{
	private readonly float[] _buf;
	private          int     _head;   // next write slot
	private          int     _count;

	public CurveColumn(int capacity) => _buf = new float[capacity];

	public int   Count  => _count;
	public float Latest => _count == 0 ? float.NaN : _buf[(_head - 1 + _buf.Length) % _buf.Length];

	public void Push(float v)
	{
		_buf[_head] = v;
		_head       = (_head + 1) % _buf.Length;
		if (_count < _buf.Length) _count++;
	}

	/// Copy the window oldest→newest into `dst`; returns the count written (≤ dst.Length, ≤ Count).
	/// NaN samples pass through — the sparkline treats them as gaps in the min/max, which is fine for
	/// the flat verdict-column lanes. The view sizes `dst` to its lane width, so this is the sparkline
	/// feed straight into `MiniGraphWidget.Draw`.
	public int CopyTo(Span<float> dst)
	{
		int n     = Math.Min(_count, dst.Length);
		int oldest = (_head - _count + _buf.Length) % _buf.Length;
		int from  = (oldest + (_count - n)) % _buf.Length;   // if dst is smaller, take the NEWEST n
		for (int i = 0; i < n; i++)
			dst[i] = _buf[(from + i) % _buf.Length];
		return n;
	}
}
