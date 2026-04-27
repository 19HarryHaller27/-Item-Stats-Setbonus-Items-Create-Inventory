# Item Traits

| | |
|-|-|
| **Mod id** | `itemtraits` |
| **Version** | 0.1.0 |
| **Game** | Vintage Story 1.22.0+ |
| **.NET** | 10+ (`net10.0`) |

**Dragoon**-style set: **wearable buffs**, set bonus (including flight in survival where implemented), **character** panel status, blackguard **sword** tune-up, sulfur **recipe** slot, etc. See `modinfo.json` for a one-liner; **MOD_PAGE.md** has extra detail if present.

## Build

- Set **`Directory.Build.props`** to your Vintage Story **game** install (the folder with `VintagestoryAPI.dll`) or `dotnet build -p:VintageStoryPath=...` / `VINTAGE_STORY_PATH`.
- `dotnet build ItemTraits.csproj -c Release`

**Deploy** copies the built DLL, `modinfo`, and the **assets** tree to this project folder and to the AppData `Mods\ItemTraits\` path on Windows.

## Layout

- `src/`, `assets/`, `modinfo.json`

## License

[MIT](LICENSE)

**Author:** adams.
