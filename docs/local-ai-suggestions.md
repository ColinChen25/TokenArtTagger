# Review-only suggestions from learned classifiers

The frozen SigLIP encoder and saved classifiers can now produce suggestions for new images without retraining. This first workflow reuses the four untagged images from the original Qwen benchmark. It does not process the entire backlog or update app tags. Every result requires review; neither a high score nor agreement with Qwen establishes reliability.

## Private pipeline

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Export-LocalAiPreviews.ps1 -Instance <app-pid> -UntaggedBenchmarkDirectory <completed-benchmark> -OutputDirectory <private-inputs> -MaxItems 1
.tools/encoder-venv/Scripts/python.exe scripts/extract_encoder_features.py --inputs <private-inputs>/inputs.json --output <private-features> --app-pid <app-pid> --vision-model-directory <private-vision-copy>
.tools/encoder-venv/Scripts/python.exe scripts/predict_encoder_probe.py --study <saved-study> --training-features <original-feature-cache> --features <private-features> --output <private-suggestions>
.tools/encoder-venv/Scripts/python.exe scripts/export_encoder_suggestions.py --suggestions <private-suggestions> --features <private-features> --inputs <private-inputs>
```

All paths are inside the ignored `.cache` directory. Finish preview export by repeating it without the one-item bound before extracting features. The exporter selects only the completed benchmark's untagged targets, verifies current full content hashes and filename state through MCP, and keeps source paths and labels out of the inference manifest. It can rebind source revisions after an app restart without trusting stale image IDs. Tagged dataset export retains its existing stricter revision checks.

The feature worker remains sequential, CPU-only and resumable, with its existing lock, STOP and memory guards. Run it separately from Qwen, not simultaneously. Use the verified vision-only checkpoint to avoid loading unused text weights. Read the [encoder guide](local-ai-encoder.md) before provisioning or resuming.

After a completed-cache resume, the latest status describes that cache-only session. Use the retained inference-bearing `session-*.json` records for the original memory peaks; a lower resume peak is not a new inference-memory measurement.

## Feature compatibility and saved coefficients

The original feature-cache fingerprint identifies the entire input dataset and checkpoint packaging. A new image set therefore has a different cache identity even when it occupies the same mathematical feature space. `predict_encoder_probe.py` compares the model revision/configuration, preprocessing, preview dimensions, numerical precision, device, feature operation and package versions separately. Only dataset provenance and the already-verified lossless packaging change are ignored for this comparison. Unknown preprocessing settings or a different source model are rejected. Existing feature caches are retained unchanged.

Saved classifiers contain numeric JSON coefficients, intercepts and class names; no executable pickle is loaded. Prediction reproduces binary logistic, multiclass softmax and constant-class behavior. Each suggestion retains the study, classifier-file digest, feature-space identity and target-feature identity. The tool verifies the saved partition and refuses any exact reserved-test target before producing suggestions. New exposed images must remain development/review data in future grouping; the current pilot already belongs to the original development benchmark.

The classifier cannot identify a race absent from training. Its scores remain uncalibrated and it has no approved automatic acceptance threshold. It does not overwrite authoritative filename labels or stage temporary tags.

## Review and verification

`review.html` displays verified MCP PNGs beside suggestions, with uncalibrated scores under a disclosure. It contains no scripts, remote resources or apply action. Preview hashes must match the features used for prediction. Duplicate targets, changed preview identities and any automatic-acceptance record are rejected. File references and PNG signatures are verified; browser rendering is not claimed.

`test_encoder_prediction.py` compares numeric prediction against scikit-learn, verifies feature-space compatibility boundaries, and—when the private study exists—reproduces all 233 already-scored development/calibration predictions. It also checks rejection of the reserved test. With the private unlabeled pilot present, it exercises the end-to-end suggestion/export path and invalid review guards. `Test-AgentIntegration.ps1` covers actual MCP preview export, cached resume and changed-source rejection with disposable images.

## Save the pilot to the agent review queue

The app now supports persistent, review-only proposals through `queue_ai_suggestions` and `list_ai_suggestions`. The saved classifier output can be imported without loading either model:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Import-LocalAiSuggestions.ps1 `
  -Instance <app-pid> -Configuration Release `
  -SuggestionsDirectory .cache/encoder-suggestions/backlog-pilot-v1 `
  -SourceBenchmarkDirectory .cache/local-ai-benchmark/pilot-v2 `
  -Model "siglip2-base-patch16-naflex + probe-v3"
```

Use the configuration of the newly built bridge; Debug remains the default. Keep the source library scanned. The importer accepts 1–100 completed review-only classifier results, matches their hashes to the source benchmark's untagged targets, pages the current app's untagged filenames, and verifies live hashes/revisions. If a source now has authoritative filename tags, disappeared, or changed, import stops. It refuses automatic-acceptance artifacts. It saves a private request and receipt beside the suggestions. Repeating a successful import is idempotent; no inference or tag application occurs.

The queue retains the supplied model label, run fingerprint, proposed fields and tag snapshot. Scores remain in the original private classifier report; they are not presented as calibrated confidence in the app. See the [queue contract](agent-integration.md#saved-ai-proposals) for limits, revisions and retry behavior.

The [desktop review window](local-ai-desktop-review.md) now supports explicit field decisions, corrections, dismissal and saved provenance. It stages only user-reviewed temporary tags, with a separate rename confirmation. Automatic tagging and background inference scheduling remain future work.
