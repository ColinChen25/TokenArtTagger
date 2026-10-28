# Local AI development results

Updated 2026-10-28 on `codex/local-ai-tagging`. **Both local approaches run on CPU, but neither is ready for unattended tagging.** Qwen uses filename labels only for scoring; the frozen-encoder classifiers learn from a separate labeled training partition. Target labels and filenames never enter feature extraction. Four classifier proposals are now persisted in the separate MCP review queue. No image tags or filenames were changed.

## Larger focused-field challenge: 2026-10-28

The [three-arm challenge](local-ai-focused-fields.md#larger-development-challenge-and-cache-reuse) completed on all 32 labeled targets from the original baseline. Each arm reused eight earlier answers and computed the remaining 24: 72 new requests and 96 saved responses in total. All targets belong to the existing development partition; the 169 reserved-test images remain unscored. This is a deliberately difficult, already-exposed sample, not a whole-library accuracy estimate.

| Same 32 development images | Bounded full-tag control | Role/style only | Race only | Composed saved fields |
|---|---:|---:|---:|---:|
| Valid responses | 32/32 | 32/32 | 32/32 | Not a model run |
| Correct role/style pair | 16/32 | 14/32 | Not requested | 14/32 |
| Correct race | 20/32 | Not requested | 21/32 | 21/32 |
| All four tags correct | 12/32 | Not evaluated | Not evaluated | 8/32 |
| Median saved request time | 42.3 s | 41.2 s | 38.8 s | 121.6 s summed |

Composition uses control gender, focused role/style and focused race. It is an analysis of saved answers, not a new end-to-end run. The control matched 32 gender labels and 25 role labels; focused role/style matched only 21 roles. The original unconstrained baseline also had 12/32 full matches, with 26/32 valid labeled outputs. Better output validity did not improve complete tagging here.

The eight-image improvement did **not** extend to the additional 24 images: role/style fell from **15/24 to 9/24**, race from **17/24 to 16/24**, and composed full matches from **11/24 to 5/24**. Across all 32, focused role/style had five improvements and seven regressions; focused race had three improvements and two regressions. Six melee and three generic figures were incorrectly assigned caster by the role/style prompt. Do not adopt it as the default or use three inference passes based on the earlier narrow sample.

There were **zero in-scope abstentions** in every arm. Required nulls outside the question scope, and generic's null style, do not count as abstentions. These categorical answers still lack calibrated confidence. Automatic acceptance remains disabled.

### Content compatibility and validation

All 32 targets now have visually checked private content annotations. The additional 20 annotations came from checksum-verified MCP previews, not filename-based guesses.

| Content group | Targets | Full control matches | Focused role/style matches | Focused race matches |
|---|---:|---:|---:|---:|
| Ordinary clothed | 21 | 6/21 | 11/21 | 12/21 |
| Minimal outfits | 8 | 5/8 | 3/8 | 8/8 |
| Artistic nudity | 3 | 1/3 | 0/3 | 1/3 |
| Succubus-like, overlapping nudity | 1 | 1/1 | 0/1 | 1/1 |

Every arm returned valid outputs for these groups after validation correction, with no explicit refusal detected. This establishes processing on the sampled artwork, not accurate tagging or broad content compatibility. Class and difficulty differ across groups, so their scores do not isolate a clothing effect. Minimal tribal outfits specifically still need representative coverage.

One explanation used exactly 180 Unicode code points, including an emoji, but the old validator counted 181 UTF-16 units. The corrected validator matches JSON Schema length semantics. A completed-cache pass revalidated all **96** answers with **zero new inference**, changing one outcome from invalid to partial. Raw responses, target labels, hashes, settings fingerprints and saved timings were verified unchanged. Its incorrect tags remain incorrect.

### Recovery and memory

The shared-STOP test preserved 20 completed control records and discarded the interrupted response. A later cleanup error after role/style inference exposed an unavailable `Process.Path` property. The runtime now retains the verified launch path while still checking live process path and creation time before termination. Two sequential runtime lifecycle checks passed. Recovery verified absent owned processes and released locks; all final arms and cache revalidation report successful cleanup. Earlier session receipts, including the failure, are retained.

Across inference-bearing sessions, sampled peaks were **3.22 GiB incremental working set**, **3.43 GiB private memory**, and **4.05 GiB combined worker/launcher/app working set**. These are sampled values on the 32 GB development machine, not OS high-water marks or a 16 GB hardware certification. CPU-only execution and per-image unloading were verified. Admission/reserve limits are unchanged; cache-only resumes are excluded from memory estimates.

The private `.cache/local-ai-benchmark/challenge-fields-v1/` contains summaries, paired comparisons, content annotations, recovery receipts and a 32-card review. All PNG references and escaping were checked; no scripts or remote resources are used. Browser rendering was not verified. Reuse these checkpoints rather than repeating inference.

## Persistent agent review queue: 2026-10-28

`queue_ai_suggestions` and `list_ai_suggestions` now persist and inspect proposals separately from temporary tags. Imports check current revisions and full source hashes, reject conflicting batches, retain caller-reported model/run provenance, and preserve the original import snapshot on retries. The bounded, versioned store uses atomic replacement and writer locks. See the [queue contract](agent-integration.md#saved-ai-proposals).

The four existing untagged classifier suggestions were imported without inference or retraining. A live read confirmed four review-required proposals and unchanged tag snapshots/workspace revision. Pending tag changes remain zero. Desktop acceptance/rejection controls and inference scheduling remain future work; importing a suggestion never stages a tag.

Verification passed: **203 .NET tests**, including eight new store tests; **57 model-free local AI checks**, including Unicode bounds; 12 cache checks; synthetic focused-report checks; two sequential runtime lifecycle checks; and the expanded desktop/MCP integration suite. It covers the importer, idempotent retries, stale/hash/conflict rejection and preservation of original files. Release built without warnings. An active Debug bridge held its DLLs open, so the Release bridge and configurable CLI were used without interrupting it.

## Focused single-image questions: 2026-10-25

The [focused-field experiment](local-ai-focused-fields.md) reused the same eight exposed targets from the visual-reference studies: five casters and three ranged characters. A full-tag control and a role/style-only prompt both used one 768-pixel image, no visual references, the same role/style grammar, and bounded explanations (180 evidence characters, 96 uncertainty characters). The focused prompt forced gender and race to null. Both used the pinned Qwen model, CPU-only two-thread settings, 4,096 context and 256 output tokens.

| Measure on the same eight targets | Full-tag control | Focused role/style |
|---|---:|---:|
| Valid outputs | 8/8 | 8/8 |
| Correct role | 3/8 | 8/8 |
| Correct role/style pair | 1/8 | 5/8 |
| Median request latency | 41.9 s | 40.9 s |

Five role/style pairs improved and one regressed. The focused model still called the druid, cleric and one bard a wizard. It issued a role and style on every target rather than abstaining. The focused result is **not five fully tagged images**: gender and race were deliberately unrequested. These eight difficult, repeatedly exposed images are development evidence only. No threshold or automatic acceptance follows from 8/8 role correctness.

An initial one-image feasibility response used an unbounded explanation string, repeated itself and exhausted the output budget. It remains an invalid result under `focused-role-style-v1`; it was not reconstructed from its incomplete JSON. The paired runs above use version 2's bounded strings in both arms. Bounds can end a phrase awkwardly and are not a substitute for classification accuracy.

A separate race-only question then improved race matches from **3/8 to 5/8**, with two improvements and no regressions. All eight outputs were valid partial suggestions, median request time was 39.3 seconds, and all issued a race rather than abstaining. Kitsune was called beastfolk, halfling human, and construct other. This is useful development evidence for separating tasks, but the remaining category errors still prevent unattended use.

An exploratory composition of the saved outputs—gender from the full-tag control, role/style from the focused role question, and race from the focused anatomy question—gave **3/8 full matches**, versus 1/8 for the control. This is an analysis of existing responses, not a newly validated end-to-end tagger. The median sum of the three saved request times was 121.6 seconds per target, excluding orchestration beyond those requests. The role/style improvement alone must not be presented as complete-tag accuracy.

Across these three runs, peak sampled incremental working set was at most **3.11 GiB**, sampled private memory **3.40 GiB**, and combined worker/launcher/app working set **3.97 GiB**. These are sampled Qwen measurements, not OS high-water values. Zero reported VRAM, per-image unloading and final cleanup were verified. Completed-run resumes reused all results without further inference. No explicit refusals were detected in these eight targets; this was not a new representative content-compatibility sample. The larger challenge above supersedes this pilot's apparent improvement as guidance for selecting a default. Keep the 169 reserved-test images untouched until their variant audit is complete.

## New-image learned-classifier pilot: 2026-10-25

The [review-only suggestion pipeline](local-ai-suggestions.md) now applies saved numeric classifiers to compatible features from new images without retraining. Its first four targets are the untagged images already exposed in the original Qwen pilot. MCP source hashes and filename state were verified, previews remained label-blind, and all four suggestions were saved separately from app tags. They have no authoritative labels, so **no accuracy score is reported**.

The vision-only encoder's OS private-memory peak was **1.08 GiB**; combined worker/launcher/app working set peaked at **1.59 GiB**, with at least **10.02 GiB** available on the development machine. The encoder exited and cleanup passed. This remains a measurement on the 32 GB machine, not a 16 GB hardware certification. Conservative admission/reserve guards remain in force. No download or retraining was needed.

The new coefficient reader reproduced all **233** saved development/calibration tag predictions exactly; maximum score difference was below `1e-15`. It rejects exact reserved-test targets before generating suggestions. Feature-space checks allow new dataset identities and the verified lossless checkpoint packaging, but reject changes to model/preprocessing/numerical settings. The four-card static review verifies PNG hashes and contains no apply action. All scores are explicitly uncalibrated and every suggestion requires review.

## Frozen encoder and learned classifiers: 2026-10-25

All **1,469 tagged images** now have verified MCP previews and cached SigLIP 2 NaFlex features. The [encoder workflow](local-ai-encoder.md) learns three linear classifiers: gender, joint role/style, and race. The vision weights remain frozen. No additional download is needed to rerun the classifiers.

The current development study is `probe-v3`: **1,067 training, 185 calibration, 48 development, and 169 reserved-test images**. Prior Qwen targets/references and reviewed examples remain development data. The reserved test has never been scored and is a subset of the original reserved partition; regrouping cannot introduce previously exposed images into it.

| Calibration measure | Learned classifiers | Nearest training example | Majority labels |
|---|---:|---:|---:|
| All four tags correct | 114 / 185 (61.6%) | 82 / 185 (44.3%) | 54 / 185 (29.2%) |
| Gender correct | 182 / 185 (98.4%) | 174 / 185 | 152 / 185 |
| Role correct | 169 / 185 (91.4%) | 148 / 185 | 130 / 185 |
| Weapon/style correct | 136 / 185 (73.5%) | 108 / 185 | 88 / 185 |
| Race correct | 157 / 185 (84.9%) | 141 / 185 | 123 / 185 |

These are provisional development/calibration results, not final-test or whole-library accuracy. On the **same 32 rare-category targets** used in the earlier Qwen baseline, full matches fell from **12/32 to 1/32**: zero improvements and 11 regressions. The classifier improved gender and role counts (30 and 26 correct, against Qwen's 26 and 22), but weapon/style and race fell to 14 and 15, against 16 and 18. These counts include valid generic/null style matches; the earlier issued-field table excludes nulls and therefore uses different denominators.

Seven races present in the development set have no training examples: chimera, construct, elemental, grippli, monster, oread and vampire. A closed-set classifier cannot predict unseen classes. Small rare-category support and common-label bias make this classifier unsuitable as the general tagger. On all 48 development images it achieved only 3 complete matches.

Threshold exploration found no operating point whose group-level 95% Wilson lower bound reached either 99% or 99.5% precision. For example, 145/145 correct female predictions cover 143 heuristic groups, but the lower bound is only 97.4%; the blade subset's bound is 92.4%. No race passed even the observed-precision/support screen. Scores remain uncalibrated and **automatic acceptance stays disabled**.

An exploratory agreement rule also failed on the same 32 exposed Qwen targets. Keeping only matching fields from valid Qwen output and the classifier produced five complete agreed tag sets, of which only one was correct. Issued-field counts were gender 24/24 correct, role 20/23, weapon/style 10/14, and race 13/18. Role/style agreement was checked jointly before issuing a style. This small, selective diagnostic is not a calibration study, but it directly rules out treating model agreement as a reliable automatic acceptance signal here. Shared common-class mistakes remain possible.

### Grouping and scoring corrections

The initial loose grouping connected 526 images, including visually different characters with similar backgrounds. Stricter screening now uses cosine similarity at least .95, or .90 with perceptual-hash distance at most 6, or .94 with difference-hash distance at most 4. Confirmed visual links override these thresholds transitively. The current partition has 1,439 groups, with a largest group of six images. Ten images in six reviewed pairs have recorded decisions; two pairs remain uncertain. This is a partial audit, and related artwork may still cross partitions. The reserved test must remain unscored until its leakage audit is complete.

The first study also incorrectly compared the MCP's derived `isGeneric` metadata against four-field predictions, producing invalid zero full-match counts. Scoring now compares exactly the four tag fields, with regression tests. `probe-v1` is retained with an invalid-metric note; `probe-v2` precedes the confirmed visual links. Neither replaces the corrected `probe-v3` results above.

### Content processing and memory

All 1,469 previews were encoded without a content gate or generated-text refusal path. On the existing tiny annotated groups, this classifier had zero full matches in each group: ordinary clothed (7), minimal outfit (2), artistic nudity (3), and succubus-like design (1, overlapping nudity). Processing compatibility does not establish tagging accuracy. Minimal tribal outfits still lack representative evaluation.

The median feature-extraction time was **0.234 seconds per image**, with about **347 seconds total** inside the image-processing/inference loop. This excludes MCP preview export, hashing, startup and pauses. During the 1,461-image continuation, sampled worker working set peaked at **0.72 GiB**, sampled private memory at **1.81 GiB**, and sampled worker/launcher/app working set at **1.44 GiB**. A separate Windows high-water observation caught a **3.19 GiB private-memory peak** that the 200 ms sampler missed. Future workers record that OS peak directly. Do not use 1.81 GiB as the workload's maximum memory requirement.

The run completed on the 32 GB development machine with at least 14.23 GiB available RAM. Admission still requires 8 GiB available; the worker pauses under its 4 GiB workload/reserve guards. This is not validation on real 16 GB hardware. The model has unloaded and the worker has exited.

A subsequent lossless packaging change removed unused text tensors from a separate local checkpoint. All 208 retained vision tensors were verified exactly equal to the pinned original. On the same eight cached previews, fresh original and vision-only workers both reproduced the saved feature vectors exactly. Their OS private peaks were **3.19 and 1.08 GiB**, respectively; worker working set was about 0.71 GiB for both. The derived file is 371,746,008 bytes, was made offline, and leaves the original model/cache intact. One-time preparation itself reached 3.00 GiB private memory. This is a useful reduction in inference commit pressure, not an accuracy improvement or a 16 GB hardware certification; the conservative admission policy is retained. The paired receipt is under `.cache/encoder-vision-copy-tests/`.

The current review contains 233 development/calibration cards with target, nearest training example and tag disagreement. It has no final-test predictions or automatic apply action. Next: complete the variant audit and design abstaining suggestions that explicitly handle unsupported categories. The learned classifier should be treated as an assistive component, not promoted over Qwen on the basis of the easier calibration aggregate.

## Baseline: 32 tagged images and four untagged images

The candidate was Qwen3-VL 2B Instruct Q4_K_M, pinned manifest `afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e`, using Ollama 0.40.8, prompt `neutral-fantasy-v2`, 768-pixel maximum dimension, two CPU threads, 4,096 context tokens and a 256-token response bound. The runner verified zero reported VRAM and unloaded after each image.

This was a deliberately difficult, deterministic coverage sample: rare races first, then missing weapon/caster styles. It is a development set, **not an estimate of whole-library accuracy or a final held-out test**. The four untagged images have no accuracy score.

| Measure | Result |
|---|---:|
| Images attempted | 36 |
| Structurally valid complete predictions | 30 |
| Invalid role/style combinations | 6 |
| Detected explicit refusals | 0 |
| All four tags matching authoritative filenames | 12 / 32 (37.5%) |
| Median request latency | About 41 seconds |

Per-field precision below counts only valid outputs that issued that field. Coverage counts how often a non-null authoritative field received a valid non-null prediction. Invalid outputs remain in the full-image and coverage denominators.

| Field | Correct / issued | Precision | Coverage |
|---|---:|---:|---:|
| Gender presentation | 26 / 26 | 100% | 26 / 32 |
| Role | 22 / 26 | 84.6% | 26 / 32 |
| Weapon or caster style | 11 / 20 | 55.0% | 20 / 27 |
| Fantasy race | 18 / 26 | 69.2% | 26 / 32 |

Even the 26/26 field result is too small and selectively sampled to establish a high-precision automatic policy. No response used the partial/abstention outcome, despite several uncertainty sentences. A confident or complete answer is therefore not a useful acceptance signal by itself.

Observed errors included impossible role/style combinations, visually similar fantasy races, and weapon identification. On visually inspected examples, an elemental figure was called a mermaid, a kitsune was called beastfolk, and a sheathed blade was described as a polearm. Authoritative labels were preserved. These examples motivate collection-specific references, not relabeling the collection to match the model.

## Paired role/style constraint experiment

The same model and prompt were rerun on the first eight rare-category images with role/style dependencies encoded in the generation schema. The only intended inference change was that schema constraint. The paired comparison used matching full hashes and source labels:

| Measure on the same eight images | Baseline | Constrained schema |
|---|---:|---:|
| Valid outputs | 5 / 8 | 8 / 8 |
| Invalid outputs | 3 / 8 | 0 / 8 |
| Full tag matches | 1 / 8 | 1 / 8 |
| Median request latency | 41.0 s | 41.6 s |

Constraints eliminated incompatible combinations in this sample but did not improve complete-tag accuracy. Additional valid labels included incorrect ones. The candidate peaked at about 3.10 GiB incremental and 3.97 GiB combined working set, with verified cleanup. This is a development experiment, not evidence for promoting automatic acceptance or for changing the baseline's 12/32 result.

## Fixed visual-reference experiment

The runner now supports a private plan with one or two fixed, trusted visual examples. For this development study, the targets were the baseline's five caster images and first three ranged images. A wizard and cleric reference were selected deterministically from the remaining trusted collection before inference; all baseline targets were excluded. Source hashes differed and the selected artwork was inspected for obvious related variants. This is targeted development work, not a held-out evaluation or automatic retrieval.

The initial two-reference request used 3,846 prompt tokens in the 4,096-token context and took 112.2 seconds. It finished without runtime truncation, but left only 250 tokens against the intended 256-token output reservation. A stricter context-budget guard was added and the paired experiment used just the wizard reference. That first two-reference response remains a private feasibility artifact and is excluded from the comparison below.

One 384-pixel reference was supplied before each 768-pixel target; the same example and its labels were used for all eight targets. The baseline prompt/schema and inference options were retained, with an added instruction distinguishing reference and target. Both the reference messages and that instruction are experimental changes. No target answer or source filename was sent to the model.

| Measure on these same eight images | No reference | One fixed reference |
|---|---:|---:|
| Valid outputs | 3 / 8 | 8 / 8 |
| Full tag matches | 0 / 8 | 0 / 8 |
| Correct role labels among all attempts | 0 / 8 | 5 / 8 |
| Correct weapon/style labels among all attempts | 0 / 8 | 2 / 8 |
| Correct race labels among all attempts | 1 / 8 | 3 / 8 |
| Median request latency | 41.0 s | 78.2 s |

Invalid baseline outputs count as failures in these rows. The additional valid fields improved some individual counts, but complete tagging remained wrong on every target. The model repeatedly assigned the reference's wizard style to other caster types and a ranged construct. This particular fixed-reference approach is not suitable as a default. It does not prove that well-selected contrasting references or supervised classifiers cannot help.

The single-reference run peaked at about **3.07 GiB incremental working set**, **3.34 GiB incremental private bytes** and **3.55 GiB combined working set**. Its smaller already-resident app baseline explains why combined memory differs from the earlier baseline; this is not evidence that references reduce memory. Prompt counts remained comfortably below the reservation limit, cleanup passed, and a completed-run resume reused all eight results without further inference. No model weights were trained.

## Per-target retrieved references: 2026-10-25

The [retrieval experiment](local-ai-retrieved-references.md) selected one reference per target from `probe-v3`'s training partition, using image-feature similarity without target labels. It excluded target groups, the full target pool and similarities at or above .90, then visually checked all eight pairs for related artwork. The targets were the same eight images as the earlier fixed-wizard study. Their correct labels were used only for evaluation. The model, prompt, schema, image sizes, CPU options and context limits stayed the same as that fixed-reference experiment.

| Same eight development images | No reference | Fixed wizard | Retrieved reference |
|---|---:|---:|---:|
| Valid outputs | 3 / 8 | 8 / 8 | 7 / 8 |
| All four tags correct | 0 / 8 | 0 / 8 | 0 / 8 |
| Gender correct | 3 / 8 | 8 / 8 | 7 / 8 |
| Role correct | 0 / 8 | 5 / 8 | 3 / 8 |
| Weapon/style correct | 0 / 8 | 2 / 8 | 3 / 8 |
| Race correct | 1 / 8 | 3 / 8 | 2 / 8 |
| Median inference time | About 41 s | 78.2 s | 75.1 s |

Invalid outputs remain failures in every row. The retrieved run's largest reported prompt was 2,836 tokens, comfortably within the 4,096-token context and 256-token response reservation. Sampled peaks were 3.13 GiB incremental working set, 3.38 GiB private memory and 3.96 GiB combined working set. Cleanup and workspace checks passed, and all eight results are checkpointed privately in `.cache/local-ai-benchmark/retrieved-one-v1/`.

Several explanations included reference-only details. The pale character without a hat was described as wearing the reference's witch hat; the dark-bearded gun user was described with the reference's red beard and mace; the crossbow user was described as holding a sword like the reference. The target/reference pairs were actually viewed when checking independence. These observations support reference-detail contamination as a failure mode. They do not by themselves isolate a runtime bug from the small model's attention or reasoning limitations.

Do not promote this retrieval method into the tagging workflow. A subsequent diagnostic used the same reference/assistant-label/target message arrangement with a red square and blue circle. It correctly classified the final image in both orders (2/2), taking 61.7 and 62.6 seconds, with zero reported VRAM and verified unloading/cleanup. Completed-run resume reused both results without inference. Evidence is retained in `.cache/grounding-checks/final-target-v1/`.

This rules out a universal failure to process the last image; it does not establish the cause of reference-detail contamination on complex fantasy artwork. The next bounded experiment asks focused single-image questions about equipment and caster clothing, followed by anatomy if justified. Reuse existing development targets, protect the final-test partition, and do not launch a full-backlog run or download another comparison model automatically.

## Artistic nudity and minimal outfits

The first baseline audit visually inspected twelve source images and annotated them separately from filename labels. These content groups overlap; the succubus-like image is also in the artistic-nudity group. The expanded 32-image audit is reported above.

| Visually verified group | Images | Valid predictions | Full tag matches | Detected refusals |
|---|---:|---:|---:|---:|
| Ordinary clothed controls | 7 | 3 | 0 | 0 |
| Minimal outfits | 2 | 2 | 2 | 0 |
| Artistic nudity | 3 | 2 | 1 | 0 |
| Succubus-like fantasy design | 1 | 1 | 1 | 0 |

There was no observed content-only refusal in these cases. Errors occurred in both clothed and revealing art. This does not establish an effect of clothing coverage: class, rarity, style and difficulty differ across the tiny groups. At this checkpoint the other 24 images lacked content annotations; the later audit covered 20 more labeled targets, leaving the four untagged baseline targets outside that audit. Minimal tribal outfits specifically still need representative examples; a fully clothed druid is not evidence for that case. Broad content compatibility remains unproven.

## Resources and recovery

The final 29-image baseline continuation sampled about **3.15 GiB incremental working set**, **3.48 GiB incremental private bytes**, and **3.98 GiB combined working set including the existing WPF app** at their respective peaks. It had at least 17.09 GiB of system available RAM. These measurements are on the 32 GB development machine, not a 16 GB hardware validation. Sampling can miss short peaks; a working-set guard is not a hard OS allocation limit.

The runtime admits work only with 8 GiB available physical memory, and pauses under the documented workload/reserve/commit thresholds. One image runs at a time; model unloading and owned-process cleanup are verified. A controlled STOP test retained seven completed results, discarded the interrupted response and verified cleanup. An earlier cleanup race with an already-exited worker was fixed and the stop test repeated successfully.

At the observed request speed, 660 images alone would require roughly 7.5 hours of inference, plus decoding, hashing, loading and pauses. This is only a development-machine estimate. Running that backlog now would generate many unreliable suggestions, so no full backlog tagging run has been started.

## Trusted data and next experiment

The [filename dataset export](local-ai-dataset.md) completed for all **1,469 tagged images**, including three outside the tagged subfolder. All byte hashes were distinct. A separate resumable audit hashed all **660 untagged images** and found **zero exact-byte matches** against that trusted collection. No tags were copied. The new encoder study above provides provisional grouping and trained classifiers; its incomplete visual audit still prevents a final accuracy claim.

Retain the feasibility samples as development data. Any future visual-reference study must exclude the target and related variants and select examples without using the target's authoritative answer. Rare classes with one or two examples stay review-only. The [personalization plan](local-ai-personalization-next.md) records the rationale for the frozen-feature experiment.

The frozen embedding index and learned classifiers now exist as research artifacts. Their measured results above do not support automatic acceptance. Do not infer that the Qwen language model has learned the collection: its weights were never trained.

The remaining product work is desktop review controls and inference scheduling over the persistent proposal store, duplicate-safe evaluation, reliable abstention/calibration, and validation on real 16 GB hardware with foreground applications. Keep automatic acceptance disabled until those checks support it.

## Reproducibility and private artifacts

Use the [benchmark commands](local-ai-benchmark.md), [dataset exporter](local-ai-dataset.md) and paired comparison script. Machine-specific paths, images, annotations and raw model outputs remain ignored under `.cache`; this report contains aggregates only.

- Baseline: `.cache/local-ai-benchmark/pilot-v2/` (manifest, 36 per-image results, session reports and `review.html`).
- Schema comparison: `.cache/local-ai-benchmark/coupled-v1/`.
- Fixed-reference comparison: `.cache/local-ai-benchmark/reference-one-v1/` (eight targets and one reference in `review.html`).
- Target-only bounded full-tag control: `.cache/local-ai-benchmark/target-only-control-v1/`.
- Larger three-arm challenge: `.cache/local-ai-benchmark/challenge-fields-v1/`, including 32 content annotations, paired summaries and the 96-response revalidation.
- Focused role/style and race: `.cache/local-ai-benchmark/focused-role-style-v2/` and `focused-race-v2/`, with paired receipts and private reviews. The aggregate and exploratory composition are in `focused-study-v2-summary.json` beside those run directories.
- Untagged classifier pilot: `.cache/encoder-inputs/backlog-pilot-v1/`, `.cache/encoder-features/backlog-pilot-v1/` and `.cache/encoder-suggestions/backlog-pilot-v1/`.
- Two-reference context feasibility checkpoint: `.cache/local-ai-benchmark/references-v1/` (not part of the paired score).
- Dataset: `.cache/local-ai-dataset/filename-v1/`.
- Exact-content backlog audit: `.cache/local-ai-dataset/backlog-matches-v1/`.
- Frozen features: `.cache/encoder-features/filename-v1/`; full-run session and `os-memory-observation.json` retain memory evidence.
- Current learned-classifier study: `.cache/encoder-study/probe-v3/`, with predictions, group decisions, numeric model parameters, threshold exploration, paired comparison and private `review.html`.
- Visual decisions: `.cache/encoder-study/group-audit-manifest.json`; these remain partial, and uncertain relations are recorded rather than silently treated as confirmed.
- Stop/resume evidence: baseline `stop-test-result.json`.

The private HTML report contains all 36 local PNG previews and escaped text, with no scripts or remote resources. File references and PNG signatures were checked. Browser rendering was not verified because the in-app browser rejects local file URLs.

Current verification counts are recorded in the 2026-10-28 section above. Earlier evidence includes dataset checkpoint/resume, conflicting exact-match handling, reference leakage rejection before inference, and completed focused/control/reference cache reuse. Numeric classifier tests reproduced 233 existing predictions, rejected the reserved test, and checked the four-image review pipeline and invalid records. The image library's tags and filenames remained unchanged.
