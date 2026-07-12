# Security Policy

## Current scope

CLYR is in Phase 0 and contains documentation, schemas, examples, and repository metadata only. There is no application or supported release. Security reports are still valuable when they identify an unsafe specification, schema bypass, privacy leak, supply-chain risk, or future privilege/action design flaw.

## Private reporting

When this repository is hosted on GitHub with Private Vulnerability Reporting enabled, use the repository's **Security → Report a vulnerability** flow. Include:

- affected document, schema, contract, version, or future component;
- impact and realistic attack preconditions;
- minimal reproduction using synthetic data only;
- proposed mitigation if known;
- whether disclosure is time-sensitive.

Until that private channel exists, do **not** put exploit details, real paths, tokens, personal data, or destructive reproduction steps in a public issue. Open a minimal non-sensitive issue asking maintainers to establish a private contact channel, or contact a maintainer through a private channel they have independently published. This repository intentionally does not invent an unmonitored security email address.

Maintainers should acknowledge a report privately, establish severity and scope, preserve evidence, assign an owner, prepare tests and documentation, and coordinate disclosure only after a fix or safe mitigation exists. No response-time promise is made before a maintained security channel and release process exist.

## Sensitive areas

Treat these as security-critical even before implementation:

- protected-path and canonicalization policy;
- rule/action and export schemas;
- privacy redaction and local retention;
- external tool adapters and argument validation;
- cleanup plans, quarantine, journals, receipts, and rollback;
- elevated helper launch, authentication, executable identity, IPC, nonce/expiry, and replay handling;
- signing, packaging, updates, dependency pinning, and release workflows.

## Supported versions

No version is supported in Phase 0. A supported-version table will be added only after signed release artifacts exist. Development branches receive best-effort review and must not be used for cleanup.

## Disclosure and safe harbor

Good-faith research that avoids privacy violations, persistence, service disruption, social engineering, real user data, and mutation outside disposable fixtures is welcome. Stop when a test could affect another person or a real machine. This statement does not authorize access to systems or data you do not own or have explicit permission to test.
