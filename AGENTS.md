# Linux development scope

- Keep all Linux changes inside this `linux/` directory, as requested by the user.
- Treat source files elsewhere in the TeXBB repository as read-only references.
- Place temporary files, builds, fixtures, caches, and packages in ignored
  `.codex-*` directories inside `linux/`; verify ignore coverage before creating
  them. Configure tools so their task-specific output stays inside this subtree.
- Preserve compatibility with existing TeXBB document and backup formats.
- Distinguish source checks, cross-compilation, and actual Linux runtime testing
  when reporting verification results.
