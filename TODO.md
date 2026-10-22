# TODO

## Next AI milestone

- Runtime setup and two synthetic CPU vision tests passed on 2026-10-23. The model cache was reused. See [setup](docs/local-ai-runtime-setup.md).
- The [resumable local benchmark and private HTML review](docs/local-ai-benchmark.md) are implemented using read-only MCP access and authoritative filename labels. Finish the development-set evaluation before selecting any automatic acceptance policy.
- Visually annotate the benchmark's artistic nudity, minimal outfits and succubus imagery, then report refusals and tag accuracy separately. Follow [content compatibility](docs/local-ai-content-compatibility.md); an answered image can still be tagged incorrectly.
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
