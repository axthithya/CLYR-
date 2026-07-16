# Clyr.Cli boundary

Phase 3 adds offline rules list, rules verify, rules describe, inactive external rule validation, classified report v2, and report explanation over shared application services. It does not implement independent action logic, invoke a shell, or expose cleanup commands.

Phase 5 adds plan candidates/create/show/validate/export/discard commands over the shared Core planner. Plans are bounded and process-memory only; export is explicit and privacy-safe. There is no execute/apply/clean/delete/prune command, external process, elevation, or helper.
