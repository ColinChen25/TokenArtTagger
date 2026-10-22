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
- Local AI must target a machine with 16 GB total RAM shared with other programs; CPU-only operation is a design requirement and GPU acceleration is optional. Do not assume this development machine's 32 GB RAM or RTX GPU. Favor accuracy, responsiveness, bounded memory, and resumable sequential work over throughput. Check available memory, pause under pressure, and benchmark actual runtime peaks before declaring a model suitable. See the low-memory profile in `docs/local-ai-tagging-draft.md`.
- Local tagging must accommodate artistic nudity and NSFW fantasy art, including minimal tribal outfits and succubus imagery. Use neutral classification, preserve relevant visual details, and evaluate content-triggered refusals separately from uncertainty. Do not add an NSFW gate that skips tagging or infer fantasy race/class from revealing clothing alone. Model compatibility is untested until benchmarked; see `docs/local-ai-content-compatibility.md`.

Keep image paths, private manifests, model files, runtime profiles, credentials, network settings and locally generated agent configuration out of commits. Reuse cached files, resume partial downloads and verify checksums. Follow the current operator's download authorization and keep downloads scoped to the selected setup.

The first Qwen3-VL 2B model is already downloaded and checksum-verified in the private local cache. Read `docs/model-download-routing.md` before setup or another download. Recheck live TUN state and the actual redirected endpoints; this machine's successful direct route is not a permanent guarantee or permission to fall back to a proxy.

Continue local AI work in small, checkpointed steps. Read `docs/local-ai-runtime-setup.md` and the private setup status before repeating runtime downloads or launching another worker. Runtime discovery is distinct from inference and accuracy validation; do not treat a downloaded archive or a prepared script as a passed runtime test.
