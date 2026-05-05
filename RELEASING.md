# Releasing a mod to Nexus

Releases run **fully locally** — `release.ps1` builds, packages, pushes the
version-bump commit + tag to origin, then calls the Nexus Upload API directly.
There's no GitHub Actions or GitHub Release involvement (the repo is public
and the build references proprietary game DLLs that can't be vendored or used
in cloud runners).

## One-time setup

1. Copy `.env.example` to `.env` and paste your Nexus personal API key:
   - Generate at https://www.nexusmods.com/users/myaccount?tab=api → "Personal API Key".
   - `.env` is gitignored.
2. For each mod you'll release, ensure `nexus.json` has its `file_group_id`:
   - On the Nexus mod page → Files tab → "API Info" button → copy `Group ID`.

## Cutting a release

From the repo root:

```powershell
./release.ps1 -Mod AutoLoot -ModVersion 1.0.1 -GameVersion 0.5.2
```

The script:
1. Writes `1.0.1` (the **mod** version) into `PluginVersion` in `<Mod>/Plugin.cs`
   and `<Version>` in `<Mod>/<Mod>.csproj`. Idempotent — re-running with the
   same version is a no-op rather than an error.
2. `dotnet build -c Release` for that one mod.
3. Stages `BepInEx/plugins/<Mod>.dll` and zips to `dist/<Mod>-v<modversion>.zip`.
4. Commits the version bump as `Release <Mod> v<modversion> (Avalon <gameversion>)`,
   tags `<Mod>-v<modversion>`, pushes branch + tag to origin.
5. **Calls Nexus directly** via the v3 Upload API:
   - Initialises a multipart upload, PUTs each part to its presigned URL,
     completes, finalises, polls until `available`, associates with `file_group_id`.
   - The Nexus file's **version** field is `<modversion>-avalon-<gameversion>`
     (the API regex-validates as `^[a-zA-Z0-9.-]+$` — no spaces or parens).
     The display name keeps the readable form with parens.
   - Archives the previous file on that mod page.

### Flags

| Flag         | Effect                                                              |
|--------------|---------------------------------------------------------------------|
| `-NoCommit`  | Skip the version-bump commit (changes stay unstaged).               |
| `-NoTag`     | Skip creating the git tag.                                          |
| `-NoPush`    | Skip just the git push (still uploads to Nexus).                    |
| `-NoNexus`   | Skip just the Nexus upload (still pushes the commit + tag).         |
| `-NoPublish` | Skip both git push AND Nexus upload — fully local dry run.          |

`-NoPublish` is the right flag for testing the build/zip locally without touching anything remote.
Combine `-NoCommit -NoTag -NoPush` to "build whatever's in the directory and just push it to Nexus" — useful for rebuilding the same version after fixing a release-script bug.

## Versioning

Two dimensions, both required:

- **`-ModVersion`** — the mod's own semver. Bumps when *the mod* changes.
  Used wherever uniqueness matters: `PluginVersion`, `<Version>`, git tag,
  zip filename. Must be a clean `System.Version` string (3 or 4 dotted parts)
  so BepInEx parses it.
- **`-GameVersion`** — the Avalon version this build was made against.
  Bumps when *the game* updates. Pure metadata — no parsing constraints.

Where each appears:

| Location                | Format                                  |
|-------------------------|-----------------------------------------|
| `PluginVersion`         | `1.0.1`                                 |
| `<Version>` in csproj   | `1.0.1`                                 |
| Git tag                 | `AutoLoot-v1.0.1`                       |
| Zip filename            | `AutoLoot-v1.0.1.zip`                   |
| Commit message          | `Release AutoLoot v1.0.1 (Avalon 0.5.2)`|
| Nexus file `version`    | `1.0.1-avalon-0.5.2`                    |
| Nexus file display name | `AutoLoot 1.0.1 (Avalon 0.5.2)`         |

Bump rules:
- Mod patch fix → bump `-ModVersion` (`1.0.1` → `1.0.2`), keep `-GameVersion`.
- Game patch but mod still works → bump `-ModVersion` (signal a fresh build to users) and `-GameVersion`.
- Mod feature → bump `-ModVersion` minor or major.

## Adding a new mod

1. Create the Nexus mod page manually and upload one initial file (any zip).
2. Files tab → "API Info" → copy the `Group ID`.
3. Add an entry to `nexus.json`:
   ```json
   "MyNewMod": { "mod_id": 999, "file_group_id": "1234567" }
   ```
4. Done — `./release.ps1 -Mod MyNewMod -ModVersion 0.1.0 -GameVersion 0.5.2` works.

## Recovering from a failed Nexus upload

If steps 1–4 succeeded but the Nexus call failed (network blip, rate limit), re-run:

```powershell
./release.ps1 -Mod AutoLoot -ModVersion 1.0.1 -GameVersion 0.5.2 -NoCommit -NoTag
```

The build/zip is rebuilt (cheap), commit/tag are skipped (already in place),
`git push` is a no-op on already-pushed tags, and the Nexus upload retries.
Add `-NoPush` if you also want to skip the no-op push.
