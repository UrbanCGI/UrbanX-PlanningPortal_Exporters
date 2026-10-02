# Planner Exporters: the Urban CGI additions to the 3ds Max exporter

The Planner Exporters are Urban CGI's fork of [BabylonJS/Exporters](https://github.com/BabylonJS/Exporters)
for models that are exported to the Urban CGI Planner (planning.urbancgi.co.uk). The fork adds two
pre-flight safeguards, the Planner layer conventions with the Planner layers window that keeps them in step
with the Planner, and the glTF extras the Planner reads; everything else is upstream, including the
`.babylon` formats, Export & Run and the Maya exporter. Exports made with
this build identify themselves in the GLB's `asset.generator` as
`UrbanCGI Planner Exporters for 3dsmax <year> v1.1-urbancgi`.

The rename is display text only. In 3ds Max the menu is called "Planner" (quad menu "Planner..."), the
actions are listed under "Planner" and the windows say "Planner Exporters". What saved scenes, scripts
and installs depend on is unchanged: `Max2Babylon.dll` and its namespace, the `MaxScriptManager` entry
points, every `babylonjs_*` property, the "Babylon Attributes" material panel, the action ids, the Max
2025+ menu GUIDs and action table, and the release asset names. Starting this build removes the menus an
earlier build registered under "Babylon" (Max 2022 to 2024; Max 2025+ rebuilds its menus from the script).

## 1. Textures are exported by content, not by file extension

**The fault this stops.** 3ds Max decodes bitmaps by content, so a TGA that was renamed `.png` renders
perfectly in Max. The upstream exporter trusted the extension: `.png` and `.jpg` sources were copied
byte for byte and declared `image/png` / `image/jpeg`. The GLB therefore carried raw TGA bytes labelled
as PNG, browsers cannot decode TGA, and Babylon.js aborts the whole model load on that single image
(`/textures/N: Error while trying to load image ... Fallback texture was used`). Four consecutive HS2
exports shipped the same bad file.

**What happens now.** Every texture path is sniffed (`ImageFormatSniffer`: PNG, JPEG, GIF, BMP, TIFF,
DDS, WebP, PSD, EXR, HDR, KTX signatures, and the TGA footer or a strict TGA header check). The real
format decides everything the extension used to decide:

- bytes that already are PNG or JPEG are exported verbatim under their real type (a JPEG behind a
  `.png` name becomes `image/jpeg`, not a re-encode);
- anything else (TGA, DDS, BMP, TIFF, GIF) is decoded with the right decoder and re-encoded as PNG or
  JPG, for GLB embedding, external glTF textures and `.babylon` exports alike;
- unknown bytes fall back to the extension, exactly as before.

Each mismatch is logged once, as a warning naming the map and the path, and the export ends with a
summary list so the source files get fixed rather than corrected forever. There is nothing to switch
on; a mislabelled texture is never a valid input.

Files: `SharedProjects/Utilities/Texture/ImageFormatSniffer.cs`, `TextureUtilities.cs`,
`SharedProjects/Babylon2GLTF/GLTFExporter.Texture.cs`, `Max2Babylon/Exporter/BabylonExporter.Texture.cs`,
`Max2Babylon/Exporter/MaxGLTFMaterialExporter.cs`.

## 2. Planner naming check

Runs after the scene has been enumerated and before anything is written, over exactly the nodes the
export will contain, and reports in the exporter log. Two options on the exporter dialog (also saved
with the scene and honoured by MAXScript exports):

| Option | Root-node property | Default |
| --- | --- | --- |
| Check Planner naming | `babylonjs_plannerNamingCheck` | on |
| Stop export on naming errors | `babylonjs_plannerNamingStrict` | off |

With strict mode on and at least one error the export stops with nothing written.

### The convention being checked

Objects carry one or two tags, `Ph<n>_St<nn>_IN|RM`: the leading tag starts the name, an optional
trailing tag ends it, the free description sits between them
(`Ph1_St01_IN_Sheet_Pile_1`, `Ph1_St00_IN_RC_Hoardings_Ph2_St06_RM`). A stage may carry a sub-stage,
`St<nn>.<m>` (`Ph2_St05.2_IN_TR_Subbase_280mm_C`): work inside stage 5 with its own place in the order,
5 < 5.1 < 5.2 < 6. Objects sit inside a group whose name carries the order number, activity name and
dates: `N_<Activity>_DD-MM-YY[_DD-MM-YY]`, or `N_<Activity>_TBC` while dates are unknown
(`01_Sheet_Piling_SA&DS3_17-08-26_18-09-26`, `35_Service_Road_Setback_TBC`). The group owns the segment
whose `St` equals its order number; a sub-numbered group (`05-1_…` or `05.1_…` — the dotted form needs the
underscore, so `2.4 High Hoarding` stays an ordinary group) owns that sub-stage (`St05.1`) and, as
before, plain `St05` objects; a plain group number (`05_…`) covers every sub-stage of its stage. The Planner reads the exported node hierarchy, so it is the *group* that matters, not the
3ds Max layer.

The parser (`PlannerNaming.cs`) is a line-for-line port of the Planner's
`packages/domain/src/phasingNaming.ts`; its tests mirror `phasingNaming.test.ts`. Change both together.

### What is reported

| Severity | Case |
| --- | --- |
| Error | leading tag malformed (`Ph1_S03_...`) |
| Error | text after the description looks like a trailing tag but does not read as one (`..._Ph1_S03_RM`, `..._Ph2_St00_RM_extra`) |
| Error | group date unreadable (`31-02-26`) or finish before start (`17-02-27_23-02-17`) |
| Error | two exported objects share a name |
| Warning | tagged object not inside a dated group (unfiled: the Planner cannot schedule it); names the offending group, or the dated 3ds Max layer if the object sits on one |
| Warning | neither tag matches the group's stage (`Ph1_St06_RM_...` inside `00_...`, `Ph2_St05.2_IN_...` inside `05-1_...`) |
| Warning | group has neither dates nor TBC; group has an order number but no name |
| Warning | groups whose labels differ only by letter case (`..._Inst` / `..._inst`) |
| Warning | empty description (a stray underscore at the description's edge is trimmed by the Planner and not reported) |
| Warning | a space anywhere in a tagged object's name or a dated group's name (names use underscores only, no notes) |
| Warning | two groups share a name |
| Note | single-digit stage (`St6`, `St6.1`); untagged object inside a dated group; a date more than 3 years in the past or 10 in the future |

The table above is for scenes **without** a Work_Phasing root; they are checked exactly as before.

### Scenes with a Work_Phasing root

Once the scene has a top-level Work_Phasing layer (section 3), the layers under it are read with the coded
scheme: the layer decides an object's folder, tags are optional, layer names carry no dates, and what matters
is that every object hangs under its layer's helper, because the exported node hierarchy is all the Planner
sees. The dated-group rules are not applied.

| Severity | Case |
| --- | --- |
| Error | leading tag malformed (`Ph1_S03_...`): tags are optional, but one that is there must read |
| Error | the end of the name looks like a removal tag but does not read as one (`IN_Kerb_Ph2_S06_RM`) |
| Error | two exported objects share a name |
| Warning | an object's tag code does not match its layer's code (`Ph1_St01_...` on `01-04.0_...`; `01-04` covers `01-04.1`) |
| Warning | objects on a Planner layer that are not linked under its helper, or sit under another layer's helper, so the model files them elsewhere (one line per layer; run Update layers) |
| Warning | a Planner layer, or the root, has no helper yet (run Update layers) |
| Warning | a layer under the root still has an old dated name (run Adopt existing layers) |
| Warning | the same code on more than one Planner layer (allowed, but check it is meant) |
| Warning | phasing layers outside the root (old dated layers, or layers whose helper carries Planner properties); only the top-most is named, with a count of the ones inside it |
| Warning | tagged objects outside the root, which the Planner treats as context (one line per layer) |
| Warning | a removal code no Planner layer has (the Planner parks the removal as TBC) |
| Warning | an install tag at the end of an install object's name (it no longer means anything; only removals go at the end) |
| Warning | a space in the name of an object on a Planner layer; empty description after a tag; several phasing roots |
| Note | single-digit stage; a re-install tag after a removal (ignored for now) |

Files: `SharedProjects/Utilities/Planner/PlannerNaming.cs`, `PlannerNamingValidator.cs` (rules, Max-free),
`PlannerLayerTree.cs` (how the layers under the root are read, shared with the Planner layers window),
`Max2Babylon/Exporter/BabylonExporter.PlannerChecks.cs` (reads the layers and the scene nodes, reports, stops).

## 3. Planner layers and the glTF extras

The Planner owns the hierarchy, folder names, codes, dates and TBC; 3ds Max mirrors the hierarchy as layers
and helpers. The full rules are the Planner <-> 3ds Max contract (v1, 2026-10-02); its parser tables are
mirrored case for case in `PlannerCodesTests` and in the Planner's own tests, so change both together.

**Layers.** Everything phased sits under a top-level layer whose name contains "Work_Phasing" (any case;
spaces, underscores and dashes ignored); everything outside it is context. Each Planner folder is a layer
named `<code>_<name>` (`01_Zone5_Working`, `01-04.2_Phase1_Retainment_Installation`), or just `<name>`
when the folder has no code. A code is digits separated by `-` or `.`; its meaning is up to the modeller,
it is stored, never derived from position, and duplicates are allowed but reported. Layer names carry no
dates and no TBC any more. Each layer has a helper of the same name on it, linked to the parent layer's
helper (top-level folders to the root's helper); the GLB hierarchy is these links. The objects on a layer,
linked under its helper, are that folder's activities: the layer decides the folder, `Ph<n>_St<n>` tags
in object names are optional and a tag that disagrees with the layer code is only a warning. `IN` / `RM`
stay in object names, and an Install object may end in its removal (`_Ph<n>_St<n>_RM` or `_RM_<code>`).

**Helper properties** (user properties, saved in the .max): `planner_folderId`, `planner_code`,
`planner_name`, `planner_root` (`true`, root helper only), and, after adopting an old dated layer,
`planner_legacyStart` / `planner_legacyFinish` / `planner_legacyTbc` (inclusive `YYYY-MM-DD` dates the old
name carried; cleared once a Planner schedule has been applied).

**glTF extras.** Every exported node gets `extras.planner.guid`, its fixed 3ds Max GUID (the one the
exporter already keeps per node and uses as the Babylon id). Helpers with the properties above also get
`folderId`, `code`, `name`, `root` and `legacy { start, finish, tbc }`, absent values omitted. The block is
merged into any extras the node already has and is written to glTF only; `.babylon` output and Maya
exports are unchanged. Babylon's glTF loader hands it to the Planner as `node.metadata.gltf.extras`.
`planner` is a reserved key in an object's own `extras` property: its other keys are kept, the exporter's
values win. A copy of an object (Shift-drag, Clone, Mirror, Array) gets a GUID of its own; when two objects
still share one (a merge, or a copy made with an older build), the older object keeps it at export. GUIDs
given at export only last once the scene is saved, and the export log says so.

**Planners (pure logic, no Max calls).** Two planners read an abstract scene (layers; nodes with their
layer, link and planner properties) and return the operations to apply plus a review list, so a dry run
can be shown before anything changes and the rules are unit-tested without Max:

- *Adopt existing layers* (one-off): converts the legacy dated layers (`_GG_<Name>` groups and
  `GG_RR[.P]_<Name>[_TBC][_DD-MM-YY[_DD-MM-YY]]` children) into coded layers under a Work_Phasing root
  (created when missing). Legacy groups at the top level or under the old phasing root move under it (a
  top-level group written without its leading underscore, such as `05_Night_Closure`, counts when it holds
  dated layers); the old root layer stays. Layers are renamed to `<code>_<name>` (a clash gets a ` (1)`
  suffix), the same-named helper is reused (else a Point helper is created), helpers are linked parent to
  child, loose objects are linked to their layer's helper, and the helpers record code, name and the old
  dates. Old dated layers it leaves outside the root are listed, and so is an Adopt that found nothing.
  Layers Update layers already manages (their helper has a folder id) are left as they are, so Adopt after
  Update changes nothing.
- *Load schedule + update layers* (Planner to Max): reads `<ProjectLabel>.planner-schedule.json` and
  matches every Planner folder before placing any, strongest evidence first: the helper's
  `planner_folderId` (on any layer, so a Planner layer dragged out of Work_Phasing is found and moved
  back), else a code that only one folder and one layer under the root carry (duplicate codes never match
  by code), else the layer name (ignoring case, as 3ds Max does), else the name without a ` (n)` suffix;
  otherwise it creates the layer and helper. Layers are renamed and re-parented to match the Planner and
  the helpers get the folder id, code and name; a name that differs only in case is left as it is. Layers
  no longer in the schedule are reported, never deleted, and so are helpers that carry another layer's
  folder id (a copy). Objects that hang loose or under another Planner layer's helper are linked to their
  own layer's helper; anything else is left alone and reported.
- Both: a group head or container never stands for a layer (ungrouping would delete it and its
  properties); a separate helper is made and the group hangs below it like any object. A root helper
  linked below part of the phasing tree is unlinked first. Objects on layers outside Work_Phasing that are
  linked into the phasing tree are reported, since the exported model files them under a folder.

### The Planner layers window

Open it with the **Planner layers...** button at the top right of the exporter window, or from MAXScript
(below). It is a read-only view: the Planner owns the hierarchy, names, codes, dates and TBC, and nothing in
the window edits them.

- The header shows the Planner project (and workspace) of the loaded schedule, when the Planner exported it
  (in this computer's time), the site time zone ("not set" until the project has one) and the file it was
  loaded from.
- The upper list is the Work_Phasing root and its layers in outline order, indented by depth: Code, Name,
  Start, Finish, TBC, the number of objects on the layer, where the dates come from (the Planner schedule, or
  the old layer name an adopted layer carried) and a note when something needs a look (no helper yet, not
  in the schedule, not linked to its Planner folder yet, an old dated name). Dates read dd/MM/yyyy. The
  schedule's finishes are exclusive, so a midnight finish shows the last day it covers (`2026-09-24T00:00`
  shows 23/09/2026, as the old layer names did); a finish at any other time shows the day and the time.
- Selecting one or more layers lists their objects below: type (Install / Dismantle), tag code, the code of
  the folder that removes it, the Planner activity it is bound to with its dates, what it is linked to, and
  a note when it is not filed under its own layer's helper (the model would file it elsewhere), its tag
  disagrees with the layer, or it is not bound to an activity yet.
- **Load schedule...** reads `<ProjectLabel>.planner-schedule.json` (contract section 6). A file of another
  format or a newer version is refused with the reason; unknown keys are ignored. Loading a schedule for a
  different Planner project than the one loaded before asks first. The file is kept in the scene (app data
  on the root node, saved with the .max), so the dates are back when the scene is reopened, and the window
  offers to run Update layers straight away.
- **Update layers** plans the changes from the loaded schedule and shows them first (below).
- **Adopt existing layers...** plans the one-off conversion of the old dated layers and shows it first.
- **Select objects** selects the listed objects in 3ds Max (only the highlighted rows, when some are).
- **Show objects outside Planner layers** lists everything outside Work_Phasing (context, not phased),
  tagged objects first, since those were probably meant to sit on a Planner layer.
- **Refresh** reads the scene again; the window also reloads itself when a scene is opened, reset or new.

**Review before anything changes.** Update and Adopt open a review window with the plan's summary, the
review list (errors, warnings, notes and changes, in that order) and every single change. Nothing is
applied until **Apply** is pressed. By default the scene is held first (Edit > Hold), so Edit > Fetch puts
it back exactly as it was. Layer, helper and link changes then run as one MAXScript undo record ("Planner
layers"): layers are created, renamed and nested by name, Point helpers are created on their layers,
same-named helpers on the wrong layer are put on theirs, and nodes are linked with `.parent`, which keeps
them where they are in the world. The helpers' planner_* properties are written afterwards through the
exporter's own user-property helpers. Each change is tried on its own, so one that fails (a name taken, a
node gone) is reported and the rest still apply. Should 3ds Max not report back on the script, the new
helpers are found by name on their layers and the properties are written anyway. Afterwards the plan is
run again against the scene to confirm nothing is left to do. Nothing is ever deleted.

### Workflow

1. **Adopt existing layers** (once per model with the old dated layers). The old `_GG_` groups and dated
   layers move under Work_Phasing as `<code>_<name>` layers with their helpers; the dates the old names
   carried are kept on the helpers and shown in the window until a schedule is applied. Check the review
   list: duplicate codes, tidied numbers and objects whose tag disagrees with their layer are all listed.
   Export once and import into the Planner, which builds its folders from the helpers.
2. **Load schedule...** with the file the Planner exports for the project.
3. **Update layers**: layers are matched to the Planner folders (folder id, then a code only one folder
   and one layer carry, then the layer name), renamed and nested to match, new folders get a layer and
   helper, and loose objects are linked to their layer's helper. Layers the schedule no longer has are
   listed, never deleted.
4. **Model**: put each object on the layer of the folder it belongs to. A loose object on a layer is fine
   until the next Update layers links it; the naming check flags it at export. Ph/St tags are optional;
   `IN` / `RM` and the removal at the end of a name keep their meaning.
5. **Export** as usual. The naming check reads the Planner layers (section 2), and the GLB carries each
   helper's folder id and code and every node's GUID as glTF extras.

Repeat 2 to 5 whenever the Planner's folders change.

**From MAXScript** (all additive; the existing `MaxScriptManager` entry points are unchanged):

```maxscript
m = dotNetClass "Max2Babylon.MaxScriptManager"
m.ShowPlannerLayers()
m.LoadPlannerSchedule @"C:\jobs\EUS_HmRd_HRB.planner-schedule.json"  -- "" when loaded, else the reason
print (m.UpdatePlannerLayers false)  -- dry run: the review list and every change
m.UpdatePlannerLayers true           -- hold, apply, check again; returns what happened
print (m.AdoptPlannerLayers false)
```

**Not yet tried inside 3ds Max.** This build was compiled against the 3ds Max 2024 and 2026 SDKs but not run
in Max: the MAXScript the window generates (it is pinned by unit tests), the layer and node reading, the
app-data store, selection and the window itself need a first run on a copy of a model before modellers use
them.

Files: `SharedProjects/Utilities/Planner/PlannerCodes.cs` (contract sections 1 to 4),
`PlannerSchedule.cs` (schedule model, display dates), `PlannerScene.cs` (scene model, operations, review
items), `PlannerLayerPlans.cs` (the two planners), `PlannerNodeExtras.cs` (helper properties, extras),
`PlannerLayerTree.cs` (the layers under the root as read), `PlannerPanelModel.cs` (what the window shows),
`PlannerMaxScript.cs` (the MAXScript that applies a plan, and reading its result); `Max2Babylon/Planner/`
(`PlannerScheduleJson.cs` reads the schedule file, `PlannerNodeProps.cs` reads and writes the helper
properties, `PlannerMaxScene.cs` reads the scene and applies plans, `PlannerScheduleStore.cs` keeps the
schedule in the scene, `PlannerLayersActions.cs` the actions shared by the window and MAXScript);
`Max2Babylon/Forms/PlannerLayersForm.cs` and `PlannerReviewDialog.cs` (the windows, built in code);
`SharedProjects/Babylon2GLTF/GLTFExporter.cs` merges the extras.

## Building

No 3ds Max installation is needed: the SDK assemblies for every supported version are vendored under
`3ds Max/Refs/<year>/`. Needed: Visual Studio 2022 (MSBuild and the .NET Framework 4.8 targeting pack)
for Max 2022 to 2025, plus the .NET 8 SDK for Max 2026 (.NET 10 for 2027).

```bat
cd "3ds Max"
msbuild Max2Babylon.sln -restore -t:Build -p:Configuration=Release_MAX2024 -p:Platform=x64 -p:PostBuildEvent= -p:PreBuildEvent=
```

`BuildAll.cmd` builds every version. Output lands in `3ds Max/Max2Babylon/bin/Release/<year>/`.
The Max-side code targets C# 7.3 (net48), so no newer language features.

## Releasing

Every push to the fork's `master` runs the "CD Release" workflow (`.github/workflows/cd.yml`, GitHub
Actions must be enabled on the fork). It builds every Max version, Maya and the installer, and publishes a
GitHub Release at https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases with the assets
`Max_<year>.zip`, `Maya_<year>.zip` and `Installer.zip`. The installer looks for exactly these names, so
keep the packaging step as it is. The workflow can also be started by hand from the Actions tab.

## Installing on a modeller's machine

**With the installer (preferred).** Download `Installer.zip` from the latest fork release, unzip it, and
run `Planner_Exporters.exe` as administrator with 3ds Max closed (up to version 1.8.0 the program was
called `BabylonJS_Exporters.exe`). It finds each installed Max version
through the registry, shows whether the installed exporter is current, and Install / Update pulls
`Max_<year>.zip` from the fork's latest release into that Max's `bin\assemblies`. Uninstall removes it
again, along with the "Planner" menu and any "Babylon" menu an earlier build left. This is the fork's own
build of the installer (window title "Planner Exporters (Urban CGI)", version 1.9.0): the stock Babylon.js
installer points at upstream and would replace the Planner build with the stock exporter, so do not mix
them.

**Offline.** A `Max_<year>.zip` placed next to `Planner_Exporters.exe` is installed instead of anything
on GitHub. That is how to test a local build before a release exists (`Compress-Archive` the contents of
`3ds Max/Max2Babylon/bin/Release/<year>/*.dll`, any folder structure inside the zip is fine), and how to
install on machines without GitHub access.

**By hand.** With 3ds Max closed, copy the DLLs listed in `Max2Babylon/OnPostBuild.bat` (Max2Babylon,
Newtonsoft.Json, SharpDX, SharpDX.Mathematics, GDImageLibrary, TargaImage, TQ.Texture, the three
Microsoft.WindowsAPICodePack assemblies) from `bin/Release/<year>/` into
`C:\Program Files\Autodesk\3ds Max <year>\bin\assemblies`. On a machine where Max is installed the
post-build event does this automatically (it reads the `ADSK_3DSMAX_x64_<year>` environment variable Max
sets up).

To confirm the install: 3ds Max shows a "Planner" menu, the exporter window has a "Planner layers..."
button at the top right, the exporter log shows "Checking Planner naming convention" at the start of an
export, and the GLB's generator string ends in `v1.1-urbancgi`.

## Reading and sharing the log

Every export writes the complete log as `<model>.export-log.txt` next to the exported file (also for
MAXScript exports), so it can be sent along with the GLB. The Log tab of the dialog has a "Copy log to
clipboard" button at its bottom; upstream had placed it off-screen. Errors come first, then warnings,
then notes, and the line starting "Planner naming check:" sums them up.

## Tests

```bat
dotnet test SharedProjects/Utilities.Tests/Utilities.Tests.csproj
```

The test project compiles the shared sources directly (as Max2Babylon does) and needs no Max; it also
compiles `Max2Babylon/Planner/PlannerScheduleJson.cs`, which makes no Max calls. The Planner layer tests
cover every row of the contract tables, the adopt and update planners on a layer hierarchy taken from a
real v3 construction model (including replaying their operations and running them twice), a glTF export
that checks the extras, the naming check on coded scenes, what the Planner layers window shows (rows,
dates, objects, notes) and the MAXScript it runs (one undo record, a try/catch per change, escaped
names, nodes by handle). Two tests run only when pointed at real data and print their report:

- `PLANNER_REAL_TGA=<file>`: a mislabelled texture, e.g. the image bytes extracted from a broken GLB;
  proves the bytes round-trip to a decodable PNG.
- `PLANNER_NODES_FIXTURE=<json>`: `{ "nodes": [ { "name", "parent" | null, "isMesh" } ] }` extracted from
  a GLB; prints the naming report for a real hierarchy.
