# Rule engine

## Status and safety scope

This is the design contract for the future `Clyr.Rules` component. Phase 0 contains schemas and fixtures only; there is no rule loader, filesystem detector, command runner, or cleanup implementation yet. Rule schema version 1 is deliberately **detection-only** and every accepted rule has the literal action type `report-only`.

The rule engine will answer “what known storage does this metadata resemble?” It will not decide that an arbitrary large item is junk, grant privilege, or bypass the protected-resource policy.

Authoritative artifacts:

- [`../rules/schemas/rule.schema.json`](../rules/schemas/rule.schema.json) — JSON Schema Draft 2020-12 contract;
- [`../rules/examples/npm-cache.valid.yaml`](../rules/examples/npm-cache.valid.yaml) — accepted rule fixture;
- [`../rules/examples/path-traversal.invalid.yaml`](../rules/examples/path-traversal.invalid.yaml) — rejected traversal fixture;
- [`../rules/examples/shell-command.invalid.yaml`](../rules/examples/shell-command.invalid.yaml) — rejected free-form command fixture;
- [`diagrams/rule-lifecycle.mmd`](diagrams/rule-lifecycle.mmd) — contribution-to-finding lifecycle.

## Trust boundaries

A YAML file is untrusted data, including a built-in rule before its packaged digest is verified. Parsing never grants authority. A conforming implementation must pass a rule through every gate below before evaluation:

1. Read with a bounded byte limit and reject aliases, custom tags, duplicate mapping keys, recursive structures, and multiple YAML documents.
2. Convert only YAML scalar, sequence, and mapping values to an in-memory document. Do not instantiate application types during parsing.
3. Validate against the exact JSON Schema selected by `schemaVersion`.
4. Perform semantic checks that JSON Schema cannot express, including version-range ordering, placeholder expansion, canonical path containment, protected-resource impact, overlap policy, and fixture availability.
5. Verify the containing built-in pack manifest and digest. Community-pack signing is a later design decision; unsigned input is never silently elevated to built-in trust.
6. Compile to immutable detector objects containing no delegates, scripts, executable names, or process arguments.
7. Evaluate only against normalized scan metadata supplied by `Clyr.Core`.

Failure is atomic at pack level. A rejected rule produces a stable diagnostic code and source location, contains no raw path in support-safe output, and contributes no partial detector. Any invalid member rejects the complete pack transactionally; a previously verified built-in pack may remain active so packaging mistakes cannot be hidden or partially applied.

## Version 1 document contract

| Area | Contract |
| --- | --- |
| Identity | `id` is a stable reverse-domain-like lowercase identifier; `version` is SemVer and changes whenever detection semantics or explanation text changes. |
| Classification | Category, risk, confidence, disposition, and regenerability use closed vocabularies. Schema risk values are invariant lowercase serializations of `Informational`, `Low`, `Medium`, `High`, and `Prohibited`. Risk does not imply that an action is available. |
| Platform | Version 1 accepts Windows rules and optional filesystem/capability constraints. Unsupported capabilities make a rule unavailable, not partially active. |
| Detection roots | Roots begin with one approved placeholder and contain fixed safe path components. Raw absolute paths, unknown environment variables, traversal, and wildcard roots are invalid. |
| Enumeration | `followReparsePoints` is always `false`; depth and name globs are bounded. Age is supporting evidence only. |
| Ownership | `exclusiveGroup` and `priority` participate in deterministic overlap resolution after protected-resource policy and pack trust. |
| Action | Only `report-only`, non-elevated, with rollback `not-applicable`. There is no command, executable, script, argument, deletion, or mutation field. |
| Privacy | Content reads and full-path exports are prohibited; each rule declares aggregate-only or redacted path disclosure. |
| Compatibility | An inclusive minimum and exclusive maximum engine version are required. An unsupported range rejects the rule safely. |
| Evidence | Every rule names a deterministic synthetic fixture. References, when supplied, must use HTTPS and should prefer official vendor documentation. |

`additionalProperties: false` applies at every object boundary. Unknown fields are an error, not an extension mechanism. Any new field or action vocabulary requires a new reviewed schema version; implementations must not ignore it optimistically.

## Root resolution and protected paths

Schema validation proves only lexical shape. The future semantic validator must apply this sequence independently for every root:

1. Resolve an allowlisted placeholder (`%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `%TEMP%`, `%PROGRAMDATA%`, or `%SYSTEMDRIVE%`) from a trusted Windows known-folder/volume provider, not from rule-supplied text.
2. Reject a missing, relative, device-namespace, UNC, alternate-data-stream, or nonlocal result.
3. Normalize separators and dot segments with Windows-aware APIs and ordinal case-insensitive component comparison.
4. Resolve the volume identity and verify component-aware containment under the resolved placeholder. A string-prefix test is insufficient.
5. Walk existing ancestors without following reparse points. Record and skip any junction, symlink, mount point, or other reparse tag.
6. Ask the protected-resource policy to classify the canonical scope. Protected policy always wins, even over a built-in rule with maximum priority.
7. Bind the compiled scope to its canonical root and volume identity for that scan. A changing or replaced root invalidates the observation.

Detection can report a protected or system-managed scope, but it cannot turn that scope into a safe candidate. Unknown content beneath `%PROGRAMDATA%` remains protected/review-only until a specifically reviewed rule and policy identify its ownership.

## Evaluation and finding production

The engine consumes bounded metadata observations: stable file identity when available, canonical scope token, logical and allocated sizes, timestamps with provenance, attributes, reparse state, accessibility, and category hints. Normal size detection does not open file contents and must not hydrate cloud placeholders.

For each compiled detector:

1. Check capability and filesystem constraints.
2. Match only observations contained by the compiled roots.
3. Apply optional fixed name globs, age signal, and maximum depth.
4. Emit evidence codes and measured values with precision (`exact`, `lower-bound`, `estimate`, or `unavailable`).
5. Create a finding candidate with rule-pack ID/version/digest and rule ID/version.
6. Run protected-resource and overlap resolution before totals are exposed.
7. Render explanation text from the rule’s fixed strings; never interpolate a raw personal path into support-safe output.

A match is evidence of identity, not proof of safe removal. Findings remain `report-only` in Phase 0 and through the initial detection implementation in Phase 3.

## Deterministic overlap resolution

The same physical item may match a general cache rule, a tool-specific rule, and a parent aggregate. Accounting ownership is assigned once using this stable order:

1. Protected-resource classification overrides every rule and excludes the item from reclaimable totals.
2. Items with a reliable filesystem identity are deduplicated before rule ownership; otherwise the result is labelled estimated or unavailable.
3. Pack trust tier (built-in before reviewed community before untrusted local), then rule `priority`, then root specificity determine the preferred candidate.
4. Remaining ties use rule ID and rule version in ordinal ascending order. They never depend on load order, locale, hash-map order, or wall-clock time.
5. The winner owns category/accounting bytes. Losing candidates may remain as explanatory links with `ownedBytes = 0` and the owner fingerprint.

If containment or identity is ambiguous, CLYR must not publish a precise reclaimable total. Pack compilation should flag unintended equal-priority overlaps, and tests must prove that reordering the same pack produces identical owners.

## No command surface

Declarative rules never invoke a shell. The schema rejects `command`, `script`, `executable`, and every other unknown action property. It also fixes elevation to false.

Later phases may introduce action contracts such as `trusted-tool-command`, but only through a new schema and a compiled first-party adapter. Such an adapter must own executable discovery, publisher/location checks, supported versions, exact subcommands, typed arguments, validation, timeouts, bounded output, exit-code handling, and dry-run behavior. Rules may reference only an immutable adapter ID; they may never provide executable text or a generic argument vector. PowerShell, `cmd.exe`, shell interpolation, and generic process runners remain prohibited.

## Pack and compatibility policy

A future rule-pack manifest binds pack ID, SemVer, schema major, engine range, ordered file list, per-file SHA-256 digest, build provenance, and trust tier. The engine records the exact pack digest with each scan and finding so snapshots remain explainable after an update.

- Schema major changes are breaking and require an explicit parser.
- Rule SemVer changes whenever output or matching can change; incompatible edits require a major change.
- Engine compatibility is `[minimumEngineVersion, maximumEngineVersionExclusive)` and is checked semantically.
- Unknown schema versions, fields, categories, actions, or capabilities fail closed.
- A snapshot is never silently reclassified under a newer pack. Re-evaluation creates a new derived result with new provenance.
- Rule rollback means selecting a previously verified pack for future scans; it never mutates historical findings.

## Privacy and diagnostics

Rules cannot request file contents. Stored finding evidence should use rule IDs, aggregate sizes, age buckets, stable local tokens, and diagnostic codes. Local logs redact user-profile components and never include YAML fragments that may contain attacker-controlled secrets. Summary exports contain neither rule roots nor expanded paths.

Detailed local export is a separate, explicitly warned privacy mode. A report-only action does not imply permission to disclose its location.

## Required verification before Phase 3 completion

- Validate every built-in rule and both invalid corpora against Draft 2020-12 with format checking enabled.
- Assert duplicate YAML keys, custom tags, aliases, oversized input, excessive nesting, and multi-document streams are rejected.
- Fuzz rule parsing and root normalization; include `..`, mixed separators, device paths, UNC paths, ADS syntax, Unicode confusables, reserved names, and reparse swaps.
- Prove that a rule cannot target or downgrade a protected resource.
- Prove that no accepted object can represent a free-form command or request elevation.
- Verify engine range ordering and reject unknown capabilities.
- Check deterministic overlap ownership under randomized rule order.
- Verify fixture expectations and privacy-safe diagnostics.
- Hash the packaged built-in manifest and detect any changed file.

These checks are planned gates; Phase 0 validates the static schemas and examples only.
