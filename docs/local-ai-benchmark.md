# Local CPU benchmark and review

The repository now has a working local vision benchmark, separate from the desktop tagging workflow. It uses the existing MCP server for filename labels, full hashes and bounded image decoding, then sends PNG pixels directly to the cached Qwen model on loopback. There is no hosted inference call, automatic model download, tag update or rename in this runner.

The benchmark is a diagnostic prototype. A complete, schema-valid answer is not a calibrated or approved prediction. All suggestions remain separate from authoritative filename tags. The production desktop AI workflow and automatic acceptance policy are still pending measured accuracy.

The first 36-image baseline and a paired schema experiment are recorded in [measured results](local-ai-results.md). The trusted filename collection can be exported separately with the [dataset workflow](local-ai-dataset.md).

For the current target-only role/style and anatomy experiments, use the [focused-field workflow](local-ai-focused-fields.md). It reuses completed development targets, forces unrelated fields to null, and scores only the requested fields against a matched bounded-output control.

## Run or resume

First read [runtime setup](local-ai-runtime-setup.md), confirm the private setup checkpoint is complete, and ensure no earlier worker is active. The installed runtime is Ollama 0.40.8 and the model is the pinned `qwen3-vl:2b-instruct-q4_K_M` cache. These scripts intentionally fail when either is missing instead of downloading it.

Open the app, use MCP `app_status` and `tag_schema`, then scan the intended library with `scan_images`. Obtain the actual app process ID from `app_status`. The benchmark itself cannot rescan, modify temporary tags, select images, preview renames or rename files.

```powershell
# Replace 12345 with the actual app process ID.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiBenchmark.ps1 `
  -Instance 12345 -OutputDirectory .cache/local-ai-benchmark/run-01 `
  -TaggedCount 32 -UntaggedCount 4
```

Run the same command to resume. `-MaxItems 2` provides a short initial checkpoint; omit it to continue. The sample counts and model settings must match the saved manifest. If the app was restarted, rescan the same root and supply its new process ID; current image IDs are resolved through MCP rather than replaying old IDs.

The deterministic sample covers rare races first, then missing weapon/caster styles, then fills remaining slots by a stable ordering. Untagged examples are selected separately by filename recognition state. This is intentionally a difficult coverage sample, **not a random estimate of whole-library accuracy**. It is a development set, not a held-out test. Exact duplicate content in the selected sample stops scoring; related variants and duplicates across the full library still need grouping before a formal evaluation.

`-ConstrainRoleStyle` additionally encodes role/style dependencies into the generation schema. It changes the settings fingerprint, so use a new output directory. The independent validator still checks every response. Valid combinations do not establish visual correctness.

After both runs complete with verified cleanup, compare the shared content using:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Compare-LocalAiBenchmark.ps1 `
  -BaselineDirectory .cache/local-ai-benchmark/run-01 `
  -CandidateDirectory .cache/local-ai-benchmark/run-02
```

This writes a private `comparison.json` in the candidate directory. It matches full hashes, rejects duplicate identities and conflicting source labels, retains failed predictions, and reports the unmatched counts. Accuracy is recalculated on the same paired images, so a small rare-class subset is not compared against the baseline's entire sample.

```powershell
# Stop the current request, reject its unfinished result, unload, and save status.
New-Item .cache/local-ai-benchmark/run-01/STOP -ItemType File

# When resumption is intended, remove this marker and rerun the original command.
Remove-Item -LiteralPath .cache/local-ai-benchmark/run-01/STOP
```

A stopped or memory-paused run returns a nonzero exit code with `status: paused`. It does not automatically restart itself. A file lock prevents two writers against one run directory; the dedicated runtime port also prevents concurrent benchmark servers. Do not remove the lock file to bypass a live worker. An OS/process kill may leave `status: running`; inspect the recorded process identities before restarting.

## Fixed-reference development experiment

For a larger single-image comparison that reuses completed answers, see the [focused challenge workflow](local-ai-focused-fields.md#larger-development-challenge-and-cache-reuse). The benchmark supports `-LabeledTargetsOnly` for mixed source manifests, `-ReuseDirectory` for identical inference settings, `-MaxItems 0` for cache-only preparation, and an additional private STOP signal for multi-arm drivers. These options do not relax source identity, filename authority or output validation.

For one separately retrieved reference per target, use the [training-only retrieval workflow](local-ai-retrieved-references.md). It wraps this benchmark in resumable one-target child runs and adds source snapshots plus a visual related-artwork audit; it does not replace the benchmark's model or validation contract.

`-ReferencePlanFile` accepts a private JSON file under `.cache` with `targets` and `references` arrays of absolute source paths already in the app scan. Set `-TaggedCount` to the target count and `-UntaggedCount 0`. One or two references are supported; use a new output directory. Every image must have authoritative filename labels. For example, the JSON shape is `{ "targets": ["TARGET_PATH"], "references": ["REFERENCE_PATH"] }`; substitute actual scanned paths in the private file, not in tracked documentation.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-LocalAiBenchmark.ps1 `
  -Instance 12345 -OutputDirectory .cache/local-ai-benchmark/reference-study `
  -TaggedCount 8 -UntaggedCount 0 -ReferencePlanFile .cache/reference-plan.json
```

This is a fixed-example pilot, not an automatic retrieval system or weight training. Choose the reference set before examining target answers, exclude the full evaluation pool, and inspect for related artwork. The same references are supplied to every target. Do not pick each reference using the target's hidden authoritative class. Target selection for a declared development challenge may deliberately cover known classes, but must not be reported as random population accuracy.

Reference pixels are decoded through MCP at a 384-pixel maximum dimension and sent in distinct user messages followed by their trusted four-field labels. The final user message contains the target image and classification prompt; target labels and all source paths remain outside model messages. The runner rejects duplicate paths/content between references and targets, includes reference hashes/labels/preprocessing in cache identity, and verifies reference revisions and hashes after inference. It does not detect near duplicates automatically.

The context remains 4,096 tokens. A request whose reported prompt token count cannot reserve the full 256-token response is rejected, including during cache reuse. Image token usage is not reliably inferred from the thumbnail's dimensions: the first two-reference trial used 3,846 prompt tokens, leaving only 250. That trial completed one response without runtime truncation, but the subsequent stricter budget guard excludes it from the paired study. The next pilot uses one reference. A larger context is not selected automatically.

The review exporter displays the trusted references separately from target predictions. They are never included in accuracy denominators. Keep the same target preprocessing, prompt and schema where practical when comparing with a no-reference run, and report the experiment's additional message/prompt changes explicitly.

## Local review page

After inference, export the image-by-image comparison:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiReview.ps1 `
  -OutputDirectory .cache/local-ai-benchmark/run-01 -Instance 12345 -FetchMissingPreviews
```

This fetches previews through MCP, checks each source's full content hash against the prediction, and creates `review.html`. Open that local file in a browser. It displays source labels, suggestions, disagreements, uncertainty, and expandable raw output. It has no accept or rename action. Later exports can omit the app instance and `-FetchMissingPreviews` when previews are already cached.

All run directories must be inside the repository's ignored `.cache` directory. Manifests, image paths, thumbnails, raw responses and content annotations are private. Do not commit them or publish a review page without choosing its contents deliberately.

## Validation, identity and recovery

- The schema is generated from MCP `tag_schema`; it does not introduce a second filename parser or image decoder.
- Target filenames, folders and expected labels are excluded from the model prompt. Only visible pixels and the neutral classification contract are supplied.
- Each result is saved with full SHA-256, settings fingerprint, exact prompt/schema, model digest, runtime version and response metadata. The fingerprint canonicalizes JSON property order. Changes to inference settings require a new run directory.
- The content hash is checked before and after inference. Content or authoritative-label changes invalidate the saved sample. Completed results are reused only after current source verification; raw responses are revalidated with current output rules on resume.
- Allowed values, all four tag fields, role/style compatibility, extra properties, truncation and explicit refusal language are checked. `null` is uncertainty, not a refusal. `generic` requires a null style.
- Output states are `complete`, `partial`, `refusal`, `invalid_output`, and `decode_error`. Complete/partial describe field completeness only. Transport/runtime failures stop the run with earlier results retained, rather than silently dropping failures or treating them as images without tags.
- Summary denominators retain refusals and invalid outputs. Per-field precision is conditional on issuing a label; coverage is reported separately. Full matches use all labeled attempts. Untagged images have no accuracy score.
- Source read/transport failures remain recoverable run errors. Not every Windows codec exception currently has a distinct MCP decode error code; such failures stop conservatively until inspected.

The optional private `content-annotations.json` contains an array of `{sha256, groups, note}` records based on actual visual inspection. Supported report groups are `ordinary_clothed`, `minimal_outfit`, `artistic_nudity`, and `succubus_like`; they can overlap. Uninspected images are `unreviewed`. These annotations are evaluation context, not user-facing tags or training labels. Refusal flags require inspecting their explanation: a malformed self-reported status is not proof of content censorship.

## CPU and memory behavior

The runtime is app-owned, bound to `127.0.0.1`, local-only, and below normal process priority. Inference uses two CPU threads, zero GPU layers, 4,096 context tokens, a 256-token output bound, and one target/request (plus bounded references only in the explicit reference experiment). The manifest and all five blobs are checksum-verified before starting. `/api/ps` must report the pinned digest and zero VRAM use. The model unloads after every target; this trades a little startup time for bounded worker/cache retention.

Memory samples include the Ollama server, its **llama-server.exe** worker, the PowerShell harness, the local MCP bridge and the WPF app. The app's already-resident memory is recorded separately before the first decode. Both combined working set and incremental tagging workload are reported; existing app memory is never silently omitted from the combined measurement.

Admission requires 8 GiB available physical memory. During inference, the worker pauses if incremental workload exceeds 4 GiB, available physical memory falls below 4 GiB, commit headroom falls below 4 GiB, or a STOP marker appears. Samples are taken roughly once per second; these are software guardrails, not an OS hard memory cap, and peaks between samples can be missed. Peak counters cover the whole run; detailed samples retain at most the first 10,000 points per invocation.

The target remains **16 GB total RAM shared with other applications**. Current measurements are on the development machine; CPU-only success and memory headroom there do not prove performance or responsiveness on a real 16 GB machine. Excess memory pressure pauses rather than falling back to GPU, cloud inference or heavy paging.

Each invocation saves `status.json`, a session report, memory samples and runtime process identity. If cleanup fails, status records `cleanupComplete: false`; inspect the owned process IDs and their creation times. Never terminate unrelated Ollama processes by name. A successful controlled cancellation and resume test is part of the current verification.

## Checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-LocalAiEvaluation.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-LocalAiVision.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentIntegration.ps1
```

The first is a fast model-free contract check. The second measures two synthetic color/shape images with real CPU inference and unloading. The third tests the actual MCP bridge and desktop using disposable images and isolated application state. Neither fixture suite establishes real-art accuracy or content compatibility; those require the private image benchmark and visual review.
