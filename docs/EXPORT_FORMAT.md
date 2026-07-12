# Export format

## Status and purpose

Phase 0 defines an export contract; it does not implement scanning or export. The first machine-readable format is the privacy-safe summary report described by [`../rules/schemas/export-report.schema.json`](../rules/schemas/export-report.schema.json), a JSON Schema Draft 2020-12 document. [`../rules/examples/summary-report.valid.json`](../rules/examples/summary-report.valid.json) is illustrative synthetic data, not evidence of a completed CLYR scan.

Exports are user-initiated local files. CLYR has no upload endpoint, cloud dependency, telemetry path, or automatic support submission.

## Modes and representations

Privacy mode and representation are separate choices:

| Mode | Privacy classification | Paths/names | Intended use | Phase |
| --- | --- | --- | --- | --- |
| Summary | `support-safe` | No full paths, usernames, filenames, machine name, file content, or raw errors | GitHub issues, support conversations, local comparison tooling | Schema defined in Phase 0; production planned after scanning |
| Detailed local | `sensitive-local` | May include selected full paths after an explicit warning | The user’s own offline diagnosis | Separate schema and UX required before implementation |

Machine-readable JSON is a representation, not a third privacy level. The v1 JSON schema covers only the support-safe Summary mode. A future human-readable view must be rendered from the same validated model and must not quietly add private fields. A future Detailed local schema uses a different `reportType`/schema ID so a consumer cannot confuse it with support-safe data.

## Summary v1 envelope

The root is a closed object (`additionalProperties: false`) with these sections:

| Section | Meaning |
| --- | --- |
| `schemaVersion`, `reportType` | Select the exact contract: major `1`, type `clyr-summary`. |
| `reportId`, `generatedAt` | Random per-report identity and UTC RFC 3339 generation time. |
| `producer` | Product name `CLYR` and producer SemVer. |
| `compatibility` | Schema major, minimum reader major, and the explicit unknown-field policy `reject`. |
| `privacy` | Machine-checkable assertions that the report is support-safe and was not automatically uploaded. |
| `environment` | Low-sensitivity compatibility facts plus a report-scoped volume digest; no drive label, mount path, machine name, or serial number. |
| `rulePack` | Exact pack ID, SemVer, and SHA-256 digest used to classify the scan. |
| `scan` | State, mode, timestamps, volume measurements, coverage, and the complete storage-accounting metric set. |
| `categories` | Bounded category totals after exclusive accounting ownership. |
| `findings` | Bounded, path-free finding summaries with rule provenance, evidence codes, and `report-only` availability. |
| `diagnostics` | Counts by stable code and limitation codes; no raw messages, exception text, or paths. |

All byte values are measurement objects rather than naked numbers:

```json
{
  "value": 4096,
  "precision": "exact",
  "source": "file-metadata"
}
```

Unavailable data is explicit:

```json
{
  "value": null,
  "precision": "unavailable",
  "source": "not-measured"
}
```

Producers use non-negative signed 64-bit values and reject overflow. Consumers must preserve integer precision and must not coerce bytes through a floating-point representation. Display units and rounding are consumer concerns; serialized values remain bytes.

## Privacy transformation

Summary export is an allowlist projection, not a pass over the normal model with a few strings replaced. The exporter must construct a new support-safe DTO containing only schema-defined fields.

Required transformations:

1. Generate a fresh random report ID and report-scoped salt.
2. Derive `volumeIdHash` from stable local volume evidence and the report salt. Do not export the installation-scoped volume key or raw volume serial.
3. Omit drive letters, volume labels, mount paths, usernames, security identifiers, machine names, account IDs, package scopes that identify private organizations, and file/directory names.
4. Export category and evidence codes from closed vocabularies. Do not export arbitrary exception text, log lines, command output, or localized OS messages.
5. Use fixed rule explanation keys rather than interpolated location text. A rule’s expanded root is never part of Summary mode.
6. Aggregate skipped/error observations by code. Rare combinations may still fingerprint a system, so cap collection sizes and allow the user to preview the complete report.
7. Emit report-scoped finding fingerprints; never emit an unsalted hash of a path because common paths are guessable.
8. Run a final privacy validator that rejects fields or values resembling Windows absolute/UNC/device paths, email addresses, security identifiers, access tokens, or secrets. This is defense in depth, not a substitute for allowlisting.
9. Validate the final object against the exact schema with `format` assertions enabled, then show a preview and explicit save action.

The UI must truthfully label Summary mode “designed to be support-safe,” not promise mathematically perfect anonymity. Capacity, OS release, category mix, and timestamps can still be identifying in combination. Users can remove optional findings or decline export.

## Compatibility and versioning

The `$id` and `schemaVersion` identify a major contract. Readers select a parser before deserializing the payload body.

- Major `1` has a reject-unknown-fields policy. Readers must not ignore extra root or nested fields.
- Because v1 objects are closed and consumers may rely on required fields, adding, removing, renaming, or changing any field/enum requires a new schema major unless the original schema explicitly reserved the variation.
- Producer application version and rule-pack version evolve independently of export schema version.
- A reader that does not support the exact major rejects the report with a stable compatibility error and does not partially import it.
- Older reports remain immutable. A migration tool, if later provided, emits a new report with provenance; it never rewrites the source file silently.
- Schema documents are packaged with the application and validation works offline. Network retrieval of `$id` is never required.
- Schema selection uses an internal allowlist; a payload cannot supply an arbitrary schema URL for CLYR to fetch.

`minimumReaderMajor` is explicit for forward planning but cannot weaken exact-major parsing. The v1 value is `1`.

## Serialization rules

- Encoding is UTF-8 without a byte-order mark.
- Media type is `application/json`; a future registered vendor media type is optional and not assumed.
- Property names and enum values are invariant ASCII as defined by the schema.
- Timestamps are UTC RFC 3339 strings with a `Z` suffix in producer output. Readers may accept schema-valid offsets but normalize internally.
- SHA-256 values use lowercase hex prefixed by `sha256:`.
- Arrays preserve a deterministic order: categories by code, findings by descending owned allocated bytes then fingerprint, diagnostics by code. The order has no semantic authority.
- Duplicate JSON object names are rejected during parsing even though JSON Schema receives an already-parsed object.
- Nonfinite numbers, comments, trailing commas, and multiple concatenated JSON values are invalid.
- A save operation will use a temporary file in the selected destination, flush/close, validate, then atomically replace where the filesystem supports it. Failure leaves no report falsely presented as complete.

A suggested filename is `clyr-summary-YYYYMMDD-HHMMSSZ.json`. The filename contains no machine, user, drive, or finding name.

## Semantic checks beyond JSON Schema

Schema validation establishes shape, not truth. A producer and strict importer also check:

- scan end time is not before start time; a `completed` scan has a non-null end time;
- volume capacity, used, and free measurements came from a compatible observation and do not overflow; small filesystem timing differences are recorded rather than forced into a false equality;
- coverage state agrees with skipped/inaccessible diagnostics;
- category/finding totals use resolved ownership and do not double-count the same physical item;
- `knownReclaimableLowerBoundBytes` excludes movable, protected, unknown, inaccessible, and overlapping bytes;
- a support-safe finding has no action other than `report-only` for the initial contract;
- pack digest and rule provenance are present even if classification produced no findings;
- privacy assertions match the generated object and the report passes the defense-in-depth value scan.

Readers must show invalid or unsupported reports as invalid; they must not “repair” malformed safety/privacy assertions.

## Detailed local export boundary

Detailed local export is not represented by the v1 summary schema. Before implementation it requires:

- a separate schema ID, report type, redaction/disclosure model, examples, and validation tests;
- a full list of included path tiers and a preview of exact fields;
- an explicit `sensitive-local` warning each time, with default selection off;
- no automatic upload, clipboard copy, issue attachment, or background creation;
- safe file permissions and cleanup of temporary staging data;
- a decision on whether content hashes are necessary (default: no) and how cloud placeholders avoid hydration;
- retention behavior controlled by the destination owner, plus a “delete generated report” convenience action that cannot affect scanned content.

Renaming a detailed report to “summary” never changes its classification. Only construction and validation against the support-safe schema can produce a Summary report.

## Validation fixtures and acceptance criteria

The synthetic example must parse as JSON and validate against Draft 2020-12 with UUID/date-time format checking. Future negative fixtures should cover unknown fields, a full-path injection, malformed digest, negative/overflow bytes, false privacy flags, unavailable values paired with non-null bytes, unsupported schema versions, duplicate keys, and inconsistent scan times.

An export implementation is acceptable only when it is deterministic, offline, user-initiated, schema-valid, semantically consistent, privacy-previewed, cancellable before save, and verified to create no network traffic.
