# Private dataset inventory

Source folders, filenames, collection composition and per-image metadata belong in ignored local manifests. A public checkout contains the inventory workflow, not a user's collection listing or detailed label distribution.

## Inventory workflow

1. Read the running app's `app_status` and `tag_schema`. Scan the selected source with appropriate exclusions when a tagged subfolder is nested in the backlog.
2. Use the authoritative filename parser through MCP. Folder membership alone does not establish whether an image lacks tags: query the `untagged` filename-state filter.
3. Use the [dataset exporter](local-ai-dataset.md) to record full content hashes and normalized labels. Do not reimplement filename parsing or image decoding in an inventory script.
4. Keep source paths, original names, aliases, file attributes, format counts, detailed category distributions and import errors in private local output. In public reports, retain only aggregate experiment results needed to assess accuracy and runtime limits.
5. Audit exact duplicates and near-duplicate/variant groups before assigning training, calibration and final-test partitions. Exact hashes do not group related artwork.
6. Keep development targets and references out of the final test. Hide filenames, folder labels and expected tags from inference inputs.
7. Evaluate common and rare categories separately. A strong aggregate score can conceal poor performance on underrepresented labels; insufficient evidence requires manual review.
8. Check animated formats, decoding failures and source readability before processing a backlog. File metadata does not prove image decoding or offline availability.

## Local outputs

Historical inventories may exist under `.cache/ai-tagging/`; current exports use the paths specified in the dataset guide. These artifacts can contain personal paths, collection preferences and image fingerprints. Keep them out of Git, including logs, previews and copied reports. Filename labels remain authoritative unless the operator explicitly requests a correction.
