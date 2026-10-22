# TODO

## Next AI milestone

- Build a resumable local-model benchmark using the existing MCP/CLI image access and authoritative filename labels. Keep outputs separate from current tags.
- Include artistic nudity, minimal tribal outfits, and succubus imagery in model selection and evaluation. Measure refusals, unsupported guesses, and tag accuracy separately; use the neutral prompt and fallback criteria in [content compatibility](docs/local-ai-content-compatibility.md). This requirement is recorded, not yet runtime-tested.
- Target 16 GB total shared RAM, CPU-only support, one image/model at a time, measured memory use, and pause/resume under memory pressure. Background speed is secondary to accuracy and foreground responsiveness.
- First verify decoding and duplicate groups, then run a small feasibility check before the larger held-out evaluation. See [the implementation draft](docs/local-ai-tagging-draft.md).

## Future Features

- SQLite database for persistent image records and rename history.
- Freeform tags beyond the fixed schema.
- Thumbnail cache management UI, including cache size display and manual clear.
- Duplicate detection using exact content hashes.
- Perceptual hash similar-image search.
- Richer search/filter UI with tag facets and missing-tag filters.
- Automated undo from JSON undo logs.
- Freeform tags for exact one-off weapon and creature details such as specific named weapons, visual motifs, or source-specific notes.
- Better animated WebP preview handling if Windows codecs expose only a static frame.
- Local AI suggestions with provenance and review; enable automatic staging only for fields with measured reliability. Keep uncertain fields unresolved and retain batch confirmation for actual filename changes.

## Safety Follow-Ups

- Add an explicit dry-run export for rename previews.
- Add a visible warning when scanning a Google Drive path with many selected files.
- Add batch-size confirmation thresholds before large rename operations.
- Add an in-app diagnostics viewer or copy button for sanitized crash logs.
