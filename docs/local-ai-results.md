# Local AI development results

Measured 2026-10-23 on `codex/local-ai-tagging`. **The cached model runs locally on CPU, but baseline tagging accuracy is too low for unattended acceptance.** Filename tags were used only for scoring, never supplied as target answers to the model. No image tags or filenames were changed.

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

## Artistic nudity and minimal outfits

Twelve source images were visually inspected and annotated separately from filename labels. These content groups overlap; the succubus-like image is also in the artistic-nudity group.

| Visually verified group | Images | Valid predictions | Full tag matches | Detected refusals |
|---|---:|---:|---:|---:|
| Ordinary clothed controls | 7 | 3 | 0 | 0 |
| Minimal outfits | 2 | 2 | 2 | 0 |
| Artistic nudity | 3 | 2 | 1 | 0 |
| Succubus-like fantasy design | 1 | 1 | 1 | 0 |

There was no observed content-only refusal in these cases. Errors occurred in both clothed and revealing art. This does not establish an effect of clothing coverage: class, rarity, style and difficulty differ across the tiny groups. The remaining 24 benchmark images lack manual content-group annotations. Minimal tribal outfits specifically still need representative examples; a fully clothed druid is not evidence for that case. Broad content compatibility remains unproven.

## Resources and recovery

The final 29-image baseline continuation sampled about **3.15 GiB incremental working set**, **3.48 GiB incremental private bytes**, and **3.98 GiB combined working set including the existing WPF app** at their respective peaks. It had at least 17.09 GiB of system available RAM. These measurements are on the 32 GB development machine, not a 16 GB hardware validation. Sampling can miss short peaks; a working-set guard is not a hard OS allocation limit.

The runtime admits work only with 8 GiB available physical memory, and pauses under the documented workload/reserve/commit thresholds. One image runs at a time; model unloading and owned-process cleanup are verified. A controlled STOP test retained seven completed results, discarded the interrupted response and verified cleanup. An earlier cleanup race with an already-exited worker was fixed and the stop test repeated successfully.

At the observed request speed, 660 images alone would require roughly 7.5 hours of inference, plus decoding, hashing, loading and pauses. This is only a development-machine estimate. Running that backlog now would generate many unreliable suggestions, so no full backlog tagging run has been started.

## Trusted data and next experiment

The [filename dataset export](local-ai-dataset.md) completed for all **1,469 tagged images**, including three outside the tagged subfolder. All byte hashes were distinct. A separate resumable audit hashed all **660 untagged images** and found **zero exact-byte matches** against that trusted collection. No tags were copied. Related crops, alternate encodings and variants have not been grouped, so the collection is not yet ready for a leakage-resistant final split. Only the fixed-reference experiment above has been attempted; no weights or classifiers have been trained.

Next, retain these feasibility samples as development data and prepare reference, calibration and final-test partitions after variant review. Evaluate frozen image features and small supervised classifiers on common classes where there are enough independent examples; see the [next experiment plan](local-ai-personalization-next.md). Any future visual-reference study must exclude the target and related variants and select examples without using the target's authoritative answer. Rare classes with one or two examples stay review-only.

A frozen visual embedding index and small per-field classifiers remain candidates for later personalization. Their runtime, pinned files and actual CPU memory must be evaluated before adoption; no additional comparison model was downloaded for this milestone. Do not infer that a language model has learned the collection merely because its filenames were exported.

The remaining product work is a persistent suggestion/review workflow, duplicate-safe evaluation, reliable abstention/calibration, and validation on real 16 GB hardware with foreground applications. Keep automatic acceptance disabled until those checks support it.

## Reproducibility and private artifacts

Use the [benchmark commands](local-ai-benchmark.md), [dataset exporter](local-ai-dataset.md) and paired comparison script. Machine-specific paths, images, annotations and raw model outputs remain ignored under `.cache`; this report contains aggregates only.

- Baseline: `.cache/local-ai-benchmark/pilot-v2/` (manifest, 36 per-image results, session reports and `review.html`).
- Schema comparison: `.cache/local-ai-benchmark/coupled-v1/`.
- Fixed-reference comparison: `.cache/local-ai-benchmark/reference-one-v1/` (eight targets and one reference in `review.html`).
- Two-reference context feasibility checkpoint: `.cache/local-ai-benchmark/references-v1/` (not part of the paired score).
- Dataset: `.cache/local-ai-dataset/filename-v1/`.
- Exact-content backlog audit: `.cache/local-ai-dataset/backlog-matches-v1/`.
- Stop/resume evidence: baseline `stop-test-result.json`.

The private HTML report contains all 36 local PNG previews and escaped text, with no scripts or remote resources. File references and PNG signatures were checked. Browser rendering was not verified because the in-app browser rejects local file URLs.

Verification passed: 36 model-free local AI checks, the real desktop/MCP integration suite using disposable fixtures, dataset checkpoint/resume, conflicting exact-match handling, reference leakage rejection before inference, and completed reference-run cache reuse. The earlier .NET suite passed 195 tests; subsequent changes here are PowerShell research tooling and documentation. The original library's workspace revision and pending-change count remained unchanged.
