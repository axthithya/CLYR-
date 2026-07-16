# CLYR rules

## Current status

This directory contains the Phase 3 verified first-party detection pack, schemas, and synthetic examples. Built-ins classify metadata only after manifest integrity checks. External rules can be validated but remain inactive. No rule can execute an action or remove data.

Rules describe known storage locations and produce explainable findings. They are untrusted declarative data, never executable plugins.

## Layout

```text
rules/
├── builtin/                         # Phase 3 reviewed built-in rule pack
├── schemas/
│   ├── rule.schema.json             # Detection rule, schema major 1
│   └── export-report.schema.json    # Support-safe summary export, major 1
└── examples/
    ├── npm-cache.valid.yaml
    ├── path-traversal.invalid.yaml
    ├── shell-command.invalid.yaml
    └── summary-report.valid.json
```

No empty built-in pack is created in Phase 0. Built-in rules arrive with a versioned, digest-bound manifest and fixtures when the loader and validation tests exist.

## Rule v1 in brief

[`schemas/rule.schema.json`](schemas/rule.schema.json) uses JSON Schema Draft 2020-12. It requires:

- stable rule ID and SemVer;
- closed category, risk, confidence, disposition, and regenerability values (`RiskLevel` serializes as lowercase `informational`, `low`, `medium`, `high`, or `prohibited`);
- Windows capability constraints;
- one or more approved placeholder-rooted fixed paths;
- reparse traversal fixed to `false`;
- deterministic overlap group and priority;
- an action fixed to `report-only`, non-elevated, with no rollback operation;
- explanations for origin, hypothetical removal effect, and user impact;
- privacy declarations that prohibit content reads and full-path export;
- inclusive minimum and exclusive maximum engine versions;
- a deterministic synthetic fixture.

Every object rejects unknown fields. There is no field for a shell, command, executable, script, argument list, arbitrary absolute path, deletion, movement, or elevation. Later actions require a new schema review and compiled first-party adapters; a community YAML file will never supply executable text.

See [`../docs/RULE_ENGINE.md`](../docs/RULE_ENGINE.md) for parsing, semantic validation, pack trust, protected-path precedence, compatibility, and overlap rules.

## Examples and expected outcomes

| Fixture | Expected result | Exact reason |
| --- | --- | --- |
| `npm-cache.valid.yaml` | Valid | Uses an approved placeholder root, inert action, closed metadata, privacy declarations, and compatible SemVer range. |
| `path-traversal.invalid.yaml` | Invalid | `detection.roots[0]` contains the component `..`, which fails the `ruleRoot` pattern. The entire rule is rejected. |
| `shell-command.invalid.yaml` | Invalid | `action.command` is not defined and `action.additionalProperties` is false. The entire free-form command is rejected even though `action.type` says `report-only`. |
| `summary-report.valid.json` | Valid against export schema | Synthetic, support-safe Summary v1 report with closed fields and explicit measurement precision. |

Comments in invalid YAML fixtures document the expectation and are not part of the parsed object.

## Validation contract

The Phase 1 verification command will be implemented as an offline repository tool; the intended public CLI shape is:

```text
clyr rules validate <file-or-directory>
```

Until that tool exists, CI/reviewer validation must use a Draft 2020-12-conformant validator with `format` assertions enabled and a YAML parser configured to reject duplicate keys, aliases, custom tags, recursive values, and multiple documents. JSON parsing must reject duplicate property names. Schema `$id` values are identifiers only; validation must not fetch a schema from the network.

Static schema validation is necessary but insufficient. The future semantic pass must also:

1. compare the engine SemVer range and reject an empty/reversed range;
2. resolve placeholders from trusted Windows known-folder/volume APIs;
3. canonicalize with Windows rules and verify component-aware containment;
4. reject device/UNC/relative/alternate-stream paths and unsafe or missing capabilities;
5. skip and record every reparse point;
6. apply protected-resource policy, which always overrides rule metadata;
7. verify fixture existence, pack manifest membership, digest, and trust tier;
8. detect unintended overlap and compile immutable detectors containing no code.

Validators fail closed on unknown schema majors, fields, action types, categories, capabilities, pack entries, and malformed input. They return stable error codes and source locations without copying attacker-controlled paths or secrets into support-safe diagnostics.

## Contribution checklist (Phase 3 onward)

Before proposing a rule:

- start with official tool/vendor documentation and record an HTTPS reference when one exists;
- choose the narrowest fixed known root; never use a whole profile, drive, Windows directory, project tree, or broad application-data directory;
- include only synthetic fixtures—never a real username, account, project, token, machine path, cache contents, database, or proprietary file list;
- explain why the data exists and the real consequences of hypothetical removal;
- treat age as supporting evidence, never proof of safety;
- use `review-required` unless evidence justifies a stronger detection classification; classification still grants no action;
- keep `action.type: report-only`, `requiresElevation: false`, and `rollback: not-applicable`;
- state whether data is regenerable, user-owned, system-managed, or unknown;
- select an exclusive group and reviewed priority, then add an overlap fixture;
- test inaccessible roots, missing tools, unsupported versions/filesystems, case differences, long paths, reparse points, and protected-path collisions;
- update schema/docs/examples together if the public contract changes.

Rule changes require maintainer review. Changes to schemas, protected-resource behavior, priorities, executable adapters, or future action types require designated security/code-owner review.

## Versioning and compatibility

- `schemaVersion` selects the rule document major. Major 1 is closed and report-only.
- Each rule `version` is SemVer; matching or explanation changes require a version change.
- Engine compatibility is `[minimumEngineVersion, maximumEngineVersionExclusive)` and is checked after schema validation.
- A built-in pack records its own version and SHA-256 digest plus every member digest.
- Findings retain the exact pack digest and rule version that produced them. Updating a rule never rewrites historical findings.
- Unsupported or invalid rules are unavailable and explain why; CLYR does not silently downgrade or guess.

The JSON export contract is documented in [`../docs/EXPORT_FORMAT.md`](../docs/EXPORT_FORMAT.md). Storage totals and ownership semantics are documented in [`../docs/STORAGE_ACCOUNTING.md`](../docs/STORAGE_ACCOUNTING.md).

## Security reporting

Do not publish a weaponized rule or a suspected bypass in a normal issue. Follow the private reporting process in the repository’s `SECURITY.md` once present. A malicious fixture committed for regression testing must be minimal, synthetic, inert, and reviewed.
Phase 5 action metadata is built-in only, integrity-checked, optional, and limited to report-only/review-files plus a trusted root identity. External rules remain detection-only. Executable paths, scripts, shell text, unrestricted arguments, arbitrary roots, and downloads are rejected.
