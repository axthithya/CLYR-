# Privacy

CLYR is designed to answer a local storage question without requiring cloud processing, telemetry, or file-content collection. Core scanning, classification, protection, explanation, snapshots, and future action decisions are offline and deterministic.

## Data principles

- **Minimize:** collect metadata needed for the selected analysis, not file contents.
- **Local by default:** no automatic upload, analytics SDK, remote API, account, or cloud sync.
- **Purpose-limit:** do not reuse scan metadata for advertising, profiling, or unrelated diagnostics.
- **Make uncertainty safe:** inaccessible content is counted/marked where possible, not bypassed.
- **User control:** expose history retention and deletion; warn before a detailed local export.
- **Redact for sharing:** summary exports omit full usernames, personal filenames, account identifiers, tokens, project names, and complete paths.

## Data classification and retention

| Data | Classification | Default handling | Planned default retention | User control |
|---|---|---|---|---|
| Capacity, used/free totals, filesystem/capability | Low sensitivity | Local aggregate | Bounded snapshot history | Delete one/all snapshots |
| Scan time/state, coverage, skipped/inaccessible counts | Low sensitivity | Local aggregate | With snapshot | Delete one/all snapshots |
| Category and finding IDs, rule-pack version | Low sensitivity | Local aggregate | With snapshot | Delete one/all snapshots |
| Redacted top-N path tokens and evidence | Sensitive metadata | Local; user segment/path components redacted or pseudonymized | With bounded snapshot history | Disable detail; delete history |
| Full paths/personal filenames | Highly sensitive metadata | In-memory during scan; not stored by default | Session only | Explicit detailed-local opt-in if later supported |
| File contents | Prohibited collection for standard scan | Never read for size scan; never persisted | None | Not applicable |
| Summary export | Shareable after validation | Versioned JSON/text with redaction | User-selected destination only | User deletes file |
| Detailed local export | Highly sensitive | Explicit warning; never uploaded | User-selected destination only | User deletes file |
| Logs | Sensitive operational metadata | Structured, redacted, bounded, local | Provisional rolling seven days; validate in Phase 1 | Clear logs / disable optional detail |
| Settings | Local product data | Product-owned directory | Until reset/uninstall policy | Reset/delete |
| Future action journal/receipt | Security/audit sensitive | Minimal targets or protected tokens; immutable outcome | Provisional 30 days, user-controlled; finalize Phase 6 | Clear only when no recovery depends on it |
| Future quarantine metadata/content | Highly sensitive | Product-owned, encrypted only if separately designed; never silent expiry deletion | Per explicit plan; no automatic final deletion | Review, restore, or create new deletion plan |

Retention values are provisional design inputs, not implemented behavior. Phase 4/6 must benchmark utility, disk cost, recovery needs, and privacy risk before shipping defaults.

## Scan behavior

A standard size scan reads filesystem metadata and does not open file contents or hash data. Deep duplicate-candidate work is separately opt-in and phased. CLYR must not hydrate a cloud placeholder, decrypt EFS content, take ownership, change ACLs, or bypass an access boundary to improve coverage. Reparse points are skipped by default.

Paths may contain identities, clients, project names, health/financial topics, tokens, or other secrets. Logs and summary exports therefore use typed fields and redaction rather than interpolating raw paths. Stable pseudonyms must be scoped to the local installation/report purpose and must not enable cross-user tracking.

## Export modes

1. **Summary:** share-safe intent, redacted paths/identifiers, aggregate/top-N evidence, coverage and schema version. It must pass validation before being labeled share-safe.
2. **Detailed local:** explicit privacy warning, more path detail only for local diagnosis, and never uploaded automatically.
3. **Machine-readable JSON:** versioned schema with a privacy classification, compatibility behavior, bounded fields, and unknown-field policy.

No export is sent anywhere by CLYR. A user explicitly chooses a destination and later decides whether to share it.

## Future telemetry and crash reporting

There is no telemetry by default. Adding analytics, cloud upload, remote configuration, or online AI requires an approved ADR and a change to the public privacy promise. Phase 9 may evaluate crash reporting only as explicit opt-in, with preview/redaction, endpoint/vendor/retention disclosure, and a fully local path.

## Product-owned locations

Exact Windows paths depend on packaged identity and are verified in Phase 1. Settings, databases, logs, demo data, and any future quarantine must use distinct product-owned locations. Uninstall/history behavior must be tested and documented; no local path in this Phase 0 repository is a runtime claim.
