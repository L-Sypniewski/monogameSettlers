# Versioning

monoSettlers follows [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.
The version comes from git tags (`vX.Y.Z`) through [MinVer](https://github.com/adamralph/minver) – there is no
version number to edit in any file. The title screen shows it at the bottom right.

## While below 1.0 (now)

The game is still growing, so it stays at `0.y.z`:

| Change | Bump | Example |
|---|---|---|
| Bug fixes, balancing (`data/*.json` values), translations, small UI corrections – save games keep loading | PATCH | `0.4.1` → `0.4.2` |
| New features (buildings, menus, AI characters, settings, map generator options …), or anything that breaks save games or `settings.json` | MINOR | `0.4.2` → `0.5.0` |

## 1.0 and after

`1.0.0` comes when a whole game against the computer can be played from the start to the end with all four peoples,
without known crashes. From then on:

- MAJOR: save games or settings of the previous version no longer load, or gameplay changes fundamentally.
- MINOR: new features that keep save games working.
- PATCH: fixes and balancing.

## Releasing

There is no schedule: tag whenever `main` has something worth playing.

```sh
git tag v0.2.0
git push origin v0.2.0
```

The [build workflow](../.github/workflows/build.yml) then tests, packages Linux (`tar.gz`) and Windows (`zip`) and
creates a GitHub release with both. A suffix makes it a pre-release, e.g. `v0.3.0-beta.1` for a test build.

Builds between tags are numbered automatically as the next patch's alpha, e.g. `0.2.1-alpha.0.14` (14 commits after
`v0.2.0`); every push to `main` keeps them as workflow artifacts for 30 days.
