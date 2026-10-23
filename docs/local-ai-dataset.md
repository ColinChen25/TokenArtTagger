# Trusted filename dataset

`Export-LocalAiDataset.ps1` creates a resumable, private reference dataset through the running app's read-only MCP interface. It pages `state: tagged` across the current scan, reads `filenameTags`, and requests full SHA-256 through `image_details`. Temporary app tags and AI suggestions are never imported as trusted labels. No separate filename parser or decoder is introduced.

Read `app_status` and `tag_schema`, scan the intended root, then supply the actual app process ID:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiDataset.ps1 `
  -Instance 12345 -OutputDirectory .cache/local-ai-dataset/filename-v1 -MaxItems 2

# Resume all remaining items with the same directory.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiDataset.ps1 `
  -Instance 12345 -OutputDirectory .cache/local-ai-dataset/filename-v1
```

Use the same scan root and schema when resuming. A changed root/schema requires another output directory. A lock excludes concurrent writers. A `STOP` file in the output directory requests a pause; remove it only when resumption is intended. Inspect `status.json` and the recorded process identity before restarting a worker that appears to be running.

The exporter checks workspace and image revisions. Cached hashes are reused only while path, source revision and dataset fingerprint still match; a new scan can invalidate those revisions and require hashing again. This is cache validity under the app's size/mtime/revision contract, not detection of an adversarial file edit that preserves metadata. For a full fresh audit, use a new output directory. Only paths present in the current scan enter the regenerated index.

Outputs remain under ignored `.cache`:

| File | Contents |
|---|---|
| `dataset.json` | Root, authoritative label source, schema and identity |
| `items/*.json` | Per-file path, revision, content hash and trusted filename tags |
| `index.json` | Current completed records; may be partial until status is complete |
| `exact-duplicates.json` | Same-byte groups and conflicting-label flags |
| `summary.json` | File/unique-content counts, class frequencies and duplicate counts |
| `status.json` | Worker identity, progress, completion and cleanup |

Conflicting labels on identical content are reported without changing either filename. Exact SHA-256 identity does not detect crops, resizes, alternate encodings or related artwork. Group those variants before assigning retrieval/training, calibration and final test splits. Do not train or score against a partial index as though it covers the entire collection.

The 2026-10-23 export completed with **1,469 tagged files and 1,469 unique byte hashes**, no exact duplicate groups and no exact-content label conflicts. This includes 1,466 files in the tagged subfolder and three already-tagged files in its parent. It does not include the 660 untagged files as training data. No model training has occurred. Rare classes with only one or two examples cannot support reliable independent training/calibration/testing for those classes.

`Test-AgentIntegration.ps1` verifies the real exporter on disposable images, including checkpoint/resume, identical image bytes with conflicting filename labels, and unchanged original files.

## Check the backlog for known images

`Find-LocalAiKnownImages.ps1` compares untagged content against the completed trusted index without running a model. It uses MCP filename recognition state rather than folder membership, hashes each backlog image, and rehashes any matching trusted sources. Conflicting labels on identical source bytes are kept unresolved. No tags are applied.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Find-LocalAiKnownImages.ps1 `
  -Instance 12345 -DatasetDirectory .cache/local-ai-dataset/filename-v1 `
  -OutputDirectory .cache/local-ai-dataset/backlog-matches-v1
```

The dataset must match the current scan's source revisions; resume its export after rescans or edits. The audit supports `-MaxItems`, a directory lock, `STOP`, per-item checkpoints and an `index.json` regenerated from the current scan. Inspect `status.json` for completion and counts of `knownLabels`, `conflictingLabels` and `noExactMatch`. Cached hashes share the dataset export's metadata-based invalidation limits. This identifies identical bytes, not visually similar crops or alternate encodings. It is a review aid, not an automatic tag writer.
