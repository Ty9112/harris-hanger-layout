# Architecture

A file-by-file tour of the codebase, oriented at someone modifying it.

---

## High-level shape

The add-in follows the standard Revit pattern:

```
HangerLayoutApp (IExternalApplication)   ← Revit entry, registers the ribbon
        │
        │  user clicks ribbon button
        ▼
HangerLayoutCommand (IExternalCommand)   ← opens the modeless dialog
        │
        ▼
HangerLayoutDialog (WPF window)          ← all user interaction lives here
        │
        │  user clicks "Apply"
        ▼
HangerPlacer.Place(doc, parts, spec, ...)  ← core placement algorithm
```

The dialog is **modeless** — it doesn't block Revit. To call back into the
Revit API safely (Revit's API is single-threaded), it posts actions through
`ExternalEvent`. That pattern is encapsulated in `RevitEventHandler`.

---

## File-by-file

### `src/HangerLayoutApp.cs`

`IExternalApplication`. Runs when Revit starts.

- Creates the **Hanger Layout** ribbon tab and **Layout** panel.
- Adds the **Hanger Layout** push button.
- Constructs the static `HangerHandler` + `HangerEvent` pair that the
  dialog uses to call back into the Revit API thread.

### `src/HangerLayoutCommand.cs`

`IExternalCommand`. Runs when the user clicks the ribbon button.

- Creates and shows the modeless `HangerLayoutDialog`.

### `src/Models/HangerSpecModels.cs`

Pure data model:

- `HangerDomain` — `Pipe | Duct`.
- `DuctShape` — `Any | Round | Rectangular`.
- `JointMode` — controls how joints (couplings, flanges, welds) are
  treated during placement.
- `SupportSpec` — one spec (a list of `SupportSpecRow`).
- `SupportSpecRow` — one size band: max size, spacing, fitting distance,
  joint distance.

All distances on the model are in **inches** (the user-facing unit). The
placer converts to feet for Revit's internal API.

### `src/Revit/HangerPlacer.cs`

The heart of the add-in. ~700 lines. The big methods:

- **`Place(doc, parts, spec, ...)`** — public entry. Groups the input
  parts into chains, dispatches to `PlaceForChain` (or `PlaceForPart` if
  joint mode is `Allow`).
- **`BuildChainInfo(doc, seed, selectionIds, flowMap)`** — walks both
  directions from a seed straight, computes joint piece gaps, reorients
  the chain so segment 0's "left" matches the user's Start Node side.
- **`PlaceForChain(...)`** — places hangers across an entire chain
  (multi-segment). Resolves a hanger button per host segment (round vs
  rect specs cycle), computes per-position offsets along the chain's
  axis, then places.
- **`BuildPositions(leftBound, rightBound, spacingFt)`** — the spacing
  math. **Hanger-to-hanger**: first hanger at `leftBound + spacing`, then
  step `spacing` until `pos >= rightBound`.
- **`ResolveHangerButton(doc, serviceName, override, sizeInches, hostShape)`** —
  walks the service buttons looking for a hanger that matches the host
  part's shape. Three-step precedence (see CLAUDE.md).
- **`ClassifyEnd(...)`** — decides whether a part's connector end is a
  "joint" (small in-line transition) or a "fitting" (elbow / tee / etc.).
  Drives which setback applies.

### `src/Revit/HangerFlowMap.cs`

Builds a BFS map from a seed connector outward, recording each
connector's position. Used by `BuildChainInfo` to decide which end of a
chain is "Start Node side" — orientation matters for setback application.

Origin storage uses `XYZ.DistanceTo` with a small tolerance, **not**
`IsAlmostEqualTo` (which is component-wise — common Revit footgun).

### `src/Revit/HangerSpecStore.cs`

ExtensibleStorage wrapper. Persists the list of `SupportSpec`s on
`doc.ProjectInformation` as a JSON blob.

- Schema GUID `8C3F2B4E-9D4F-4C9B-B67E-3D5F92DA014F` — fresh, independent
  of any other add-in.
- Schema name `HangerLayout_HangerSpecs`.
- Single string field `HangerSpecsJson` holding `System.Text.Json` output.

### `src/Revit/HangerSettingsStore.cs`

Tiny sibling store for non-spec settings. Currently holds **one** field:
the Fabrication `Database` folder path, used to auto-locate `HSpecs.MAP`
on subsequent imports.

- Schema GUID `4A8B2C9D-5E6F-4B1A-9D3C-7F2E81AB6543`.
- The dialog seeds this from the user's first file-pick on Import from
  Fab; the next Import lands without a prompt.

### `src/Revit/HangerSelectionFilters.cs`

`ISelectionFilter` implementations for the picker:

- **`FabricationPipeOrDuctFilter`** — accepts fabrication pipes / ducts
  for the Apply selection.
- Picker uses `PickObject(ObjectType.PointOnElement, filter)`, which
  requires the filter's `AllowReference(reference, point)` to return
  `true`. Easy footgun.

### `src/Revit/HangerWarningSwallower.cs`

`IFailuresPreprocessor`. Silences the "small joins" / "support not in
fitting plane" warnings that Revit raises during hanger creation —
expected and not actionable. Without this every Apply spams the user
with the same warning dialog.

### `src/Revit/HSpecsMapReader.cs`

Parses Fab's `HSpecs.MAP` binary format. Reverse-engineered byte-level
schema:

- Container record `AB BF 4C 00`.
- Spec records `AA BF 4C 00`.
- Rules `AF BF 4C 00`.
- Constraints `AD BF 4C 00` with codes 1000 (max size), 1001 (...), 1003 (...).
- Properties `AE BF 4C 00` with codes 3015 (Spacing), 3021 (FittingDist),
  3022 (Component), 3023 (JointDist).
- Numeric `AE` doubles at payload offset `0` (not `4` — early-version
  mistake).
- `ToSupportSpecs()` splits each Fab spec into Round vs Rect buckets by
  component-name hint.

### `src/Revit/SupportMapDumper.cs`

Generic MAP-file dumper kept around for debugging:

- `Dump(path, outPath)` — dumps a parsed structure to a text file.
- `TryGetDatabaseFolder(doc)` — reads the saved folder hint from
  `HangerSettingsStore` (for auto-locate).

### `src/Revit/PartTypeClassifier.cs`

Identifies fabrication part shapes and PCF-type categories:

- `StraightDuctCids` — rectangular (1, 35, 866, 924) + round (40, 41).
- `IsStraightDuctByCid(part)` — fast CID-based check.
- `IsStraightPipe(part)` — geometric check (anti-parallel connectors).
- `GetPcfType(part)` — classifies into PIPE / FLANGE / COUPLING / WELD /
  etc. via alias + description keywords.
- `ValveCids` — for completeness.

There's also a SKEY-derivation block (`GetSkey`, `DeriveXxxSkey`)
inherited from the parent PCF Exporter project — **dead code here**,
harmless, left in for low-cost future use.

### `src/Revit/ConnectorHelper.cs`

`GetPhysicalConnectors(part)` — filters `ConnectorManager.Connectors` to
the physical ones (excludes logical / curve-driven secondaries). Snapshot
to a list so the lazy enumeration's instability doesn't bite.

### `src/Revit/MapFileHelper.cs`

zlib MAP envelope decoder. Fab's `.MAP` files are concatenated zlib
deflate streams with a small header. This helper handles the framing
and feeds raw deflated data to `HSpecsMapReader`.

### `src/Revit/RevitEventHandler.cs`

Generic `IExternalEventHandler`. The modeless dialog stores an action
via `SetAction()`, calls `ExternalEvent.Raise()`, and Revit invokes
`Execute()` on its API thread with a `UIApplication`. This is **the**
canonical way to call into the Revit API from a modeless WPF window.

### `src/Revit/RibbonIconFactory.cs`

Generates the ring-hanger icon at runtime via `DrawingVisual` +
`RenderTargetBitmap`. Two sizes (16 px for small ribbon, 32 px for large).
No bitmap files ship with the DLL — the icon is drawn from primitives.

### `src/UI/HangerLayoutDialog.xaml`

The WPF window itself. Three top-level expanders:

- **Hanger Settings** (collapsed by default).
- **Hanger Placement** (expanded — primary workflow).
- **Diagnostics** (collapsed — power-user / debugging).

Pipe Specs and Duct Specs tabs sit inside Hanger Settings. The Apply
panel sits inside Hanger Placement.

### `src/UI/HangerLayoutDialog.xaml.cs`

The code-behind. ~1600 lines. The biggest concerns:

- `HangerLayoutViewModel` — observable collections + properties for
  every bound UI control. Manual `INotifyPropertyChanged`, no MVVM
  framework.
- `SpecVm` — view-model wrapper around `SupportSpec` for spec-row
  editing.
- Apply flow — splits the selection into Round-duct, Rect-duct, and
  Pipe buckets, then dispatches the appropriate spec per bucket via
  `HangerPlacer.Place(...)`.
- Import from Fab — uses `SupportMapDumper.TryGetDatabaseFolder` to
  auto-locate `HSpecs.MAP`, falls back to a file picker, remembers the
  picked folder for next time.
- Dirty tracking — `IsSpecsDirty` flag set whenever the user mutates a
  spec; close-prompt fires if dirty.

---

## Data flow at Apply time

```
User clicks Apply
    │
    ▼
Dialog reads:                                 ┐
  - selection (FabricationParts)              │
  - chosen Service                            │  (UI thread)
  - chosen pipe spec / round-duct spec /      │
    rect-duct spec / hanger overrides         │
  - Start Node (optional)                     │
    │                                         │
    ▼
HangerHandler.SetAction(uiApp => {            │
                                              │
  HangerPlacer.Place(                         │
    doc, selectedParts, spec, ...             │
  );                                          │
})                                            │
    │                                         │
    ▼
HangerEvent.Raise()                           ┘
    │
    │  ↓ Revit dispatches to API thread ↓
    │
    ▼
HangerPlacer.Place:                           ┐
                                              │
  1. Group parts into chains (if JointMode    │
     = NotAtJoint)                            │
  2. For each chain:                          │
     a. BuildChainInfo (walk both directions, │
        compute joint gaps, reorient)         │  (Revit API thread)
     b. Per host straight:                    │
        - ResolveHangerButton (shape filter)  │
        - BuildPositions (spacing math)       │
        - For each position:                  │
            FabricationPart.CreateHanger(...)  │
  3. Commit transaction                       │
                                              ┘
```

---

## Things that aren't here but could be

If you're picking up the project and looking for the next slice of work,
these all have hooks already and could be straightforward:

- **Multiple hanger types per spec row** — currently one button per
  spec; could be one button per size band.
- **Vertical-pipe support** — currently the algorithm assumes horizontal
  runs. Vertical needs a different anchor (the bottom of the run, not
  centred between fittings).
- **Insulation-aware spacing** — Fab's HSpecs has insulation thickness
  thresholds; not yet used.
- **Spec inheritance / Service-based defaults** — currently each spec is
  flat. Could derive from a parent.
- **Multi-document spec sharing** — currently per-project. A JSON
  export/import would let teams share spec libraries.
