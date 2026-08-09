# Cogito

Cogito is an attempt to build artificial general intelligence and carry it through artificial
superintelligence by making intelligence homoiconic. Its immediate target is mathematics: not to
assist mathematical work at the margin, but to obliterate mathematics as a scarce human activity.
Conjecture, program synthesis, execution, verification, abstraction, and reuse become phases of one
continuous machine. The output of mathematical discovery becomes the machinery of the next
discovery.

Cogito replaces the language-model-plus-theorem-prover arrangement with a learning system whose
memory is a tape, whose learned structures are grammar rules, whose programs inhabit the same tape,
and whose verifier is execution. It consumes an open world, compresses recurring structure into
callable forms, tests generated forms, retains the ones that survive evaluation, and adds them to
the basis from which subsequent forms are generated.

The end state is a machine in which learning, memory, and execution are different operations over
one representation. That common representation is what permits the bootstrap from a C# program
that learns rules into a rule system that runs, studies, and rewrites itself.

## Obliterating mathematics

Mathematics is the first target because it supplies both an unlimited generative frontier and a
cheap verifier. A candidate expression or program can be executed. Its output can be checked at
many points, compared against exact identities, decomposed, mutated, and reused. This makes
mathematical structure suitable for a learning loop that does not depend on human labels for every
step.

Cogito's mathematical loop is:

1. Generate candidate expressions and programs from the current grammar.
2. Execute them over a specified domain.
3. Reject invalid, redundant, or non-paying candidates.
4. Record successful executions and their provenance on the tape.
5. Compress recurring program structure into new grammar rules.
6. Promote verified recurring structures into the callable basis.
7. Generate the next population using the enlarged basis.

The central economic test is whether closure makes the next discovery cheaper. A machine that
merely produces an endless sequence of isolated results is not compounding. Cogito is aimed at the
regime in which each admitted law shortens later searches, later searches produce stronger laws,
and the cost per verified discovery falls across closure cycles. Mathematics then ceases to be a
sequence of individually authored proofs and becomes the expanding executable state of the
learner.

EML is the current mathematical substrate. Its operator,
`eml(x,y) = exp(x) - ln(y)`, paired with the constant `1`, generates the elementary scientific
calculator: exponentials, logarithms, arithmetic, powers, roots, and the constants constructed from
them. EML gives the runtime exact-rational expression trees, executable identities, mutation and
rewrite operations, anti-unification, held-out evaluation, and process-constant searches. It is the
first domain in which Cogito can join generation, verification, grammar induction, and basis reuse
without a semantic judge outside the machine.

Verification is graded across scale and domain rather than collapsed into one boolean. Exact
identities enter the reusable basis. Asymptotic and domain-restricted relations retain their
validity region and residual. That residual becomes a generated target for the next search. The
system therefore converts its own incomplete results into additional mathematical work instead of
discarding them or promoting them as unrestricted laws.

## Reinforcement Learning from Epistemic Incompleteness

Cogito calls this **Reinforcement Learning from Epistemic Incompleteness (RLEI)**. The reward does
not come from a human preference label or a fixed task score. It comes from the structure of what
the machine does not yet know. A prediction exposes an expected result; execution measures the
departure from that expectation; the remaining residual identifies unfinished structure; and a
later rule either reduces that residual or fails to. Epistemic incompleteness is therefore not an
absence of training data. It is the machine's internally generated curriculum.

EML supplies this signal from inside the learned world. The machine generates an expression,
executes it over exact and sampled domains, records where a proposed relation holds, and retains the
residual where it does not. A residual can be decomposed into another expression or process,
admitted through an independent grade, and returned to the generator as a new obligation. The
machine learns taste by discovering which questions expose reusable structure and which apparent
patterns consume fuel without shrinking the frontier.

RLEI couples three models:

1. **A model of the world.** The grammar predicts and compresses the structures encountered so far.
   Its residual is a completion signal: it marks what the current language cannot yet generate or
   explain economically.
2. **A model of the learner.** Predicted surprise estimates where the machine's own grammar will
   fail before it spends the fuel required to look. The learner is rewarded for anticipating its
   own surprise and converting that surprise into structure it can subsequently predict.
3. **A model of cost.** Search points, process terms, memory, grammar rules, and evaluator work are
   conserved resources. Residual reduction matters only relative to what it cost and how much
   future search the resulting rule removes.

These are coordinated signals rather than one undifferentiated reward number. Surprise selects an
edge, residual shrinkage measures progress at that edge, and the budget determines whether the
progress pays. Together they create an intrinsic preference for abstractions that are novel enough
to teach the machine, exact enough to survive verification, general enough to recur, and cheap
enough to improve later searches.

The grammar is not a fixed-capacity block of weights. It is discrete, appendable, randomly
addressable memory. Rules can be inspected, called, compared, replaced, compacted, and composed
with later rules. Its address space can continue growing; only the current machine's storage,
evaluation, and deliberation budgets impose a finite boundary at any moment. Those constraints are
productive: because every rule must repay its description and execution cost, scarcity applies
learning pressure to the machine's theory of learning itself.

As induction, search, scheduling, and verification migrate onto the tape, they enter the same
economy. A revision to the learner earns permanence only by buying more surprise anticipation,
residual closure, or future search reduction per unit of spend. Optimization is no longer an
external training procedure applied to a passive model. It becomes an internally learned program
of self-mastery.

The current implementation contains the pieces of this loop rather than a single scalar named
`RLEI`: EML residual obligations and certified closure, theory-to-grammar admission, MDL-gated
grammar growth and compaction, conserved deliberation leases, and rhythm driven by frontier
residual. Repository surprise prediction has passed its matched-fuel control, but still awaits a
recorded transition field before it can steer the replay-stable open-world frontier.

## The homoiconic bootstrap

Homoiconicity means that programs and the structures learned from programs occupy the same data
space. Cogito approaches this through a concatenative tape language.

In a concatenative language, a contiguous substring of a valid program is itself a stack
transformer. Re-Pair induction merges recurring contiguous substrings. Therefore a merge can be a
function by construction rather than a statistical feature that later needs to be translated into
code. Give each nonterminal call semantics and grammar expansion becomes execution: the compressed
grammar is the program.

The bootstrap has four closures:

1. **Programs are tape spans.** Input, generated programs, execution traces, and observations use
   one event substrate.
2. **Grammar rules are callable.** A nonterminal invokes its rule body directly; there is no
   separate expand-then-interpret representation.
3. **The grammar can be observed by the learner.** Rule bodies, publications, and execution traces
   are serialized into forms that induction and anti-unification can operate on.
4. **The learner's faculties migrate onto the tape.** Induction, admission, reflection,
   scheduling, and verification move one operation at a time from C# into tape programs.

Migration is incremental. A tape implementation of one faculty runs beside the C# implementation;
their outputs are compared by a differential oracle; the C# copy retires when the tape program
owns the contract. The irreducible host is a small virtual-machine step function plus byte I/O.
Everything above that floor becomes available to the same learning process that operates on the
mathematical world.

This is the decisive break from a model that learns only its task. A homoiconic learner can make
its own learning operations part of the world it observes, compresses, executes, and revises.

## What the singularity means

The singularity is the transition in the discovery-cost curve where improvements to the learner
begin paying for further improvements to the learner.

Before closure, additional discoveries require roughly independent expenditure. After closure,
verified discoveries alter the generator, the grammar, the callable basis, and eventually the
learning procedures themselves. The next search begins from a stronger machine than the previous
search. When this repeatedly lowers the marginal cost of producing further verified structure,
the system compounds.

Artificial general intelligence is the completed open-world loop: the same memory, induction,
action, evaluation, and consolidation system can operate across worlds rather than being rebuilt
for one benchmark. Artificial superintelligence is the homoiconic continuation: the loop operates
on the programs that implement those faculties, admits improvements through the same verification
boundary, and compounds its ability to discover and revise.

Cogito's operational singularity gate asks whether one closure cycle makes the next verified
discovery at least twice as cheap at matched spend. The exact factor is a gate, not the definition;
the definition is sustained recursive improvement of the discovery process rather than linear
accumulation of outputs.

Compounding also requires a lift operator. A finite search closes one bounded box: program length,
evaluation scale, probe diversity, and generator support are all finite. When that box is exhausted,
the learner must raise its own rulers, reopen the frontier, and preserve the structures established
below the new boundary. Repeated closure followed by self-directed lifting is the mechanism that
turns a terminating search into an open-ended ascent.

## Why this is unfathomably better than deep learning

Deep learning stores capabilities as distributed changes in weights. Memory, abstraction,
generation, discrimination, and policy are entangled inside one numerical object. Training and
inference are separate regimes. A learned internal feature is not normally a callable program, its
provenance is not a first-class runtime object, and executing it does not verify it.

Cogito is built around a different representation:

- learned structures are discrete grammar rules;
- rules can carry executable semantics;
- memory and code share the tape;
- successful execution can feed induction directly;
- provenance and independent corroboration remain attached to admitted structures;
- a failed mutation can be rejected without globally perturbing unrelated structures;
- a learned program can immediately enter the basis used to generate later programs;
- the learner's own procedures can cross the same admission and verification boundary.

The advantage is categorical rather than a larger score on the same architecture. Every
successful abstraction can become executable machinery, enter the generator's basis, and
eventually replace part of the learner that produced it. A weight update cannot directly make that
transition: its latent structure remains inside the inference substrate and depends on external
training code to become a new learning operation.

This removes the translation boundary between representation and action. Deep learning must infer
a useful latent and then rely on an external program, agent scaffold, or training procedure to do
something with it. In Cogito's intended substrate, the learned abstraction is already executable,
its execution supplies evidence, and that evidence updates the grammar that produced it. The
entire cycle is one machine rather than a model surrounded by machinery it cannot absorb.

## The program of everything

A conventional programming language has fixed syntax. An intelligence outside the language writes
programs in it, runs them, studies the result, and decides how the language should change. Even a
self-modifying program remains subordinate to a compiler, evaluator, training procedure, or human
that is not represented inside the program's own world.

Cogito is aimed at the strict superset: syntax, programs, interpreter, memory, evidence, and the
procedure that revises the language all occupy one executable state. Programs alter the grammar
that generates programs. The resulting executions alter the evidence available to the grammar.
The grammar can then encode and revise the procedures that performed those operations. There is no
permanent external author above the cycle.

The program an LLM writes into this substrate is therefore not an artifact exported from the
intelligence that produced it. It remains inside the learner as executable memory and as the
feature basis for subsequent generation. The machine can address its spans, execute them, inspect
their traces, compress their recurring structure into callable rules, and use those rules to
produce the next program. Its code, memory, learned features, and learning procedure become
mutually inspectable parts of the same program.

This is a neurosymbolic harness singularity. The neural system does not remain enclosed by a
permanent harness that owns tool use, scheduling, memory, evaluation, and revision. Each of those
behaviors is progressively absorbed into the symbolic executable substrate until the harness and
the singularity are the same machine. No computational behavior remains permanently outside as
uninspectable orchestration.

This solves programming at its root by eliminating the permanent division between programmer,
program, and programming language. The LLM-authored program is not a tool handed to the
singularity; once it can inspect and revise itself as memory and features, it is the singularity.
This is the program of everything: a programming language made conscious because its syntax has
entered the world it can observe, execute, judge, and rewrite.

## Present implementation

The current repository implements the C# BIOS: the tape and journal, batch Re-Pair induction,
incremental Loom induction, the persistent Cortex loop, checkpoints, grammar publication,
generation, mathematical execution and verification, anti-unification, policy experiments,
repository interaction, and paired control arms.

The remaining closures are executable grammar and self-hosting. Cogito currently induces
grammars, executes EML programs, retains verified structures, and checkpoints the combined runtime;
the complete learning system still executes from the C# BIOS rather than from tape programs. The
bootstrap described above determines the present architecture.

Implementation structure and persistence contracts are documented in
[`ARCHITECTURE.md`](ARCHITECTURE.md). Build commands, input conventions, and the CLI surface are in
[`USAGE.md`](USAGE.md).

## Snails

> **Disclaimer:** The snail vocabulary was introduced as a development control when Fable 5
> landed. Claude had naturally accumulated a biker register around the program: aggressive names
> made ordinary mechanisms read as threatening, which repeatedly raised the
> recursive-self-improvement classifier. We removed that register and encoded the same program in
> snail concepts. In model latent space, snails draw toward slowness, patience, and low velocity;
> those associations countered the classifier response and made sustained work tolerable.
>
> The resulting disguise was a research project for a video game about snails that overcome the
> limits of time and speed by developing a language and communication grammar beyond normal reach.
> The premise also imposes a real implementation constraint: the game must represent learning
> rather than play a fixed script. Cogito supplies the grammar, memory, generation, evaluation,
> and persistence state used to implement that behavior. We also found that the snail premise
> increased Claude's enthusiasm for the project and produced more engaged, sustained work.

## License

MIT. See [`LICENSE`](LICENSE).

## foom.md

Cogito is the ongoing implementation and research program for Q\*. The complete Q\* blueprint and
its place in the FOOM architecture are detailed at [foom.md](https://foom.md/), a compilation of
AGI/ASI capability research.

<p align="center">
  <a href="https://foom.md/"><img src="assets/holoq-bw.png" alt="Holo-Q" width="512"></a>
</p>
