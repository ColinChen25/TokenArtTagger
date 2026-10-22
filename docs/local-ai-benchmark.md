# Local CPU benchmark and review

The repository now has a working local vision benchmark, separate from the desktop tagging workflow. It uses the existing MCP server for filename labels, full hashes and bounded image decoding, then sends PNG pixels directly to the cached Qwen model on loopback. There is no hosted inference call, automatic model download, tag update or rename in this runner.

The benchmark is a diagnostic prototype. A complete, schema-valid answer is not a calibrated or approved prediction. All suggestions remain separate from authoritative filename tags. The production desktop AI workflow and automatic acceptance policy are still pending measured accuracy.

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

```powershell
# Stop the current request, reject its unfinished result, unload, and save status.
New-Item .cache/local-ai-benchmark/run-01/STOP -ItemType File

# When resumption is intended, remove this marker and rerun the original command.
Remove-Item -LiteralPath .cache/local-ai-benchmark/run-01/STOP
```

A stopped or memory-paused run returns a nonzero exit code with `status: paused`. It does not automatically restart itself. A file lock prevents two writers against one run directory; the dedicated runtime port also prevents concurrent benchmark servers. Do not remove the lock file to bypass a live worker. An OS/process kill may leave `status: running`; inspect the recorded process identities before restarting.

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

The runtime is app-owned, bound to `127.0.0.1`, local-only, and below normal process priority. Inference uses two CPU threads, zero GPU layers, 4,096 context tokens, a 256-token output bound, and one image/request. The manifest and all five blobs are checksum-verified before starting. `/api/ps` must report the pinned digest and zero VRAM use. The model unloads after every image; this trades a little startup time for bounded worker/cache retention.

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
