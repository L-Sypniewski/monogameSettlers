# Third-Party Notices

## JSettlers / settlers-remake

https://github.com/paulwedeck/settlers-remake

Used as a reference for the original file formats (GFX images, maps) and terrain rendering.
The C# code was written independently; the following data tables and rules were ported:

- Torso/shadow alignment rules (`src/Homestead.Assets/Gfx/GfxFile.cs`, from `AdvancedDatFileReader`)
- Landscape type mapping and image numbers (`src/Homestead.Client/Terrain/TerrainKind.cs`,
  from `ELandscapeType` and `OriginalLandscape`)
- Building definitions for all four civilizations (`data/buildings/*.json`: footprints, doors,
  piles, image references), converted from `jsettlers.common/.../buildings/*.xml` by
  `tools/data-import/convert_jsettlers_buildings.py`
- Original map codes for buildings, piles and settlers (`OriginalScenario.cs`, `Material.cs`), pile and
  carrier graphics per material (`EconomyRenderer.cs`, `SettlerRenderer.cs`), from `EOriginalMap*Type`,
  `EMaterialType` and the movables tables; worker walk/work animations of all trades (`SettlerRenderer.cs`,
  from `movables*.txt`); grain image sequence (`MapObjectCatalog.cs`, from `MapObjectDrawer`); flag and work area mark images
  and the work circle metric (`EconomyRenderer.cs`, `PathFinder.InCircle`, from `MapObjectDrawer`, `MainGrid`, `MapCircle`)
- Soldier places of military buildings (`occupiers` in `data/buildings/*.json`), soldier and border
  post images (`SettlerRenderer.cs`, `EconomyRenderer.cs`), original settler type codes (`OriginalScenario.cs`,
  from `EOriginalMapSettlersType`), tools per trade (`data/production.json`, from `EMovableType`)
- Original map object names (`docs/formate/map.md`, from `EOriginalMapObjectType`)
- Terrain texture atlas layout, transition table and triangle texture orientation
  (`src/Homestead.Client/Terrain/TerrainAtlas.cs`, `TerrainMesh.cs`, from `Background` and `ETextureOrientation`)

```
The MIT License (MIT)

Copyright (c) 2015

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
