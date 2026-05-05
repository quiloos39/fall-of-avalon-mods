# Releasing a mod to Nexus

Releases run **fully locally** — `release.ps1` builds, packages, pushes a GitHub
release for archival, then calls the Nexus Upload API directly. There's no
GitHub Actions workflow involved (the repo is public and the build references
proprietary game DLLs that can't be vendored or used in cloud runners).

## One-time setup

1. Copy `.env.example` to `.env` and paste your Nexus personal API key:
   - Generate at https://www.nexusmods.com/users/myaccount?tab=api → "Personal API Key".
   - `.env` is gitignored.
2. Make sure `gh` (GitHub CLI) is installed and authenticated (`gh auth status`).
3. For each mod you'll release, ensure `nexus.json` has its `file_group_id`:
   - On the Nexus mod page → Files tab → "API Info" button → copy `Group ID`.

## Cutting a release

From the repo root:

```powershell
./release.ps1 -Mod AutoLoot -GameVersion 0.5.2
```

The script:
1. Writes `0.5.2` into `PluginVersion` in `<Mod>/Plugin.cs` and `<Version>` in `<Mod>/<Mod>.csproj`.
2. `dotnet build -c Release` for that one mod.
3. Stages `BepInEx/plugins/<Mod>.dll` and zips to `dist/<Mod>-v<gameversion>.zip`.
4. Commits the version bump, tags `<Mod>-v<gameversion>`, pushes branch + tag.
5. `gh release create` — publishes a GitHub Release with the zip attached (archival).
6. **Calls Nexus directly** via the v3 Upload API:
   - Initialises a multipart upload, PUTs each part to its presigned URL,
     completes, finalises, polls until `available`, associates with `file_group_id`.
   - Archives the previous file on that mod page.

### Flags

| Flag         | Effect                                                                  |
|--------------|-------------------------------------------------------------------------|
| `-NoCommit`  | Skip the version-bump commit (changes stay unstaged).                   |
| `-NoTag`     | Skip creating the git tag.                                              |
| `-NoPublish` | Skip git push, GH release, AND Nexus upload — fully local dry run.      |
| `-NoNexus`   | Skip just the Nexus upload (still pushes + creates the GitHub release). |

`-NoPublish` is the right flag for testing the build/zip locally without touching anything remote.

## Versioning

Mod version = current Avalon game version (e.g. `0.5.2`). The same string is written into:
- `PluginVersion` in `Plugin.cs` (visible to BepInEx)
- `<Version>` in the `.csproj`
- The git tag (`<Mod>-v0.5.2`)
- The GitHub Release title and the Nexus file version

If you ship a second build against the same game version, append a fourth part:
`./release.ps1 -Mod AutoLoot -GameVersion 0.5.2.1`. `System.Version` (BepInEx) accepts
a 4-part form and Nexus version strings are arbitrary.

## Adding a new mod

1. Create the Nexus mod page manually and upload one initial file (any zip).
2. Files tab → "API Info" → copy the `Group ID`.
3. Add an entry to `nexus.json`:
   ```json
   "MyNewMod": { "mod_id": 999, "file_group_id": "1234567" }
   ```
4. Done — `./release.ps1 -Mod MyNewMod -GameVersion ...` works.

## Recovering from a failed Nexus upload

If steps 1–5 succeeded but the Nexus call failed (network blip, rate limit), re-run:

```powershell
./release.ps1 -Mod AutoLoot -GameVersion 0.5.2 -NoCommit -NoTag
```

The build/zip is rebuilt (cheap), commit/tag are skipped (already in place),
`git push` and `gh release create` are no-ops on already-pushed tags / existing releases
(both will warn but continue), and the Nexus upload retries.
