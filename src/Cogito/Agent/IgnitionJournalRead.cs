namespace Cogito;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class IgnitionJournalRead
{
    private const string DefaultStamp = "ignition_20260708T002231634Z";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(string? stampArg, string? outPath)
    {
        string stamp = string.IsNullOrWhiteSpace(stampArg) ? DefaultStamp : stampArg.Trim();
        string runsRoot = Path.Combine(ProjectRoot(), "runs");
        string reportDir = Path.Combine(runsRoot, stamp + "_report");
        Directory.CreateDirectory(reportDir);

        var cells = DiscoverCells(runsRoot, stamp);
        if (cells.Count == 0)
        {
            Console.Error.WriteLine($"no run dirs for {stamp}");
            return 1;
        }

        var termCache = new Dictionary<string, int>(StringComparer.Ordinal);
        var curveRows = new List<CurveRow>();
        foreach (var cell in cells)
        {
            string curvePath = Path.Combine(cell.Dir, "curve.tsv");
            if (!File.Exists(curvePath)) continue;
            var vestByInstance = ReadVestCounts(Path.Combine(cell.Dir, "journal.log"));
            foreach (var row in ReadCurve(cell, curvePath))
            {
                int termCount = QueryTermCount(cell.IntakeRoot, row.Instance, termCache);
                var shape = DeriveActions(row.Actions, row.Committed, termCount);
                int vest = vestByInstance.GetValueOrDefault(row.Instance);
                curveRows.Add(row with { QueryTerms = termCount, ActionsShape = shape, VestEvents = vest });
            }
        }

        var firstChosen = new List<FirstChosenRow>();
        var valueRows = new List<ValueCreditRow>();
        var selectionRows = new List<SelectionMassRow>();
        foreach (var cell in cells)
        {
            firstChosen.AddRange(ReadFirstChosen(cell, Path.Combine(cell.Dir, "first_chosen.tsv")));
            valueRows.AddRange(ReadValueCredit(cell, Path.Combine(cell.Dir, "value_credit.tsv")));
            selectionRows.AddRange(ReadSelectionMass(cell, Path.Combine(cell.Dir, "selection_mass.tsv")));
        }

        var report = RenderReport(stamp, cells, curveRows, valueRows, firstChosen, selectionRows);
        string path = string.IsNullOrWhiteSpace(outPath)
            ? Path.Combine(reportDir, "lane2_dead_data.md")
            : Path.GetFullPath(outPath);
        File.WriteAllText(path, report);
        Console.WriteLine(report);
        Console.WriteLine();
        Console.WriteLine($"wrote {Path.GetRelativePath(ProjectRoot(), path)}");
        return 0;
    }

    private static List<Cell> DiscoverCells(string runsRoot, string stamp)
    {
        var cells = new List<Cell>();
        foreach (var dir in Directory.GetDirectories(runsRoot, stamp + "_*").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            if (name.EndsWith("_report", StringComparison.Ordinal)) continue;
            string suffix = name[(stamp.Length + 1)..];
            var (plane, rest) = suffix.StartsWith("answer_leak_free_", StringComparison.Ordinal)
                ? ("answer_leak_free", suffix["answer_leak_free_".Length..])
                : ("standard", suffix.StartsWith("standard_", StringComparison.Ordinal) ? suffix["standard_".Length..] : suffix);

            int pass = 0;
            string split = rest;
            string arm = rest;
            if (rest.EndsWith("_heldout_pass1", StringComparison.Ordinal) || rest.EndsWith("_heldout_pass2", StringComparison.Ordinal))
            {
                pass = rest.EndsWith("_heldout_pass1", StringComparison.Ordinal) ? 1 : 2;
                arm = rest[..^"_heldout_pass1".Length];
                split = "heldout";
            }
            else if (rest.EndsWith("_revisited", StringComparison.Ordinal))
            {
                arm = rest[..^"_revisited".Length];
                split = "revisited";
            }
            else if (rest.EndsWith("_discarded_aestivation", StringComparison.Ordinal))
            {
                arm = rest[..^"_discarded_aestivation".Length];
                split = "discarded";
            }

            cells.Add(new Cell(dir, plane, PlaneLabel(plane), arm, ArmLabel(arm), split, pass, IntakeRoot(dir)));
        }
        return cells;
    }

    private static IEnumerable<CurveRow> ReadCurve(Cell cell, string path)
    {
        using var reader = new StreamReader(path);
        string? header = reader.ReadLine();
        if (header is null) yield break;
        var h = Header(header);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            yield return new CurveRow(
                cell,
                Get(f, h, "instance"),
                Get(f, h, "repo"),
                Int(Get(f, h, "pass")),
                Get(f, h, "split"),
                Bool(Get(f, h, "committed")),
                Bool(Get(f, h, "correct")),
                Bool(Get(f, h, "reached")),
                Bool(Get(f, h, "recalled")),
                Int(Get(f, h, "actions")),
                Get(f, h, "target_depth_band"),
                Get(f, h, "commit_depth_band"),
                Get(f, h, "commit_depth_log"),
                Int(Get(f, h, "commit_pick_count")),
                Bool(Get(f, h, "literal_answer_policy")),
                0,
                default,
                0);
        }
    }

    private static Dictionary<string, int> ReadVestCounts(string path)
    {
        var byStep = new Dictionary<int, int>();
        var byInstance = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path)) return byInstance;
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('\t', 3);
            if (f.Length < 2 || !int.TryParse(f[0], NumberStyles.Integer, Inv, out int step)) continue;
            if (f[1] == "vest") byStep[step] = byStep.GetValueOrDefault(step) + 1;
            else if (f[1] == "index" && f.Length == 3 && f[2].StartsWith("solve ", StringComparison.Ordinal))
            {
                string rest = f[2]["solve ".Length..];
                int cut = rest.IndexOf(" · ", StringComparison.Ordinal);
                if (cut > 0) byInstance[rest[..cut]] = byStep.GetValueOrDefault(step);
            }
        }
        return byInstance;
    }

    private static IEnumerable<FirstChosenRow> ReadFirstChosen(Cell cell, string path)
    {
        if (!File.Exists(path)) yield break;
        using var reader = new StreamReader(path);
        string? header = reader.ReadLine();
        if (header is null) yield break;
        var h = Header(header);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            yield return new FirstChosenRow(
                cell,
                Int(Get(f, h, "step")),
                Get(f, h, "instance"),
                Get(f, h, "site"),
                Get(f, h, "line"),
                Get(f, h, "rule"),
                Get(f, h, "identity"),
                Int(Get(f, h, "depth")),
                Int(Get(f, h, "uses")),
                Int(Get(f, h, "use_bin")),
                Int(Get(f, h, "depth_band")),
                Double(Get(f, h, "advantage")),
                Double(Get(f, h, "factor")),
                Get(f, h, "expansion"));
        }
    }

    private static IEnumerable<ValueCreditRow> ReadValueCredit(Cell cell, string path)
    {
        if (!File.Exists(path)) yield break;
        using var reader = new StreamReader(path);
        string? header = reader.ReadLine();
        if (header is null) yield break;
        var h = Header(header);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            yield return new ValueCreditRow(
                cell,
                Int(Get(f, h, "step")),
                Get(f, h, "instance"),
                Bool(Get(f, h, "correct")),
                Get(f, h, "arm"),
                Get(f, h, "lane"),
                Get(f, h, "source_rule"),
                Get(f, h, "credit_rule"),
                Double(Get(f, h, "before")),
                Double(Get(f, h, "after")),
                Double(Get(f, h, "delta")),
                Int(Get(f, h, "breadth")),
                Double(Get(f, h, "home")),
                Long(Get(f, h, "load")),
                Get(f, h, "sources"),
                Get(f, h, "expansion"));
        }
    }

    private static IEnumerable<SelectionMassRow> ReadSelectionMass(Cell cell, string path)
    {
        if (!File.Exists(path)) yield break;
        using var reader = new StreamReader(path);
        string? header = reader.ReadLine();
        if (header is null) yield break;
        var h = Header(header);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            yield return new SelectionMassRow(
                cell,
                Int(Get(f, h, "step")),
                Get(f, h, "instance"),
                Get(f, h, "site"),
                Get(f, h, "line"),
                Get(f, h, "mode"),
                Double(Get(f, h, "gain")),
                Double(Get(f, h, "base_mass")),
                Double(Get(f, h, "coupled_mass")),
                Double(Get(f, h, "max_factor")),
                Get(f, h, "picked_rules"));
        }
    }

    private static string RenderReport(string stamp, List<Cell> cells, List<CurveRow> rows, List<ValueCreditRow> valueRows, List<FirstChosenRow> firstChosen, List<SelectionMassRow> selectionRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Lane 2 dead-data journal reads");
        sb.AppendLine();
        sb.AppendLine($"Run set: `runs/{stamp}_*`.");
        sb.AppendLine("Reader: `cogito probe ignition-journal-read` over existing `curve.tsv`, `journal.log`, `value_credit.tsv`, `first_chosen.tsv`, and `selection_mass.tsv` only.");
        sb.AppendLine();
        sb.AppendLine("Journal boundary: the run did not emit one row per look. The exact emitted walk surface is final `actions`, `reached/recalled/committed/correct`, final depth labels, `journal.log` vest events, and sparse value-selection rows. The action mix below is the deterministic `ChooseAction` branch shape reconstructed from each fixture query's ranked term count plus final action count; it is not a hidden rerun.");
        sb.AppendLine();

        AppendRead1(sb, rows);
        AppendRead2(sb, cells, rows, valueRows, firstChosen, selectionRows);
        return sb.ToString();
    }

    private static void AppendRead1(StringBuilder sb, List<CurveRow> allRows)
    {
        var heldout = allRows.Where(r => r.Cell.Split == "heldout" && (r.Cell.Arm == "consolidation_only" || r.Cell.Arm == "both_frozen")).ToList();
        var rows = heldout.Where(r => r.Pass == 2).ToList();
        sb.AppendLine("## Read 1 - consolidation-transfer walk shape");
        sb.AppendLine();
        sb.AppendLine("### Pass-2 held-out aggregate");
        sb.AppendLine();
        sb.AppendLine("| plane | arm | n | solved | commit success | reached | recalled | all actions mean | actions-to-commit | all path dist | vest events mean |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|---:|");
        foreach (var g in rows.GroupBy(r => (r.Cell.Plane, r.Cell.Arm)).OrderBy(g => PlaneOrder(g.Key.Plane)).ThenBy(g => ArmOrder(g.Key.Arm)))
        {
            var list = g.ToList();
            sb.AppendLine($"| {PlaneLabel(g.Key.Plane)} | {ArmLabel(g.Key.Arm)} | {list.Count} | {Pct(list.Count(r => r.Correct), list.Count)} | {Pct(list.Count(r => r.Committed && r.Correct), list.Count(r => r.Committed))} | {Pct(list.Count(r => r.Reached), list.Count)} | {Pct(list.Count(r => r.Recalled), list.Count)} | {Mean(list.Select(r => (double)r.Actions)):0.00} | {CommitActionMean(list):0.00} | {Hist(list.Select(r => r.Actions))} | {Mean(list.Select(r => (double)r.VestEvents)):0.00} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Pass-2 target-depth walk shape");
        sb.AppendLine();
        sb.AppendLine("| plane | arm | target depth | n | solved | all actions mean | actions-to-commit | all path dist | commit path dist | action mix per instance | vest dist |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => (r.Cell.Plane, r.Cell.Arm, Depth: r.TargetDepth)).OrderBy(g => PlaneOrder(g.Key.Plane)).ThenBy(g => ArmOrder(g.Key.Arm)).ThenBy(g => DepthOrder(g.Key.Depth)))
        {
            var list = g.ToList();
            var shape = Sum(list.Select(r => r.ActionsShape));
            sb.AppendLine($"| {PlaneLabel(g.Key.Plane)} | {ArmLabel(g.Key.Arm)} | {g.Key.Depth} | {list.Count} | {Pct(list.Count(r => r.Correct), list.Count)} | {Mean(list.Select(r => (double)r.Actions)):0.00} | {CommitActionMean(list):0.00} | {Hist(list.Select(r => r.Actions))} | {Hist(list.Where(r => r.Committed).Select(r => r.Actions))} | {shape.PerInstance(list.Count)} | {Hist(list.Select(r => r.VestEvents))} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Deep-target pass split");
        sb.AppendLine();
        sb.AppendLine("| plane | arm | pass | n | solved | all actions mean | actions-to-commit | reached | recalled | all path dist |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var g in heldout.Where(r => r.TargetDepth == "deep").GroupBy(r => (r.Cell.Plane, r.Cell.Arm, r.Pass)).OrderBy(g => PlaneOrder(g.Key.Plane)).ThenBy(g => ArmOrder(g.Key.Arm)).ThenBy(g => g.Key.Pass))
        {
            var list = g.ToList();
            sb.AppendLine($"| {PlaneLabel(g.Key.Plane)} | {ArmLabel(g.Key.Arm)} | {g.Key.Pass} | {list.Count} | {Pct(list.Count(r => r.Correct), list.Count)} | {Mean(list.Select(r => (double)r.Actions)):0.00} | {CommitActionMean(list):0.00} | {Pct(list.Count(r => r.Reached), list.Count)} | {Pct(list.Count(r => r.Recalled), list.Count)} | {Hist(list.Select(r => r.Actions))} |");
        }

        sb.AppendLine();
        sb.AppendLine("**Read 1 verdict.** Consolidation does not transfer a better procedure on this journal. The reconstructed action mix stays the same branch skeleton: one recall when recall exists, then greps until the commit gate answers or the run abstains. STANDARD consolidation-only is worse than the floor on solved rate and deep targets while spending the same committed action budget. ANSWER-LEAK-FREE gains solved rate over the floor; its all-action mean drops because fewer worlds run to abstention, while actions-to-commit stays matched. The gain is destination/remembered-structure, not a visibly different walk policy. A future value branch must beat consolidation on held-out success while also moving these walk-shape rows, not just replaying the same path with different memories.");
        sb.AppendLine();
    }

    private static void AppendRead2(StringBuilder sb, List<Cell> cells, List<CurveRow> curveRows, List<ValueCreditRow> valueRows, List<FirstChosenRow> firstChosen, List<SelectionMassRow> selectionRows)
    {
        sb.AppendLine("## Read 2 - class-attribution oracle");
        sb.AppendLine();

        var curveByRunInstance = new Dictionary<(string A, string B), CurveRow>(StringComparerTuple.Instance);
        foreach (var row in curveRows.OrderBy(r => r.Pass))
            curveByRunInstance[(row.Cell.Dir, row.Instance)] = row;
        var chosenWithOutcome = new List<ChosenOutcome>();
        foreach (var choice in firstChosen)
            if (curveByRunInstance.TryGetValue((choice.Cell.Dir, choice.Instance), out var row))
                chosenWithOutcome.Add(new ChosenOutcome(choice, row));

        sb.AppendLine("### Journal completeness");
        sb.AppendLine();
        sb.AppendLine("| plane | split | arm | value rows | wrong-value rows | first-chosen rows | selection rows |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
        foreach (var g in cells.Where(c => (c.Plane == "standard" || c.Plane == "answer_leak_free") && (c.Split == "revisited" || c.Split == "heldout"))
                     .GroupBy(c => (c.Plane, c.Split, c.Pass, c.Arm))
                     .OrderBy(g => PlaneOrder(g.Key.Plane)).ThenBy(g => SplitOrder(g.Key.Split)).ThenBy(g => g.Key.Pass).ThenBy(g => ArmOrder(g.Key.Arm)))
        {
            var dirs = g.Select(c => c.Dir).ToHashSet(StringComparer.Ordinal);
            int vc = valueRows.Count(r => dirs.Contains(r.Cell.Dir));
            int wrong = valueRows.Count(r => dirs.Contains(r.Cell.Dir) && !r.Correct);
            int fc = firstChosen.Count(r => dirs.Contains(r.Cell.Dir));
            int sm = selectionRows.Count(r => dirs.Contains(r.Cell.Dir));
            string split = g.Key.Split == "heldout" ? $"heldout p{g.Key.Pass}" : g.Key.Split;
            sb.AppendLine($"| {PlaneLabel(g.Key.Plane)} | {split} | {ArmLabel(g.Key.Arm)} | {vc} | {wrong} | {fc} | {sm} |");
        }

        var meta = firstChosen
            .GroupBy(c => c.Expansion, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Depth).First(), StringComparer.Ordinal);

        var trainScores = ClassGrains.ToDictionary(
            grain => grain,
            grain => valueRows
                .Where(v => v.Cell.Split == "revisited")
                .GroupBy(v => (v.Cell.Plane, Class: ClassOf(v.Expansion, meta, grain, v.Lane)))
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0, x.Delta))));

        var eval = chosenWithOutcome
            .Where(x => x.Row.Cell.Split == "heldout" && x.Row.Pass == 2 && (x.Row.Cell.Arm == "credit_only" || x.Row.Cell.Arm == "full"))
            .SelectMany(x =>
            {
                var rows = new List<ClassEval>(ClassGrains.Length);
                foreach (var grain in ClassGrains)
                {
                    string cls = ClassOf(x.Choice.Expansion, meta, grain, x.Choice.Identity);
                    double score = trainScores[grain].GetValueOrDefault((x.Row.Cell.Plane, cls));
                    rows.Add(new ClassEval(GrainLabel(grain), x.Row.Cell.Plane, x.Row.Cell.Arm, cls, x.Row.Instance, x.Row.Correct, score, x.Choice.Advantage, x.Choice.Depth, x.Choice.Identity));
                }
                return rows;
            })
            .ToList();

        sb.AppendLine();
        sb.AppendLine("### Pre-outcome class score on actual pass-2 held-out value picks");
        sb.AppendLine();
        sb.AppendLine("| grain | plane | arm | first-chosen rows | instances | selected-instance success | mean train class score log10(correct) | mean train class score log10(wrong) |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");
        foreach (var g in eval.GroupBy(x => (x.Grain, x.Plane, x.Arm)).OrderBy(g => GrainOrder(g.Key.Grain)).ThenBy(g => PlaneOrder(g.Key.Plane)).ThenBy(g => ArmOrder(g.Key.Arm)))
        {
            var list = g.ToList();
            var instances = list.GroupBy(x => x.Instance).Select(gr => gr.First()).ToList();
            var correct = list.Where(x => x.Correct).Select(x => Math.Log10(1 + x.TrainScore)).ToList();
            var wrong = list.Where(x => !x.Correct).Select(x => Math.Log10(1 + x.TrainScore)).ToList();
            sb.AppendLine($"| {g.Key.Grain} | {PlaneLabel(g.Key.Plane)} | {ArmLabel(g.Key.Arm)} | {list.Count} | {instances.Count} | {Pct(instances.Count(x => x.Correct), instances.Count)} | {MeanText(correct)} | {MeanText(wrong)} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Top selected classes by revisited class score");
        sb.AppendLine();
        sb.AppendLine("| grain | plane | class | selected rows | selected instances | selected success | train class score | examples |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|");
        foreach (var g in eval.Where(x => x.TrainScore > 0)
                     .GroupBy(x => (x.Grain, x.Plane, x.Class))
                     .OrderByDescending(g => g.Sum(x => x.TrainScore))
                     .ThenBy(g => GrainOrder(g.Key.Grain))
                     .ThenBy(g => PlaneOrder(g.Key.Plane))
                     .ThenBy(g => g.Key.Class, StringComparer.Ordinal)
                     .Take(16))
        {
            var list = g.ToList();
            var inst = list.GroupBy(x => x.Instance).Select(x => x.First()).ToList();
            string examples = string.Join(", ", list.Select(x => x.Instance).Distinct(StringComparer.Ordinal).Take(4));
            sb.AppendLine($"| {g.Key.Grain} | {PlaneLabel(g.Key.Plane)} | `{Esc(g.Key.Class, 96)}` | {list.Count} | {inst.Count} | {Pct(inst.Count(x => x.Correct), inst.Count)} | {g.Sum(x => x.TrainScore):0.###} | {examples} |");
        }

        var heldValue = valueRows.Where(v => v.Cell.Split == "heldout").ToList();
        int heldWrongValue = heldValue.Count(v => !v.Correct);
        sb.AppendLine();
        sb.AppendLine("**Read 2 verdict.** The oracle class-value branch stays unbuilt from this readout. The journal does not contain the requested blur slot-class IDs or per-look candidate class rows, and `value_credit.tsv` is an outcome-credit log: held-out value rows are emitted only after successful vesting, with wrong-value rows at " + heldWrongValue.ToString(Inv) + ". That makes post-outcome class aggregation tautological, not a transferable value signal. The only pre-outcome class surface is sparse `first_chosen.tsv`; on pass-2 held-out value picks, even coarse family aggregation does not point to useful transfer where instance-grain value failed. The live next build is replay-priority / consolidation scheduling, unless Lane 3 first adds a real pre-outcome class trace.");
        sb.AppendLine();
    }

    private static ActionShape DeriveActions(int actions, bool committed, int termCount)
    {
        int nonTerminal = committed ? Math.Max(0, actions - 1) : actions;
        int recall = 0, grep = 0, gen = 0;
        for (int look = 0; look < nonTerminal; look++)
        {
            if (look == 0 && termCount > 0) recall++;
            else if (look > 0 && grep < termCount && look < Math.Max(3, termCount)) grep++;
            else gen++;
        }
        return new ActionShape(recall, grep, gen, committed ? 1 : 0, committed ? 0 : 1);
    }

    private static int QueryTermCount(string intakeRoot, string instance, Dictionary<string, int> cache)
    {
        string key = intakeRoot + "\n" + instance;
        if (cache.TryGetValue(key, out int cached)) return cached;
        if (intakeRoot.Length == 0) return cache[key] = 8;
        string dir = Path.Combine(intakeRoot, instance);
        string queryPath = Path.Combine(dir, "query.txt");
        string sitesPath = Path.Combine(dir, "sites.jsonl");
        if (!File.Exists(queryPath) || !File.Exists(sitesPath)) return cache[key] = 8;
        string query = File.ReadAllText(queryPath);
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        int nSites = 0;
        foreach (var line in File.ReadLines(sitesPath))
        {
            if (line.Length == 0) continue;
            string text = "";
            try
            {
                using var doc = JsonDocument.Parse(line);
                text = doc.RootElement.GetProperty("text").GetString() ?? "";
            }
            catch (JsonException) { }
            if (text.Length == 0) continue;
            nSites++;
            foreach (var tok in Tokens(text).Distinct(StringComparer.Ordinal))
                df[tok] = df.GetValueOrDefault(tok) + 1;
        }
        nSites = Math.Max(1, nSites);
        var scored = new List<(string Tok, double Score)>();
        foreach (var tok in Tokens(query).Distinct(StringComparer.Ordinal))
        {
            if (tok.Length < 4) continue;
            int freq = df.GetValueOrDefault(tok);
            if (freq == 0) continue;
            scored.Add((tok, -(double)freq / nSites * 4 + tok.Length * 0.05));
        }
        scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Tok, b.Tok));
        return cache[key] = Math.Min(8, scored.Count);
    }

    private static IEnumerable<string> Tokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsLetterOrDigit(text[i]) || text[i] == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                yield return text[start..i];
            }
            else i++;
        }
    }

    private static string ClassOf(string expansion, Dictionary<string, FirstChosenRow> meta, ClassGrain grain, string fallbackRole)
    {
        string family = BlurFamily(expansion);
        string depth = "unknown";
        string role = fallbackRole.Length == 0 ? "unknown" : fallbackRole;
        if (meta.TryGetValue(expansion, out var m))
        {
            depth = DepthBand(m.DepthBand);
            if (m.Identity.Length > 0) role = m.Identity;
        }

        return grain switch
        {
            ClassGrain.Family => family,
            ClassGrain.FamilyDepth => $"{family} | depth={depth}",
            _ => $"{family} | depth={depth} | role={role}"
        };
    }

    private static string BlurFamily(string expansion)
    {
        string s = expansion.ToLowerInvariant();
        s = Regex.Replace(s, @"src/mod_[a-z]+_\d+\.py", "src/mod_ROLE_#.py", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\d+", "#", RegexOptions.CultureInvariant);
        foreach (var role in SyntheticRoles)
            s = Regex.Replace(s, $@"\b{Regex.Escape(role)}\b", "ROLE", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return s.Length <= 72 ? s : s[..72] + "...";
    }

    private static readonly string[] SyntheticRoles =
    [
        "traversal", "payload", "quantizer", "manifold", "cursor", "frontier", "checkpoint", "vector",
        "residual", "matrix", "boundary", "cache", "registry", "scheduler", "session", "envelope"
    ];

    private static string IntakeRoot(string dir)
    {
        string path = Path.Combine(dir, "manifest");
        if (!File.Exists(path)) return "";
        foreach (var line in File.ReadLines(path))
            if (line.StartsWith("intake=", StringComparison.Ordinal))
                return line["intake=".Length..].Trim();
        return "";
    }

    private static Dictionary<string, int> Header(string header)
    {
        var h = new Dictionary<string, int>(StringComparer.Ordinal);
        var parts = header.Split('\t');
        for (int i = 0; i < parts.Length; i++) h[parts[i]] = i;
        return h;
    }

    private static string Get(string[] f, Dictionary<string, int> h, string name)
        => h.TryGetValue(name, out int i) && i >= 0 && i < f.Length ? f[i] : "";

    private static bool Bool(string s) => s is "1" or "true" or "True";
    private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : 0;
    private static long Long(string s) => long.TryParse(s, NumberStyles.Integer, Inv, out long v) ? v : 0;
    private static double Double(string s) => double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : double.NaN;

    private static ActionShape Sum(IEnumerable<ActionShape> shapes)
    {
        int recall = 0, grep = 0, generation = 0, answer = 0, abstain = 0;
        foreach (var s in shapes)
        {
            recall += s.Recall; grep += s.Grep; generation += s.Generation; answer += s.Answer; abstain += s.Abstain;
        }
        return new ActionShape(recall, grep, generation, answer, abstain);
    }

    private static string Hist(IEnumerable<int> values)
        => string.Join(" ", values.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));

    private static double Mean(IEnumerable<double> values)
    {
        double sum = 0; int n = 0;
        foreach (double v in values) { if (double.IsNaN(v)) continue; sum += v; n++; }
        return n == 0 ? 0 : sum / n;
    }

    private static double MeanOrNan(IEnumerable<double> values)
    {
        double sum = 0; int n = 0;
        foreach (double v in values) { if (double.IsNaN(v)) continue; sum += v; n++; }
        return n == 0 ? double.NaN : sum / n;
    }

    private static double CommitActionMean(IEnumerable<CurveRow> rows)
        => Mean(rows.Where(r => r.Committed).Select(r => (double)r.Actions));

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.50);

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var xs = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        if (xs.Length == 0) return 0;
        int idx = Math.Clamp((int)Math.Ceiling(p * xs.Length) - 1, 0, xs.Length - 1);
        return xs[idx];
    }

    private static string Pct(int num, int den)
        => den <= 0 ? "n/a" : $"{num}/{den} ({100.0 * num / den:0.0}%)";

    private static string Esc(string s, int cap)
    {
        s = s.Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);
        return s.Length <= cap ? s : s[..cap] + "...";
    }

    private static string PlaneLabel(string plane) => plane == "answer_leak_free" ? "ANSWER-LEAK-FREE" : "STANDARD";
    private static int PlaneOrder(string plane) => plane == "standard" ? 0 : 1;
    private static int SplitOrder(string split) => split == "revisited" ? 0 : split == "heldout" ? 1 : 2;
    private static readonly ClassGrain[] ClassGrains = [ClassGrain.Family, ClassGrain.FamilyDepth, ClassGrain.FamilyDepthRole];

    private static string ArmLabel(string arm) => arm switch
    {
        "both_frozen" => "BOTH-FROZEN",
        "consolidation_only" => "CONSOLIDATION-ONLY",
        "credit_only" => "CREDIT-ONLY",
        "full" => "FULL",
        _ => arm
    };

    private static int ArmOrder(string arm) => arm switch
    {
        "both_frozen" => 0,
        "consolidation_only" => 1,
        "credit_only" => 2,
        "full" => 3,
        _ => 9
    };

    private static int DepthOrder(string depth) => depth switch { "shallow" => 0, "mid" => 1, "deep" => 2, _ => 3 };
    private static string DepthBand(int band) => band < 0 ? "none" : band <= 1 ? "shallow" : band == 2 ? "mid" : "deep";
    private static string GrainLabel(ClassGrain grain) => grain switch
    {
        ClassGrain.Family => "family",
        ClassGrain.FamilyDepth => "family+depth",
        _ => "family+depth+role"
    };

    private static int GrainOrder(string grain) => grain switch { "family" => 0, "family+depth" => 1, "family+depth+role" => 2, _ => 3 };

    private static string MeanText(IEnumerable<double> values)
    {
        double v = MeanOrNan(values);
        return double.IsNaN(v) ? "n/a" : v.ToString("0.00", Inv);
    }

    private static string ProjectRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "cogito.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? Directory.GetCurrentDirectory();
    }

    private sealed record Cell(string Dir, string Plane, string PlaneLabel, string Arm, string ArmLabel, string Split, int Pass, string IntakeRoot);

    private sealed record CurveRow(
        Cell Cell,
        string Instance,
        string Repo,
        int Pass,
        string Split,
        bool Committed,
        bool Correct,
        bool Reached,
        bool Recalled,
        int Actions,
        string TargetDepth,
        string CommitDepthBand,
        string CommitDepthLog,
        int CommitPickCount,
        bool LiteralAnswerPolicy,
        int QueryTerms,
        ActionShape ActionsShape,
        int VestEvents);

    private readonly record struct ActionShape(int Recall, int Grep, int Generation, int Answer, int Abstain)
    {
        public string PerInstance(int n)
            => n <= 0 ? "n/a" : $"recall {Recall / (double)n:0.00}, grep {Grep / (double)n:0.00}, gen {Generation / (double)n:0.00}, answer {Answer / (double)n:0.00}, abstain {Abstain / (double)n:0.00}";
    }

    private sealed record FirstChosenRow(Cell Cell, int Step, string Instance, string Site, string Line, string Rule, string Identity, int Depth, int Uses, int UseBin, int DepthBand, double Advantage, double Factor, string Expansion);
    private sealed record ValueCreditRow(Cell Cell, int Step, string Instance, bool Correct, string Arm, string Lane, string SourceRule, string CreditRule, double Before, double After, double Delta, int Breadth, double Home, long Load, string Sources, string Expansion);
    private sealed record SelectionMassRow(Cell Cell, int Step, string Instance, string Site, string Line, string Mode, double Gain, double BaseMass, double CoupledMass, double MaxFactor, string PickedRules);
    private readonly record struct ChosenOutcome(FirstChosenRow Choice, CurveRow Row);
    private readonly record struct ClassEval(string Grain, string Plane, string Arm, string Class, string Instance, bool Correct, double TrainScore, double Advantage, int Depth, string Identity);
    private enum ClassGrain { Family, FamilyDepth, FamilyDepthRole }

    private sealed class StringComparerTuple : IEqualityComparer<(string A, string B)>
    {
        public static readonly StringComparerTuple Instance = new();
        public bool Equals((string A, string B) x, (string A, string B) y) => StringComparer.Ordinal.Equals(x.A, y.A) && StringComparer.Ordinal.Equals(x.B, y.B);
        public int GetHashCode((string A, string B) obj) => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.A), StringComparer.Ordinal.GetHashCode(obj.B));
    }
}
