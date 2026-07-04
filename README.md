# TokenArtTagger v0.2.4

TokenArtTagger is a small local Windows WPF app for quickly tagging tabletop RPG character art, building partial tags with virtual buckets, and previewing safe in-place renames.

## Build

This workspace uses the project-local .NET SDK at `.tools\dotnet`.

```powershell
.\.tools\dotnet\dotnet.exe restore TokenArtTagger.slnx --configfile NuGet.Config
.\.tools\dotnet\dotnet.exe build TokenArtTagger.slnx
```

## Run

```powershell
.\.tools\dotnet\dotnet.exe run --project src\TokenArtTagger.App\TokenArtTagger.App.csproj
```

In the app, choose a root image folder and scan it. Use the `Library` tab for image-first review and the `Bucket Tagging` tab for category-first bulk tagging.

Use a copied test folder first, for example `C:\Path\To\Your\ImageLibrary`.

## Workflows

- `Library`: select individual images, apply tags, preview selected or changed items, then confirm `Rename Selected`.
- `Bucket Tagging`: choose a pass such as Gender, Role, Caster Style, or Race; assign selected thumbnails to virtual buckets; then apply a default bucket to the remaining images on the current page.

Partial bucket tags are saved as work-in-progress app data outside the scanned image folder and outside the repository by default.

Use the clear temporary tag buttons to reset selected images, the current bucket pass, or all changed images back to tags parsed from their current filenames. These reset actions do not rename, move, delete, or edit image files.

Right-click a thumbnail to inspect a larger preview from the original file path. In the preview window, mouse wheel zooms, left-drag pans, right-click closes, and Escape closes. Animated GIFs play in the inspection window.

The selection panel uses selectable text for important details and includes `Show in Explorer` for the selected image.

## Supported Formats

- `.jpg`
- `.jpeg`
- `.jfif`
- `.png`
- `.webp`
- `.gif`

GIF thumbnails show as a still frame. Animated GIFs play in the enlarged inspection window. WebP thumbnail and preview support depends on the Windows imaging codecs available on the machine; animated WebP may display as a still image if the local codec exposes only one frame.

## Controlled Tags

Filename tags use a controlled vocabulary rather than adding a new filename tag for every one-off weapon or creature detail. Rare outlier weapon names such as `drill` or `chainsaw` normalize to `exotic`; older `rare` filenames normalize to `exotic` for backward compatibility. The `monster` race is available for monstrous humanoid or unclear enemy-creature art that is not cleanly covered by a more specific race tag. Exact one-off details should eventually live in freeform tags when the future database/freeform-tag work lands.

## Rename Safety

Test on a copied folder before using the real image library.

The app only renames selected files in place after preview and confirmation. It does not move files, delete files, or rename automatically after tagging. Each rename batch writes a JSON undo log under `%LOCALAPPDATA%\TokenArtTagger\UndoLogs`.

Older `.tokenarttagger-undo` folders in scanned image libraries are left alone. If one is found, the app warns so you can manually archive or remove it later.

Sanitized crash diagnostics, when needed, are written under `%LOCALAPPDATA%\TokenArtTagger\Logs`. These logs are outside the repository and outside scanned image folders; they are not intended to include full image-library paths.

Output filenames use stable content hashes:

- Non-generic: `gender-role-weaponOrStyle-race__shortHash.ext`
- Generic: `gender-generic-race__shortHash.ext`

The original extension and extension casing are preserved.

## Current Limitations

- Undo is not automated yet, though JSON undo logs are written.
- No SQLite database yet.
- No freeform tags yet.
- Thumbnail cache files are runtime artifacts stored outside the scanned image folder by default.
- No AI tagging or automatic tag suggestions.
- Thumbnail decode failures are shown as blank thumbnails rather than blocking scan/rename.
- Middle-click autoscroll is not implemented; normal scrollbar dragging and mouse wheel scrolling are the supported fast-scroll methods.
- Rectangle selection is basic: drag from empty grid space to select intersecting visible tiles. Ctrl-drag toggles intersecting tiles.
- Animated WebP playback depends on installed Windows imaging codec behavior and may fall back to a static frame.

## Tests

```powershell
.\.tools\dotnet\dotnet.exe test TokenArtTagger.slnx
```
