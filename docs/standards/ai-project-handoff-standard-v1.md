# AI Project Handoff Standard v1

## Purpose

This standard defines the Markdown continuation checkpoint that Codex must create after every prompt. It is project-neutral and intended for AI-assisted engineering and project work.

A handoff allows another session, model, workstation, or collaborator to continue without rediscovering the completed work. It records evidence and decisions; it does not replace the repository or current system state.

## Authority rule

**A handoff is a continuation checkpoint, not authoritative truth.**

Current repository files and history, the authoritative remote, current configuration, and fresh runtime or test evidence win when they conflict with a handoff. The next worker must verify time-sensitive facts before acting and must record material discrepancies rather than silently forcing reality to match the handoff.

The handoff must distinguish:

- implemented in the repository;
- committed locally;
- pushed to the authoritative remote;
- deployed or activated;
- validated in the relevant runtime or external system;
- documented or planned only; and
- unresolved or unverified.

None of these states implies another.

## Location, naming, and retention

Store handoffs in:

```text
docs/handoffs/
```

Use a descriptive milestone or topic plus the date:

```text
docs/handoffs/<topic-or-milestone>-handoff-YYYY-MM-DD.md
```

Use lowercase repository-appropriate naming. If more than one handoff for the same topic is created on one date, append a sequence or timestamp rather than overwriting an earlier checkpoint.

Retain handoffs by default as chronological project history. Supersede an older handoff with a new checkpoint and link the relationship when useful. Do not delete or rewrite historical handoffs merely because the project advanced. Correct or remove exposed secrets immediately if one is discovered; history remediation then follows the project’s security process.

## Required metadata

Immediately below the H1 title, include compact metadata containing at least:

- `Date`: handoff date, with time and timezone when timing matters;
- `Status`: completed, partial, blocked, read-only, or another precise state;
- `Model`: the Codex execution model that performed the work;
- `Effort`: the reasoning effort used;
- `Repository`: repository name and, when useful, checkout context;
- `Branch`: branch or detached state;
- `HEAD`: exact revision when Git is in use; and
- `Authoritative remote`: remote name and destination, or `not configured`.

Use `unknown` or `not applicable` rather than inventing a value. Never include credentials, tokens, private keys, passwords, session material, secret values, or unnecessarily sensitive host data.

## Required section order

Every handoff must contain the following 14 H2 sections exactly once and in this order:

1. `## Objective`
2. `## Authoritative Sources`
3. `## Execution Context`
4. `## Current Repository State`
5. `## Current Known-Good State`
6. `## Completed Work`
7. `## Decisions Made`
8. `## Files Changed`
9. `## Validation Completed`
10. `## Production State Versus Repository State`
11. `## Unresolved Issues and Unverified Assumptions`
12. `## Safety, Rollback, and Access Considerations`
13. `## Do Not Redo or Reopen`
14. `## Next Recommended Action`

Use `None`, with a brief reason, when a required section has no applicable content. Do not omit the section.

## Section requirements

### Objective

State the bounded outcome of the prompt and whether it was fully achieved. Preserve the milestone boundary and explicit exclusions.

### Authoritative Sources

List the repository-relative documents, code, configuration, schemas, issue or decision records, authoritative remote, and direct runtime evidence that governed the work. Identify which sources are durable and which evidence is time-sensitive.

### Execution Context

Record the relevant machine or environment, operating system and shell when material, repository root, tools or isolated environments used, and any limitations that affected execution. Keep paths workstation-neutral where possible, but preserve an exact path when it is necessary to resume safely.

### Current Repository State

Record the branch, exact `HEAD`, working-tree status, upstream, authoritative remote, ahead/behind or divergence state, commit created, push/readback result, and any intentionally preserved unrelated changes. State explicitly when a fact was not checked.

### Current Known-Good State

Identify the last state directly supported by passing tests, accepted runtime behavior, deployment evidence, or another reliable checkpoint. Separate fresh evidence from inherited claims and dated observations.

### Completed Work

Summarize implemented behavior and documentation changes in concrete terms. Include meaningful failure-path or compatibility work, but do not turn this section into a command transcript.

### Decisions Made

Record durable choices, their relevant tradeoffs, rejected alternatives when future workers might otherwise repeat them, and decisions deliberately deferred to the user or a later milestone.

### Files Changed

List each intended changed, added, moved, or removed file with a short description. Separately identify generated artifacts or unrelated existing changes that were intentionally excluded.

### Validation Completed

List only validation actually executed and observed. Include commands or procedures when they aid reproducibility, along with results and important environment limitations. Distinguish focused tests, broader suites, build/lint/static checks, diff review, runtime checks, browser/device checks, secret scanning, and remote verification. State what was not run and why.

### Production State Versus Repository State

State independently what is implemented, committed, pushed, deployed, activated, runtime-validated, documented only, planned, and unverified. Include relevant revisions or release identifiers. If the project has no production environment, say so and describe the corresponding local or external state.

### Unresolved Issues and Unverified Assumptions

List blockers, known limitations, failures, deferred work, stale or time-sensitive facts, and assumptions not proven. Do not convert uncertainty into a root cause or claim.

### Safety, Rollback, and Access Considerations

Record secret-safe rollback guidance, irreversible or destructive boundaries, access prerequisites, credential locations without values, external side effects, and operations that still require explicit approval. If no runtime or data mutation occurred, state that clearly.

### Do Not Redo or Reopen

Identify completed investigations, rejected approaches, failed attempts with known causes, settled decisions, and work that should not be repeated unless named evidence changes. Do not use this section to suppress legitimate revalidation of time-sensitive state.

### Next Recommended Action

Give exactly one best next action. Make it bounded and executable, and name any approval or verification gate that must precede it. Do not smuggle later milestones into the current completion claim.

## Commit and publication behavior

The handoff normally belongs in the same commit as the completed work from its prompt. Unless the prompt establishes a different gate, Codex should commit that scoped checkpoint and push it to the project’s authoritative remote when safe.

Before commit and push, Codex must:

- stage only the intended work and handoff;
- preserve unrelated user changes;
- review the final diff and run `git diff --check` when Git is used;
- run the project’s required validation and secret checks;
- avoid force-pushes, history rewrites, implicit merges, or destructive cleanup; and
- verify authoritative remote readback after a successful push.

If commit or push cannot be completed safely, the handoff must say exactly what remains local, why it stopped, and the next safe publication action. A local commit is not remote publication, remote publication is not deployment, and deployment is not runtime acceptance.

If a task expressly prohibits all repository writes, Codex must return the complete handoff content for later placement in `docs/handoffs/` and state that the file was not created because of that boundary. It must not omit the handoff.

## Content quality rules

A handoff must be:

- evidence-led and explicit about uncertainty;
- concise enough to scan but complete enough to resume;
- written as a checkpoint, not a transcript;
- repository-relative where paths are durable;
- precise about revisions, files, commands, and results;
- clear about what remains unchanged or out of scope;
- secret-safe; and
- free of claims that were not directly verified.

Prefer references to stable repository documents over copying their contents. Include enough context to explain why a reference matters.

## Required template

```markdown
# <Topic or Milestone> Handoff

Date: <YYYY-MM-DD or timestamp with timezone>
Status: <completed | partial | blocked | read-only | precise alternative>
Model: <execution model>
Effort: <reasoning effort>
Repository: <repository and relevant checkout context>
Branch: <branch or detached state>
HEAD: <exact revision | not applicable | unknown>
Authoritative remote: <remote and destination | not configured | unknown>

> This handoff is a continuation checkpoint, not authoritative truth. Current
> repository, remote, runtime, and test evidence wins if it conflicts with this
> document.

## Objective

<Bounded outcome, completion status, and exclusions.>

## Authoritative Sources

- <Repository-relative source or direct evidence and why it governs.>

## Execution Context

- <Relevant environment, repository root, tools, and limitations.>

## Current Repository State

- Branch and HEAD: <...>
- Working tree: <...>
- Upstream and synchronization: <...>
- Commit and authoritative remote readback: <...>
- Preserved unrelated changes: <...>

## Current Known-Good State

- <Last directly verified good state and evidence date or revision.>

## Completed Work

- <Concrete completed behavior or documentation.>

## Decisions Made

- <Decision, tradeoff, rejection, or explicit deferral.>

## Files Changed

- `<path>`: <purpose>

## Validation Completed

- <Executed check and observed result.>
- Not run: <check and reason, or None.>

## Production State Versus Repository State

- Implemented: <...>
- Committed: <...>
- Pushed: <...>
- Deployed or activated: <...>
- Runtime-validated: <...>
- Documented or planned only: <...>
- Unverified: <...>

## Unresolved Issues and Unverified Assumptions

- <Blocker, limitation, uncertainty, or None.>

## Safety, Rollback, and Access Considerations

- <Secret-safe rollback, side-effect boundary, access need, or None.>

## Do Not Redo or Reopen

- <Settled or completed work that should not be repeated without changed evidence.>

## Next Recommended Action

<Exactly one bounded next action, including any prerequisite approval or verification.>
```

## Validation checklist

Before considering a handoff complete, verify:

- one descriptive H1 is present;
- required metadata includes model and effort;
- all 14 required H2 headings appear exactly once and in order;
- referenced repository paths resolve or are explicitly historical/external;
- branch, `HEAD`, working-tree, commit, and publication claims match observed evidence;
- test and runtime claims identify what actually ran;
- implementation, deployment, validation, and planning states are not conflated;
- unresolved and unverified items are explicit;
- no secrets or sensitive values are present;
- the next action is singular and bounded;
- `git diff --check` and applicable project validation pass; and
- the final diff contains only intended changes, with unrelated work preserved.
