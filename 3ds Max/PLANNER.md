# UrbanCGI Planner additions to the 3ds Max exporter

This fork of [BabylonJS/Exporters](https://github.com/BabylonJS/Exporters) adds what a modeller needs to prepare a scene
for the UrbanCGI Planner (planning.urbancgi.co.uk). Modellers see it as **Planner**: the menu, the window and the quad
menu say so (section 5).

- Textures are exported by what their bytes are, not by their file extension (section 1).
- The export checks the scene's names against the Planner's naming convention (section 2).
- **Clean up scene graph** brings a scene to that convention, and its layers and helpers in line with the Planner's layer
  list, after a review and in one undo step (section 3).
- An Update button installs the latest release from inside 3ds Max (section 4).
- The **Schedule** tab imports the Planner's layer list, shows each layer's IN and RM, edits them, adds layers and
  previews the schedule at any date (sections 5 and 6), with a Gantt beside the layers (section 7).

Everything else is upstream. Exports made with this build identify themselves in the GLB's `asset.generator` as
`babylon.js glTF exporter for 3dsmax <year> v1.0-urbancgi`.

**Naming v2 and live projects.** From this release the tool works on the new names (section 2): layer names without
dates, `IN_` and `RM_` tags on objects, and every date held in the Planner. The tool is released before the Planner
release that reads the new names. Until that Planner release is live, a cleaned scene's model does not schedule in the
Planner. So on a live project, such as HRB, do not run Clean up scene graph, and do not export a cleaned scene, until the
planning team says to go ahead. A scene on the old names still exports as before.

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

## 2. The naming convention and the export check

The Planner reads only what the GLB carries: node names and how nodes are linked. So the names in the scene are the
schedule. Dates are not in 3ds Max at all: the Planner holds every date, and the tool reads them from the Planner's
layer list (section 5).

### 2.1 Layers and helpers

A **layer** is a 3ds Max layer together with its **helper**, a Dummy of the same name that sits on that layer. The GLB
carries only the helper, so to the Planner the helper is the layer. A layer's objects are linked under its helper.

| Rule | Detail |
| --- | --- |
| Form | `<ID>_<Name>`, for example `02_01_4_Construction_of_Phase1_TM` |
| ID | Two numbers (phase, stage) or three (phase, stage, sub-stage). Each number has one or two digits, with single underscores between them. |
| Same ID | `6_5_2` and `06_05_2` are the same layer. `06_05` and `06_05_2` are not. The tools always write the ID the current way: two-digit phase and stage, then the sub-stage. |
| Name | At least one character, with no spaces (use underscores). Hyphens are fine: Unreal's Datasmith import leaves layer names alone. |
| Helper | Exactly the layer's name. |
| Dates and TBC | Never in the name. The Planner holds every date. |
| One ID, one layer | No two layers share an ID, across all of a project's working models. |
| Trap | After a two-number ID, a name must not start with one or two digits and an underscore. `02_05_3_Storey_Frame` reads as layer `02_05_3` named `Storey_Frame`. |
| One number | `05_Retainment` is not a layer on the new names. The Planner does not schedule the tagged objects under it. |
| Unchanged | Phase groups, `_<n>_<Name>` (`_02_Phase1_TM`), and the phasing root: the first top-level node with "phasing" in its name. |

### 2.2 Object names

An object's name is an optional leading tag, the object's **own name**, and an optional trailing tag.

| Part | Form |
| --- | --- |
| Leading tag | `IN_` or `RM_` at the very start, in capitals. |
| Own name | Everything between the tags. At least one character, with no spaces. Anything after the first space is a note and is not read. |
| Trailing tag | `_IN` or `_RM` at the very end, in capitals. |
| Layer reference | A layer ID straight after the trailing type word: `_RM_06_05_2`, `_IN_03_01`. |

Tags are capitals only, so `In_Situ_Wall` has no tag. A name needs at least one tag to be scheduled. An untagged object
is not scheduled and shows for the whole programme.

**What each form means.** The object sits under the helper of layer L. M is another layer.

| Name (sits in L) | Means |
| --- | --- |
| `IN_Kerb` | installed while L runs |
| `RM_Kerb` | there from the start, removed while L runs |
| `IN_Hoarding_RM` | installed while L runs, removed at the end of L |
| `RM_Bench_IN` | taken away while L runs, put back at the end of L |
| `IN_Hoarding_RM_06_05_2` | installed while L runs, removed in layer 06_05_2 |
| `RM_Bench_IN_03_01` | taken away while L runs, put back in layer 03_01 |
| `Kerb_RM` | there from the start, removed at the end of L |
| `Kerb_RM_06_05_2` | there from the start, removed in layer 06_05_2 |
| `Kerb_IN` | installed at the end of L (write `IN_Kerb` to install it while L runs) |
| `Kerb_IN_03_01` | installed in layer 03_01, while filed under L's helper |
| `Kerb` | not scheduled, so shown for the whole programme |

An object is installed at most once and removed at most once. Work that repeats stays a hand-made task in the Planner.

**Why the layer ID goes after the type word.** About a third of object names end in a copy number, such as
`Barrier_01`. The type word marks where the layer ID starts: `IN_Barrier_01_RM` has the own name `Barrier_01`.
`IN_Barrier_RM_01` is an error, because one number is not a layer ID.

**Which nodes are objects.** Helpers of layers, phase groups and the phasing root are never objects. A split piece
(`Name_2` linked under `Name`) and a material shard are part of their object. A closed 3ds Max group is one object, its
head.

**Not allowed.** The export check reports each of these as an error, and the Planner lists it.

| Case | Example | What the Planner does |
| --- | --- | --- |
| Same type at both ends | `IN_Kerb_IN`, `RM_Kerb_RM_03_01` | schedules the leading tag only |
| Type word doubled | `RM_RM_Marwood_Barrier3` | reads the first `RM_`; the own name is `RM_Marwood_Barrier3` |
| Two tags at the end | `X_IN_06_05_2_RM` | reads the last one only |
| Type word followed by numbers that are not a layer ID (often a copy number 3ds Max wrote) | `X_RM_1`, `X_RM001`, `X_RM_06_05_2_3` | reads no trailing tag |
| An old Ph/St tag anywhere | `Ph2_St01_4_IN_Kerbs2`, `IN_Kerb_Ph6_St05_RM` | does not schedule it |
| A layer ID before the tag | `02_01_4_IN_Kerb` | reads no tag: start the name with the tag |
| A tag after a space | `IN_Road Plates_RM_03_09` | reads `IN_Road` only |
| Nothing left between the tags | `IN__` | not an object |
| Two exported objects with one name | `IN_Sheet_Pile_4` twice | the export renames one `IN_Sheet_Pile_4 (1)`; in one layer, their activities share a name |

**Warned, but allowed.**

- Objects that share an own name, such as `IN_Cycling_Bollards` and `IN_Cycling_Bollards_RM_04_02_3`. The Planner cannot
  follow a tag change on them.
- Objects in one layer whose own names are the same, or differ only in their last number, but which name different
  layers. 3ds Max may have renumbered a copy (below).
- The old order without the letters: `IN_Kerb_06_05_2_RM`. The layer goes after the tag: `IN_Kerb_RM_06_05_2`.
- A reference to the object's own layer. Write the plain tag (`_RM`) instead.
- A reference to a layer that neither the scene nor the Planner's list has. The Planner parks that event TBC.
  `_RM_99_99` is the way to write "removed later, date not known yet".
- A split piece whose name, read alone, names a layer.
- A type word in the middle of a name (`Pipe_RM_Trench`), and lower-case near-tags (`in_`, `_rm`). Neither is read as a
  tag.
- Hyphens in object names. Unreal's Datasmith import turns them into underscores, so the names would not match between
  the Planner and Unreal.
- Spaces in object names.

**Copy objects before you tag them.** When 3ds Max copies an object it may renumber the end of the name, and that is
where a layer reference sits. A copy of `IN_Hoarding_RM_06_05_2` could come out as `IN_Hoarding_RM_06_05_3`: removed in
another layer, without notice. Make the copies first and tag them after, or check the copies' names. The warning on
nearly the same names in one layer catches some of these.

### 2.3 Old names and new names

The **old names** are the `Ph<n>_St<s>_IN|RM` tags and the dated layer names of earlier releases. The tool tells which
naming a scene uses the way the Planner does:

1. It counts the tagged objects under layer nodes, on the old names and on the new names. The larger count wins.
2. On a tie (none of either included), a layer name with a date makes the scene old-name. Otherwise it is new-name. TBC
   alone does not count.

A scene with nothing to tell by (no tagged object, no layer node, no dated layer name), such as a new empty scene, works
in the Schedule tab as a scene on the new names.

The Schedule tab needs the new names: a scene on the old names needs Clean up scene graph first (section 5.6). Its
export runs the old rules (section 2.5).

The readers in `PlannerNaming.cs` (`ParseObjectNameV2`, `ParseLayerNameV2`, `NamingOf`) are line-for-line ports of the
Planner's `packages/domain/src/phasingNaming.ts`, so both read every name the same way. Change both together. The tests
check the port against the Planner's own answers for about 29,000 names (see Tests).

### 2.4 The export check on the new names

The check runs after the scene has been read and before anything is written. It reads the nodes this export will
contain, with this export's own settings (hidden objects, Export only selected, the export layers). Two options on the
exporter's Options tab (also saved with the scene and honoured by MAXScript exports):

| Option | Root-node property | Default |
| --- | --- | --- |
| Check Planner naming | `babylonjs_plannerNamingCheck` | on |
| Stop export on naming errors | `babylonjs_plannerNamingStrict` | off |

With "Stop export on naming errors" on and at least one error, the export stops with nothing written.

On a scene on the new names the check reads the scene as the Planner reads the exported model: every layer node under
the phasing root, empty ones included, and every object with its own layer. What the Planner would list, the check
reports, with the Planner's code and severity. With a layer list imported in the Schedule tab, the list's layers count
as the Planner's.

| Severity | Case |
| --- | --- |
| Error | a name that is not allowed (the table above); two exported objects with one name; objects that still carry an old Ph/St tag (gathered in one error that points to Clean up); a node led by a number that is not a layer ID and holds tagged objects; layer nodes that share an ID but not a name; text after a space that makes two own names the same |
| Warning | a reference to a layer neither the scene nor the imported list has (parked TBC; `99_99` is work whose date is not known yet); own names shared by several objects; the same or nearly the same own names in one layer naming different layers; the old order without the letters; a reference to the object's own layer; a split piece whose name alone names a layer; a type word in the middle; lower-case near-tags; hyphens and spaces in object names; dates or TBC in a helper's name, or in a layer's Max layer name (not in a group's: its name is the Planner's folder name, kept as written); a space in a layer name, or nothing after its ID; a layer helper that does not reach the Planner (hidden or not exported, with nothing exported under it); a helper in the phasing tree led by a number that is not a layer ID; a tagged object under no layer's helper; two exported groups with one name |
| Note | underscores at the ends of an own name; a reference to a layer only the imported list has (planned in the Planner, not built in 3ds Max yet); an install at the end only (`Kerb_IN`); an untagged object in a layer; a layer that holds no object (the Planner still makes a folder for it, flagged TBC); a layer ID written another way than the current one; a Max layer and its helper named differently; layers and helpers that do not pair up |

After the issues the log has, in this order:

- when Clean up can convert names: "N names can be converted by Clean up scene graph… on the Schedule tab.";
- the summary: "Planner naming check (new names): N tagged objects in N layers; N error(s), N warning(s), N note(s).";
- what the Planner reads: its layers by ID (empty ones each make an empty folder), the objects it schedules with their
  installs and removals, the layers that references name, and the untagged objects it shows for the whole programme;
- the phasing counts (section 2.6).

Layer checks of 3ds Max's own (Max layers, helpers left out of the export, pairing) run only in the phasing tree, so
3ds Max's layer `0` is never checked.

### 2.5 The export check on the old names

On a scene that reads as the old names, the check runs the rules of the previous release, unchanged: Ph/St tags, dated
groups, stage mismatches and the rest. What it lists is renamed by hand: nothing in the tool corrects old names any
more. It adds one note: "This scene uses the old Ph/St names. The Schedule tab needs the new names: Clean up scene
graph… there converts the scene. On a live project, wait for the planning team's go-ahead first, as the Planner reads
the new names only from its next release."

### 2.6 Phasing counts

After the summary the log gives the phasing counts, for a quick comparison with the project's layer and activity
matrix, for example "Phasing counts: 91 layers in the Max layer HS2_…, 91 helper nodes under HS2_…."

- **Layers**: the 3ds Max layers inside the phasing top layer's tree, at any depth. The top layer is the Max layer named
  like the phasing root (any capitals), else the first top-level Max layer with "phasing" in its name. It is not counted.
- **Helper nodes**: the dummies, points, group heads and other helper objects under the phasing root, at any depth (the
  root not counted). 3ds Max decides what is a helper.
- **Shapes** (splines such as RailClone paths) are not counted: the export leaves them out, so the Planner never sees
  them. A shape with something exported linked under it is written as an empty node, and is counted as a helper. A
  spline that a modifier makes into geometry (Sweep, Extrude) is geometry.

A scene has a Max layer and a helper of the same name for each layer and group, so the two numbers normally pair up.
When they do not, a note lists what is missing on each side.

Files: `SharedProjects/Utilities/Planner/PlannerNaming.cs` (the readers, Max-free), `PlannerNamingValidatorV2.cs` (the
check on the new names), `PlannerNamingValidator.cs` (the check on the old names, and the report), `PhasingNameFixer.cs`
(the old names' layer reading and the counts), `Max2Babylon/Exporter/BabylonExporter.PlannerChecks.cs` (reads the scene
as this export writes it, picks the rules, reports, stops), `Max2Babylon/Tools/PhasingScene.cs` (which nodes are
helpers and which are shapes).

## 3. Clean up scene graph

**What it is for.** Clean up scene graph brings a scene to the new names, and, with the Planner's layer list imported,
brings its layers and helpers in line with the list. It replaces Fix phasing names and Build layers and helpers, whose
buttons are gone. It is on the Schedule tab.

**On a live project, wait.** The Planner reads the new names only from its next release (after 0.9.5). Until then a
cleaned scene's model does not schedule in the live Planner. On a live project, such as HRB, do not run Clean up and do
not export a cleaned scene until the planning team says to go ahead. The window says so on a scene with the old names,
and so do the button's tooltip and the export log's note.

**What it never does.** It never deletes a layer, a helper or an object. It never renumbers a stage. It never guesses:
what it cannot convert safely, it asks (section 3.3).

### 3.1 What it does, in order

Every step is planned on a copy of the scene in memory and shown in one review. Apply then runs them all as one
MAXScript batch, inside one undo record, "Clean up scene graph".

1. **Layers and helpers.**
   - Dates and TBC come out of the layer and helper names. They are not kept anywhere in 3ds Max.
   - The project's word fixes apply, and spaces become underscores.
   - The ID is written the current way (`01_04-2_…` and `01_04.2_…` become `01_04_2_…`).
   - A one-number layer takes its phase from its objects' tags, as the old fixer did (`06-2_Hoarding_Removal_…` with
     objects tagged phase 3 becomes `03_06_2_Hoarding_Removal`).
   - A layer directly under the phasing root moves into the group of its phase.
   - A layer in the group of another phase (`02_05` in `_03_Roads`) is listed to check, not moved: the Planner may file
     it there on purpose. With the Planner's layer list imported, the list's nesting decides, and it is not listed.
   - The Max layer is renamed with its helper.
   - A node led by a number inside a layer that does not read as a layer ID (a Max group called "3 Bay Gate") is never
     taken for a layer. It is listed when the Planner would not schedule the tagged objects under it.
   - With the Planner's layer list imported, the layers and helpers are then brought in line with it (section 3.2).
2. **Link objects to their helpers.** The same as the Schedule tab's Link (section 5.5), run on the scene as step 1
   leaves it.
3. **Objects**, each read against its own layer, on the hierarchy step 2 leaves:
   - a leading `Ph<p>_St<s>_IN|RM` tag that names the object's own layer becomes `IN_` or `RM_`;
   - a trailing tag that names the own layer becomes a plain `_IN` or `_RM`; one that names exactly one other layer
     becomes `_IN_<ID>` or `_RM_<ID>`;
   - old sub-stages (`St04-2`, `St04.2`) read as `St04_2`;
   - hyphens become underscores, underscores at the ends of the own name go, and the word fixes apply;
   - split pieces are renamed with their object.
4. **Numbering.** An own name that an object earlier in layer order already has gets a number on the later object
   (layer IDs compared as numbers, then full names). Numbers count from `_02`, as the unnumbered object counts as the
   first, and skip numbers already in use. The number goes on the own name, before any trailing tag:
   - `IN_Barrier_RM` becomes `IN_Barrier_02_RM`;
   - `IN_Barrier_RM_06_05_2` becomes `IN_Barrier_02_RM_06_05_2`;
   - with no trailing tag, at the end: `IN_Barrier` becomes `IN_Barrier_02`.

   A name that reads with an error is listed, never numbered. The numbering runs again after every answer.

Every name Clean up plans is read back with the new names' reader. A name that would not read back as meant (the ID,
the tags, the reference, the own name) is not used: the node keeps its name and is listed.

**Examples from the HRB v30 model:**

| Old name (layer) | New name |
| --- | --- |
| `02_01_4_Construction_of_Phase1_TM_TBC_07-09-26_05-02-27` | `02_01_4_Construction_of_Phase1_TM` |
| `Ph1_St04_1_IN_Sheet_Pile_4` (01_04_1) | `IN_Sheet_Pile_4` |
| `Ph1_St04_2_IN_Sheet_Pile_4` (01_04_2) | `IN_Sheet_Pile_4_02` (numbered: the own names clash) |
| `Ph2_St02_1_IN_RB22_Barrier01_Ph6_St05_2_RM` (02_02_1) | `IN_RB22_Barrier01_RM_06_05_2` |
| `Ph3_St04_1_IN_RC_HF_Outer_Full_Closure_Ph3_St04_1_RM` (03_04_1) | `IN_RC_HF_Outer_Full_Closure_RM` |
| `Ph3_St04_2_IN_RC_HF_Outer_Full_Closure_Ph3_St09_RM` (03_04_2) | `IN_RC_HF_Outer_Full_Closure_02_RM_03_09` |
| `Ph2_St02_13_IN_Cycling_Bollards` (02_02_13) | `IN_Cycling_Bollards_03` (`_02` is already used) |

Run again on a cleaned scene, Clean up finds nothing to change.

### 3.2 With the Planner's layer list

With a layer list imported (section 5.2), the same review and the same undo record also do what Build layers and
helpers did. The review says which: "The layers and helpers are brought in line with the Planner's layer list (N rows):
made, renamed and nested as it has them, never with dates. Nothing is deleted." Without a list, only the scene's own
names are cleaned, and a scene with no phasing root has nothing to clean up (Clean up says so). MAXScript's
`CleanUpSceneGraph` uses the list kept with the scene.

**The phasing root** is the first top-level node with "phasing" in its name, and the layer of its name (else the first
top-level layer with "phasing" in its name). A scene with neither gets the layer and helper `Work_Phasing` at the top. A
root with only its layer or only its helper gets the other half. Top-level rows go under the root. A top-level row named
like the root stands for the root itself.

**Each row finds its layer and helper** in this order:

1. a helper whose user property `planner_id` is the row's Planner ID;
2. a phasing layer whose name has the row's layer ID, when exactly one row and one layer have that ID;
3. a layer or helper of the row's exact name (any capitals). Inside the root, any. Outside it, only when the scene has
   no phasing root, when the name reads as a phasing name, or when it is a layer with its helper of the same name. An
   ordinary layer of the same name (context, survey) is never taken, and a 3ds Max group of objects never is;
4. for a phase group's row, the phase group in the root's tree with the same phase number, when that number is on
   exactly one row and one group, and that group's helper has no Planner ID. A group renamed in the Planner is renamed,
   not built again;
5. none: the row is new.

A layer and a helper pair up by name. A helper with a Planner ID that sits on a layer with no helper of its own pairs
with that layer. A helper copied in 3ds Max carries the other helper's Planner ID: the row of that ID keeps its own
helper, and the copy can be found by another row's layer ID or name. One that no row finds is listed ("a copy?").

**What it changes.**

- The layer and the helper take the row's Layer name, `<ID>_<Name>`, and the helper takes the row's Planner ID.
- The layer goes under the parent row's layer. The helper goes under the parent row's helper (attached when that is a
  3ds Max group head). A helper not on its layer is put on it.
- A missing layer is created under its parent. A missing helper is created as a Dummy at the origin (box 1 unit), on its
  layer, linked under its parent's helper, with its Planner ID. Its objects are linked and converted in the same run.
- Names never carry dates, even when the list's Layer cell does: a list exported before the new names is read without
  them, and the review says how many names that touched. The list's name wins over a word fix. A group's name is the
  Planner's folder name and is built as the list writes it (a date or TBC typed into it stays), unless it carries a
  layer ID, when it is read as a layer's name is.
- A layer found by its Planner ID whose ID the Planner changed takes the references to it along: every `_IN_<old ID>`
  and `_RM_<old ID>` is rewritten to the new ID in the same run. All references move at once, so swapped IDs come out
  right.
- A reference to an ID that no layer of the scene has, which the list now gives a layer that had another ID, was never
  written for that layer: it is listed and not rewritten ("the Planner's list now gives 02_02 to 02_02_Kerb, which was
  02_01"). On the old names its question has no default answer (section 3.3).

**What it leaves.** A phasing layer the list does not have is kept, cleaned and listed: "not in the Planner's list: kept,
as Clean up never deletes". A row that cannot be brought in line (a name another layer or node keeps, a problem in the
list) is listed and left as it is, and so is every row under it. No two layers may share an ID (D2), so a row is left as
it is too when the layer ID its name gives is kept by a layer no row brings in line (one the list does not have, or one
whose row is left as it is): "the layer 02_02_Gully, which the list does not have, keeps the layer ID 02_02". Give one
of them another ID by hand, then Clean up again.

### 3.3 The questions, and how to answer them

The objects Clean up cannot convert safely come first in the review, each with its answers in the **Answer** column.
Nothing is applied to an object until an answer is chosen; until then it keeps its old name, and the Planner does not
schedule it on the new names. Every answer plans the clean-up again at once, the numbering included. "Leave it for now:
it keeps its old name" is always an answer.

**A closing `_RM` or `_IN` with no Ph/St tag** (`Ph2_St01_8_IN_RC_Hrd_from_PDF_01_RM`). The old names never read this
word, so the old schedule had no such event and Clean up cannot tell when it happens. Check the programme, or ask the
planner:
- it comes down when its own layer finishes: **Removed at the end of its own layer** (`…_RM`);
- it comes down in another layer: **Removed in another layer**, and choose the layer. When the name already writes a
  layer ID after the word, **Removed in layer …, as the name writes it** keeps it;
- it never comes down: **No removal: the word is taken out**.

In v30, the corrected programme answered its 12 closing words this way: 8 hoardings come down in 06_05_1 or 06_05_2, 3
closure barriers come down at the end of their own layers, and `Utility_Crossing_SB_H` has no removal at all.

**A leading tag that names another layer** (`Ph2_St02_10_IN_RC_Hrd_004_from_PDF_Ph6_St05_2_RM` sits under the helper of
02_02_3, but its leading tag names layer 02_02_10; in v30 the answer was to install it while its own layer runs):
- the object belongs to that layer: **Link it under that layer's helper**. This answer is offered only when the object
  sits directly under its layer's helper and no tagged object sits under it; otherwise link it by hand and run Clean up
  again;
- the tag is the mistake: **Installed (or removed) while its own layer runs** (`IN_` or `RM_`);
- it stays filed where it is, but its event is in the other layer: **Installed in layer …, while it stays under the
  helper of …** (`…_IN_<ID>`). Offered only when the name has no trailing tag, as an object has one leading and one
  trailing tag.

**A doubled type word** (`RM_RM_Marwood_Barrier3`): **Take out the second RM_**. For a leading `IN_` followed by
`RM_` (`IN_RM_…`), the answer takes the `RM_` out. If a removal was meant, take it out here and use Add to RM… on the
right layer afterwards (section 6.2).

**A trailing tag naming a stage that no layer has** (HS2 has 8: `Ph1_St06`, `Ph1_St14`, `Ph1_St23` twice, `Ph1_St26`,
`Ph1_St34` and `Ph99_St99`). The default answer
writes the reference as the tag does (`_RM_01_23`, `_RM_99_99`): the Planner parks that removal TBC. Keep it when the
layer will be planned in the Planner, or when the date is not known yet. Otherwise choose another layer, the end of its
own layer, or no removal. When the Planner's list now gives that ID to a layer that had another one (Kerb moved from
02_01 to 02_02), the tag was not written for that layer: the question says so, the answer that refers to the ID says it
files the removal in that layer now, and there is no default, so the object keeps its name until someone answers.

**A trailing tag naming a stage that several layers have** (its sub-stages): choose the layer it means, or one of the
other answers.

**Rows to check.** Clean up lists, and leaves as they are, what a person has to look at: a name another node or Max
layer already has; a cleaned name that would not read back; a layer of a phase with no group to move it into, or with
several; layers sharing an ID; the same type at both ends; nothing left between the tags; a word fix that would leave a
name unreadable; a tagged object under no layer's helper that Link could not place; a list row that cannot be brought in
line. On a scene with the old names, an object that already reads as the new names is listed and left as it is ("it will
be scheduled so"). Rename by hand, then run Clean up again.

### 3.4 The review window

The columns are Status, Item, Layer it sits in, Current name in Max, New name, What changes and Answer. For a layer,
helper or Max layer, Layer it sits in is where it is now (named as Clean up names it), and for one Clean up moves, What
changes says where it goes (also when only its Max layer moves). For an object it is the layer it ends in, after any
link. The line above the list sums it up, for example "1 layer and 2 helpers to create, 76 layers and 152
objects to rename, 2 helpers and 2 Max layers to move, 3 links, 91 Planner IDs to write, 14 to choose, 2 to check." (the
moves only when there are any).

| Status | Colour | Means |
| --- | --- | --- |
| Choose | orange | waiting for an answer; it keeps its name |
| Default | blue | the default answer is taken unless another is chosen |
| Answered | green | an answer is chosen |
| Left | grey | "leave it for now" was chosen; it keeps its old name |
| Change | yellow | renamed, moved, linked or made |
| Check | red | left as it is for a person to look at |

- **Only rows to choose or check** hides the rest.
- **Word fixes…** edits the project's spelling fixes (section 3.6). The scene is read again when it closes.
- **Save list…** writes the review as CSV (`<scene>_clean_up.csv`).
- **Save a copy first** is ticked on a scene with the old names. Apply then asks where to save the scene as it is now,
  offering `<scene>_before_v2.max` in the scene's folder. The copy is saved without switching to it: you go on working
  in the original file, which is then cleaned. Save the scene afterwards to keep the clean-up.
- **Hold the scene first** is ticked by default. Edit > Fetch then puts the scene back as it was, and is the sure way
  back: whether Edit > Undo also takes back the Max layers Clean up renamed, moved or made has not been checked in 3ds
  Max yet. Fetch restores the most recent hold only: the next Hold, including an export with "Use pre-export process",
  replaces it.
- **Apply** asks first when listed objects keep their old names (No is the default).

On a scene with the old names the window opens with the warning: "Clean up converts this scene to the new names, which
the Planner reads only from its next release. On a live project, do not clean up or export this scene until the planning
team says to go ahead. Save a copy first to keep the scene as it is now."

**After Apply.** The tab reads the scene again first. If anything changed since the review, nothing is changed and it
says so. Otherwise it saves the copy, holds the scene and runs the batch. The report says what was done and what was
not, and what the scene still needs when it is read again ("nothing is left to change", or "Clean up again to finish").
The clean-up is one undo record, and Edit > Undo takes it back in one step, but whether that also takes back the Max
layers it renamed, moved or made has not been checked in 3ds Max yet: when Max layers changed, the report says so and
offers Edit > Fetch as the sure way back. The tab reads the scene again after Edit > Undo and Redo. Layers added in Max
(section 6.4) follow their layers' new names, and their parents', and follow them back on Edit > Undo. Their records
are read from the scene first, so records a Clean up run from MAXScript renamed in the meantime are kept.

The window does not stop anyone working in the scene. Opening another scene, File > New or Reset reads the new scene.
While an export runs, Clean up, like the tab's other changes to the scene, says to try again once it has finished.

### 3.5 From MAXScript

```maxscript
maxScriptManager = dotNetObject "Max2Babylon.MaxScriptManager"
maxScriptManager.CleanUpSceneGraph false  -- the summary and notes, then the review as CSV text; nothing changes
maxScriptManager.CleanUpSceneGraph true   -- holds the scene, applies the clean-up, adds the outcome after the summary
```

Nobody answers the questions from MAXScript: only the default answers are used (a reference to a stage no layer has,
written as the tag writes it, unless the Planner's list now gives that ID to a layer that had another), and every other
listed object keeps its old name. No copy is saved. On a scene with the
old names the first line is the live-project warning. `CleanUpSceneGraph()` is the same as `CleanUpSceneGraph false`.
`FixPhasingNames` still answers, changes nothing and points to Clean up.

### 3.6 Word fixes

Plain text, capitals as written, applied in order to the root, group, layer and object names before spaces are replaced.
The fixes run again until a name stops changing. A fix is never used where it would change a date or TBC, or where it
would leave a name unreadable; the review lists the second to check. A scene with no list of its own starts with the
five fixes the HS2 model needed:

| Find | Replace |
| --- | --- |
| `Constraction` | `Construction` |
| `SubGgrade` | `SubGrade` |
| `Cource` | `Course` |
| `Islandsl` | `Islands` |
| `Sub-grade_and_Sub-base` | `SubGrade_and_SubBase` |

Pressing OK in the editor saves the list with the scene (root node app data under the exporter's class ID, sub-id
`0x50484658`), so save the .max file to keep it. A fix whose new text contains the text it replaces (`Road` to `Roads`) is
refused, since every run would change the names again.

### 3.7 How it applies

One MAXScript batch inside one undo record, "Clean up scene graph". Before anything changes, it finds every node by its
handle (a node renamed since the review is left alone) and every Max layer by its current name. Then it:

1. renames the nodes: helpers, objects and split pieces;
2. renames the Max layers in two steps through temporary names, so a new name never meets an old one;
3. makes the Max layers the list adds, under their parents, and moves Max layers under their groups or parents;
4. makes the helpers the list adds;
5. links the layers into their phase groups and the helpers under their parents, then the objects under their
   layers' helpers (attached when the parent is a group head; world positions kept);
6. puts helpers on the Max layers of their names, and an object an answer links under another layer's helper on that
   helper's layer;
7. writes the Planner IDs (user property `planner_id`).

No date is written anywhere. Each item reports its own failure, and an end marker shows whether the batch ran to its end.
The tests run the batch's data, not MAXScript, so its MAXScript after the arrays is also kept word for word with them
(`SharedProjects/Utilities.Tests/Golden/PlannerCleanUpScript.ms`, and `PlannerLayerScript.ms` for Add layer's): a change
to a loop fails there until it has been checked in 3ds Max and the copy updated.

**Checking by hand in 3ds Max** (none of this has been run in 3ds Max yet): Clean up on a copy of v30 and of HS2, with
and without the list; the copy saved and the open scene keeping its file name; Edit > Undo after a clean-up (do the Max
layer renames and the new layers go too?) and Edit > Fetch; Clean up again finds nothing; Planner IDs on the helpers;
`CleanUpSceneGraph` from MAXScript; the review window at a high display scaling; an export afterwards and Generate in a
new Planner workspace once the Planner release is live.

Files: `SharedProjects/Utilities/Planner/PlannerCleanUp.cs` (the plan, Max-free), `PlannerCleanUpList.cs` (with the
layer list), `PlannerCleanUpPlan.cs` (questions, rows, results, the CSV), `PlannerCleanUpScript.cs` (the batch and the
reading of its report), `PlannerLayerPlanner.cs` (matching rows to the scene), `PhasingNameFixer.cs` (the text fixes it
reuses), `PhasingWordFixText.cs` (the stored word fixes); `Max2Babylon/Forms/PlannerCleanUpForm.cs`,
`PhasingWordFixForm.cs`, `PlannerScheduleTab.cs` (the button and Apply), `Max2Babylon/Tools/PlannerScheduleScene.cs`
(runs the batch, saves the copy), `PhasingScene.cs` (the word fixes), `MaxScriptManager.CleanUpSceneGraph`.

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

The Update button waits while an export runs, from the window or from MAXScript (`MaxScriptManager.Export`). So do
the Schedule tab's changes to the scene (Clean up, Link, Add layer, Add to IN / RM): they say to try again once the
export has finished. A **development build**
(any build not made by the CD workflow) says "Development build", never checks by itself, and its "Check for
updates" names the latest release and offers Update.

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
exporter window cannot be used while 3ds Max's prompts are open; a change made after the save prompt (Clean up
scene graph, for example) gets 3ds Max's own prompt on closing; Cancel in 3ds Max's prompt on closing, then Cancel in the
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

## 5. Planner: the Schedule tab and the layer list

The Planner owns the layer names, the hierarchy and every date. It exports them as a layer list (its **Layer list for
3ds Max (CSV)** export). The Schedule tab imports the list, shows it as a tree beside the scene, and Clean up scene graph
builds the scene's layers and helpers to match (section 3.2). That hierarchy is what the GLB carries and what the
Planner's Generate from model reads.

### 5.1 The window

**The name "Planner".** The menu is **Planner** (it was "Babylon"), its item **Planner…** opens the window, the quad
menu is **Planner…** with "Planner Properties", "Planner Animation Groups" and so on, and the window is titled "Planner -
v1.0-urbancgi - release v…". On 3ds Max 2022 to 2024 the old "Babylon" menu is taken off the menu bar and the quad on
start-up (menus are found by title there); on 2025 and later the menu is the same menu as before (the same GUID in
`CreateBabylonMenus.ms`) with its new title. Everything saved scenes and scripts rely on is unchanged: the Max2Babylon
DLL and namespace, the `babylonjs_*` properties, the MAXScript entry points (`MaxScriptManager.*`), the action table and
action ids, and the actions' category ("Babylon" in Customize User Interface). The installer is unchanged.

**Two tabs.** **Exporter** holds everything the window held before: the banner, the version and update block, the
Options / Advanced / Log tabs, the buttons and the progress bar. **Schedule** holds the tools below. The tabs are built
when the window opens (the designer file is not edited): the window grows by the height of the tab strip, and the
existing controls move into the Exporter page with the same positions and anchors, so nothing shifts. An export switches
to the Exporter page's Log tab.

**The Schedule tab**, top to bottom:

- the buttons: **Import layer list…**, **Read the scene**, **Clean up scene graph…**, **Link objects to their
  helpers…**, **Fill IN / RM from names** and **Add layer…**;
- the list's name and date, what the scene needs, and the notes (as many lines as there is room for; every note in the
  tooltip);
- the tree of layers, with the Gantt beside it (section 7), and the side list of the chosen row's IN and RM with **Add
  to IN…**, **Add to RM…** and **Remove from list…** (its heading, the row's name and then its counts or what to do
  first, wraps to the side list's width);
- two lists below the tree: **Not in the Planner's list** and **Not scheduled**;
- the date scrubber, with **Planner project start** at its right (section 6.3).

The last imported list, the layers added in Max and the Planner project start are kept with the scene and shown again
when the window opens or the scene is opened.

**Choosing a row.** A row is chosen only by the modeller: a click on it, an arrow key, or a day clicked in the Gantt. The
tree and the lists below it choose nothing by themselves, when the tab opens, after any reading of the scene (Read the
scene again, Fill, Clean up, the edits, Edit > Undo and Redo), or when one of them is clicked on its header or takes the
focus; showing **Not scheduled** for the first time selects nothing in 3ds Max. A reading keeps the row chosen, in the
list it was chosen in, while that row is still there; when it is gone, no row is chosen. Ctrl+click the chosen row to
choose none. Folding away the group of the chosen row chooses that group instead; folding away a row whose choice a
Ctrl+click took away chooses nothing.

### 5.2 The layer list (CSV)

One row per layer. UTF-8 with its byte order mark, comma separated, quotes as in RFC 4180, CRLF. The header is
`Planner ID,Parent ID,Layer,Kind,ID,Phase,Stage,Sub-stage,Name,Start,Finish,TBC,Order,Project start`. The first four
are needed, in any order and any capitals; other columns are read when they are there, and columns the tool does not
know are ignored, with a note.

| Column | Meaning |
| --- | --- |
| Planner ID | the Planner folder's ID; unique; how the tool finds the row's layer again (the helper's `planner_id`) |
| Parent ID | the parent row's Planner ID; empty for a top-level row, which goes directly under the phasing root |
| Layer | the name of the Max layer and its helper: `<ID>_<Name>` for a layer (`02_01_4_Construction_of_Phase1_TM`), `_<n>_<Name>` for a phase group |
| Kind | `Group` (holds other layers) or `Layer` (holds work) |
| ID, Phase, Stage, Sub-stage | the layer ID (`02_01_2`, 2, 1, 2), empty for a group; the ID also finds a layer built by hand |
| Name | the name after the ID |
| Start, Finish | the layer's first and last day in the Planner: the span of its own work. The preview and the Gantt use them. |
| TBC | `Yes` when the Planner has not settled the layer's dates: a layer it has not dated yet (still on its placeholder day at the start of the schedule), or one dated but not confirmed |
| Order | the row's place among its siblings in the Planner; read but not used, as the tool sorts the rows itself (D24) |
| Project start | the Planner workspace's project start, the same on every row |

**Dates.** One reader serves the preview, the Gantt and the project start. ISO dates (`2026-09-01`, as the Planner
writes them) are read first. Otherwise the whole Start, Finish and Project start columns are read day first (as Excel
saves them in Australia or Britain: `31/08/2026`, `1.9.26`), but only when some value's first number is above 12 and no
value's second number is, so day and month cannot be mixed up. Anything else leaves every row undated, with one note.

**A list exported before the new names** has dates or TBC in its Layer cells. The tool reads it without them, with a
note: "N layer names in the list carry dates or TBC (a list exported before the new names): they are read without them,
as layers and helpers carry no dates (the dates stay in the list's Start, Finish and TBC)." The tree, the statuses and
Clean up all use the names without dates. A list with no Project start column leaves the Planner project start as it is.

**Files saved by Excel** read too: without the byte order mark, LF line ends, a last empty line, semicolons or tabs
instead of commas, and Excel's "CSV (Comma delimited)" (Windows-1252; the tab says so). A file without the four needed
columns is not imported.

**Checks.** A missing or repeated Planner ID, a Parent ID not in the list, parents that go round in a circle, an empty
Layer, a Layer with a line break or another control character, a Kind other than Group or Layer, a layer name another
row already has (any capitals, as 3ds Max layer names must differ), and a layer ID (two or three numbers) another row's
name already carries (no two layers share an ID, D2: the row earlier in the file keeps it, the preview dates the layer by
that row too, and the Planner's export only warns of it) make the row a **Problem**, and so does every row under it. An ID that does not read as a layer ID, or that is not the ID the name carries, is not used to find
layers (a note on the row says so).

### 5.3 The tree

**Import layer list…** reads a list and keeps it with the scene (root node app data). The tree shows it with each level
indented and groups in bold. The tool sorts the rows itself (D24: the list keeps no particular folder order): siblings
come in the order the Planner sorts names in (numbers as numbers, so `02_10` after `02_02`), then in file order, whatever
the list's Order column says. Layers added in Max sit among their parent's rows in the same order, never in the order
they were added. With no list imported, the tree shows the scene's own phasing tree (the layers in the root, nested as
the Max layers nest, in the same order).

The columns are Layer, Kind, Start, Finish, TBC, IN, RM and **Status in the scene**. Beside the Gantt, Start, Finish and
TBC are hidden (section 7). The status says what Clean up scene graph would do to bring the row in line with the list:

| Status | Colour |
| --- | --- |
| New | green |
| Rename from `<old name>`, Move under `<parent>` (also "from outside the phasing root"), Layer added, Helper added, Helper put on its layer | yellow |
| Up to date | none |
| Problem: `<why>` | red |
| Added in Max — not yet in the Planner | blue |

The status tooltip says how the row found its layer: by its Planner ID, its layer ID, its name or its phase number. The
statuses are worked out when the tab shows and on **Read the scene**. After Clean up with the list, every row is
Up to date.

**Not in the Planner's list** (below the tree) lists the scene's phasing layers and helpers that no row matches: a layer
removed in the Planner, or never in it, in the order the Planner sorts names in. They are left as they are, never
deleted. One built from an earlier list says so, with its Planner ID. Layers added in Max that the list does not have yet
show in the tree instead: the line above the list says how many ("Every phasing layer in the scene is in the Planner's
list, apart from 1 layer added in Max, which the tree shows."), so it agrees with the summary's count.

### 5.4 TBC rows

A row whose dates the Planner has not settled is highlighted in its Layer cell: the TBC colour, with an orange stripe at
its left edge that still shows when the row is chosen. That is:

- a row the imported list marks TBC: a layer the Planner has not dated yet, which sits on its placeholder day at the
  start of the schedule, and a layer dated but not confirmed;
- once filled, a row whose layer the preview marks TBC;
- a layer of the scene the list does not have: one added in 3ds Max, or one the list leaves out. These are highlighted in
  **Not in the Planner's list** too;
- with no list imported, every layer of the scene.

Groups are highlighted only when the list marks them TBC. The Layer cell's tooltip says why: "TBC: the Planner has not
settled this layer's dates yet.", "TBC: this layer is not in the Planner's list yet, so the Planner has no dates for
it.", or "TBC: no layer list is imported, so the Planner's dates for this layer are not known here." The TBC column says
"Yes" (it shows when the Gantt is hidden), and the Gantt's hover gives the same sentence. The layer list's format does
not change: TBC is all it needs.

### 5.5 Link objects to their helpers

**Link objects to their helpers…** links every object on a phasing layer (a layer in the root layer's tree with its
helper) under that layer's helper, keeping its place in the world. What is linked is the object's top-most ancestor
that is not a phasing helper (the object itself when it has none), so a hierarchy moves whole, and a 3ds Max group by
its group head. Helpers, and splines the export leaves out (RailClone paths), are not objects; a spline with an object
linked under it is. Listed as Problems and not moved: an object whose top ancestor sits on another layer, and the
objects of a phasing layer without its helper (Clean up adds the helper when the Planner's list has the layer). Objects
on the root layer itself are left as they are (a note says how many). A review list, then Apply with Hold and one undo
record ("Link objects to their helpers"); the report gives the counts. Clean up runs the same Link as its step 2.

A Max group (by its group head), a Point or another helper, or a spline with objects under it, once linked under a
layer's helper, is exported as an empty node, which the export log counts as a helper node without a layer of its name.
The Link review says so beforehand; it does not affect the export or the Planner.

### 5.6 Scenes on the old names

The tab works on the new names only. A scene that reads as the old names (section 2.3) needs **Clean up scene graph…**
first. No message above the tree says so: Fill and Add layer are greyed out, and the Gantt, the scrubber and the side
list say why (below).

The tab reads the scene again after Edit > Undo and Redo in 3ds Max (a clean-up taken back is on the old names again),
and Fill and Add layer read it first too, so a scene put back on the old names waits again. A tab that was filled
fills again by itself when the scene reads as the new names again (Edit > Redo of the clean-up).

Until the scene reads as the new names:

- Fill IN / RM and Add layer are greyed out (if the tab has not yet noticed the scene went back to the old names, pressing
  them reads it and says "This scene uses the old Ph/St names: run Clean up scene graph first.");
- the scrubber, the preview and Select changing this day are off;
- the Gantt shows a message instead of the chart;
- the side list says IN and RM wait for Clean up, and Add to IN / RM and Remove are off;
- Import layer list, Read the scene, Clean up and Link work as usual.

On a live project that is waiting for the go-ahead, keep exporting the scene as it is: its export runs the old rules
(section 2.5).

**Checking by hand in 3ds Max** (none of this has been run in 3ds Max yet): the Planner menu on 2022/2024 (no "Babylon"
menu left after an upgrade) and on 2025/2026; the window's two tabs at the usual and a high display scaling (nothing
shifted or overlapping, resizing and the minimum size); importing the HS2 list into an empty scene and running Clean up,
then Edit > Undo and Edit > Fetch; importing it into the HS2 model; renaming, moving and changing an ID in the Planner,
exporting the list again and cleaning up; Link objects to their helpers on the HS2 model; the list shown again after
saving and reopening the scene; an export afterwards (the counts pair up, but for any groups, Points or splines Link linked).

Files: `SharedProjects/Utilities/Planner/PlannerLayerList.cs` (reading and checking the CSV, the date reader, the stored
form), `PlannerLayerScene.cs` (the scene model: root, layer and helper units), `PlannerLayerPlanner.cs` (matching,
statuses), `PlannerObjectLinks.cs` (the link plan and its batch), `PlannerScheduleRows.cs` (the tab's rows, TBC rows);
`Max2Babylon/Tools/PlannerScheduleScene.cs` (reads the scene, keeps the list, runs the batches),
`Max2Babylon/Forms/PlannerScheduleTab.cs`, `PlannerReviewForm.cs`, the outer tabs in `Max2Babylon/Forms/ExporterForm.cs`;
the rename in `GlobalUtility.cs`, `Scripts/CreateBabylonMenus.ms` and the action items.

## 6. Planner: IN and RM, the scrubber and Add layer (Schedule tab)

Each layer row shows its **IN** and **RM**: the objects its work installs and removes. They are stored nowhere. They are
read from the objects' names, the tags Generate from model reads, so the lists and the names always agree, and changing
a list renames objects.

### 6.1 Fill IN / RM from names

**Fill IN / RM from names** previews what the Planner's first Generate makes of the scene on the new names. It is a port
of the Planner's code, checked against that code in three time zones (see Tests).

- **The model is what the export writes.** A node hidden while Export hidden objects is off, or with Do not export
  ticked, is left out, unless something exported is under it. A second node of a name reaches the Planner as `X (1)`, an
  object of its own; the side list says what it is called in 3ds Max.
- **Layers by ID.** Each layer node is the folder of its ID. An empty layer gets a folder too. A reference to an ID only
  the list has files its work in that row's folder.
- **Where each event goes.** An object's leading event and a plain trailing event go in its own layer. An event with a
  layer reference goes in the layer it names.
- **The order inside a layer.** First the layer's own objects by their leading tag: removals, then installs, each by own
  name in the Planner's natural order. Then other layers' objects whose reference names this layer, by the ID of the
  layer they sit in, then by own name. Last, the layer's own end-of-layer events (`X_RM`, `IN_X_RM`).
- **Times.** The first two groups split the layer's dates evenly, chained with no gaps. End-of-layer events take no slot
  of their own: they run together over the last slot, so every end-of-layer removal ends when the layer ends. An object
  that holds the last slot and also has an end-of-layer event splits that slot: its leading event takes the first half.
- **Dates.** A layer's dates are its list row's Start and Finish. A layer with no dates (an undated row, a layer added in
  Max, or every layer while no list is imported) is parked on a day of its own, TBC, one day each in layer order from the
  project start.
- **The project start** is the Planner workspace's. It never moves on the new names. **Planner project start** (by the
  scrubber, kept with the scene) holds it; importing a list with a Project start column sets it; unticked, the preview
  starts today.
- **A reference to a layer nobody has** (neither the scene nor the list): a removal is parked TBC the day after the last
  dated work (with no dated work, the day after the last parked day); an install sits on the project start, TBC. Layer
  `99_99` is work whose date is not known yet.

A layer's **IN** and **RM** are everything filed in its folder: its own events, other layers' events that name it, and
its end-of-layer events. The side list shows them, with a note when an object sits in another layer, is at the end of
the layer, is named for this layer by its tag, names a layer nobody has, or is TBC. Choosing objects there selects them in
3ds Max.

**Not scheduled** (below the tree) lists the objects the Planner leaves out, with why: an old Ph/St tag (Clean up
converts it); no IN or RM tag (so it shows for the whole programme); not linked under a layer's helper (with the layer
it sits on, when Link would put it there); under a node led by a number that is not a layer; not exported; another node
reaches the Planner with the same name; no name. Objects the Planner reads only in part are listed too: a tag it does
not read, or a reference to a layer nobody has. Choosing one selects it.

The summary under the tree says how many objects were scheduled in how many layers, how many layers the list dated and
how many were parked, and where the project start came from. It is a preview of the Planner's first Generate: once
planners edit timings in the Planner, the Planner's schedule differs.

### 6.2 Add to IN…, Add to RM…, Remove from list…

The buttons under the side list rename objects, after a review. **Add to IN…** and **Add to RM…** take the objects
selected in 3ds Max; **Remove from list…** takes the objects chosen in the side list. The buttons are on for any layer
row once filled, a row only the Planner's list has included, and off on a scene with the old names. Only names change:
nothing is moved or linked.

**Add** gives the selected objects the chosen layer's install (IN) or removal (RM). The object's tag of that type comes
out first, wherever it is, so an object keeps one install and one removal. Then:

- **for the object's own layer**: at the start (`IN_X`) when that place is free, else at the end (`X_IN`: at the end of
  the layer), else it is refused;
- **for another layer**: at the end with the layer's ID (`X_IN_06_05_2`), when that place is free. Otherwise it is
  refused: an object has one leading and one trailing tag.

| Object now | Add IN for its own layer | Add IN for layer M | Add RM for its own layer | Add RM for layer M |
| --- | --- | --- | --- | --- |
| `X` | `IN_X` | `X_IN_M` | `RM_X` | `X_RM_M` |
| `IN_X` | unchanged | `X_IN_M` | `IN_X_RM` | `IN_X_RM_M` |
| `RM_X` | `RM_X_IN` | `RM_X_IN_M` | unchanged | `X_RM_M` |
| `IN_X_RM` | unchanged | refused | unchanged | `IN_X_RM_M` |

An object whose tag already puts it in that layer keeps its name (`X_IN` given Add to IN for its own layer stays
`X_IN`). A tag at the end that names the object's own layer becomes the plain `_IN` or `_RM` it means.

**Remove from list** takes the chosen objects' tag of that column out, where it is. The other tag stays where it is, as
a tag's place carries its meaning: `IN_X_RM` with its IN removed becomes `X_RM`. An object left with no tag is no longer
scheduled, and the review says so.

**Rules.**

- The own name, and any note after a space, stay as written. A tag at the end goes before the note.
- Every new name is read back as the Planner reads it. A name with the same type at both ends, another own name, or a new
  naming error is refused.
- Refused too: a name that still carries an old Ph/St tag (Clean up converts it first); a layer's helper; the phasing
  root; a split piece chosen alone (choose its object); an object under no layer's helper (link it first); a new name
  another node has; removing from a column the object is not in.
- A closed group selected whole is tagged on its head. Its untagged members are left out, with a note, as the Planner
  shows and hides them with it. A member with a tag of its own inside a closed group is a Problem (open the group to
  choose it).
- Split pieces (`Name_2` under `Name`) are renamed with their object, so the Planner still reads them as part of it.

**Checked before Apply.** Each edit is checked by filling the preview again with the new names. An object that would not
then be in the column asked for is a Problem and keeps its name, with why. Objects the renames make the Planner read
differently without being renamed are listed as **Also**. The review notes an object whose own name another object
shares (the Planner cannot follow a tag change on it), naming warnings the new names bring, two events that would run
the other way round from the name, and a layer the preview parks.

A review list, then **Apply**: Hold the scene first (on by default), one MAXScript batch in one undo record ("Planner IN
/ RM"), and the lists filled again from the names, which give back exactly the edited lists.

### 6.3 The date scrubber

**The scrubber** (at the bottom of the tab, once filled) is a day slider from the project start to the day the last
activity ends. The days after the works end are the **TBC zone** (shaded), where removals for a layer nobody has are
parked. It shows the date, the layers at work that day and **Select changing this day (n)**: the objects installed or
removed that day (installs that start on it, removals whose last minute is on it). While the scrubber is off the button
shows no count.

**Preview in the viewport** hides what the Planner's 4D view hides at that date: at the start of the day, as the 4D view
shows a day, or, with **End of the day**, once the day's work is done. Objects not yet installed and objects already
removed are hidden, and so is everything linked under them. An object hidden by its own flag (or its layer) is never
touched. The changes are made in one go with the undo hold suspended, so the preview is never an undo step, then one
redraw; a scene that needed no saving before a step needs none after it. Nothing is renamed, moved or linked. While the
preview is on, its button is filled and edged in blue.

The preview ends, and every object gets its own hidden state back, on its button, on leaving the Schedule tab, on
closing the window, before a new scene, a reset or opening a file, and before every export (from the window or from
MAXScript), as the export leaves hidden objects out of the GLB. While an export runs, the preview does not start, and
choosing in the lists does not change the selection. Every save shows the hidden objects first and hides them again
after, so the file keeps everyone's own state. Read the scene, Fill and the edits read the objects the preview hid
as they were before it.

Nothing in the scrubber's row changes size as it moves, so the slider stays under the pointer. At the window's smallest
size the lists below the tree give way first (down to their tabs), then the notes above it (down to one line counting
them, every note in the tooltip), so the tree keeps three rows and the side list two. They get their height back when the window grows.

**Days and times as the Planner reads them.** The tab's days are local midnights, as the Planner's day steps are. An
activity starts at its time on the wall clock, also on the day the clocks change (a time in the hour the clocks repeat is
its first occurrence, as the Planner reads it), so work chained across a change neither gaps nor overlaps.

### 6.4 Add layer…

**Add layer…** adds a layer or a phase group, with its helper, under the phasing root (made as Clean up makes it when
the scene has none) or a phase group (`_<n>_<name>`) with its Max layer and helper. The Planner files every layer in the
phase group above it, so nothing goes under a layer or a group that is not a phase group. A phase group may go in
another phase group: the Planner reads it as one.

The dialog asks for:

- **Under**: the phasing root, or one of the scene's phase groups that can take a layer, in the tree's order (nested ones
  indented). It starts on the chosen row: a phase group's row starts on that group; a layer's row, or a group that is
  not a phase group, starts on the phase group above it (or the phasing root), with a grey note saying why; with no row
  chosen, the phasing root. A row of the list with no Max layer yet (one Clean up has not built) is refused, with a
  message. The phase starts on, and follows, the phase of the group Under names;
- **Kind**: Layer (holds work) or Phase group (holds layers);
- **ID**: for a layer, its phase (always), its stage and, when ticked, a sub-stage; for a group, its phase;
- **Name**.

It asks for no date. The Planner gives every date: the layer shows TBC until the Planner's list has it, and the preview
parks it. **Will be** shows the name as it will be written, and follows the ID and the name as they change: `<ID>_<Name>`
for a layer (spaces become underscores), `_<pp>_<Name>` for a group.

It is refused when:

- the scene reads as the old names (Add layer reads the scene first, so a clean-up taken back with Edit > Undo is caught);
- the name does not read back as that ID and name. A name led by a number is refused, because it would read as part of
  the ID (`02_05` with "3 Storey Frame" reads as `02_05_3`). So is a name ending in a number in brackets ("Hoarding
  (1)"), which the Planner reads as the copy number 3ds Max gives a copied node, and drops;
- the name holds a date or TBC;
- a Max layer, a node, a row of the list or a layer added before has the name, or a layer of the phasing tree, a row or a
  layer added before has the ID;
- for a group, a phase group of the tree or a row of the list has its phase number.

**Review…** opens the review, then **Apply** builds it with the layer builder's batch, in one undo record ("Add layer"):
the Max layer under its parent's layer and a Dummy of the same name on it, linked under the parent's helper. With the
scene held first (on by default), the report ends "Edit > Fetch puts the scene back as it was before the layer was
added.", the sure way back, as whether Edit > Undo takes back a Max layer has not been checked in 3ds Max yet. The row
shows as **Added in Max — not yet in the Planner** and is kept with the scene (root node app data "PLAD", record
"PlannerAddedLayers 2"; a record of the first version is still read, its dates passed over). A layer's row is
highlighted TBC; a phase group's is not, as groups are highlighted only when the list marks them TBC (section 5.4).

**Reaching the Planner.** A layer added in Max reaches the Planner with the next GLB, even while empty, as long as its
helper is exported, and arrives TBC (Generate from model with Merge). A phase group reaches it once a layer in it does.
The next list from the Planner then has it: the tab stops showing it as added, and the next Clean up writes its Planner
ID. The scene is the truth: an added layer taken back with Edit > Undo, deleted or renamed by hand is no longer an added
layer, and its ID and name are free again. Its record stays with the scene meanwhile, so an Edit > Redo that brings the
layer back shows it as added again; adding a layer of the same name again replaces the record, so it is kept once.
Clean up renames the record along with its layer and its parent's.

**Checking by hand in 3ds Max** (none of this has been run in 3ds Max yet): Fill on a cleaned HS2 or v30 with and without
its list; choosing in the side list and in Not scheduled selects in the viewport; Add to IN, Add to RM and Remove on a
few objects, then Edit > Undo; Add to RM on a closed group clicked in the viewport (one rename, the head's); an object
with split pieces; Add layer under a phase group and under the root, Edit > Undo and Edit > Fetch, save and reopen
(still "Added in Max"), then a list that has it; Edit > Undo and Redo with the Schedule tab open (the tab reads the scene
again; after an undone clean-up Fill and the scrubber wait at once, and after Redo the tab is filled again); Planner project start set, saved and reopened; the scrubber across the
dates with the preview on, dragged, and every object's hidden state back after the button, a tab change, closing the
window, File > New and opening a file; saving during a preview; a MAXScript export during a preview (the GLB has every
object); the preview's speed on the full HS2 scene.

Files: `SharedProjects/Utilities/Planner/PlannerScheduleV2.cs` (the port of the new names' Generate and the 4D view),
`PlannerSchedule.cs` (the scene model, its indexes, the 4D view and the helpers the engine shares with the export check), `PlannerNaturalOrder.cs`
(the Planner's natural order, generated from Node's ICU), `PlannerTagEdit.cs` (tag rewriting, the checked edit plan and
its batch), `PlannerAddLayer.cs` (Add layer and the added layers' store), `PlannerScheduleRows.cs` (the tab's rows),
`PlannerPreview.cs` (the preview's bookkeeping), `PlannerLayerScript.cs` (the batch Add layer uses);
`Max2Babylon/Tools/PlannerPreviewController.cs`, `PlannerScheduleScene.cs` (hidden and Do not export flags, the added
layers, selection, the batches); `Max2Babylon/Forms/PlannerScheduleTab.cs`, `PlannerAddLayerForm.cs`.

## 7. Planner: the Gantt beside the tree (Schedule tab)

A Gantt sits beside the Schedule tab's tree, **row for row with it**: the same rows in the same order, at the same
heights, scrolling and folding with it. Nothing in the Gantt changes a date, a name or the scene.

**What it draws**, as the Planner's Gantt-to-Excel export draws a schedule:

- One column per calendar day (**Days**, the default), or **Weeks** or **Months** for long programmes. The header has
  two rows: months over days, months over weeks starting on Monday, or years over months. Weekend days are shaded. The
  header's days in the scrubber's TBC zone are tinted.
- **Layer rows**: the days the layer's IN work runs in the install colour, and the days its RM work runs in the Gantt's
  red (a row with both: installs above, removals below). The work is the row's IN and RM, and the days are the ones the
  Planner's scheduler gives each activity, from its start date to the day of its last minute. Up to the scrubber's date
  the bars are solid, after it tinted, as the Planner's Gantt fills a bar up to its playhead. **TBC** work is hatched.
- **Group rows**: a grey span over everything inside them, hatched for a TBC group.
- **Before Fill IN / RM**: each layer's own dates from the Planner's list, in a neutral slate, hatched when TBC. A layer
  with no dates in the list has none. Dates never come from a layer's name.
- **Today** is a grey line down the middle of its day. The **scrubber's** date is an orange line with a head on the
  header. Before Fill there is no scrubber, and none is drawn.
- A phasing layer of the scene that the Planner's list does not have, whose work the Planner reads, gets a row after the
  list's, so the Gantt charts every layer the scrubber finds at work.

Colours are the Planner's own (`config/activity.ts`, `ganttExport.ts`, `styles.css`): install `#43B04A` and its tint
`#98D49B`; removal `#E23B3B` and `#EF9393`; header `#E7EBF0`, weekend `#CFD5DE`; group `#D9DEE5`, TBC group hatch
`#9AA5B4`; the scrubber `#F7921D` (the Planner's accent); today `#7F8A99`. The neutral slate before Fill is `#9AA5B4`.

**Hover and click.** Hovering a day says the row, the date (a weekend, today, the TBC zone) and what installs or removes
that day: each activity at work, IN or RM, its name, how many objects, its dates, TBC, and whether its objects show
from its start or are hidden from its finish. On a layer row it also says where the layer's dates come from: "From the
Planner", or none, with the day the preview parks it. On a TBC row it gives the same reason as the tree's tooltip.
**Clicking** a day moves the scrubber there and chooses that row in the tree; no preview starts, and a preview that is
on follows.

**Zoom and scrolling.** **Days**, **Weeks** and **Months** sit at the right of the header's top row (D, W and M when the
Gantt is narrow); Ctrl and the mouse wheel zoom too. The mouse wheel scrolls the rows, Shift and the wheel scroll the
days, the chart can be dragged sideways, and a strip along the header's top edge shows the part in view and moves it.
With the Gantt focused, Left, Right, Home and End scroll. Its right-click menu has the zoom, **Go to the scrubber**,
**Go to today** and **Hide the Gantt**.

**Folding.** A row with rows under it has a triangle before its name: click it, double-click the row, or press Left and
Right on it. The triangle in the Layer header folds every group, or opens them all when none is open. The tree's
right-click menu has **Expand all**, **Collapse all** and **Show the Gantt**. Folds are kept while the window is open.

**Layout.** Beside the Gantt the tree hides its Start, Finish and TBC columns: the Gantt draws each row's dates. Its
Layer column keeps the width it had before the Gantt was there, and the Gantt takes the rest and every width the window
gains. Dragging the splitter sets the tree's width from then on. **Hide the Gantt** gives the tree the whole width, with
every column. Where the window is so short that the lists below the tree give way, the header goes down to one row, so
the tree keeps three rows at the smallest window. In the harness at 100% and the window's default size, the Layer
column is 212 px and the Gantt 184 px, about a week of days: for more days, widen the window, drag the splitter or zoom
out.

**Checking by hand in 3ds Max** (not run in 3ds Max yet): the Gantt beside a cleaned HS2 tree before and after Fill;
folding, scrolling and the rows staying level with the tree; the hover text; a click with the preview off and on; the
zoom; Hide and Show the Gantt; the window at its default size and at its smallest, at display scalings of 100, 125, 150
and 200%.

Files: `SharedProjects/Utilities/Planner/PlannerGantt.cs` (the chart: rows, days, colours, header, tooltip text,
folding, the TBC zone), `PlannerScheduleRows.cs` (`UnlistedAtWork`, the rows for a scene layer not in the list);
`Max2Babylon/Forms/PlannerGanttView.cs` (the owner-drawn control), `PlannerScheduleTab.cs` (the split, the columns
beside the Gantt, the folds, the click to the scrubber).

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

**Naming v2 release order.** This exporter goes out first, then the Planner release that reads the new names. Each
modeller is offered the update and clicks Update before a scene is cleaned. Live projects' scenes are cleaned only
after the Planner release, when the planning team says to go ahead.

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

To confirm the install: the menu says Planner, the window (Planner > Planner…) has the Exporter and Schedule
tabs and shows the release ("Version v…") at the top right of the Exporter tab, the exporter
log shows "Checking Planner naming convention" at the start of an export, and the GLB's generator string ends
in `v1.0-urbancgi`.

## Reading and sharing the log

Every export writes the complete log as `<model>.export-log.txt` next to the exported file (also for
MAXScript exports), so it can be sent along with the GLB. The Log tab of the dialog has a "Copy log to
clipboard" button at its bottom; upstream had placed it off-screen. Errors come first, then warnings,
then notes, and the line starting "Planner naming check" sums them up.

## Tests

```bat
dotnet test SharedProjects/Utilities.Tests/Utilities.Tests.csproj
```

The test project compiles the shared sources directly (as Max2Babylon does) and needs no 3ds Max. It also compiles the
three Max-free update files (`ReleaseInfo.cs`, `GitHubReleaseJson.cs`, `UpdateStateFile.cs`), runs the update command
line through Windows' own argument splitting into the installer's parser, and reads `cd.yml` to check that the release
tag still reaches the one placeholder.

**In the repo.** Unit tests for every rule above, and parity with the Planner's own code:

- `PlannerScheduleScenesV2.json`: 41 synthetic scenes on the new names, with the list's days per layer ID, list-only IDs
  and the project start. `PlannerScheduleScenesV2.utc.json`, `.sydney.json` and `.london.json` are the Planner's results
  for them in UTC, Sydney and London time. `PlannerScheduleScenesV2Edge*.json` are the edge scenes review found, in the
  same three zones. The preview must give every activity (name, folder, TBC, start and finish instants, the objects it
  binds, its event layer, the days the Gantt colours), every folder, the report's lists and the 4D view's hidden objects
  at every instant something changes, as the Planner does. The export check must report what the Planner lists for the
  same scenes. Regenerate these files with the Planner's harness rather than edit them.
- `PlannerScheduleScenes.json`: the old names' synthetic scenes, kept as the naming reader's old-name cases (the old names' engine they were checked on is gone).
- `PlannerNaturalOrderCases.cs` and `PlannerNaturalOrderUnicodeCases.cs`: name pairs and the order JavaScript gives them.
- `Golden/PlannerCleanUpScript.ms` and `Golden/PlannerLayerScript.ms`: Clean up's and Add layer's MAXScript after their
  arrays, word for word (section 3.7), as the other tests run the batches' data, not MAXScript.

**Kept out of the repo.** Some tests read real models, which are too large for the repo. They find their inputs through
environment variables:

| Variable | What it is |
| --- | --- |
| `PLANNER_NAMING_V2_CASES` | the Planner's answers for about 29,000 names and 11,000 groups: every answer of the naming reader must be the Planner's |
| `PLANNER_NAMING_V2_CHECK` | real models and what the Planner lists for them: the export check on the new names. `PLANNER_NAMING_V2_CHECK_REPORT` (optional) is where the reports are written |
| `PLANNER_SCHEDULE_V2_REAL` | v30 and HS2 v3_06u converted to the new names |
| `PLANNER_SCHEDULE_V2_REAL_PLANNER` | the Planner's results for them in one time zone: the preview must match |
| `PLANNER_CLEANUP_V30_TREE` | the node tree of "EUS_HmRd_HRB_TR_Construction_v30 2.glb" |
| `PLANNER_CLEANUP_V30_CSV` | that model converted to the new names, every rename: Clean up must give it exactly, with the schedule's 14 answers |
| `PLANNER_CLEANUP_HS2` | HS2 v3_06u with its real Max layers: Clean up against the dry run's counts |
| `PLANNER_CLEANUP_V30_LIST`, `PLANNER_CLEANUP_HS2_LIST` | the Planner's layer lists for those models on the new names: Clean up with the list must name every row exactly as the list does |
| `PLANNER_LAYERS_CSV` | the HS2 layer list exported before the new names, with dates: Clean up must read it without them. The layer builder's acceptance on HS2 reads it too; `PLANNER_LAYERS_REPORT` (optional) is where that writes what it found |
| `PLANNER_CLEANUP_OUT_CSV` | optional: where to write Clean up's review of v30 2, to read by eye |
| `PLANNER_SCHEDULE_HS2` | the old-name HS2 scene: the naming reader must read it as the old names. (`PLANNER_SCHEDULE_HS2_PLANNER`, the Planner's old-name results for it, is no longer read: the old names' engine is gone) |
| `PLANNER_FIX_V305`, `PLANNER_FIX_V306`, `PLANNER_FIX_REF_CSV`, `PLANNER_FIX_OUT_CSV` | the old phasing name fixer's acceptance on HS2, kept because the old-name export check still reads the scene through it |
| `PLANNER_NODES_FIXTURE` | a real exported hierarchy: prints the old names' naming report |
| `PLANNER_REAL_TGA` | a mislabelled texture: proves the bytes round-trip to a decodable PNG |

A test that reads the new names' inputs is reported as **skipped**, not passed, when its variables are not set, and
fails when a variable names a file that is not there. The older ones (`PLANNER_FIX_*`, `PLANNER_SCHEDULE_HS2`,
`PLANNER_NODES_FIXTURE`, `PLANNER_REAL_TGA`) pass quietly without their inputs.

**The full run.** The inputs are kept in the naming v2 parity kit (`NamingV2_parity_inputs_2026-10-18`, about 60 MB,
outside the repo). Its `tests.sh <log folder> [exporter repo]` sets every variable and runs the suite once per time
zone's Planner results (UTC, Sydney, London). Each zone must report every test passed and none skipped. Each zone's line
says PASSED or FAILED, and the runner exits 1 unless every zone passed; a zone where no test ran fails (VSTest finds no
test, and exits 0, when its adapter cannot load, for example from a copy of the repo whose path is over 260 characters).
Its `README.txt` says where every input came from and how to make it again. Add new large inputs to the kit, not to the
repo.
