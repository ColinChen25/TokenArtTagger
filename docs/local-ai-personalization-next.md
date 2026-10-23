# Next personalization experiment

The Qwen CPU baseline can emit plausible but incorrect tags, and the schema constraint experiment only improved output validity. Fixed visual examples are now supported by the benchmark, but that mechanism is an experiment rather than an automatic retrieval or learning system. Read the latest [measured results](local-ai-results.md) before repeating a prompt experiment.

The next approach to evaluate is **frozen image features plus small supervised classifiers**, using the existing filename labels. This separates recognizing the collection's categories from generating prose. It is a proposed experiment, not an installed or validated component. No additional model was downloaded for this milestone.

## Candidate and boundaries

Google's SigLIP 2 model card explicitly supports obtaining image feature vectors, and Transformers documents fixed-resolution and aspect-preserving NaFlex variants. This makes the family a plausible feature-extraction candidate for the mixed-style collection; it does not establish fantasy-tag accuracy, NSFW compatibility or CPU memory use here. [Official model card](https://huggingface.co/google/siglip2-base-patch16-224), [official implementation documentation](https://huggingface.co/docs/transformers/en/model_doc/siglip2).

Before provisioning a candidate, select one exact checkpoint and CPU runtime, inspect its files and license, record expected download sizes and hashes, and check the user's current download scope. Do not download several variants as an automatic comparison set. Weight size and an embedding matrix estimate are not runtime measurements. Prefer loading only the required vision encoder if the selected runtime supports it without an unverified conversion.

Keep original image decoding in the app/MCP layer. A model adapter consumes the bounded PNG response and performs the selected model's documented tensor preprocessing. Preserve the complete character, aspect ratio and transparency handling; explicitly evaluate padding or an aspect-preserving variant rather than silently cropping weapons or stretching anatomy. Pin preprocessing alongside model identity.

## Small implementation sequence

1. **Partition before training.** Use the completed private filename/hash index; keep the current feasibility samples in development. Find related crops and variants and group them across all partitions. Give common labels separate reference/training, calibration and final-test examples. Flag classes too rare for independent evaluation instead of manufacturing a precision estimate.
2. **Run an encoder smoke test.** Process one image at a time on CPU with the existing admission/pause policy. Record complete process-tree memory and latency, then unload. Start with a small, diverse set including ordinary clothed, minimal-outfit and artistic-nudity images. A feature vector is not proof of useful tagging or content compatibility.
3. **Cache features by identity.** Key each vector by full source hash, model digest and preprocessing fingerprint. Check finite values and expected dimensions. Store vectors separately from current paths and labels; invalidate on changed inputs. Keep labels out of feature extraction so it cannot read its own answer.
4. **Train simple per-field models.** Start with regularized linear models and a nearest-neighbor baseline. Train weapon/caster style conditionally on role, or reject incompatible pairs after prediction. Preserve class imbalance in reporting and compare against simple majority-label baselines. Wrong role decisions must not be hidden by scoring style only on correct roles.
5. **Measure selective acceptance.** Fit calibration and thresholds on data separate from the final test. Report correct/issued counts, coverage, full-tag matches and rare-class failures, including uncertainty. A model score or similarity is not a calibrated probability. [Probability calibration documentation](https://scikit-learn.org/stable/modules/calibration.html).
6. **Integrate only useful output.** If the encoder/classifiers improve the measured precision/coverage tradeoff, expose suggestions through the desktop review workflow. Keep uncertain fields null and preserve filename labels. Benchmark the entire workload on a real 16 GB machine before declaring the deployment target met.

Visual nearest-neighbor retrieval can also provide relevant references to Qwen, but exclude the target and its variants and select neighbors without consulting its hidden answer. Test whether that helps before adding it to every request. Keep the encoder and Qwen sequential rather than resident together. Neither a successful prototype nor a smaller model changes the current review-only acceptance policy.

If useful accuracy still cannot be reached, keep the tool as a review aid and report the unsupported categories. Do not replace authoritative labels, relax the accuracy requirement, or introduce an NSFW skip gate to make the evaluation look better.
