# Local AI tagging implementation draft


Development branch: `codex/local-ai-tagging`, based on `master`.

Configure source folders in the private local dataset manifest. Use the [inventory workflow](ai-dataset-inventory.md) to distinguish authoritative filename tags from the untagged backlog; retain collection listings and label distributions only locally.

**Recommendation:** add a local vision model that predicts the existing controlled tags, then improve it with a searchable collection of human-tagged examples. Automatically stage only sufficiently reliable fields, and send uncertain fields to the existing manual workflow. Keep filename changes behind the existing preview and confirmation.

This is feasible enough to justify a small benchmark first. Neither accuracy nor throughput on this image collection has been measured. The first success metric should be time saved at an acceptable error rate, rather than the percentage of images receiving some prediction.

## Confirmed requirements

- Support collections mixing TokenArtTagger filenames, older manual filename formats and untagged images.
- **Filename tags are authoritative.** Import these as trusted labels after verifying the parser maps the old and new formats correctly. Unsupported or ambiguous filename syntax is an import issue, not evidence that the user's tags are wrong.
- Art styles are mixed. Evaluate performance across styles rather than optimizing solely for one illustration domain.
- Artistic nudity and other NSFW fantasy elements must remain taggable, including minimal tribal outfits and succubus imagery. Do not block images based on an NSFW score or remove visual evidence before classification. Evaluate model refusals and exposure-related tagging errors explicitly; see [content compatibility, prompt, and benchmark criteria](local-ai-content-compatibility.md). The cached Qwen model has not yet been tested against this requirement.
- Deployment baseline: **16 GB total system RAM shared with other running programs**, with CPU-only operation as a design requirement and a discrete GPU optional. The development machine's discrete GPU and additional RAM must not become minimum requirements. Installed RAM is not a usable-memory budget; admission and pause decisions must use current available memory and measured workload peaks.
- Slower background processing is acceptable. Prefer accuracy, low memory use, foreground responsiveness, and reliable pause/resume over throughput. Do not substitute confident guesses or repeated low-quality predictions for reliable tagging merely to use a smaller model.
- Favor **fewer mistakes**, accepting lower automatic coverage. The earlier suggested 98% field precision is not an accepted tolerance; choose a more conservative policy from measured results and leave unsupported cases unresolved.
- Support both tagging the untagged backlog and auditing existing tags. An AI disagreement with an authoritative filename is a separate suggestion and must not silently replace the original tag or become a training label.
- Caster cues: wizard clothing tends toward robes and hats; cleric clothing may be white and resemble Roman togas; druid clothing is more plant/nature themed. Treat these as soft cues alongside trusted examples, not universal rules.
- The user identified **wings, and possibly hooves**, as major distinguishing features. The exact category pairing and direction were not specified; preserve these as features to inspect without inventing a deterministic race rule.
- Provision only the selected setup under the current operator's download authorization. Reuse verified caches, resume partial transfers and verify checksums. Keep network configuration and transfer evidence private.
- Minimize confirmation work. An initial implementation using the existing batch rename confirmation is acceptable for simplicity; avoid requiring a separate click for every image. More autonomous renaming is a later decision after reliability is measured.

Unanswered details are collected at the end; they do not block branch preparation or the software design.

## What the current app already provides

Repository inspection found a .NET 10 WPF application, with reusable foundations:

- `TagSchema` defines gender, role, role-dependent weapon/style, and fantasy race tags. It also normalizes aliases such as `chainsaw` to `exotic`.
- `TagSet` supports missing fields. Setting role to `generic` clears style; changing role can clear an incompatible style.
- `FilenameParser` imports existing filename tags. The user confirms existing labels are correct; inspect legacy filename coverage so importing them preserves that ground truth.
- `WorkInProgressTagStore` persists partial tags in local app data. It currently records tags without human/AI provenance.
- `FilenameGenerator`, `RenameReadiness`, and `FileRenamer` provide validation and a separate rename workflow.
- `ContentHasher` computes full SHA-256 hashes. The existing `FileIdentity` includes path, size, and modification time, so it is unsuitable as the only long-lived AI identity across renames.

The TODO now follows the agreed product direction: calibrated high-confidence fields may eventually be staged automatically; uncertain cases require review. During benchmarking, all predictions stay separate from accepted tags. Renaming remains a separate action.

Validate the baseline on a 16 GB machine without requiring a discrete GPU. Until measured there, portability remains a target. Recheck current CPU/runtime compatibility and available memory rather than relying on a development-machine snapshot.

## Options worth comparing

These are candidates, not claims about which model performs best on tabletop character art.

| Approach | Advantages for this app | Main limitation | Proposed use |
|---|---|---|---|
| Local vision-language model | Directly examines an image and can follow custom tag definitions | Can invent details or confidently guess visually ambiguous categories | First prototype |
| Image embeddings plus nearest tagged examples | Reuses personal conventions and shows the user supporting examples | Visual similarity can follow pose or art style instead of the relevant tag | Personalization and review assistance |
| Small classifiers trained on frozen embeddings | Learns the user's labels with relatively modest training work | Requires sufficiently varied, trusted examples for each class | Later acceleration and calibrated predictions |
| WD image tagger | Produces many illustration-oriented attributes; has an ONNX distribution | Its Danbooru vocabulary and training domain differ from this app's fantasy taxonomy | Optional comparison for an anime-heavy collection |
| Fine-tuning a full vision-language model | Could adapt more deeply to the collection | More data preparation, training, deployment, and regression work | Defer until simpler approaches plateau |

Start the feasibility benchmark with **Qwen3-VL 2B Instruct Q4_K_M**, whose current Ollama listing is approximately **1.9 GB** including its vision projector. This is a candidate, not a claim of sufficient accuracy or a 1.9 GB RAM requirement. Record the resolved model digest and runtime version; even explicit tags can change. [Exact model listing](https://ollama.com/library/qwen3-vl:2b-instruct-q4_K_M).

If the first model is unsuitable, compare **Qwen3.5 2B** next, one model at a time. Its current Windows-relevant llama.cpp listing is approximately **2.7 GB**, using Q8_0 language weights and a BF16 projector. A **Qwen3.5 0.8B** comparison is smaller (about **1.3 GB** for that backend), but may yield more unresolved fields or errors and must pass the same evaluation. Download and parameter counts are not runtime-memory measurements. Larger 4B models are optional later quality comparisons only if the measured shared-memory budget permits; 8B/9B models are removed from the default plan. [Qwen 2B](https://ollama.com/library/qwen3.5:2b), [Qwen 0.8B](https://ollama.com/library/qwen3.5:0.8b).

For embeddings, start by evaluating **SigLIP 2 Base**. Its official model card documents image feature extraction and zero-shot image classification. Whether these features separate this user's fantasy races and weapon categories is an experiment, not an established result. [Google model card](https://huggingface.co/google/siglip2-base-patch16-256).

If the vision-language model refuses otherwise classifiable art or gives poor attribute recognition, compare **WD ViT Tagger v3** as a smaller classifier candidate before EVA02-Large. It supports ratings, character tags, and general tags with an ONNX distribution; its reference inference returns scores without a conversational refusal step or a rating-based image gate. Use relevant attributes as auxiliary evidence and explicitly map them into this app's vocabulary. Its Danbooru training domain may fit some illustrations better than the mixed collection, and suitability for 16 GB shared RAM still requires measurements. See [the author's model card](https://huggingface.co/SmilingWolf/wd-vit-tagger-v3), [reference inference](https://huggingface.co/spaces/SmilingWolf/wd-tagger/blob/main/app.py), and [comparison criteria](local-ai-content-compatibility.md).

## Low-memory background profile

These are proposed starting settings, not tested guarantees or already-implemented limits:

| Setting | Initial policy |
|---|---|
| Target machine | 16 GB total shared RAM; CPU-only inference must be tested; GPU optional |
| Memory target | Aim for at most 4 GiB incremental system RAM for the tagging workload, including app/decoder growth and model runtime; measure cold load and inference peaks |
| Headroom | Initially reserve 4 GiB of available system memory; admit work only when the measured additional peak plus reserve fits. If it does not fit, wait or pause |
| Concurrency | One image request and one loaded model; unload the embedding model before loading the vision-language model |
| CPU use | Start with two inference threads and an app-owned worker at below-normal priority; make both configurable after measuring responsiveness |
| Input | Stream one original at a time; begin around 768 pixels on the longest edge, preserving aspect ratio and small-detail limitations |
| Context/output | Begin with a 4,096-token context and a short bounded structured response; detect truncation and fail the item rather than silently losing schema or evidence |
| Queue/cache | Disk-backed checkpoints after each image; cache by full content hash and versioned settings; no preloading of the whole library |
| Idle/pressure | Stop admitting images under memory pressure, cancel the current request if needed, unload the app-owned model, and resume after headroom recovers with hysteresis |

The memory target is not an operating-system hard cap. Track the model process tree, app allocations, system available memory, and commit pressure; integrated-GPU memory also competes with system RAM. Sampling can miss spikes, so use measured peaks plus a margin and validate under competing foreground load. Never claim that having 16 GB installed alone guarantees a safe run. Avoid relying on paging to make a larger model appear to fit.

For an app-owned Ollama service, explicitly set one loaded model and one parallel request, bound context, and unload when idle or paused. These controls are supported by Ollama, but do not impose a hard RAM ceiling. Do not change global runtime settings or stop models belonging to other applications; isolate the worker or report that isolation is needed. [Ollama concurrency and unloading](https://docs.ollama.com/faq), [context memory](https://docs.ollama.com/context-length).

Longer wall-clock time can buy sequential processing, unloading between phases, and a limited second look at difficult images. It cannot guarantee that a smaller model will recover distinctions it does not recognize. Keep the acceptance standard fixed: prefer more abstentions and manual review to more incorrect tags. Test a higher-resolution second pass only when it fits the same memory policy. Treat repeated answers as correlated evidence, not independent votes.

For personalization, build embeddings incrementally and store them on disk. Begin evaluation without examples, then test one or two retrieved image examples within the context budget. If that raises memory too much, compare a lightweight classifier over frozen embeddings and show neighbors in review; do not require a large multi-image prompt to make the basic feature usable.

## Proposed behavior

1. Select a folder or subset and choose **Analyze with AI**. Offer both **Tag untagged images** and **Check existing tags**. Fill missing fields in the former; record possible discrepancies separately in the latter, preserving authoritative filename tags.
2. Decode each original locally and prepare an aspect-preserving inference image. Do not use the small UI thumbnail as the only input; ears, instruments, and weapon tips matter.
3. Ask the model for allowed tag candidates, short visible evidence, alternatives, and explicit abstentions.
4. Validate output against the application's schema and role/style dependencies.
5. Save predictions separately from accepted tags. After validation establishes reliable acceptance rules, automatically stage qualifying fields into work in progress.
6. Route incomplete, conflicting, unfamiliar, or low-confidence results into **Needs review**. Save progress between images so the batch can resume after cancellation or restart.
7. Human corrections become trusted examples. Automatically accepted AI predictions stay marked as AI-originated until a human verifies them.
8. Complete images can enter the ordinary rename preview with one batch confirmation in the initial version. AI inference itself never initiates file renames; avoid introducing mandatory per-image acceptance for results that qualify for automatic staging.

For example, the system might stage `female / range / bow`, while leaving race unresolved between `elf` and `human`. Review should focus on that remaining field instead of making the user retag the entire image.

Use `null` plus a reason for uncertainty. Do not translate “I cannot tell” into `other`, `monster`, or `generic`: those are actual categories with their own meanings. Since filenames require gender and race, abstention deliberately leaves an image incomplete. For fictional characters, the gender field follows the user's tagging convention for depicted presentation; it should permit abstention for ambiguous or obscured art.

Role and style must be evaluated together where needed. A visible axe does not resolve melee versus range. A staff alone may not establish caster, and horns alone may not distinguish tiefling from demon. Multiple characters, several prominent weapons, cropped bodies, and visually indistinguishable fantasy races are expected review cases.

## Learning from the existing library

Begin with **retrieval**, then consider **training**:

- Import authoritative filename labels as trusted examples, retaining the original filename and parser version. Inventory old filename variants and verify normalization before importing; route unrecognized syntax to an import report. New AI-only predictions remain separate from this trusted collection.
- Compute an embedding per image and retrieve roughly 3–5 diverse relevant examples. Use similarity for search, not as a probability that a tag is correct.
- Present example images with their confirmed labels to the vision model when testing whether examples help. Labels alone cannot teach a visual correspondence. Under the low-memory profile, retrieve candidates on disk but initially send only one or two examples; do not load all retrieved images into one prompt by default.
- Include contrasting examples for confusing categories, not just several near-identical pictures. Avoid allowing one duplicated illustration or art style to dominate the examples.
- When auditing an already-tagged image, exclude the image and its duplicate variants from retrieved examples, and withhold its current labels from the initial prediction. Otherwise the audit can simply reproduce the answer it was shown. Compare the independent result with the filename afterward.
- Store corrections immediately. Rebuild the example index incrementally; this changes the model's reference material without changing its weights.
- Once enough trusted examples exist, train lightweight per-category classifiers on frozen embeddings. Train style conditionally on role, or validate role/style jointly. Missing fields provide no training label for that category.
- Refresh classifiers in explicit versioned batches and compare each candidate with the previous version. Promote only a measured improvement.

For an MVP-sized library, a local matrix search over embeddings is sufficient to test retrieval; a dedicated vector database is not a prerequisite. Optimize the index only after measuring size and search latency.

Fine-tuning with LoRA is a possible later experiment if consistent errors survive better definitions, examples, and lightweight classifiers. It should not be the first dependency for getting useful automation.

## Confidence and manual review

**A model-generated confidence number is not a calibrated probability.** Agreement between models or repeated prompts is useful evidence, but correlated mistakes can remain. The acceptance policy must be measured on separate labeled examples. Probability calibration provides a way to compare predicted confidence with observed correctness. [scikit-learn calibration documentation](https://scikit-learn.org/stable/modules/calibration.html).

Candidate reliability signals include classifier margins, agreement with independently obtained predictions, neighbor label consistency, distance from known examples, schema validity, and ambiguity flags. Derive thresholds on a calibration split, separately where data permits for each category or label. Rare categories with insufficient evidence remain review-only.

The user prefers **fewer errors than the originally proposed 98% field precision**. Do not hard-code 98% as the acceptance policy. During calibration, compare more conservative operating points, such as 99% and 99.5%, alongside automatic coverage and uncertainty; these are experiment targets, not agreed guarantees. Where the available data cannot support a reliable threshold, keep that category review-only. Four individually reliable fields do not automatically imply equally reliable complete filenames; measure whole-image exact matches as well.

During the pilot, all output is reviewable suggestions. Enable automatic staging only for categories with adequate measured evidence. Continue randomly auditing a small share of auto-staged results, because reviewing only uncertain images would miss confident errors. Reset or revalidate acceptance policies when the model, taxonomy, prompt, preprocessing, or example pool materially changes.

The review panel should show the image, current tags, proposed tags, unresolved fields, a brief reason, and relevant confirmed examples. Useful actions are accept, correct, skip, apply the same correction to selected images, and revert an AI batch. Keep review keyboard-friendly and compatible with Bucket Tagging.

## Implementation shape

Keep the WPF application and its core rules. Add narrow services rather than putting model calls into `MainViewModel`:

| Component | Responsibility |
|---|---|
| `IAiTaggingProvider` / `OllamaTaggingProvider` | Send image bytes and a versioned prompt to a local model; parse bounded structured responses |
| `AiTaggingCoordinator` | Sequential queue, cancellation, timeouts, resumable jobs, available-memory admission, pressure pause/unload, CPU/GPU selection, progress |
| `AiSuggestionValidator` | Allowed values, role/style compatibility, abstention, dependency-aware merging |
| `AiSuggestionStore` | Predictions, provenance, run metadata, review decisions, errors, rollback data |
| `ExampleIndex` | Full-hash identities, embeddings, confirmed examples, similarity retrieval |
| `ConfidencePolicy` | Versioned acceptance rules derived from evaluation, separate from model output |
| `AiReviewViewModel` | Review filters, side-by-side proposals, corrections, batch actions |

Ollama supports a JSON schema through the chat API's `format` field, including for vision models. Generate enum choices from `TagSchema`; allow null values explicitly. Still validate responses in C# and handle malformed, truncated, or contradictory output. The model should return descriptive data only and have no access to file-operation tools. [Ollama structured-output documentation](https://github.com/ollama/ollama/blob/main/docs/capabilities/structured-outputs.mdx).

Start with C# `HttpClient` calling Ollama on loopback. This keeps Python out of the first end-user feature. For the embedding experiment, a pinned local Python environment is a practical research option. Before shipping embeddings, compare that sidecar with an ONNX export tested for matching preprocessing and output. ONNX Runtime has C# bindings; current Windows documentation recommends evaluating WinML for deployment, while DirectML remains supported. Do not assume a usable SigLIP export or provider compatibility until tested. [C# runtime](https://onnxruntime.ai/docs/get-started/with-csharp), [Windows deployment](https://onnxruntime.ai/docs/get-started/with-windows.html).

Use a small dedicated SQLite store under local app data for resumable AI jobs and provenance, without first migrating the whole library. Suggested records: assets, asset locations, prediction runs, per-field suggestions, per-field human decisions, embeddings, and policy versions. SQLite is a new dependency in this proposal, not an existing app capability. A JSON prototype is acceptable for the initial benchmark; avoid extending the existing tag-only JSON file into an unversioned AI database.

Key cached predictions by the **full content hash**, model digest, prompt/schema versions, preprocessing version, and personalization version. Keep separate file-instance decisions: duplicate content may be reused computationally without silently overwriting different manual annotations. Track locations separately so renames do not discard learning history. Before applying a delayed result, verify that the image and current tag revision still match the job snapshot. Manual edits made during inference always take precedence.

Store evidence and raw scores separately from any calibrated score. Keep previous tag values for reverting an AI batch. Avoid averaging scores from different models as though they share a probability scale.

For GIFs and animated WebP, use representative frames only after reliable decoding is available. Otherwise label the limitation and send the item to review. Preserve aspect ratio and handle transparency predictably; preprocessing must not crop away a weapon or other decisive detail. Decode failures become explicit job errors rather than tag guesses.

Local execution has no per-image hosted API fee, though it uses electricity, disk space, and GPU time. Download during setup under the user's current download authorization, then support offline operation. Begin with one candidate rather than downloading the whole comparison set at once. Configure Ollama's local-only mode and reject cloud model selections; loopback alone does not establish that the chosen model is local. [Ollama local-only configuration](https://github.com/ollama/ollama/blob/main/docs/faq.mdx).

## Delivery sequence and acceptance checks

The next concrete deliverable is a **resumable local benchmark runner and report**, controlled through the app's existing integration where practical. Complete it in this order:

1. Verify image decoding (especially the 101 backlog WebP files), stream full content hashes, and identify duplicate/variant groups. Use existing MCP/CLI tools for app inspection rather than duplicating filename parsing. Keep private per-image results outside Git.
2. Add the local provider, strict output validator, disk checkpoints, resource sampling, and one-request queue. Save benchmark predictions separately; this phase must not call `update_tags` or rename files. Unit and fixture checks can proceed before model downloads.
3. The first candidate is now downloaded and checksum-verified; reuse that cache. Complete the [pinned runtime setup](local-ai-runtime-setup.md) under the updated download authorization, then run one bounded CPU vision smoke test. Run a roughly 30-50-image feasibility set to check decoding, schema output, cold-load/inference memory, CPU latency, and pause/resume. Include the artistic-nudity and NSFW challenge cases in the [content-compatibility protocol](local-ai-content-compatibility.md), recording refusals separately from uncertain tags. This small set cannot establish automatic-acceptance precision.
4. If feasible, run the larger evaluation below with fixed duplicate-safe splits. Compare against authoritative filenames hidden from the model. Validate the baseline on 16 GB hardware under foreground load; a CPU run or memory limit on the 32 GB development machine is useful preliminary evidence, not a substitute for that test.
5. Choose the smallest candidate that meets both the measured resource policy and useful conservative accuracy. Then add the background suggestions/review workflow. If no candidate qualifies, preserve review-only output and report the shortfall instead of increasing memory requirements silently.

| Phase | Deliverable | Evidence needed to proceed |
|---|---|---|
| 1. Dataset audit and benchmark | Filename inventory complete; next: duplicate/decoder audit, low-memory local runner, reproducible comparison | Useful conservative predictions, bounded peak RAM, responsive foreground apps, and recoverable interrupted jobs on the 16 GB target |
| 2. Suggestion MVP | Local model connection, background queue, saved proposals, review UI | No loss of manual edits; valid schema; cancel/resume and decoder failures handled |
| 3. Personalization | Confirmed-example retrieval; optional frozen-embedding classifiers | Improvement over the no-example baseline on held-out images |
| 4. Selective automation | Per-category acceptance policies, automatic partial tags, batch revert, audit sampling | Sufficient precision and useful coverage, measured separately for fields and complete images |
| 5. Optimization | Model scheduling, cache tuning, larger-library retrieval | Measurements identify a worthwhile bottleneck |

After the feasibility check, use a roughly **300–500 image pilot** for model comparison with common and rare categories, different art styles, and difficult cases represented. The confirmed filename labels supply ground truth without requesting wholesale relabeling. Reserve disjoint examples for retrieval/training, calibration, and final testing before tuning; feasibility images belong to development data, not the final test set. The dataset may not establish very high precision for every rare class, even if the overall result looks strong.

Separate training/example retrieval, calibration, and final testing. Keep exact duplicates and related variants in the same split; exclude test examples from retrieval. Hide filenames, folder labels, and the target labels from the vision prompt during evaluation so it actually has to inspect the image. Include previously untagged images in a later audited pilot, since previously tagged examples may be easier than the backlog.

Record per-category precision/recall, common confusions, full-tag exact match, automatic acceptance coverage, review rate, median and slow-case latency, total peak workload RAM (including cold load and decoding), minimum system available memory, commit pressure, peak VRAM where relevant, CPU/foreground responsiveness, decoding/output errors, and measured human review time. Report sample counts and uncertainty; a handful of correct predictions is insufficient evidence for unattended acceptance.

Focused implementation checks should cover invalid tags, role/style conflicts, generic-role handling, partial acceptance, human edits racing with inference, cache invalidation, crash/resume, cancellation, batch revert, and the fact that AI processing cannot invoke rename. The benchmark should estimate total backlog duration from observed throughput rather than promise a speed now.

## Remaining details for later

1. Source paths and filename import coverage are now resolved. The tagged folder is nested inside the backlog root and must be excluded from the backlog enumeration. Content-based duplicate grouping and image decoding remain to be checked before assigning evaluation splits.
2. Which categories wings and hooves distinguish, and which tag each feature suggests. Caster clothing guidance is recorded above; other ambiguous race/weapon rules can be learned from the labeled examples where sufficiently consistent.
3. Whether unresolved gender/race should remain blank or eventually use a new unknown tag. Default: keep them blank without changing the filename schema.
4. Specific CPU and operating-system details of additional target machines. Default to the 16 GB shared-RAM profile and optional GPU; slower unattended background work is accepted, subject to memory and responsiveness checks.
5. Provisioning on additional machines requires a fresh route check and current download authorization; see the [download guide](model-download-routing.md).

Proceed with the current taxonomy, local-only inference, the 16 GB shared-RAM target, conservative partial acceptance, separate audits of existing tags, preserved human labels, and a single batch rename confirmation in the first version. Build the benchmark before a full-library tagging run. Keep installation/downloads within the user's current authorization and verify their integrity.
