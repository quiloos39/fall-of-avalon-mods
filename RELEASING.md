# Releasing a mod to Nexus

## One-time setup

1. Add a `NEXUSMODS_API_KEY` repo secret:
   - Generate the key at https://www.nexusmods.com/users/myaccount?tab=api → "Personal API Key".
   - Add it under https://github.com/quiloos39/fall-of-avalon-mods/settings/secrets/actions
2. Make sure `gh` (GitHub CLI) is installed and authenticated locally (`gh auth status`).
3. Make sure each mod has an entry in `nexus.json` with its `file_group_id`. To find it on Nexus:
   - Files tab of the mod page → "API Info" button → copy `Group ID`.

## Cutting a release

Run from the repo root:

```powershell
./release.ps1 -Mod AutoLoot -GameVersion 0.5.2
```

The script:
1. Writes `0.5.2` into `PluginVersion` in `<Mod>/Plugin.cs` and `<Version>` in `<Mod>/<Mod>.csproj`.
2. Builds Release (`dotnet build -c Release`).
3. Stages `BepInEx/plugins/<Mod>.dll` and zips it to `dist/<Mod>-v<gameversion>.zip`.
4. Commits the version bump, tags `<Mod>-v<gameversion>`, pushes, and publishes a GitHub Release with the zip attached.

The `Upload to Nexus` GitHub Action then fires on the release-publish event, looks up the
mod's `file_group_id` in `nexus.json`, and uploads the zip to that mod's Nexus page —
archiving the previous file.

### Flags

| Flag          | Effect                                                         |
|---------------|----------------------------------------------------------------|
| `-NoCommit`   | Skip the version-bump commit (changes stay unstaged).          |
| `-NoTag`      | Skip creating the git tag.                                     |
| `-NoPublish`  | Skip `git push` and `gh release create` — fully local dry run. |

`-NoPublish` is the right flag for testing the build/zip locally without triggering a Nexus upload.

## Versioning

Mod version = current Avalon game version (e.g. `0.5.2`). The same string is written into:
- `PluginVersion` in `Plugin.cs` (visible to BepInEx)
- `<Version>` in the `.csproj`
- The git tag (`<Mod>-v0.5.2`)
- The GitHub Release title and the Nexus file version

If you ship a second build against the same game version, append a fourth part:
`./release.ps1 -Mod AutoLoot -GameVersion 0.5.2.1` — `System.Version` (BepInEx) accepts it
and Nexus version strings are arbitrary.

## Adding a new mod to the workflow

1. Create the Nexus mod page manually and upload one initial file (any zip).
2. Files tab → "API Info" → copy the `Group ID`.
3. Add an entry to `nexus.json`:
   ```json
   "MyNewMod": { "mod_id": 999, "file_group_id": "1234567" }
   ```
4. Done — `./release.ps1 -Mod MyNewMod -GameVersion ...` works.
