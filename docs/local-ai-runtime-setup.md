# Local runtime setup checkpoint

Setup completed and live discovery verified **2026-10-23** on `codex/local-ai-tagging`. Ollama 0.40.8 is installed locally, all five cached Qwen model blobs passed full size/hash verification, and the runtime discovered the pinned model with vision capability. Subsequent synthetic and real-art CPU inference have now run; see [measured results](local-ai-results.md). The private setup status describes installation/discovery only. Read the latest benchmark status before resuming; files or machine state can change after this checkpoint.

## This small step

Completed: provision the standalone Ollama runtime in the repository's ignored tools directory, reuse the verified model cache, and verify local model discovery. That setup step stopped before inference or image-library changes. The later CPU smoke test and development benchmark are documented below and in the results report.

The standalone distribution supports embedding the CLI in another application. This setup uses a temporary loopback server with its own profile; it does not install the desktop tray app, a global service, or a startup entry. [Official Windows distribution](https://docs.ollama.com/windows).

Pinned runtime:

- Release: **Ollama v0.40.8**, published 2026-10-21; not a prerelease in the fetched release metadata.
- Asset: `ollama-windows-amd64.zip`, **1,582,537,058 bytes**.
- SHA-256: `04d1d43ac62c2deb0393307bb49fed7caf6b192de41bdd497aaa9488089d5552`.
- This digest agrees between the official release API and its `sha256sum.txt` asset.
- Destination after verified extraction: `.tools/ollama/0.40.8/`.
- Source: [official release](https://github.com/ollama/ollama/releases/tag/v0.40.8).

The archive includes GPU libraries, even though CPU-only inference is the deployment requirement. This release has no separate Windows x64 CPU-only asset. Archive size does not establish runtime RAM requirements.

## Provisioning and private checkpoints

Download the pinned official runtime under the operator's current authorization, resume partial transfers and verify its size and checksum before extraction. Reuse the existing model cache. Preserve route evidence, credentials, logs and worker identities only under ignored `.cache/runtime-setup/`; they are not portable setup instructions.

Read `.cache/runtime-setup/setup-status.json` before resuming. Verify any recorded process is still the owned worker, respect deliberate `STOP` markers and avoid a second writer to a partial archive. A completed download is distinct from installation, model discovery and inference validation. Local helpers may be absent in a fresh clone; provision the selected assets before running the reusable check below. See [download integrity and routing](model-download-routing.md).

## Repeatable discovery check

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-LocalAiRuntime.ps1
```

Optional parameters are `-RuntimePath`, `-ModelDirectory`, and `-Port` (default `11435`). The model name and manifest digest are deliberately pinned to the cached Qwen candidate. The check:

1. Validates the manifest and all five model blobs by size and full SHA-256; missing files cause an error, never an automatic pull.
2. Refuses an occupied port, then launches its own hidden, below-normal-priority server bound to `127.0.0.1` with an isolated profile and the existing cache.
3. Sets local-only mode, one parallel request, one loaded model, a 4,096-token context, and zero keep-alive in the child environment. The script restores its parent environment immediately after launch. [Ollama local-only configuration](https://docs.ollama.com/faq#how-do-i-disable-ollamas-cloud-features).
4. Calls only local metadata APIs, verifies the expected model digest and declared vision capability, and confirms no model is loaded. HTTP proxy use is disabled for those loopback calls.
5. Saves a private report and shuts down its own server in `finally`. It does not stop existing Ollama services.

This check does **not** establish CPU inference support, image decoding, tag accuracy, refusal behavior, or suitability for 16 GB shared RAM. Check current available memory before inference; an earlier reading is not a memory reservation. Keep [artistic-nudity/NSFW compatibility](local-ai-content-compatibility.md) in the subsequent real-image benchmark.

## CPU inference after setup

The two-image synthetic CPU check now passes: a red square and blue circle were identified correctly in about 31-32 seconds each, with zero reported VRAM use and verified unloading. Peak sampled working set was about 3.12 GiB for the runtime process tree and harness. This is inference evidence, not a tagging-accuracy or 16 GB hardware certification.

The [resumable real-image benchmark and review workflow](local-ai-benchmark.md) is now implemented. It records the WPF app, MCP bridge, runtime worker and harness separately from the already-resident app baseline, and unloads after every image. Read that document and the private run checkpoint before launching another inference worker. The earlier setup-status file describes installation/discovery only; inference results live in their own run directories.
