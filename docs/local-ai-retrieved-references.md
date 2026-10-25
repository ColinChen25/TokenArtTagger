# Retrieved visual-reference experiment

This development experiment reuses the cached SigLIP 2 features to select one trusted reference for each Qwen target. It tests whether per-target retrieval improves on the earlier fixed wizard example. No new model is downloaded, no tags are staged, and no filename is changed.

## Selection and evaluation boundaries

`scripts/plan_encoder_references.py` selects from the classifier study's **training partition only**. Targets must already belong to development. It ranks normalized image-feature cosine similarity, excludes every target, excludes the target's variant group, and excludes similarity at or above .90 as an additional conservative near-variant screen. The remaining highest-scoring reference is selected deterministically, with content hash breaking ties. Target labels are not an input to the selection function; source paths and labels are joined afterward for the existing benchmark runner.

This is not a validated semantic retrieval method. Similarity may emphasize art style, hair, palette or background over the anatomy, clothing or weapon needed for a tag. The .90 screen cannot establish independence. Inspect each selected target/reference pair for related artwork before inference. Record `visual-audit.json` with the matching plan fingerprint and one entry per pair: `sha256`, `referenceSha256`, `related: false`, and a short observation. Uncertain or related pairs must be resolved by a new selection, not silently approved. Keep the original plan as the record of what was selected.

The current eight targets are the same five caster and three ranged targets used in the earlier fixed-reference study. All are previously exposed development data. The 169 original reserved-test images remain unscored and cannot supply references. Include the experiment's targets and reference hashes in development manifests for later experiments; the current run provides `development-manifest.json` for that purpose.

## Prepare and run

All paths below are private directories under the repository's ignored `.cache`:

```powershell
.tools/encoder-venv/Scripts/python.exe scripts/plan_encoder_references.py --dataset <dataset> --features <features> --study <classifier-study> --inputs <MCP-previews> --target-manifest <previous-study>/manifest.json --output <retrieval-study>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiRetrievedReferences.ps1 -Instance <app-pid> -OutputDirectory <retrieval-study> -MaxItems 1
```

The planner writes an immutable selection fingerprint, one ordinary reference plan per target, and a label-blind `reference-audit.html` using existing MCP previews. After recording the visual audit, the driver invokes the existing `Invoke-LocalAiBenchmark.ps1` once per target. It retains that benchmark's pinned Qwen/Ollama setup, neutral prompt, 768-pixel target and 384-pixel reference, CPU-only two-thread execution, 4,096-token context, 256-token response reservation, source verification and memory/cleanup guards. The reference message and its authoritative labels are followed by the final target. Target labels are used for evaluation only.

Each child plan also records the selected source hashes and filename labels. The benchmark rejects changed bytes, changed labels or incomplete snapshots before inference. The driver rejects stale/uncertain visual audits, changed child selections and paths escaping the experiment directory. Different references intentionally produce different child settings fingerprints.

`-MaxItems` limits new results and saves a checkpoint. Repeat the same command without that bound to continue. Completed child runs are revalidated against current sources and reused without inference. The driver lock prevents two writers; each child retains the benchmark's own lock. Check `driver-status.json`, PID/start time, current child status and owned runtime identities before resuming.

A study-root `STOP` stops admission between targets. To interrupt the current inference, create `STOP` in the `currentDirectory` reported by the driver; the existing benchmark cancels/unloads and retains completed work. Do not remove an intentional STOP without a resume instruction. Only the app-owned runtime is stopped. A final `cleanupComplete` result is required before interpreting a run as cleanly finished.

## Compare and review

```powershell
.tools/encoder-venv/Scripts/python.exe scripts/export_retrieved_reference_review.py --study <retrieval-study> --inputs <MCP-previews> --baseline <original-Qwen-baseline> --fixed-reference <previous-fixed-reference-study>
```

The exporter runs `Compare-LocalAiBenchmark.ps1` against both baselines for every child, retaining the paired receipts. It then aggregates exactly the declared target hashes and authoritative labels. Invalid outputs and decode failures stay in the denominator. The private static review displays each target, its chosen reference and the resulting disagreement. Timing and context counts exclude decode failures without fabricating zero-duration inference. Memory maxima come from inference-bearing session records, so a completed-run resume cannot erase the original measured peak.

No confidence calibration or automatic acceptance follows from this small experiment. Report all eight outcomes, including regressions, and compare the same target set. See [current results](local-ai-results.md) for measured findings.

## Verification

`test_encoder.py` verifies that reference selection excludes held-out images, target groups and very close variants. `test_retrieved_references.py` checks six preflight failures without starting a model, plus a report containing a decode failure and no timing/memory samples. `Test-AgentIntegration.ps1` verifies stale byte/label snapshots and incomplete snapshots against a disposable real MCP application, before any model inference.

## Final-image grounding diagnostic

`Invoke-LocalAiGroundingCheck.ps1 -OutputDirectory <private-study>` runs two synthetic colored-shape cases through the same reference/assistant-label/target message builder. It retains the CPU runtime, memory guards, STOP marker, checkpoint lock and per-image unloading. It generates its own fixtures and never opens source-library files.

The initial run identified the final image correctly in both orders (2/2), with zero reported VRAM and verified cleanup; a completed resume performed no inference. This excludes a universal first-image-only failure. It does not validate complex-art grounding or explain the reference-detail contamination in the eight real-art targets. The next experiment removes visual references and focuses on individual tag fields; see [measured results](local-ai-results.md).
