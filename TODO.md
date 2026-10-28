# TODO

## Next AI milestone

- Runtime setup and two synthetic CPU vision tests passed on 2026-10-23. The model cache was reused. See [setup](docs/local-ai-runtime-setup.md).
- The [resumable local benchmark and private HTML review](docs/local-ai-benchmark.md) completed a 36-image baseline: 12/32 full tag matches and six invalid outputs. Read [measured results](docs/local-ai-results.md) before continuing; automatic acceptance remains disabled.
- Twelve images have visual content annotations, including three artistic-nudity and two minimal-outfit examples. No explicit refusals were observed, but broader compatibility and minimal tribal outfits still need evaluation. Follow [content compatibility](docs/local-ai-content-compatibility.md); an answered image can still be tagged incorrectly.
- Target 16 GB total shared RAM, CPU-only support, one image/model at a time, measured memory use, and pause/resume under memory pressure. Background speed is secondary to accuracy and foreground responsiveness.
- The [trusted dataset exporter](docs/local-ai-dataset.md) hashed all 1,469 filename-tagged images; no exact duplicates were found. Group near duplicates/variants and check remaining formats before disjoint reference/calibration/test partitions.
- The fixed-reference pilot completed: valid outputs improved but full matches remained 0/8. The [SigLIP 2 encoder experiment](docs/local-ai-encoder.md) encoded all 1,469 tagged images and trained a grouped linear probe: 114/185 full calibration matches, but only 1/32 on the earlier rare-category challenge (Qwen: 12/32). Preserve the original 169 reserved-test images, complete the variant audit, and address unsupported rare classes before a final evaluation. Automatic acceptance remains disabled.
- A verified vision-only copy of the cached encoder reduced an eight-image CPU private peak from 3.19 to 1.08 GiB, with identical features and no download. Prefer it for new runs; keep existing features and the 8 GiB admission/4 GiB reserve policy until broader low-memory validation.
- The exact-content backlog audit completed for all 660 untagged files, with no matches against the trusted dataset. There are no confirmed labels to copy through that shortcut.
- Per-target retrieved references also produced 0/8 full matches. Agreement between Qwen and the classifier yielded five complete agreed tag sets with only one correct. A two-order synthetic final-image check passed 2/2. Keep all targets/references in development and preserve the final test.
- The 32-image focused challenge is complete: role/style scored 14/32 versus control 16/32, race 21/32 versus 20/32, and composed full tags 8/32 versus 12/32. The earlier eight-image improvement did not extend to the additional 24 targets. No in-scope abstention occurred. Do not adopt the focused prompts as the default; automatic acceptance remains disabled.
- All 96 saved answers were revalidated without new inference after correcting Unicode length counting. Shared STOP, recovery from the runtime-path cleanup error, and final cleanup were verified. The complete private report and receipts are in `.cache/local-ai-benchmark/challenge-fields-v1/`; reuse them instead of repeating inference.
- The persistent MCP proposal queue and saved-classifier importer are implemented and tested. Four existing backlog proposals are queued with no applied tags. Next product step: desktop review controls with explicit field decisions and provenance; keep acceptance separate from rename confirmation.
- The [new-image classifier pipeline](docs/local-ai-suggestions.md) produced four private review-only suggestions without retraining or downloading. Numeric predictions reproduced all 233 previously scored development/calibration results, with reserved-test rejection. The vision-only pilot again peaked at 1.08 GiB OS private memory; actual 16 GB validation remains outstanding.

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
