# Model download integrity and routing

The local benchmark uses `qwen3-vl:2b-instruct-q4_K_M`, pinned to manifest SHA-256 `afc393e3572c07db0bc84aa460d928a16c75f3fbb9ca9e0dccc4fbd2dafb573e`. Its five blobs total 1,930,568,531 bytes. A mutable registry tag is not a substitute for this manifest identity. [Official model listing](https://ollama.com/library/qwen3-vl:2b-instruct-q4_K_M).

## Provisioning requirements

1. Use the current operator's authorized download route. Do not copy another machine's network configuration or assume an earlier route is still available.
2. Fetch only the selected runtime/model and reuse verified cached files. Resume partial transfers when the source supports ranges.
3. Preserve TLS verification, follow only HTTPS redirects, and validate both final byte counts and the pinned SHA-256 digests before publishing completed files into the cache.
4. Keep credentials, signed redirect URLs, controller responses, interface addresses and transfer logs in private ignored storage. Public documentation should describe the method, not record an operator's network setup.
5. Distinguish model discovery, inference, accuracy and memory validation. Successful downloads establish none of the latter three.

If a transfer must use a direct route, inspect live routing and tunnel state and verify the redirected file endpoints as well as the initial registry host. `curl --noproxy '*'` disables application-level proxies but does not establish the route taken by a system tunnel. Stop if the permitted route is unavailable; do not silently change it. Report the actual route used. [curl options](https://curl.se/docs/manpage.html).

## Private cache

Verified model blobs and manifests belong under ignored `.cache/models/ollama/`. Configure the runtime's `OLLAMA_MODELS` to reuse that store. [Ollama model storage](https://docs.ollama.com/faq#how-do-i-set-them-to-a-different-location).

Machine-specific setup helpers, transfer receipts and network evidence belong under ignored `.cache/model-network/` or `.cache/runtime-setup/`. They are not part of a fresh clone. Check local setup status before provisioning or resuming. The [runtime setup guide](local-ai-runtime-setup.md) describes the pinned runtime and reusable discovery check.
