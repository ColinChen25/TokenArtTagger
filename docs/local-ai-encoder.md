# CPU encoder and classifier experiment

This experiment uses a frozen image encoder and learns small linear classifiers from authoritative filename labels. It is a private research workflow; it does not stage tags or rename images. Automatic acceptance remains disabled.

## Selected setup

- Model: `google/siglip2-base-patch16-naflex`, revision `b53b807d3a2d5e2b3911292f2d69e5341cdc064c`, Apache 2.0. Published checkpoint: 1,500,985,224 bytes; SHA-256 `ac5f28bbdf92c0c1696ccbd3ce716426049cd67ad8045b66d0d938b0f9c8bbec`.
- Runtime: isolated Windows CPython 3.12 environment, PyTorch `2.9.1+cpu`, Transformers `4.57.1`, scikit-learn `1.7.2`. The complete wheel versions and hashes are in `scripts/encoder-windows-py312.lock.txt`.
- Inference: only `Siglip2VisionModel`, 92,930,304 parameters; frozen float32 weights, CPU, two compute threads, one image at a time. Loading must report zero missing or mismatched vision weights. The published checkpoint also contains text weights; no text model or tokenizer is instantiated.
- Preprocessing: the app supplies a full-image PNG bounded to 768 pixels on the longer side. The adapter composites transparency on white, applies the documented NaFlex processor with at most 256 patches, and normalizes its 768-dimensional pooled vector. No center crop, label prompt, source filename, or NSFW filter is supplied to the encoder.

NaFlex preserves aspect ratio approximately by resizing to patch multiples. These implementation choices follow the [official SigLIP 2 documentation](https://huggingface.co/docs/transformers/v4.57.1/en/model_doc/siglip2) and [pinned model card](https://huggingface.co/google/siglip2-base-patch16-naflex/tree/b53b807d3a2d5e2b3911292f2d69e5341cdc064c). CPU wheel installation follows [PyTorch's official instructions](https://pytorch.org/get-started/previous-versions/). Model capability claims are not collection-specific accuracy evidence.

The selected model/config/card payloads total 1,500,989,329 bytes; the 32 pinned wheel payloads total 206,333,278 bytes. Reuse verified caches and keep installation receipts under ignored `.cache/encoder-setup/`. No model files or private source paths belong in commits.

### Smaller local checkpoint

For new feature runs, prefer the verified lossless vision-only copy. It contains the original 208 float32 vision tensors (371,746,008 bytes), with no quantization, retraining or download. The original published cache is retained. Preparation verifies every tensor's key, dtype, shape and exact values against that pinned source, then writes a hash receipt. Inference verifies the source and derived file hashes before loading.

```powershell
.tools/encoder-venv/Scripts/python.exe scripts/prepare_encoder_vision.py --app-pid <app-pid>
.tools/encoder-venv/Scripts/python.exe scripts/extract_encoder_features.py --inputs <preview-directory>/inputs.json --output <new-feature-directory> --vision-model-directory .cache/models/siglip2-base-patch16-naflex-vision-f32 --app-pid <app-pid>
```

Preparation is checkpointed under the derived directory, respects STOP and an exclusive lock, and verifies an existing copy before reusing it. It temporarily reached about 3.00 GiB OS private memory; this is a one-time setup cost. The paired eight-image test reduced the inference worker's OS private high-water mark from **3.19 to 1.08 GiB**, while every normalized feature, preview hash and perceptual hash exactly matched the original complete cache. Worker working set stayed around 0.71 GiB; private commit and resident working set are different measures. Both workers exited cleanly.

The smaller checkpoint changes the feature run's provenance fingerprint. Use a new output directory when explicitly selecting it. Keep the completed original feature cache for existing studies; do not re-encode it just to change packaging. The default command still accepts the original cache for compatibility. The study driver forwards `-VisionModelDirectory` for new runs. Admission remains 8 GiB until broader low-memory validation supports a lower threshold; eight images do not certify 16 GB hardware.

## Reusable commands

For a new installation, supply an existing Python 3.12 executable and the authorized proxy URL. This installs only in `.tools/encoder-venv`, preserves pip caches and partial model downloads, and uses the selected checkpoint and hashed dependency lock:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Install-LocalAiEncoder.ps1 -BootstrapPython <python312.exe> -Proxy <authorized-proxy-url>
```

Before resuming, read `.cache/encoder-setup/status.json`, verify any recorded live worker identity, and respect its `STOP` marker. Installation success does not imply inference success. Inference is offline, with local-only loading and remote model code disabled by the selected built-in model class.

After scanning the collection in the app, refresh the authoritative dataset with `Export-LocalAiDataset.ps1` as documented in [the dataset guide](local-ai-dataset.md). Then:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiPreviews.ps1 -Instance <app-pid> -DatasetDirectory <private-dataset-directory> -OutputDirectory <private-preview-directory>
.tools/encoder-venv/Scripts/python.exe scripts/extract_encoder_features.py --inputs <private-preview-directory>/inputs.json --output <private-feature-directory> --app-pid <app-pid>
.tools/encoder-venv/Scripts/python.exe scripts/train_encoder_probe.py --dataset <private-dataset-directory> --features <private-feature-directory> --inputs <private-preview-directory> --output <private-study-directory> --development-manifest <earlier-benchmark-manifest>
```

All data/output paths must be inside the ignored repository `.cache`. Repeat `--development-manifest` for other prior studies so their targets and references cannot become final-test data. The current study should include the Qwen baseline and both reference-plan manifests. Supply the real app PID, not an unrelated process.

After the first study has been inspected, pass `--preserve-test-splits <original-study>/splits.json` to every regrouped study. The final test may shrink when groups merge, but cannot gain exposed images. Use `--variant-audit <private-audit.json>` to preserve confirmed visual links; its `pairs` entries contain two `hashes`, `related` (true, false, or null for uncertain), and a note. Only confirmed links add grouping constraints. Include reviewed development examples in a development manifest. The current run uses `.cache/encoder-study/probe-v1/splits.json` and `.cache/encoder-study/group-audit-manifest.json`.

`scripts/Invoke-LocalAiEncoderStudy.ps1` runs these stages sequentially. Supply `-Instance`, `-DatasetDirectory`, `-PreviewDirectory`, `-FeatureDirectory`, `-OutputDirectory`, and a PowerShell `-DevelopmentManifest @('<manifest-1>', '<manifest-2>')` array. It can wait for an already-running preview exporter after verifying its process identity. `driver-status.json` records the active stage. A study-directory STOP is checked between stages; use the current preview/feature directory's STOP for an image-boundary pause within that stage.

The driver also forwards `-PreserveTestSplits` and `-VariantAudit`; supply them for the current collection's follow-up studies.

`Export-LocalAiPreviews.ps1` uses the existing read-only MCP client. It validates source revisions, filename labels, and full source hashes before and after rendering. Its inference manifest contains content hashes and cached PNG references, without original paths or labels. Completed preview hashes are checked on resume. `-MaxItems` bounds new work and retains prior results.

The feature worker verifies the model files and preview hash, performs inference, validates a finite unit vector, rechecks the preview hash, and atomically writes one result. Cache identities include the model, preprocessing, preview provenance and package versions. `--max-items` bounds new vectors. `STOP` is checked before work and after each inference; the current uncommitted vector is discarded. Existing successful items are preserved.

Workers take exclusive locks. Inspect `status.json`, the lock and process PID/creation time before resuming; a leftover lock file is not itself evidence of a running process. Do not remove deliberate STOP markers without a resume request. A completed feature run can describe a deliberately partial preview manifest; classifier training separately requires full identity agreement with the complete dataset.

## Resource policy

Admission requires 8 GiB available physical RAM. A 200 ms sampler records the inference worker tree, its launchers, the existing app, physical headroom and worker private memory. Windows process high-water counters additionally catch transient working-set/private peaks between samples; summed process peaks are an upper bound rather than a concurrent tree peak. Pause if worker plus launcher working set or worker OS private-memory high-water exceeds 4 GiB, or if physical/commit availability falls below 4 GiB. The guard is cooperative at image boundaries, not a hard OS allocation limit. Session reports preserve earlier peaks when a later resume does no inference.

All 1,469 tagged previews now have valid cached vectors. The full continuation sampled about 0.72 GiB worker working set and 1.81 GiB private memory, but Windows recorded a 3.19 GiB private high-water mark. Use the larger value for planning. This was the 32 GB development machine, not a 16 GB hardware validation. See [measured results](local-ai-results.md) for timing, accuracy, grouping limitations and the separate workload/app measurements. Run preview export and sustained inference sequentially when measuring complete workload peaks.

## Evaluation boundaries

Near-variant screening uses embedding similarity and perceptual hashes from app previews. Its transitive groups stay together across training, calibration and reserved-test partitions. Groups containing earlier feasibility targets or references are assigned to development. The screening is heuristic; the generated cross-partition gallery still needs visual audit, especially for crops and different backgrounds.

Three regularized linear models predict gender, joint role/style, and race. The joint label preserves role/style validity. Reports include majority-label and nearest-neighbor baselines, full-tag matches, and field accuracy with every evaluated image in the denominator. The final-test partition is saved but never scored by this script.

The saved classifier records the source-label fingerprint, feature identity, partition identity, training recipe, implementation hash and dependency versions. A changed label snapshot or implementation requires a new study directory; old experiment results cannot silently become the provenance for newly trained weights.

Scores are uncalibrated. Threshold exploration compares 99% and 99.5% observed precision targets on calibration data, with minimum support in distinct variant groups. These are exploratory operating points, not agreed guarantees. A group is counted correct only if every issued prediction in it is correct. The report also gives a 95% Wilson lower bound and says whether it reaches the requested target; observed precision alone is insufficient. Heuristic groups may still be related, and repeated threshold selection does not establish a statistical guarantee. All output remains review-only. Classes with very few independent examples cannot acquire trustworthy precision estimates by this method.

After a complete study, `scripts/export_encoder_review.py --study <study-directory> --inputs <preview-directory> --baseline <Qwen-baseline-directory> --annotations <content-annotations.json>` creates a private static review, paired comparison and overlapping content-group counts. Invalid baseline outputs remain failures, including when a missing style happens to equal a generic character's null style. The final-test partition is absent from the review. Reports show uncalibrated scores, the target and nearest training example, and authoritative versus predicted tags.

The encoder has no generated-text refusal path or content gate. Successful features on revealing art establish processing compatibility only; relevant anatomy or clothing can still be misclassified. Retain the separate content-group evaluation described in [the compatibility guide](local-ai-content-compatibility.md).

The cached features also support a [per-target visual-reference experiment](local-ai-retrieved-references.md). Reference selection uses training-only embeddings and no target labels, followed by a visual related-artwork audit. It then reuses the existing Qwen benchmark one image at a time; this is distinct from accepting the linear classifier's own prediction.

## Verification

```powershell
.tools/encoder-venv/Scripts/python.exe scripts/test_encoder.py
.tools/encoder-venv/Scripts/python.exe scripts/test_encoder_study.py
.tools/encoder-venv/Scripts/python.exe scripts/test_encoder_runtime.py --app-pid <app-pid>
.tools/encoder-venv/Scripts/python.exe scripts/test_encoder_vision_copy.py --inputs <preview-directory>/inputs.json --reference-features <completed-feature-directory> --app-pid <app-pid>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-LocalAiEvaluation.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AgentIntegration.ps1
```

The encoder checks cover data boundaries, finite normalized vectors, settings identity, transitive and visually confirmed group isolation, preservation of the original holdout, role/style round trips, linear and constant-class cases, exclusion of derived MCP metadata from scoring, small-sample uncertainty, paired-comparison denominators, and Windows checkpoint replacement while a reader holds the file. The complete synthetic study checks development isolation, label-provenance invalidation and final-test exclusion. The opt-in runtime check uses cached weights and disposable PNGs to test item limits, STOP preservation, resume, cache reuse without loading weights, and corrupt-vector rejection. MCP integration additionally checks label-free previews, hash receipts, cache reuse and preprocessing invalidation with disposable images.
