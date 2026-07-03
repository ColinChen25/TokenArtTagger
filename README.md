# TokenArtTagger v0.2.0

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

## Supported Formats

- `.jpg`
- `.jpeg`
- `.jfif`
- `.png`
- `.webp`
- `.gif`

GIF thumbnails show as a still frame. WebP thumbnail support depends on the Windows imaging codecs available on the machine; files still scan and rename even if a thumbnail cannot be decoded.

## Rename Safety

Test on a copied folder before using the real image library.

The app only renames selected files in place after preview and confirmation. It does not move files, delete files, or rename automatically after tagging. Each rename batch writes a JSON undo log under `.tokenarttagger-undo` in the selected root folder; that folder is ignored by Git.

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
- Rubber-band rectangle selection is not implemented.

## Tests

```powershell
.\.tools\dotnet\dotnet.exe test TokenArtTagger.slnx
```
