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
