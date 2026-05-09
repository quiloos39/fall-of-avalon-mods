# Releasing a mod to Nexus

Releases run **fully locally** — `release.ps1` builds, packages, and calls the
Nexus Upload API directly. Git commit / tag / push is **your responsibility** —
the script no longer touches git. There's no GitHub Actions or GitHub Release
involvement (the repo is public and the build references proprietary game DLLs
that can't be vendored or used in cloud runners).

## One-time setup

1. Copy `.env.example` to `.env` and paste your Nexus personal API key:
   - Generate at https://www.nexusmods.com/users/myaccount?tab=api → "Personal API Key".
   - `.env` is gitignored.
2. For each mod you'll release, ensure `nexus.json` has its `file_group_id`:
   - On the Nexus mod page → Files tab → "API Info" button → copy `Group ID`.

## Cutting a release

From the repo root:

```powershell
.\tools\release.ps1 -Mod AutoLoot -ModVersion 1.0.1 -GameVersion 0.5.2
```

The script:
1. Writes `1.0.1` (the **mod** version) into `PluginVersion` in `mods/<Mod>/Plugin.cs`
   and `<Version>` in `mods/<Mod>/<Mod>.csproj`. Idempotent — re-running with the
   same version is a no-op rather than an error.
2. `dotnet build -c Release` for that one mod.
3. Stages `BepInEx/plugins/<Mod>.dll` and zips to `dist/<Mod>-v<modversion>.zip`.
4. **Calls Nexus directly** via the v3 Upload API:
   - Initialises a multipart upload, PUTs each part to its presigned URL,
     completes, finalises, polls until `available`, associates with `file_group_id`.
   - The Nexus file's **version** field is `<modversion>-avalon-<gameversion>`
     (the API regex-validates as `^[a-zA-Z0-9.-]+$` — no spaces or parens).
     The display name keeps the readable form with parens.
   - Archives the previous file on that mod page.

After the script finishes, commit + tag + push manually if you want git to
match the released version:

```powershell
git add mods/<Mod>/Plugin.cs mods/<Mod>/<Mod>.csproj
git commit -m "Release <Mod> v<modversion> (Avalon <gameversion>)"
git tag <Mod>-v<modversion>
git push origin HEAD --tags
```

### Flags

| Flag       | Effect                                                |
|------------|-------------------------------------------------------|
| `-NoNexus` | Skip the Nexus upload (build + zip locally only).     |

Use `-NoNexus` when testing the build/zip locally without touching Nexus.

## Versioning

Two dimensions, both required:

- **`-ModVersion`** — the mod's own semver. Bumps when *the mod* changes.
  Used wherever uniqueness matters: `PluginVersion`, `<Version>`, zip filename,
  and the suggested git tag. Must be a clean `System.Version` string (3 or 4
  dotted parts) so BepInEx parses it.
- **`-GameVersion`** — the Avalon version this build was made against.
  Bumps when *the game* updates. Pure metadata — no parsing constraints.

Where each appears:

| Location                | Format                                  | Set by  |
|-------------------------|-----------------------------------------|---------|
| `PluginVersion`         | `1.0.1`                                 | script  |
| `<Version>` in csproj   | `1.0.1`                                 | script  |
| Zip filename            | `AutoLoot-v1.0.1.zip`                   | script  |
| Nexus file `version`    | `1.0.1-avalon-0.5.2`                    | script  |
| Nexus file display name | `AutoLoot 1.0.1 (Avalon 0.5.2)`         | script  |
| Git tag                 | `AutoLoot-v1.0.1`                       | manual  |
| Commit message          | `Release AutoLoot v1.0.1 (Avalon 0.5.2)`| manual  |

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
4. Done — `.\tools\release.ps1 -Mod MyNewMod -ModVersion 0.1.0 -GameVersion 0.5.2` works.

## Recovering from a failed Nexus upload

If the Nexus call failed (network blip, rate limit), just re-run the same command:

```powershell
.\tools\release.ps1 -Mod AutoLoot -ModVersion 1.0.1 -GameVersion 0.5.2
```

The version-bump step is idempotent, the build is cached, the zip rebuilds,
and the Nexus upload retries.
