# Cogito architecture

Cogito is a C# runtime for inducing and operating discrete grammars over byte and token
sequences. The engine can apply Re-Pair induction once to a corpus or maintain a persistent tape,
update its grammar from appended events, generate spans, evaluate them, and checkpoint the
combined state.

## Tape

`Tape` is the mutable sequence store. Each appended event carries its bytes, source, provenance,
event role, and stable `TapeEventID`. The tape supports single appends and atomic append
transactions. It tracks append and non-append revisions separately so incremental consumers can
distinguish new material from mutations of existing state.

`TapePacketCreator` owns the packet schemas. `Journal` records the runtime transitions associated
with those packets and line-flushes them while a run is active. Older events may leave resident
memory after their durable journal and checkpoint state has been committed.

Implementation: `src/Cogito/Tape/`.

## Grammar induction

`Engine.Induce` is the batch path. It accepts bytes, tokens, or a `Tape` and returns the induced
symbol tape and `RePairResult`. The result contains the rule set, encoded tape, rule-use data, and
reconstruction information.

`Loom` is the persistent path. `SpliceAppended` receives tape events appended since the previous
revision and updates its arenas, pair indexes, grammar publication, and installed revision without
reconstructing the complete input. Loom state is serialized into Cortex checkpoints.

The kernel also contains reconstruction, weighted induction, grammar analysis, merge tracing,
coupling graphs, SimHash calculations, and content-addressed grammar objects.

Implementation: `src/Cogito/Kernel/`.

## Cortex

`CortexConfig` supplies the curriculum, step count, seed, action budget, generation parameters,
induction cadence, learning controls, policy controls, stop conditions, and checkpoint cadence.

A Cortex run performs these transitions:

1. The curriculum supplies corpus spans or task observations.
2. Admitted material enters the tape with its source, provenance, and role.
3. Registered actions and tools operate on the current state.
4. Loom or batch induction updates the grammar.
5. Generation produces candidate spans from the installed grammar.
6. Evaluators and policies classify outcomes and append observations.
7. Enabled `SelfStream`, `Homeostat`, `Energy`, `Rhythm`, and consolidation controls update their
   state.
8. The run appends journal and curve rows and writes checkpoints.

Cross-reflection, near-duplicate detection, anti-unification, shedding, policy trials, EML
deliberation, and homeostatic control are independent mounts selected by configuration.

Implementation: `src/Cogito/Drive/`.

## Persistence

Stateful commands allocate `runs/<lineage>_<NNNN>/` beneath the project root. `NNNN` is the next
unused integer for that lineage.

| artifact | contents |
|---|---|
| `config.txt` | persisted runtime configuration and digest |
| `curve.tsv` | line-flushed per-step measurements |
| `journal.log` | line-flushed transition records |
| `checkpoint.bin` | base checkpoint image |
| `checkpoint.delta` | mutations after the base image |
| `checkpoint.delta.tail` | delta-tail commit state |
| grammar artifact | installed grammar revision checked by SHA-256 |

Checkpoint writes use a sibling temporary file, flush it to disk, and atomically replace the
destination. Resume loads the base image and committed delta, validates configuration and grammar
state, restores mounted subsystems, and truncates incremental artifacts to the checkpoint horizon.

Implementation: `src/Cogito/Drive/Checkpoint.cs`, `src/Cogito/spine/Run.cs`.

## Source topology

| directory | responsibility |
|---|---|
| `Agent/` | repository navigation, localization, tool calls, retrieval, and repository-loop evaluation |
| `Drive/` | Cortex, curricula, policy, consolidation, checkpoints, and experiment orchestration |
| `Eml/` | exact-rational EML expressions, evaluation, rewriting, anti-unification, and populations |
| `Exec/` | tape virtual machine and Weft execution |
| `Kernel/` | induction, grammar representation, reconstruction, publication, and analysis |
| `Llm/` | optional external language-model calls used by the `rag` commands |
| `Mesh/` | coupling counts, graph walks, generators, node birth, and mesh homeostasis |
| `Probes/` | structural measurements and paired probes |
| `Recursion/` | branch execution, deep rematching, matched controls, and marathon runs |
| `spine/` | entry point, CLI, corpus intake, run allocation, and artifact rendering |
| `Tape/` | event storage, packet schemas, journal, memory, and reflection state |

`Cogito.GUI` is a separate executable. It reads run artifacts such as `curve.tsv` and renders
them through Vibekit/Bob. It does not own or mutate Cortex state.
