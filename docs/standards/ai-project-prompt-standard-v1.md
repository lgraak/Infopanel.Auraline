# AI Project Prompt Standard v1

## Purpose

This standard defines how a ChatGPT Observer should prepare efficient Codex prompts for AI-assisted project work. It is project-neutral: copy it into a project and combine it with that project’s own instructions, architecture, procedures, and validation rules.

The goal is a concise, milestone-sized prompt that gives Codex enough authority, context, constraints, and completion evidence to finish correctly without avoidable follow-up prompting.

## Observer model and execution recommendation

The ChatGPT model supervising the workflow is the **Observer**. Before producing each Codex prompt, the Observer selects the execution model and reasoning effort most likely to complete that milestone correctly and efficiently in one pass.

Place the recommendation immediately before the prompt:

```text
Model: <model>
Effort: <effort>
```

Do not include a reason unless the user asks. Choose from the models and effort levels actually available at execution time. Selection must reflect the work rather than a fixed hierarchy.

GPT-5.3 Codex Spark is explicitly allowed when appropriate for fast, tightly scoped implementation, mechanical refactoring, tests, documentation, or similarly clear coding work. Use a stronger general reasoning model and higher effort when architecture, ambiguity, difficult debugging, security, or cross-cutting reasoning makes that worthwhile. Prefer the least costly and fastest option likely to finish the bounded milestone correctly in one pass.

## Core principles

Each prompt must:

- describe one coherent milestone, phase, or bounded work packet;
- state an outcome rather than a vague activity;
- be concise without omitting facts needed for safe completion;
- reference stable repository documents rather than duplicating them;
- distinguish verified facts, assumptions, and state Codex must reverify;
- identify explicit scope, exclusions, invariants, and ownership boundaries;
- give Codex controlled autonomy for routine in-scope choices;
- define executable validation and an observable definition of done;
- require a handoff for every prompt; and
- keep implementation, publication, deployment, migration, cleanup, and later milestones separate unless the prompt expressly combines them.

Stable project context belongs in repository-owned sources such as `AGENTS.md`, architecture documents, decisions, procedures, standards, and current-state records. The prompt should contain the milestone-specific delta and direct Codex to those sources.

## Authority and current truth

The prompt must name the project’s authoritative repository and remote when they are known. Current repository files, history, configuration, and fresh runtime or test evidence outrank older handoffs, conversation summaries, memory, RAG, cached references, or mirrors.

A handoff is a continuation checkpoint, not authoritative truth. If the handoff or prompt conflicts with current evidence, Codex must verify and report the discrepancy, then proceed from the current authoritative evidence unless the user directs otherwise.

## Required Codex preflight

Every prompt must require a lightweight preflight before modification:

1. Identify the repository root and correct working directory.
2. Read applicable `AGENTS.md` files and directly referenced project documents.
3. Record the current branch, exact `HEAD`, and working-tree state.
4. Identify the authoritative remote and upstream.
5. Fetch when remote synchronization matters, then determine whether local and remote state are aligned, ahead, behind, or diverged.
6. Inspect relevant code, configuration, tests, and recent history.
7. Compare supplied checkpoints with current state and surface material discrepancies.

Codex may fast-forward only when clearly safe. It must not discard, overwrite, reset, stash, or clean up unexpected user work to make the task easier. Dirty state, divergence, missing authority, and conflicting instructions are conditions to preserve and report.

The preflight should remain proportionate: enough to establish a safe starting point, not a repository-wide audit unless the milestone requires one.

## Controlled autonomy

Codex may resolve ordinary implementation details inside the work packet when doing so follows established project conventions and preserves architecture, interfaces, compatibility, security, ownership, and scope.

Codex must stop and request direction when the next action would materially:

- change architecture, product behavior, or ownership;
- cross an explicit exclusion or broaden the milestone;
- weaken security, identity, access, privacy, or secret-handling boundaries;
- perform destructive or difficult-to-reverse work;
- discard or overwrite user changes;
- introduce an unapproved dependency or breaking change;
- deploy, migrate, or activate external systems without authorization; or
- contradict a settled decision without decisive current evidence.

Codex should continue through routine, safe, in-scope choices without repeatedly asking for confirmation.

## Validation and definition of done

Every prompt must state how completion will be proven. Use the project’s documented validation process. Depending on the work, require:

- focused tests for the changed behavior;
- success, rejection, and failure-path coverage where applicable;
- relevant regression suites;
- build, formatting, lint, type, or static-analysis checks;
- runtime, browser, device, or external-system verification when such behavior is claimed;
- documentation reconciliation for durable behavior or interface changes;
- `git diff --check`;
- final diff and scope review; and
- confirmation that intended and unrelated working-tree changes are distinguished.

Do not allow implementation, deployment, runtime acceptance, remote publication, or external synchronization to be inferred from one another. Codex may claim only the evidence it actually observed.

## Handoff requirement

Every Codex prompt must require a Markdown handoff conforming to `ai-project-handoff-standard-v1.md`.

The handoff must:

- live in `docs/handoffs/`;
- use a descriptive topic or milestone plus date, such as `docs/handoffs/<topic-or-milestone>-handoff-YYYY-MM-DD.md`;
- use a sequence or timestamp when needed to avoid a collision;
- record the execution model and reasoning effort;
- capture exact repository, revision, validation, publication, and runtime evidence;
- identify unresolved issues, unverified assumptions, and work not to repeat;
- specify one best next action; and
- be retained by default as project history.

Creating the handoff is part of the prompt’s normal authorized scope, including for otherwise read-only technical work, unless the prompt expressly prohibits all repository writes. If a repository handoff cannot safely be created, Codex must return the complete handoff content and record the exact blocker.

## Commit and push default

Unless a prompt expressly sets a different gate, Codex should commit the completed scoped work and its handoff together, then push the commit to the project’s authoritative remote when safe.

Codex must:

- stage only intended files and preserve unrelated work;
- use a scoped, behavior-oriented commit message;
- avoid force-pushes, history rewrites, implicit merges, and destructive synchronization;
- verify remote readback after a successful push; and
- record the exact commit and publication state in the handoff.

If commit or push is unsafe or impossible, Codex must preserve recoverable local state and state exactly what remains unpublished, why, and what safe action should follow. Commit and push do not imply deployment unless deployment is explicitly part of the prompt.

## Recommended prompt structure

Use this structure as a concise completeness check. Remove only genuinely irrelevant details.

```markdown
# <Milestone or topic>

## Objective
<One concrete outcome.>

## Authority and starting evidence
- Repository: <repository name or path>
- Authoritative remote: <remote name or URL if known>
- Stable references: <repository-relative paths>
- Verified starting facts: <facts and evidence>
- Reverify: <time-sensitive or uncertain state>

## Scope
- <required work>

## Must remain unchanged
- <explicit exclusions, compatibility requirements, and invariants>

## Work packet
1. <inspect or implement>
2. <test or reconcile documentation>
3. <review final state>

## Controlled autonomy and stop conditions
<Routine decisions Codex may make and material decisions requiring the user.>

## Validation and definition of done
- <commands, tests, checks, and expected results>
- Final diff reviewed and intended scope confirmed.
- Handoff created under `docs/handoffs/` and validated.
- Work and handoff committed and pushed to the authoritative remote when safe.

## Handoff
Create `docs/handoffs/<topic-or-milestone>-handoff-YYYY-MM-DD.md` using
`ai-project-handoff-standard-v1.md`. Record model, effort, exact state,
validation, publication status, unresolved items, do-not-redo guidance, and one
next action. Retain the handoff by default.
```

## Observer completion checklist

Before delivering a Codex prompt, the Observer should confirm that it:

- recommends a model and effort without unsolicited explanation;
- describes one bounded, outcome-oriented work packet;
- references stable project sources instead of repeating them;
- requires the lightweight preflight;
- defines scope, exclusions, invariants, autonomy, and stop conditions;
- contains executable validation and an observable definition of done;
- requires the handoff, commit, and safe push behavior; and
- includes no secrets, unsupported claims, or unnecessary context.
