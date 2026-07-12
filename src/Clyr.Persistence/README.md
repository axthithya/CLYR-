# Clyr.Persistence boundary

Planned for Phase 1: SQLite migration/repository foundation for product settings and later aggregate snapshots. It uses transactional versioned migrations, integrity/recovery and bounded retention; no full filename index, file contents, secrets, direct scan/action logic, or undocumented pragmas belong here.
