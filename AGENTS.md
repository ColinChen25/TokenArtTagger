# Agent access to TokenArtTagger

For image-library operations, use the `token_art_tagger` MCP tools when available. They operate on the running WPF application's live state. Read `docs/agent-integration.md` for setup and the complete contract.

- Start with `app_status` and `tag_schema`; list images in pages rather than dumping the library.
- Use `scan_images` exclusions or `list_images` folder filters when a tagged folder is nested in the backlog. Folder membership is not proof of missing tags; use the `untagged` filename-state filter.
- Use `view_image` to inspect pixels, and `image_details` with `includeHash` for full content hashes. Do not rewrite filename-parsing or image-decoding scripts.
- Filename tags are authoritative for this user's collection. Flag disagreements separately unless an overwrite was requested.
- Use `update_tags` with explicit image IDs, current revisions, and all four fields. Unknown fields remain null. Re-read after stale-revision errors; do not retry old writes blindly.
- `preview_rename` never changes files. Actual renames retain the existing desktop confirmation.
- The bridge does not call a model itself. Image tool responses go to the calling agent/provider; that is separate from the planned local inference engine.
- If MCP is unavailable in the current chat, use `scripts/Start-Agent.ps1 -Call <tool> -ArgumentFile <json-file>` as the stable fallback. A newly registered MCP server may require a new chat or client reload.
- Use `scripts/Test-AgentIntegration.ps1` for end-to-end verification; it uses disposable images and isolated application state.

Keep image paths, private manifests, model files, runtime profiles, credentials, network settings and locally generated agent configuration out of commits. Reuse cached files, resume partial downloads and verify checksums. Follow the current operator's download authorization and keep downloads scoped to the selected setup.
