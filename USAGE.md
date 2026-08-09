# Cogito usage

## Build

The authored project files use the RON solution/project syntax consumed by `rk`. Generated
`.g.csproj`, `.g.slnx`, and `.g.cs` files are not committed.

Required checkout layout:

```text
repo-foom/cogito
repo-kit/vibekit
repo-os3/bob
repo-lib/Datasets.cs
repo-lib/Guitek
repo-lib/Mathtek
repo-lib/ronmamon
repo-lib/VTR.cs
```

The engine targets .NET 10 with C# 14. Its direct packages are `System.CommandLine`, `ScottPlot`,
and `Utf8StringInterpolation`.

```text
rk build src/Cogito/Cogito.csproj
rk run src/Cogito/Cogito.csproj -- --help
```

## Input data

No corpus is committed. Commands that consume a corpus or task set require its path as an
argument. Repository-local corpora and generated state are excluded from Git:

```text
data/
docs/
scratchpad/
runs/
tmp/
.tmp/
```

`cortex` accepts file and directory corpora through its curriculum configuration. `solve`, `nav`,
and `traces` accept task directories explicitly. Corpus-materialization commands write beneath
`data/` unless given another output path.

## Command-line interface

```text
cogito --help
cogito <command> --help
cogito <cluster> <command> --help
```

Top-level stateful commands:

| command | operation |
|---|---|
| `cogito cortex <corpus> <steps>` | run the curriculum, tape, grammar, generation, and consolidation cycle |
| `cogito solve <data-dir>` | run the Cortex-backed localization workload |
| `cogito nav <dir>` | evaluate repository localization in `frozen`, `dyn`, or `loop` mode |
| `cogito dreamcalc` | run the EML expression-generation and evaluation environment |

Command clusters:

| cluster | operations |
|---|---|
| `kernel` | induction, grammar inspection, reconstruction, export, coupling graphs, and differential verification |
| `drive` | mesh creation, seriation, criticality schedules, intake comparisons, and frontier benchmarks |
| `gate` | paired runs, loop-closure registration and certification, null arms, and fixtures |
| `probe` | structural assays over grammar depth, slots, lattices, rematching, and execution |
| `rag` | retrieval and optional LLM-assisted codec, generation, distillation, and curriculum commands |
| `eml` | EML benchmarks, Sheffer sweep, anti-unification, mint scaling, and process constants |
| `recursion` | branch rendering, matched forks, populations, marathons, policy assays, and fixtures |
| `tape` | checkpoint resume, trace-corpus materialization, and corpus materialization |
| `runs` | retention operations over `runs/` |

## Verification commands

Each command checks one equivalence:

```text
cogito kernel verify-induct <corpus>      # linear induction against the reference path
cogito kernel verify-loom <corpus>        # persistent splice/pump against batch induction
cogito kernel verify-weighted             # weighted-induction contract
cogito kernel verify-grammar-analysis     # grammar analysis against its differential oracle
cogito kernel verify-energy-incremental   # incremental Energy state against full recomputation
cogito kernel simhash-vectors             # fixed SimHash vectors and struct rows
```

## Runs and resume

Stateful commands allocate `runs/<lineage>_<NNNN>/`. The directory contains the configuration,
curve, journal, checkpoint, checkpoint deltas, grammar revisions, and command-specific reports.

Resume accepts an explicit directory, a project-relative directory, or a basename beneath
`runs/`:

```text
cogito tape resume <run-dir>
```

Run retention is managed through `cogito runs gc`.
