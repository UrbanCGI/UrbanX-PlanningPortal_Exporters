# UrbanCGI Planner additions to the 3ds Max exporter

This fork of [BabylonJS/Exporters](https://github.com/BabylonJS/Exporters) adds two pre-flight
safeguards for models that are exported to the UrbanCGI Planner (planning.urbancgi.co.uk), a tool
that corrects the phasing names in the scene before export, and an Update button that installs the latest
release from inside 3ds Max. Everything else is upstream. Exports made with this build identify themselves in the GLB's `asset.generator` as
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
(`Ph1_St01_IN_Sheet_Pile_1`, `Ph1_St00_IN_RC_Hoardings_Ph2_St06_RM`). A stage may carry a sub-stage
after a hyphen, `St<nn>-<m>` (`Ph1_St04-2_IN_Sheet_Pile_6`, `..._Ph2_St05-2_RM`): work inside stage 4 with
its own place in the order, 4 < 4-1 < 4-2 < 4-10 < 5. The old spelling with a full stop (`St04.2`) still
reads as the same stage, and the log asks for the hyphen (below). Objects sit inside a group whose name
carries its ID, activity name and dates: `<ID>_<Activity>_DD-MM-YY[_DD-MM-YY]`, or `<ID>_<Activity>_TBC`
while dates are unknown. The ID is one number or two:

- **two numbers, phase then stage**, `<PP>_<SS>[-<m>]` (`02_01-2_Construction_of_Phase1_TM_31-08-26_17-10-26`:
  phase 2, stage 1, sub-stage 2; the old `01_04.2_…` is phase 1, stage 4-2): each one or two digits, apart by
  underscores or spaces, the stage followed by an underscore, a space or the end; a stage with the old full stop
  only by an underscore or the end, so `01_2.4 High Hoarding_…` is stage 1 named `2.4 High Hoarding`. The group
  owns the work of a tag with its phase and stage. This is how Fix phasing names writes every layer;
- **one number, the stage** (`01_Sheet_Piling_SA&DS3_17-08-26_18-09-26`, `35_Service_Road_Setback_TBC`): the
  group owns the work of a tag with its stage, in the phase the Planner reads off its objects' *leading* tags: the
  phase most of the leading tags with the group's stage carry (the lower phase on a tie). Only when no leading tag
  has the group's stage does it look at the trailing tags, and take the phase most of those with its stage carry.
  So one object whose leading tag has the stage sets the phase on its own, whatever the others' trailing tags carry:
  in `06-2_Hoarding_Removal_…`, a single `Ph2_St06-2_RM_Old_Barrier` makes the group phase 2, and the
  `Ph0_St00_IN_…_Ph1_St06-2_RM` hoardings beside it are then not its work.
  An object of that stage in another phase is not its work: when another dated layer has the object's phase and
  stage, the Planner dates the object from that layer, though it stays in the group's folder (the naming check
  warns); with none, it keeps the group's dates. When only an object's trailing tag has the group's stage, in
  another phase, the group does not own the removal, and the Planner dates the leading tag's work from its own
  layer, or, with no dated layer for it, takes it as the group's own work, on the group's dates (TBC like the rest
  of the group when the group is TBC; with no dates at all, at the start of the project) (the naming check warns in
  both cases). The warnings say how the group got its phase and, when leading tags set it, name the
  objects that did; when those are fewer than the objects warned in the group for another phase, the advice says
  the slip may be theirs.

A sub-numbered stage (`05-1`, or the old `05.1`, which needs the underscore after it, so `2.4 High Hoarding`
stays an ordinary group) owns that sub-stage (`St05-1`) and, as before, plain `St05` objects; a plain stage
(`05`) covers every sub-stage of its stage. Three hyphenated numbers are always a date, never a stage
(`01_18-09-26_Kerbs` is stage 1, dated), and a stage (`04-2`) is never a date. The Planner reads the exported
node hierarchy, so it is the *group* that matters, not the 3ds Max layer.

Inside the phasing hierarchy that Fix phasing names (section 3) looks after — a group directly under a phase
group `_<n>_<name>` of a top-level node with "phasing" in its name, or directly under that top node — the
group is a layer and its name is read as the fixer reads it, `<PP>_<SS>[-<split>]_<name>_<dates>`
(`01_04-2_Phase1_Retainment_Installation_18-09-26_23-10-26`: phase 01, stage 04, sub-stage 2), also where the
Planner could not read it (a full stop after the phase, `6.06_…`, which the fixer corrects). An object belongs to
such a layer when one of its tags has the layer's phase and stage. A scene that Fix phasing names leaves clean
therefore reads clean, apart from what the fixer itself cannot correct (a removal tag naming a stage with no
layer, below). The fixer corrects only the first top-level node with "phasing" in its name (the export asks it
which); the layers of any other such node are pointed out to be renamed by hand. One known difference remains: a
layer ID with a phase or stage of three digits or more (`01_100_…`) is read by the fixer, and so by the naming
check, as phase 1, stage 100, while the Planner reads two numbers only when each has one or two digits, so it
reads that group as stage 1 named with one number; a removal tag naming that stage then has no layer for the
Planner, and the naming check does not say so. Stages run to two digits in practice.

The parser (`PlannerNaming.cs`) is a line-for-line port of the Planner's
`packages/domain/src/phasingNaming.ts`; its tests mirror `phasingNaming.test.ts`. Change both together.
Both read `St04.2` and `St04-2` (and `01_04.2_…` and `01_04-2_…`) the same way and mark the full stop form
as legacy, which only the naming check uses. A group name is legacy when the full stop is in its stage: a
one-number name's (`05.2_…`) or a two-number name's (`01_04.2_…`, also after several underscores or a space,
`01__04.2_…`, `01 04.2_…`), followed by an underscore or the end. A measurement in that place
(`01_1.8_x_2.4_…`) reads the same to both parsers, so the naming check looks closer (below). For a two-number
name the activity name is what follows the ID (`Retainment` for `01_04-2_Retainment_…`), and the label the
Planner groups by is unchanged (`01_04-2_Retainment`). Spaces, line ends and trimming follow the Planner's
JavaScript too (U+FEFF counts as a space, U+0085 does not).

### What is reported

| Severity | Case |
| --- | --- |
| Error | leading tag malformed (`Ph1_S03_...`) |
| Error | text after the description looks like a trailing tag but does not read as one (`..._Ph1_S03_RM`, `..._Ph2_St00_RM_extra`) |
| Error | group date unreadable (`31-02-26`) or finish before start (`17-02-27_23-02-17`) |
| Error | two exported objects share a name |
| Warning | tagged object not inside a dated group (unfiled: the Planner cannot schedule it); names the offending group, or the dated 3ds Max layer if the object sits on one. In a phasing layer whose ID only Fix phasing names reads (a full stop after the phase, `06.01.2_…`), the message says so and points to Fix phasing names (or, under a second top node with "phasing" in its name, gives the ID to rename it to by hand) |
| Warning | neither tag matches the group: for a two-number group (any layer of the phasing hierarchy, and any other group named phase then stage) neither tag has its phase and stage (`Ph2_St02-3_IN_...` or `Ph3_St02-7_IN_...` inside `02_02-7_...`, `Ph1_St01_IN_...` inside `01_04_...`); for a one-number group neither tag has its stage (`Ph1_St06_RM_...` inside `00_...`, `Ph2_St05-2_IN_...` inside `05-1_...`); or the leading tag has its stage but not its phase (the phase most of the leading tags with its stage carry, or, when no leading tag has its stage, most of the trailing tags with it; see section 1) and another dated layer has the leading tag's phase and stage, so the Planner dates the object from that layer, which the message names (`Ph2_St07_IN_...` among `Ph1_St07_IN_...` objects in `07_Kerbs_...`, with `02_07_Other_01-03-27` in the file; with no such layer the Planner keeps the object on the group's dates, as meant, and nothing is said); or only the trailing tag has its stage, in another phase (`Ph2_St03_IN_..._Ph2_St07_RM` among `Ph1_St07_IN_...` objects in `07_Kerbs_...`), so the group does not own the removal: the message is worded around the removal tag, says where the Planner dates the removal (the layer with its phase and stage, or after the end of the works, TBC, when there is none) and where it puts the leading tag's work (its own layer as usual, or, when no dated layer has the leading tag's phase and stage, the group's own work: spread over the group's dates, not listed as unresolved, which nothing else reports, and TBC only when the group is; a TBC group with no dates puts it at the start of the project, TBC like the rest of the group). Both messages say how the group got its phase; when leading tags set it they name the objects that did (up to five), and when those are fewer than the objects warned in the group for another phase, the advice says the slip may be theirs (`Ph2_St06-2_RM_Old_Barrier` beside three `Ph0_St00_IN_…_Ph1_St06-2_RM` hoardings in `06-2_Hoarding_Removal_…`). An object directly in a layer of the phasing top node is pointed to Fix phasing names, which gives it the layer's tag; one linked under another node inside the layer (a Max group, an object), or in a layer of a second top node with "phasing" in its name, is told to correct it by hand, as the fixer leaves it |
| Warning | tags whose phase and stage have no layer in the file, one warning for removal tags and one for install tags, each with the count, the tags and up to 10 object names: the Planner cannot date that work, so it puts a removal after the end of the works and an install at the start of the project, both marked TBC (HS2: `Ph1_St06`, `Ph1_St14`, `Ph1_St23`, `Ph1_St26`, `Ph1_St34`, `Ph99_St99`, eight objects; Fix phasing names lists these as Check lines and cannot correct them). A tag is looked at only where the Planner looks for another layer: not the one its own group owns, not the leading tag when its group owns neither (the mismatch above), not an install in phase 0 (there before the works), and not on an unfiled object. As for the Planner, a group is a layer only when it holds a tagged object: one with the tag's phase and stage that holds none is named in the warning |
| Warning | sub-stages written with a full stop, the old form: one warning for object names (`Ph1_St04.2_IN_...`) and one for group names (`01_04.2_...`, `05.2_...`), each with the count and up to 10 names, saying which ones Fix phasing names converts (the layers of the phasing top node it corrects and the objects directly in them) and which to rename by hand (anything else, e.g. an object linked under a Max group inside a layer, or a layer of a second top node with "phasing" in its name). In a phasing layer only a full stop before the split counts (a layer's `6.06` is corrected by the fixer with the rest of the ID). In any other group name only a full stop after a two-digit stage counts, and not one before `_x_`, so a measurement (`2.4_High_Hoarding`, `01_2.4_High_Hoarding_…`, `1.8_x_2.4_Heras_Panel`) is not counted; nor is a full stop inside a name (`…_v.02`) |
| Warning | group has neither dates nor TBC; group has an order number but no name |
| Warning | groups whose labels differ only by letter case (`..._Inst` / `..._inst`) |
| Warning | empty description (a stray underscore at the description's edge is trimmed by the Planner and not reported) |
| Warning | a space anywhere in a tagged object's name or a dated group's name (names use underscores only, no notes) |
| Warning | two groups share a name |
| Note | single-digit stage (`St6`, `St6-1`); untagged object inside a dated group; a date more than 3 years in the past or 10 in the future |
| Note | the phasing layers and helper nodes do not pair up by name (see the counts below): how many on each side, with up to 10 names |
| Note | tags whose phase and stage only an undated layer has (`01_05_Hoarding_TBC`), one note for removal tags and one for install tags, with the tags, the layers and up to 10 object names: the Planner dates another stage's work only from a layer with a start date, so until that layer has dates it puts the work where it puts a tag with no layer, marked TBC |

After the summary line ("Planner naming check: …", which gives the tagged objects of the export) comes the
phasing counts line, for a quick comparison with the project's layer / activity matrix, e.g. "Phasing counts:
91 layers in the Max layer HS2_…_v.02, 91 helper nodes under HS2_…_v.02." (the HS2 figures as worked out from its
GLB and Max layers; not yet seen in 3ds Max itself):

- **layers**: the 3ds Max layers inside the phasing top layer's tree, at any depth. The top layer is the Max layer
  named like the phasing top node (any capitals), else the first top-level Max layer with "phasing" in its name;
  it is not counted itself;
- **helper nodes**: the dummies, points, group heads and other helper objects inside the phasing top node's
  hierarchy, at any depth (the top node not counted). 3ds Max decides: a group head, or a node whose evaluated
  object is of the helper class. Other objects are not counted here;
- **shapes** (splines such as RailClone paths, whose evaluated object is of the shape class) are not counted at
  all: the export leaves them out, so the Planner never sees them. The exception is a shape with something
  exported linked under it, which the export writes as an empty node: it is counted with the helper nodes. A
  spline with a modifier that makes geometry (Sweep, Extrude) is geometry, not a shape.

A modeller's scene has a Max layer and a helper node of the same name for each group and layer, so the two
numbers normally pair up; when they do not, the note above lists what is missing on each side. The counts come
from the same reading of the scene as Fix phasing names, and its window shows them too.

Files: `SharedProjects/Utilities/Planner/PlannerNaming.cs`, `PlannerNamingValidator.cs` (rules, Max-free),
`PhasingNameFixer.cs` (the layer IDs and the counts), `Max2Babylon/Exporter/BabylonExporter.PlannerChecks.cs`
(collects the scene nodes, reports, stops), `Max2Babylon/Tools/PhasingScene.cs` (which nodes are helpers and
which are shapes).

## 3. Fix phasing names

**What it does.** Corrects the names of the phasing hierarchy in the 3ds Max scene, so the exported GLB
carries the names the Planner reads, the same result as cleaning up the exported file by hand. It works on:

- the phasing top node: the first top-level node with "phasing" in its name (any capitals);
- its phase groups, `_<n>_<name>` (`_06_Weekend_Closure`);
- the groups' layers, `<PP>_<SS>[-<split>]_<name>` (`02_05-1_Eastside_Kerbing_17-08-26`; the old `02_05.1_…`
  reads the same);
- the layers' objects, `Ph<n>_St<nn>[-<split>]_IN|RM_<description>[_Ph<n>_St<nn>[-<split>]_RM]`;
- the 3ds Max layers named like the top node, the groups and the layers, in any capitals (a modeller's scene has a
  Max layer and a helper node of the same name for each).

Shapes (splines such as RailClone paths) are left out wherever they sit: the export does not write them, so they
are not listed, renamed or counted. The RailClone object itself is geometry and is handled like any other object.
A shape with something exported linked under it is written as an empty node, and is handled like a helper.

What it corrects:

- the project's spelling mistakes (the word-fix list below), then spaces: ` - ` becomes `-`, any other space `_`;
- layer IDs: the phase written with two digits and an underscore after it (`6.06_…` becomes `06_06_…`), the stage
  with two digits;
- a layer sitting directly under the top node is moved into the group of its phase;
- a stage with several layers is split `-1`, `-2`, … in start-date order (the first date in the name; undated
  layers last); a stage with a single layer keeps its split, unless the review moved the layer there (the split
  belonged to its old stage, so it goes). `02_05.1` and `02_05-1` are the same ID;
- each object's leading tag becomes its layer's ID (`Ph2_St05-1_IN_…`), and a trailing removal tag follows the
  layer it points at to that layer's new ID;
- a sub-stage written the old way, with a full stop, gets the hyphen: in layer IDs (`01_04.2_…` becomes
  `01_04-2_…`), leading tags, removal tags (also one naming a stage with no layer) and a tag closing an RM
  object's name (`…_Ph3_St07.2_IN`), with the reason "hyphen before the sub-stage number, not a full stop". No
  full stop inside a name is touched (`…_v.02`, `…_2.4_High_Hoarding_…`, `…_1.8_x_2.4_Heras_Panel`), and neither
  are dates. A phase group's child whose name starts `<n>.<n>_` reads as a layer ID, though (`6.06_…` becomes
  `06_06_…`), so a group named after a measurement there needs a space (`2.4 High Hoarding`) or a start that is not
  a number;
- objects that would end up with the same name get `_02`, `_03`, … on their description;
- the Max layers are renamed with their nodes, and a moved layer's Max layer goes under its group's. A Max layer
  still written with the old full stop (`02_05.1_…`) takes the hyphen of its node also when the node already has it
  (`02_05-1_…`), as a Changed line of its own, unless a Max layer of the node's name exists too.

Dates and TBC are never changed. Anything that does not read is left exactly as it is and listed as **Check** for
a person to look at: an object with no Ph/St tag, a group child that is not a layer, a removal tag naming a stage
with no layer in the file (or a layer that cannot be moved), a corrected top, group, layer or object name that
another node already has, a word fix that would leave a name unreadable, a second top-level node with "phasing" in
its name (only the first is corrected), and so on. A removal tag naming a stage with no layer keeps its stage, but
its spelling is still corrected (the phase's zeros, the hyphen): its Check line then says "tag kept, only its
spelling corrected" and the object's Changed line shows the new spelling; otherwise "left as it is". The export's
naming check gathers these objects in one warning (section 2). When the stage has no phasing layer but a layer
elsewhere in the scene that the Planner reads as that phase and stage (at the scene root, under a second top node
with "phasing" in its name; a group that holds a tagged object, read as the Planner reads it), the Check line names
it instead, "points at `<layer>`, outside the phasing layers; tag kept - the Planner dates the removal from that
group only if the group is exported" (", which has no dates yet" and "once it has dates, and only if ..." when it
is undated). The Planner and the export's naming check see only what is exported, and what the export leaves out
(hidden nodes, the export layers, selected objects only) depends on the export's settings, which Fix phasing names
does not read: when that group is exported the naming check does not warn about the removal (an undated one gets
the note on undated layers); when it is not, the naming check lists the removal in its warning on tags with no
layer.

**How to use it.**

1. Open the exporter. On the Options tab, next to the Planner naming options, press **Fix phasing names…**.
   Nothing changes yet.
2. The window lists every change, one line per name: Status (Changed, Check or No change), Type, Stage, the layer
   it sits in, the current name, the corrected name and what changed. Check lines are highlighted. At the very top
   are the phasing counts (section 2), with the tagged objects of the scene: "Phasing counts: 91 layers in the
   Max layer …, 91 helper nodes and 173 tagged objects under …" (HS2, as worked out from its GLB), and the note
   when layers and helpers do not pair up. The line under them sums the list up, for example "55 layers and 103
   objects to change, 11 to check."
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
the export. The same reading of the scene gives the phasing counts line and the pairing note (section 2).

**From MAXScript.** Both calls use the scene's word fixes and no stage changes:

```maxscript
maxScriptManager = dotNetObject "Max2Babylon.MaxScriptManager"
maxScriptManager.FixPhasingNames false  -- the summary and the counts, then the list as CSV text; nothing changes
maxScriptManager.FixPhasingNames true   -- holds the scene, applies the list, adds the outcome after the summary,
                                        -- then the counts of the scene as the fix left it
```

**How it applies.** One MAXScript batch inside one undo record: nodes are found by handle and Max layers by their
current names before anything is renamed; nodes are renamed (one renamed since the list was made is left alone),
the layer nodes moved (attached when the group is a 3ds Max group), the Max layers re-parented, then renamed in two
steps through temporary names so a new name never meets an old one. Each item reports its own failure.

The rules are a port of the clean-up first done on the HS2 GLB (`reference-fix.mjs`, kept out of the repo), with
one change since: the clean-up wrote sub-stages with a full stop, the fixer writes the hyphen. The acceptance
tests run them on that model's node trees.

Files: `SharedProjects/Utilities/Planner/PhasingNameFixer.cs`, `PhasingFixPlan.cs` (rules and list, Max-free),
`PhasingFixScript.cs` (the MAXScript batch and the reading of its report), `PhasingWordFixText.cs` (the stored
list); `Max2Babylon/Tools/PhasingScene.cs` (reads the scene, keeps the word fixes, runs the batch),
`Max2Babylon/Forms/PhasingFixForm.cs`, `PhasingWordFixForm.cs`, the button in `Max2Babylon/Forms/ExporterForm.cs`,
`MaxScriptManager.FixPhasingNames`, the export warning in `BabylonExporter.PlannerChecks.cs`.

## 4. Updating from inside the exporter

**First time.** Exporters installed before this feature was released have no Update button: update them once
more with the installer (see "Installing" below). From then on the button does it.

**What the window shows.** Top right of the exporter window: the release this exporter is ("Version
v20261009.3", also in the window title and at the top of every export log), a status line ("Up to date",
"Update available: v…", "Checking…", "Couldn't reach GitHub", "No release published yet", "GitHub is busy, try
again in an hour"), the "Check for updates" and "Update" buttons, and the download progress.

**When it checks.** When the window opens, at most once a day, in the background, so opening the window is never
slower. The answer is remembered for 24 hours in `%LOCALAPPDATA%\UrbanCGI\Exporter\updates.json` (one file per
Windows user, shared by every 3ds Max version). A failed check is silent and is not tried again for an hour.
"Check for updates" always asks GitHub and says so when it cannot reach it. The check reads the fork's latest
release (never a draft or pre-release) from the GitHub API, with a 10 second time-out. GitHub allows 60 such
requests an hour per network without signing in, which a once-a-day check stays well inside. A proxy that asks
for a Windows sign-in is given the signed-in user's (GitHub itself never is); one that still refuses shows
"Blocked by the network proxy". A latest release without a package for this 3ds Max (or without the installer)
shows "v… has no package for 3ds Max <year>" (or "cannot be installed from here") and Update stays off.

**Update, one click.**
1. Downloads `Max_<year>.zip` for the running 3ds Max and `Installer.zip` from the latest release into a new
   folder under `%TEMP%\UrbanCGI-Exporter-Update`, and checks them: the sizes the release lists, the exporter
   DLL inside the package, and an installer that knows the update mode (version 1.8.1 or later; a release made
   before this feature stops here with a message, and 3ds Max stays open). The end of the download can come in
   the middle of other work that lets the window handle messages (a MAXScript export, a render, a modal dialog).
   Then, and when the exporter window is hidden (also while 3ds Max is minimised), the status line says "Ready to install v…" and the install
   waits for the next press of Update; otherwise it goes straight on. A later check that finds a different
   latest release drops the waiting download.
2. It first checks that no other update of this 3ds Max year is still running (one is: a
   message, and nothing else happens). Then it asks "Version v… is ready. 3ds Max will close to install it and
   open again afterwards.", adding, when another 3ds Max of the same installation is open (a render or batch job
   counts too), that the update also waits for that one to close. Then 3ds Max's own
   "save changes?" prompt; Cancel there stops the update and nothing changes. If the scene still has unsaved
   changes after that prompt (No was chosen, or the save did not happen: Save As cancelled, a read-only file, a
   lost network share), it asks "The scene has unsaved changes that will be lost. Close 3ds Max anyway?", with No
   as the default; No stops the update. From the first question until 3ds Max closes, the exporter window is
   disabled, so nothing in it can be pressed while 3ds Max's own dialogs are open.
3. Starts the installer, and Windows asks for permission (the installer needs administrator rights to write to
   Program Files). Saying No shows "Update cancelled" and 3ds Max stays open; pressing Update again uses the same
   download. An installer that stops within 3 seconds did not start properly: 3ds Max then stays open and the
   window points to the log.
4. Once the installer runs, 3ds Max closes. The scene as it was left after the save prompt goes without a second
   prompt; anything changed since then (from another tool window) gets 3ds Max's own save prompt rather than being
   lost. The installer waits for 3ds Max to exit, installs the package exactly as an offline install does, and
   opens 3ds Max again with the scene (an unsaved scene is not reopened). If the install fails, it still reopens
   3ds Max and says why, and the installer from the releases page works as before. It logs to
   `%LOCALAPPDATA%\UrbanCGI\Exporter\update.log`.

If 3ds Max is still open a minute after it was asked to close (a prompt was cancelled), the exporter window works
again and says "3ds Max did not close": close 3ds Max to finish the update, or press Cancel in the installer. After
Cancel (or a failure before anything was written) the installer waits up to five minutes for a 3ds Max that is
closing, which a heavy scene can need, and opens it again (Close in its window stops waiting, and 3ds Max is then
not opened again). A 3ds Max still
open then is left as it is, and the installer says it is not opened again. Another 3ds Max that was open all
along does not stop the reopening; only one started after the 3ds Max that asked had closed (it was opened again
already) does. Only one update per 3ds Max year runs at a time, and the installer lets go of it as soon as it has
nothing left to change or open. A second one that starts anyway changes nothing, says so, still waits for its
3ds Max, and leaves the reopening to the first, which reopens only the 3ds Max that started it. The exporter
looks for a running update before it asks and again just before it starts the installer, so that takes two
near-simultaneous presses in two 3ds Max windows. If the remembered "Update available" turns out to be out of date,
Update shows "Up to date" and nothing else happens.

The Update button waits while an export runs, from the window or from MAXScript (`MaxScriptManager.Export`), and
so does Apply in Fix phasing names. A **development build** (any build not made by the CD workflow)
says "Development build", never checks by itself, and its "Check for updates" names the latest release and
offers Update.

The exporter starts the installer with this command line; both sides compile the same
`SharedProjects/Utilities/Updates/UpdateCommandLine.cs`, so what one writes is what the other reads:

```
BabylonJS_Exporters.exe --update --max <year> --package "<folder>\Max_<year>.zip" --wait-pid <3ds Max process id>
  [--restart "<folder>\3dsmax.exe"] [--open "<scene>.max"]
```

**The release number.** The CD workflow works out the tag `v<yyyyMMdd>.<run number>` once, writes it into
`Max2Babylon/Updates/ReleaseInfo.cs` in place of the placeholder before it builds 3ds Max, and publishes the
release under the same tag. A build made anywhere else keeps the placeholder and is a development build. The
glTF generator string (`v1.0-urbancgi`) does not change.

**Checking by hand in 3ds Max** (none of this can run on a machine without 3ds Max): the block sits clear of the
banner and the tabs at the usual and at high display scaling; opening the window offline is as quick as before
and shows nothing; "Check for updates" offline says "Couldn't reach GitHub"; Update with an unsaved, a saved and
a modified scene; Yes at the save prompt of an untitled scene and then Cancel in Save As brings the "unsaved
changes" question; Cancel at each prompt and No at the permission prompt leave 3ds Max open and unchanged, and the
exporter window cannot be used while 3ds Max's prompts are open; a change made after the save prompt (Fix
phasing names) gets 3ds Max's own prompt on closing; Cancel in 3ds Max's prompt on closing, then Cancel in the
installer, leaves 3ds Max open and the window says "3ds Max did not close" after a minute, and Update pressed
then says an update is already running; a download that ends while a MAXScript export or a render runs stops
at "Ready to install v…" and the next press installs, and Update during a MAXScript export says to wait; with a second 3ds Max of the same
year open, the question says the update also waits for it, and Cancel in the installer then opens the first one
again; after a successful update 3ds Max reopens with the scene and the window shows the new version.

Files: `SharedProjects/Utilities/Updates/ReleaseTag.cs` (tags), `UpdateCheckLogic.cs` (verdict, texts, once a
day), `UpdatePackages.cs` (release assets, the installer in the zip, the request), `UpdateCommandLine.cs` (the
command line, shared with the installer); `Max2Babylon/Updates/ReleaseInfo.cs` (the release number),
`GitHubReleaseJson.cs`, `UpdateStateFile.cs`, `UpdateChecker.cs` (the GitHub request), `UpdateRunner.cs`
(download, prompts, starting the installer, closing 3ds Max), the block in `Max2Babylon/Forms/ExporterForm.cs`.

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
`Max_<year>.zip`, `Maya_<year>.zip` and `Installer.zip`. The installer and the exporter's Update button look
for exactly these names, so keep the packaging step as it is. The workflow writes the release tag into the 3ds
Max exporter before building it (section 4), so every `Max_<year>.zip` of a release knows its release. The
workflow can also be started by hand from the Actions tab.

Every exporter offers the latest release as an update, so the workflow guards it:

- Only `master` publishes. A run started by hand on any other branch builds and packages everything (a useful
  build check, e.g. for Max 2027, which needs the .NET 10 SDK) but creates no tag and no release. This holds
  only for branches that already contain this workflow: an older branch (one made before the update feature)
  runs its own older copy, which publishes a release that every exporter is offered. Check such branches with
  a pull request instead (the CI workflow builds every Max year), or merge `master` into them first.
- The latest release is the one exporters read (`releases/latest`), not the newest tag, so a tag left behind by
  a publish that failed half-way does not stop the next run from publishing.
- Each Max year must build and package (`Max_<year>.zip` holding `Max2Babylon.dll`), and `Installer.zip` must
  be there; otherwise the run fails and nothing is published, rather than a release that every Update of the
  missing year would fail on.
- Runs go one at a time per branch (a newer run replaces one still waiting).
- A run whose commit does not contain the latest release stops at once. Re-running an old run gives it today's
  date with its old code, which exporters would take as newer: start a new run from `master` instead.
- A run of the latest release's own commit (a re-run of that release, or a run started by hand with nothing new
  since) builds and packages everything but publishes nothing: the same code under a newer tag would show every
  exporter an "Update available" that closes 3ds Max for no change.

**Taking back a bad release.** Revert it with a new commit on `master` (GitHub's Revert button on the pull
request does this): the history keeps the release, so the run publishes the old code under a newer tag, which
every exporter is then offered. Do not reset `master` and force-push: every run then stops at the guard above
until the bad release and its tag are deleted (`gh release delete <tag> --cleanup-tag`), and even then the
machines that took the bad release stay on it until a newer release is published (push a new commit to
`master`; an empty one is enough).

## Installing on a modeller's machine

**With the installer (preferred).** Download `Installer.zip` from the latest fork release, unzip it, and
run `BabylonJS_Exporters.exe` as administrator with 3ds Max closed. It finds each installed Max version
through the registry, shows whether the installed exporter is current, and Install / Update pulls
`Max_<year>.zip` from the fork's latest release into that Max's `bin\assemblies`. Uninstall removes it
again. This is the fork's own build of the installer (window title "UrbanCGI Planner build", version
1.8.1): the stock Babylon.js installer points at upstream and would replace the Planner build with the
stock exporter, so do not mix them. Once an exporter with the Update button is installed, later releases
install from inside 3ds Max (section 4).

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

To confirm the install: the exporter window shows the release ("Version v…") at the top right, the exporter
log shows "Checking Planner naming convention" at the start of an export, and the GLB's generator string ends
in `v1.0-urbancgi`.

## Reading and sharing the log

Every export writes the complete log as `<model>.export-log.txt` next to the exported file (also for
MAXScript exports), so it can be sent along with the GLB. The Log tab of the dialog has a "Copy log to
clipboard" button at its bottom; upstream had placed it off-screen. Errors come first, then warnings,
then notes, and the line starting "Planner naming check:" sums them up.

## Tests

```bat
dotnet test SharedProjects/Utilities.Tests/Utilities.Tests.csproj
```

The test project compiles the shared sources directly (as Max2Babylon does) and needs no Max. It also
compiles the three Max-free update files (`ReleaseInfo.cs`, `GitHubReleaseJson.cs`, `UpdateStateFile.cs`), runs
the update command line through Windows' own argument splitting into the installer's parser, and reads
`cd.yml` to check that the release tag still reaches the one placeholder. Some tests run only when pointed at
real data and print their report:

- `PLANNER_REAL_TGA=<file>`: a mislabelled texture, e.g. the image bytes extracted from a broken GLB;
  proves the bytes round-trip to a decodable PNG.
- `PLANNER_NODES_FIXTURE=<json>`: `{ "nodes": [ { "name", "parent" | null, "isMesh" } ] }` extracted from
  a GLB; prints the naming report for a real hierarchy.
- `PLANNER_FIX_V305=<json>` and `PLANNER_FIX_V306=<json>`: `{ "roots": [i…], "nodes": [ { "name", "children": [i…] } ] }`,
  the node trees of a GLB before and after its phasing clean-up (the same nodes in the same order). The clean-up
  wrote sub-stages with a full stop, so the expected result is the second tree with every sub-stage full stop
  (layer IDs and Ph/St tags) turned into a hyphen ("v3_06h"). The fixer must turn the first tree into v3_06h (with
  the review's stages, Max layers included), turn the second into v3_06h with hyphen-only renames, and find
  nothing to do on v3_06h; the naming check must find no error on v3_06h and one warning only, the eight removal
  tags whose stage has no layer (the fixer's own Check lines), with every object's tags matching its layer's phase
  and stage; on v3_06 the two full stop warnings come on top. The report on the first tree is printed. Optional:
  `PLANNER_FIX_REF_CSV=<csv>` compares the list with the clean-up's (full stops hyphenated on both sides, the
  fixer's hyphen-only rows counted apart), `PLANNER_FIX_OUT_CSV=<csv>` writes the fixer's own.
