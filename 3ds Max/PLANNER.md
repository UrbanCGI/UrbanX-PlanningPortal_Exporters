# UrbanCGI Planner additions to the 3ds Max exporter

This fork of [BabylonJS/Exporters](https://github.com/BabylonJS/Exporters) adds two pre-flight
safeguards for models that are exported to the UrbanCGI Planner (planning.urbancgi.co.uk), and a tool
that corrects the phasing names in the scene before export. Everything else is upstream. Exports made with this build identify themselves in the GLB's `asset.generator` as
`babylon.js glTF exporter for 3dsmax <year> v1.0-urbancgi`.

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

Files: `SharedProjects/Utilities/Planner/PlannerNaming.cs`, `PlannerNamingValidator.cs` (rules, Max-free),
`Max2Babylon/Exporter/BabylonExporter.PlannerChecks.cs` (collects the scene nodes, reports, stops).

## 3. Fix phasing names

**What it does.** Corrects the names of the phasing hierarchy in the 3ds Max scene, so the exported GLB
carries the names the Planner reads, the same result as cleaning up the exported file by hand. It works on:

- the phasing top node: the first top-level node with "phasing" in its name (any capitals);
- its phase groups, `_<n>_<name>` (`_06_Weekend_Closure`);
- the groups' layers, `<PP>_<SS>[.<split>]_<name>` (`02_05.1_Eastside_Kerbing_17-08-26`);
- the layers' objects, `Ph<n>_St<nn>[.<split>]_IN|RM_<description>[_Ph<n>_St<nn>_RM]`;
- the 3ds Max layers named like the top node, the groups and the layers, in any capitals (a modeller's scene has a
  Max layer and a helper node of the same name for each).

What it corrects:

- the project's spelling mistakes (the word-fix list below), then spaces: ` - ` becomes `-`, any other space `_`;
- layer IDs: the phase written with two digits and an underscore after it (`6.06_…` becomes `06_06_…`), the stage
  with two digits;
- a layer sitting directly under the top node is moved into the group of its phase;
- a stage with several layers is split `.1`, `.2`, … in start-date order (the first date in the name; undated
  layers last); a stage with a single layer keeps its split, unless the review moved the layer there (the split
  belonged to its old stage, so it goes);
- each object's leading tag becomes its layer's ID (`Ph2_St05.1_IN_…`), and a trailing removal tag follows the
  layer it points at to that layer's new ID;
- objects that would end up with the same name get `_02`, `_03`, … on their description;
- the Max layers are renamed with their nodes, and a moved layer's Max layer goes under its group's.

Dates and TBC are never changed. Anything that does not read is left exactly as it is and listed as **Check** for
a person to look at: an object with no Ph/St tag, a group child that is not a layer, a removal tag naming a stage
with no layer in the file (or a layer that cannot be moved), a corrected top, group, layer or object name that
another node already has, a word fix that would leave a name unreadable, and so on.

**How to use it.**

1. Open the exporter. On the Options tab, next to the Planner naming options, press **Fix phasing names…**.
   Nothing changes yet.
2. The window lists every change, one line per name: Status (Changed, Check or No change), Type, Stage, the layer
   it sits in, the current name, the corrected name and what changed. Check lines are highlighted. The line at the
   top sums it up, for example "55 layers and 103 objects to change, 11 to check."
3. To give a layer another stage, type the number in its Stage column; clear the cell to go back to the stage in
   its name. The list is worked out again at once: the layer's ID and split, the tags of its objects and any
   removal tags pointing at it follow. **Show every layer** (on) also lists the layers that need no change, so any
   layer's stage can be changed. Stages given here last only while the window is open.
4. **Word fixes…** edits the project's spelling fixes (below); the list is worked out again when it closes.
5. **Save list…** writes the list as a CSV file with the same columns, to keep or to send for review.
6. **Apply** asks first, then carries the whole list out in one go. With **Hold the scene first** ticked (the
   default) the scene is held before anything changes, and Edit > Fetch puts it back as it was. Fetch restores the
   most recent hold only: the next Hold, including an export with "Use pre-export process", replaces it. The node
   renames and moves are also a single Edit > Undo step ("Fix phasing names"); whether 3ds Max undoes the Max layer
   renames with them has not been checked yet, so Hold is the safer way back. Afterwards the scene is read again
   and the window says what was done, anything that could not be changed and anything still left. A Max layer
   change that could not be made stays in the list as a Check line until it is done by hand.
7. Export as usual.

The window does not stop anyone working in the scene. If the scene has changed since the list was made, Apply
reads it again and asks for another look before changing anything; **Read the scene again** does the same by
hand. Opening another scene, or File > New or Reset, reads the new scene with its own word fixes and forgets the
stages given in the window. While an export is running Apply does nothing but say so, and the exporter's button
is greyed out.

**The word-fix list.** Plain text, capitals as written, applied in order to the top node, group, layer and object
names before spaces are replaced. The fixes and the space step run again until a name stops changing, so a fix
written with underscores also catches a name written with spaces, and a fix that writes another fix's text is
followed through. A fix is never used where it would change a date or TBC, or where it would leave a name the
fixer could no longer read (an empty group or layer name, a top node without "phasing", a broken object tag); the
window notes the first and lists the second to check. Fixes that keep feeding each other are used once only on
that name, with a note. A scene with no list of its own starts with the five fixes the HS2 model needed:

| Find | Replace |
| --- | --- |
| `Constraction` | `Construction` |
| `SubGgrade` | `SubGrade` |
| `Cource` | `Course` |
| `Islandsl` | `Islands` |
| `Sub-grade_and_Sub-base` | `SubGrade_and_SubBase` |

Pressing OK in the editor saves the list with the scene (root node app data under the exporter's class ID,
sub-id `0x50484658`), so save the .max file to keep it; an emptied list stays empty. A fix whose new text contains
the text it replaces (`Road` to `Roads`) is refused, since every run would change the names again.

**At export.** When "Check Planner naming" is on and the fix would change anything, the log has one warning:
"N phasing layers and objects can be corrected automatically — use Fix phasing names… on the exporter window". It never stops
the export.

**From MAXScript.** Both calls use the scene's word fixes and no stage changes:

```maxscript
maxScriptManager = dotNetObject "Max2Babylon.MaxScriptManager"
maxScriptManager.FixPhasingNames false  -- the summary, then the list as CSV text; nothing changes
maxScriptManager.FixPhasingNames true   -- holds the scene, applies the list, adds the outcome after the summary
```

**How it applies.** One MAXScript batch inside one undo record: nodes are found by handle and Max layers by their
current names before anything is renamed; nodes are renamed (one renamed since the list was made is left alone),
the layer nodes moved (attached when the group is a 3ds Max group), the Max layers re-parented, then renamed in two
steps through temporary names so a new name never meets an old one. Each item reports its own failure.

The rules are a port of the clean-up first done on the HS2 GLB (`reference-fix.mjs`, kept out of the repo); the
acceptance tests run them on that model's node trees.

Files: `SharedProjects/Utilities/Planner/PhasingNameFixer.cs`, `PhasingFixPlan.cs` (rules and list, Max-free),
`PhasingFixScript.cs` (the MAXScript batch and the reading of its report), `PhasingWordFixText.cs` (the stored
list); `Max2Babylon/Tools/PhasingScene.cs` (reads the scene, keeps the word fixes, runs the batch),
`Max2Babylon/Forms/PhasingFixForm.cs`, `PhasingWordFixForm.cs`, the button in `Max2Babylon/Forms/ExporterForm.cs`,
`MaxScriptManager.FixPhasingNames`, the export warning in `BabylonExporter.PlannerChecks.cs`.

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
run `BabylonJS_Exporters.exe` as administrator with 3ds Max closed. It finds each installed Max version
through the registry, shows whether the installed exporter is current, and Install / Update pulls
`Max_<year>.zip` from the fork's latest release into that Max's `bin\assemblies`. Uninstall removes it
again. This is the fork's own build of the installer (window title "UrbanCGI Planner build", version
1.8.0): the stock Babylon.js installer points at upstream and would replace the Planner build with the
stock exporter, so do not mix them.

**Offline.** A `Max_<year>.zip` placed next to `BabylonJS_Exporters.exe` is installed instead of anything
on GitHub. That is how to test a local build before a release exists (`Compress-Archive` the contents of
`3ds Max/Max2Babylon/bin/Release/<year>/*.dll`, any folder structure inside the zip is fine), and how to
install on machines without GitHub access.

**By hand.** With 3ds Max closed, copy the DLLs listed in `Max2Babylon/OnPostBuild.bat` (Max2Babylon,
Newtonsoft.Json, SharpDX, SharpDX.Mathematics, GDImageLibrary, TargaImage, TQ.Texture, the three
Microsoft.WindowsAPICodePack assemblies) from `bin/Release/<year>/` into
`C:\Program Files\Autodesk\3ds Max <year>\bin\assemblies`. On a machine where Max is installed the
post-build event does this automatically (it reads the `ADSK_3DSMAX_x64_<year>` environment variable Max
sets up).

To confirm the install: the exporter log shows "Checking Planner naming convention" at the start of an
export, and the GLB's generator string ends in `v1.0-urbancgi`.

## Reading and sharing the log

Every export writes the complete log as `<model>.export-log.txt` next to the exported file (also for
MAXScript exports), so it can be sent along with the GLB. The Log tab of the dialog has a "Copy log to
clipboard" button at its bottom; upstream had placed it off-screen. Errors come first, then warnings,
then notes, and the line starting "Planner naming check:" sums them up.

## Tests

```bat
dotnet test SharedProjects/Utilities.Tests/Utilities.Tests.csproj
```

The test project compiles the shared sources directly (as Max2Babylon does) and needs no Max. Some tests
run only when pointed at real data and print their report:

- `PLANNER_REAL_TGA=<file>`: a mislabelled texture, e.g. the image bytes extracted from a broken GLB;
  proves the bytes round-trip to a decodable PNG.
- `PLANNER_NODES_FIXTURE=<json>`: `{ "nodes": [ { "name", "parent" | null, "isMesh" } ] }` extracted from
  a GLB; prints the naming report for a real hierarchy.
- `PLANNER_FIX_V305=<json>` and `PLANNER_FIX_V306=<json>`: `{ "roots": [i…], "nodes": [ { "name", "children": [i…] } ] }`,
  the node trees of a GLB before and after its phasing clean-up (the same nodes in the same order); the fixer must
  turn the first into the second (with the review's stages, Max layers included) and find nothing to do on the
  second. Optional: `PLANNER_FIX_REF_CSV=<csv>` compares the list with the clean-up's, `PLANNER_FIX_OUT_CSV=<csv>`
  writes the fixer's own.
