# Desktop review of AI suggestions

Implemented 2026-10-29 on `codex/local-ai-tagging`. This window reviews previously imported proposals; it does not run a model or automatically accept predictions.

## Use the window

1. Launch the newly built app and scan the folder containing the proposal images. Import proposals with the [existing agent workflow](local-ai-suggestions.md#save-the-pilot-to-the-agent-review-queue) if needed.
2. In **Library**, click **Review AI suggestions** above the tag buttons. The queue includes proposals whose original paths are in the current scan. **Refresh queue** reloads persisted state and discards unsaved field choices.
3. Select a proposal. The window shows a bounded image preview, current tags, proposed tags, model/run provenance and evidence. Previewing verifies the full source hash. Model evidence is plain text; no confidence threshold is implied.
4. Choose each field independently: **Keep current**, **Use AI**, **Set** a correction, or **Leave unresolved**. Every field starts with Keep current. Changing role never silently clears style; choose a compatible style or explicitly leave it unresolved. Partial results are allowed.
5. Click **Save reviewed tags** to persist those choices as temporary tags. Replacing a non-null current or filename-derived tag requires the visible replacement checkbox. Any subsequent field edit clears that confirmation. At least one explicit field decision is required.
6. Alternatively, **Dismiss proposal** records dismissal without changing tags. **Show reviewed proposals** displays saved and dismissed decisions as read-only history, including the result and field actions. Further tag edits use the Library's normal controls.
7. Close the review window, use **Preview Changed**, then select the intended images for the existing **Rename Selected** preview/confirmation workflow. Saving a review never renames an image.

The window is modal: normal workspace operations and MCP requests report busy while it is open. Close it to resume agent access. Use one editing app per data profile, as with existing temporary-tag edits.

## Identity, persistence and recovery

- A review binds to the stored proposal and the image/tag revision captured when the queue was loaded. Saving checks that proposal again and rehashes the source. Source reads are held open with write/delete sharing denied during verification and tag persistence. Changed tags, changed bytes (even with preserved size/time), missing sources and stale revisions block staging. Refresh to review current tags; changed image bytes require a new proposal. A stale or missing-source proposal can still be dismissed.
- Preview decoding reuses the existing bounded image service: 1,024-pixel longest edge, first frame, 64 MB file and 80 megapixel decode limits. Only one preview operation runs at a time, and obsolete selection results are discarded. No inference engine is loaded.
- Original proposals remain in `AiSuggestions/queue-v1.json`. Decisions are immutable private receipts in `AiSuggestions/Reviews/<suggestion-id>.json` under the normal app-data root, or `TOKENARTTAGGER_DATA_DIR`. A receipt includes the original proposal/provenance, before/after tags, filename-tag snapshot, field actions, explicit replacement acknowledgement and review time. It is an audit record, not an authoritative training label.
- Review writes use a process/OS lock and atomic publication. Duplicate decisions cannot apply tags twice. The receipt is prepared first, temporary tags are persisted next, and only then is the receipt published. A failed tag save rolls the view model back and leaves the proposal pending. The tag file and receipt cannot commit atomically: a crash or receipt-publication failure after tag persistence leaves saved tags with a pending proposal. The app reports that case explicitly; reload and inspect current tags before retrying. It never records a successful stage before tag persistence completes.
- Dismissed/reviewed proposals are hidden from the pending desktop queue. The MCP `list_ai_suggestions` contract still lists **all original proposals**, including reviewed ones; it does not expose receipt status. Its queue revision tracks imports, not desktop review history. No new agent apply/review tool was added.
- Records for renamed or no-longer-scanned paths remain on disk but are omitted from the window. There is no automatic rebinding, archive/prune control, undo of decisions, automatic training, or background inference scheduler yet. Review does not free the proposal store's 5,000-record capacity.

## Build and validation

Normal builds use `scripts/Build-Agent.ps1 -Offline -Configuration Release`. If the running app holds its output DLLs open, a separate project configuration avoids interrupting it:

```powershell
.\.tools\dotnet\dotnet.exe test tests/TokenArtTagger.App.Tests/TokenArtTagger.App.Tests.csproj --no-restore -c Review
.\.tools\dotnet\dotnet.exe test tests/TokenArtTagger.Tests/TokenArtTagger.Tests.csproj --no-restore -c Review
```

`Review` is a project configuration, not a solution configuration. Restore the new test project once from cached packages using the offline build script; restore succeeds even if its subsequent Release build encounters locked DLLs. The review app is then at `src/TokenArtTagger.App/bin/Review/net10.0-windows/TokenArtTagger.App.dll`. Close the old app before opening a new one with the same data profile; the old process will not acquire new controls automatically.

After closing the old app, launch the verified build from the repository root:

```powershell
.\.tools\dotnet\dotnet.exe .\src\TokenArtTagger.App\bin\Review\net10.0-windows\TokenArtTagger.App.dll
```

Validation passed: 211 core/protocol tests and seven WPF dispatcher tests. Coverage includes actual field templates/bindings at minimum window size, field corrections, explicit filename replacement, same-metadata content changes, stale edits, failed tag persistence, immutable history, missing-source dismissal and modal agent exclusion. The expanded 13-tool MCP integration suite also passed against the review build with disposable images and isolated state. No model inference or downloads were needed.

Interactive screenshot/click verification remains outstanding: the computer-use helper reported app-approval/capture timeouts, ending with `window capture timed out: timed out waiting on channel`. The isolated fixture process was stopped; no review decisions were made on the real collection. Programmatic WPF layout/binding checks are not a substitute for a visual inspection on the target desktop.
