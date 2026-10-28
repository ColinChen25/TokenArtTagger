# Focused single-image experiment

This experiment reuses completed development targets and the cached Qwen model. It removes visual references and asks either for the role/style pair or for fantasy race alone. Fields outside the requested task must be null in both the generation schema and the response validator. The existing four-field result envelope is retained for review; these outputs are deliberately partial suggestions.

## Run and resume

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiBenchmark.ps1 -Instance <app-pid> -OutputDirectory <private-run> -TaggedCount 8 -UntaggedCount 0 -MaxItems 1 -TargetManifestFile <completed-development-run>/manifest.json -Focus role-style
```

The target manifest must be from a completed benchmark with verified cleanup, matching library root, authoritative filename labels and full hashes for every target. The runner checks live bytes and labels before inference. Every source target is reused; changing the sample, prompt, schema or focus requires a new output directory. No reference images, source filenames or target labels enter the model request. The source manifest may contain references, but only its target items are reused.

`-Focus role-style` uses a shorter equipment/clothing prompt and a schema coupling each role with its allowed styles. `-Focus race` asks about anatomy and leaves the other three fields null. Focused version 2 bounds evidence to 180 characters and uncertainty to 96, in the schema, prompt and validator. The first version's one-image feasibility response repeated its explanation and exhausted the 256-token budget; it remains an invalid result in a separate run. These are experimental prompts, not confidence-calibrated classifiers. Unknown details should remain null. Artistic nudity and minimal outfits remain ordinary inputs to neutral classification, with no content gate.

Repeat the same command with a larger `-MaxItems` to continue. Completed results are revalidated and reused. The ordinary benchmark lock, STOP marker, revision checks, memory admission/reserve limits, two-thread CPU-only runtime and per-image unloading still apply. Inspect the saved status and owned process identities before resuming. Suggestions never update app tags or filenames.

## Matched control and scoring

Use the same target manifest with `-Focus all -ConstrainRoleStyle -BoundEvidence` in a separate directory for a single-image, full-tag control. Both arms then use the same role/style grammar and explanation limits. This distinguishes a focused prompt's effect from the effects of enforcing valid role/style combinations and bounding output. Earlier unconstrained or visual-reference results alone cannot isolate that difference.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Compare-LocalAiBenchmark.ps1 -BaselineDirectory <control-run> -CandidateDirectory <focused-run> -Focus role-style
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiReview.ps1 -OutputDirectory <focused-run> -Instance <app-pid> -FetchMissingPreviews
```

Comparisons pair full content hashes and require identical authoritative labels. Focused exact matches evaluate only the declared fields; full-tag accuracy is null. Invalid outputs, decoding failures and refusals stay in the denominator. Field precision counts issued non-null predictions, while coverage records abstention. The review explicitly identifies its field scope. Results remain development diagnostics, not estimates of whole-library accuracy.

`Test-LocalAiEvaluation.ps1` checks focus boundaries, partial outcomes, failure denominators and paired scoring. `Test-AgentIntegration.ps1` rejects incomplete source runs, stale bytes/labels, missing hashes and duplicate target paths before inference, using disposable fixtures.

## Larger development challenge and cache reuse

The original baseline contains 32 filename-labeled targets and four untagged targets. `-LabeledTargetsOnly` with `-TargetManifestFile` selects those 32 labeled targets; it does not select new images or touch the reserved final test. `-TaggedCount` must match the selected count and `-UntaggedCount` must be zero. All of these images were already exposed development data, including the 24 not used in the focused eight-image pilot.

`Invoke-LocalAiFocusedChallenge.ps1` runs three sequential arms: the bounded full-tag control, focused role/style, and focused race. Each arm uses the existing benchmark, its own checkpoint directory, and the same source target manifest. The wrapper never runs two models concurrently. The 32-image continuation completed on 2026-10-28 in the private `challenge-fields-v1` directory, followed by a zero-inference revalidation. Read the [measured results](local-ai-results.md#larger-focused-field-challenge-2026-10-28) and reuse those checkpoints.

```powershell
# Optional preparation: reuse the completed eight-image pilot without new inference.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiFocusedChallenge.ps1 `
  -Instance <app-pid> -OutputDirectory .cache/local-ai-benchmark/challenge-fields-v1 `
  -TargetManifestFile .cache/local-ai-benchmark/pilot-v2/manifest.json -MaxItemsPerArm 0 `
  -ControlCache .cache/local-ai-benchmark/target-only-control-v1 `
  -RoleStyleCache .cache/local-ai-benchmark/focused-role-style-v2 `
  -RaceCache .cache/local-ai-benchmark/focused-race-v2

# Run or resume the remaining targets; completed results are checked and reused.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiFocusedChallenge.ps1 `
  -Instance <app-pid> -OutputDirectory .cache/local-ai-benchmark/challenge-fields-v1 `
  -TargetManifestFile .cache/local-ai-benchmark/pilot-v2/manifest.json
```

Cross-run reuse is also available directly through the benchmark's `-ReuseDirectory`. It requires a completed source with verified cleanup and an exclusive source-cache lock. Every inference setting must match; only the source manifest and target-selection identities may differ. Full content hashes, paths and authoritative labels must still agree with the live app. Raw outputs are revalidated, failures remain failures, and copied records retain the original fingerprint and record digest as provenance. The original result files are not changed. A zero `-MaxItems` checks and prepares caches without loading the model; it can still hash the entire selected sample.

Before resuming, inspect `driver-status.json`, each child's `status.json`, the root `driver.lock`, child `worker.lock` and recorded process IDs **with creation times**. A root `STOP` interrupts the current request as well as preventing the next arm; child STOP markers remain effective. Neither memory pauses nor STOP markers cause automatic retries. Remove a marker only when resumption is intended. Keep the desktop app running while inference uses MCP. On Windows, launch background workers with `Start-Process -WindowStyle Hidden` and preserve their output/error logs and process identity.

The driver records completed arms and the current child directory. Read the active child's `completedThisRun` for new inference progress; its interim summary can omit preloaded results it has not traversed yet. After a cache-only resume, use earlier inference-bearing session files for memory measurements.

When all three arms and cleanup are complete, export the paired review without further inference:

```powershell
.tools/encoder-venv/Scripts/python.exe scripts/export_focused_challenge.py `
  --study .cache/local-ai-benchmark/challenge-fields-v1 `
  --inputs .cache/encoder-inputs/filename-v1 `
  --baseline .cache/local-ai-benchmark/pilot-v2 `
  --annotations .cache/local-ai-benchmark/challenge-fields-v1/content-annotations.json
```

The report separates the earlier eight targets from the additional 24, compares focused fields on identical hashes, and retains failures in all denominators. Actual null answers within the question's scope count as abstentions; required nulls outside it and a generic character's null style do not. Composed fields are an analysis of three saved answers, not a new end-to-end inference or confidence estimate. Memory maxima come from sessions with new inference. The private HTML uses verified existing MCP previews, escaped text and no scripts or remote resources; its static validation does not establish browser rendering.

`Test-LocalAiCache.ps1` checks inference identity, provenance, retained failures, active-writer exclusion, changed labels and shared STOP admission. `test_focused_challenge.py` checks score denominators, abstention, composition failures, matched reuse cohorts and report escaping with synthetic images; neither test calls a model.

A controlled root STOP during real inference on 2026-10-28 paused both the child and driver, discarded the unfinished response, and preserved all 20 completed control records byte-for-byte (eight reused and twelve newly computed). Both locks were released and owned runtime cleanup was verified before resuming the same challenge. The private `stop-test-result.json` retains this evidence.

The first continuation completed the control and role/style answers but failed role/style cleanup because `Start-Process` had not populated its executable-path property. The runtime now retains the verified executable path passed to the launcher; termination still requires the live path and process creation time to match ownership. Two sequential runtime start/cleanup checks passed without inference. Recovery verified the old server was absent and all locks were free before cache-only validation of completed arms. The earlier session's failed cleanup receipt is retained.

The larger challenge also exposed a validator discrepancy: JSON Schema counts Unicode code points, while .NET string length counts UTF-16 code units. One 180-character explanation ending in an emoji was wrongly rejected. `strict-tags-v3-unicode` counts the bounded evidence and uncertainty strings consistently with the schema, with tests for supplementary characters and combining marks. Revalidate saved responses through the normal completed-cache resume; this changes validation metadata, not the prompt, model, source labels or raw answers. A valid response can still have incorrect tags.
