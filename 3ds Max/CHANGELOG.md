# Changelog

## v1.3.2
**Implemented changes:**
- Added changelog

**Fixed bugs:**
- Fixed a bug in exporting KHR_lights_punctual to glTF where ambient was still being indexed, causing the resulting glTF to cause errors in loaders (https://github.com/BabylonJS/Exporters/issues/340)

## v1.3.3
**Implemented changes:**
- Added checkbox to toggle KHR_lights_punctual in exporter
- Remove copyright from glTF files

## v1.3.4
**Fixed bugs:**
- Fixed a bug in exporting occlusion textures without enabling the mergeAO flag

## v1.3.5
**Implemented changes:**
- KHR_texture_transform is now disabled by default but can be enabled with a checkbox in the exporter interface
- KHR_texture_transform is now implemented as a used and required extension in the glTF file

## v1.3.6
**Implemented changes:**
- KHR_materials_unlit can now be toggled from the exporter iterface
- Alpha Test can now be enabled by setting "babylonAlphaTest" as a boolean custom attribute on a material

## v1.3.7
**Implemented changes:**
- Added option to not export materials (https://github.com/BabylonJS/Exporters/issues/362)

## v1.3.8
**Fixed bugs:**
- Fixed normals for models that contain offset transforms (https://github.com/BabylonJS/Exporters/issues/360)

## v1.3.9
**Fixed bugs:**
- Fixed bug where setting the base color texture overwrites the base color factor (https://github.com/BabylonJS/Exporters/issues/367)

## v1.3.10
**Fixed bugs:**
- Fixed bug where export would fail if the number of max material param blocks is 0 (https://github.com/BabylonJS/Exporters/issues/392)

## v1.3.11
**Fixed bugs:**
- Fixed bug where binary babylon files were not copied to the output directory (https://github.com/BabylonJS/Exporters/issues/397)

## v1.3.12
**Fixed bugs:**
- Fixed bug where texture names are changed when exporting from the exporter(https://github.com/BabylonJS/Exporters/issues/379)

## v1.3.13
**Fixed bugs:**
- Fixed bug where babylonbinarymeshdata file was not getting copied to the output directory(https://github.com/BabylonJS/Exporters/issues/401)

## v1.3.14
**Fixed bugs:**
- Fixed bug where texture transform offset and scale is
not exported correctly using glTF
(https://github.com/BabylonJS/Exporters/issues/383)

## v1.3.15
**Fixed bugs:**
- Fixed bug where morph target normals were not exported unless the property menu was opened
(https://github.com/BabylonJS/Exporters/issues/382)

## v1.3.16
**Fixed bugs:**
- Fixed issue where the scale factor was treated as 1 / scale factor in the exported glTF
(https://github.com/BabylonJS/Exporters/issues/394)

## v1.3.17
**Fixed bugs:**
- Fixed issue where textures with different texture transforms applied were not exported properly with glTF
(https://github.com/BabylonJS/Exporters/issues/409)

## v1.3.18
**Fixed bugs:**
- Fixed issue where a texture connected to multiple material inputs would be cloned on export instead of reused
(https://github.com/BabylonJS/Exporters/issues/386)

## v1.3.19
**Fixed bugs:**
- Fixed issue where standard materials with different texture transforms were not exported to glTF
(https://github.com/BabylonJS/Exporters/issues/409)

## v1.3.20
**Fixed bugs:**
- Allow ambient occlusion texture coordinates and texture transforms to be respected when merged with an ORM texture
(https://github.com/BabylonJS/Exporters/issues/385)

## v1.3.21
**Fixed bugs:**
- fixed material-slot allocation for Arnold materials to better withstand changes to slot IDs (due to feature changes on arnold material)
- optimizedmaterial-alphasettings-export by adding .png to valid extensions when exporting opaque materials
(https://github.com/BabylonJS/Exporters/pull/416)

## v1.3.22
**Fixed bugs:**
- modify texture name if it is not a valid uri name
(https://github.com/BabylonJS/Exporters/issues/419)

## v1.3.23
**Changes:**
- preserve texture name if it is not a valid uri name, but modify within glTF file
(https://github.com/BabylonJS/Exporters/issues/419)

## v1.3.24
**Changes:**
- Add version number in exporter window
(https://github.com/BabylonJS/Exporters/issues/424)

## v1.3.25
**Fixed Bugs:**
- Fixed bug in GLTFImage uri which caused glb files to not export
(https://github.com/BabylonJS/Exporters/issues/423)

## v1.3.26
**Fixed Bugs:**
- Fixed bug in GLTFImage uri which caused glb files with textures containing spaces to not export
(https://github.com/BabylonJS/Exporters/issues/429)

## v1.3.27
**Fixed Bugs:**
- Fixed issue where shell material may fail to export
(https://github.com/BabylonJS/Exporters/issues/413)

## v1.3.28
**Implemented Changes:**
- Joint indices for glTF are now set to 0 instead of joint count

## v1.3.29
**Fixed Bugs:**
- Change "tag" to "tags" in babylon file and glTF extras
(https://github.com/BabylonJS/Exporters/issues/436)

## v1.3.30
**Implemented Changes:**
- Added @Selmar 's multi file export
(https://github.com/BabylonJS/Exporters/issues/436)

## v1.3.31
**Fixed Bugs:**
- Disable exporting a multimaterial connect to another multimaterial
(https://github.com/BabylonJS/Exporters/issues/384)

## v1.3.32
**Fixed Bugs:**
- Fixed bug causing DirectX material to fail the exporter
(https://github.com/BabylonJS/Exporters/issues/405)

## v1.3.33
**Fixed Bugs:**
- Fixed bug where KHR_texture_transform warning is always displayed
(https://github.com/BabylonJS/Exporters/issues/460)

## v1.3.34
**Implemented Changes**
- Added @MackeyK24 's support for BabylonMesh LOD
(https://github.com/BabylonJS/Exporters/pull/479)

## v1.3.35
**Implemented Changes**
- Added @elpie89 's implementation for different unit scale systems
(https://github.com/BabylonJS/Exporters/pull/481)

- Added @MackeyK24 's Support for Bablyon Lightmap mode parsing
(https://github.com/BabylonJS/Exporters/pull/486)

- Added @MackeyK24 's Support for Babylon Link Transform Nodes
(https://github.com/BabylonJS/Exporters/pull/488)

- Added @Selmar 's support for custom material exporters 
(https://github.com/BabylonJS/Exporters/pull/493)

- Added @elpie89 's support for animation group exporting
(https://github.com/BabylonJS/Exporters/pull/492)


**Fixed Bugs:**
- Fixed bug where high standard shader specular power results in a white material base color
(https://github.com/BabylonJS/Exporters/issues/466)

- Added @Selmar 's fix where the export hierarchy is incorrect when exporting skinning nodes
(https://github.com/BabylonJS/Exporters/issues/489)

- Fixed bug where babylon material is exported with improper alpha blending mode when texture lacks alpha channel
(https://github.com/BabylonJS/Exporters/issues/470)

## v1.3.36
**Fixed Bugs**
- Fixed build break caused by shared components using 3dsMax implementations
(https://github.com/BabylonJS/Exporters/pull/498)

## v1.4.0
### (2019-05-17 12:00:00)
**Fixed Bugs**
- Added @elpie89 's fix for null references when exporting Physical Material with no Texture
(https://github.com/BabylonJS/Exporters/issues/509)

## v1.4.1
### (2019-06-11 14:15:45)
**Implemented Changes**
- 3ds Max exporter batching improvements, add parameter saving, decouple export entrypoint from export button click callback. (https://github.com/BabylonJS/Exporters/pull/530)

- Texture folder explicit (https://github.com/BabylonJS/Exporters/pull/532)

- Get animation group by node (https://github.com/BabylonJS/Exporters/pull/516)

- Relative file path (https://github.com/BabylonJS/Exporters/pull/515)

- AnimationGroup multiselection, merge, clean features (https://github.com/BabylonJS/Exporters/pull/507)

- Implement 3DS Max double-sided materials (https://github.com/BabylonJS/Exporters/pull/514)

**Fixed Bugs**
- fix merge, clean button after last merge issues (https://github.com/BabylonJS/Exporters/pull/528)

- Fix merge regression, Re-Add animation group selection (https://github.com/BabylonJS/Exporters/pull/527)

- VS 2015 build fixes for 3DS Max Exporter (https://github.com/BabylonJS/Exporters/pull/513)

- Fix #533 : Base Color Texture is half darker than the original (https://github.com/BabylonJS/Exporters/commit/7df0e691cbd099bd56514c233d63e8dabd46c32a)

## v1.4.2
### (2019-06-11 15:40:44)
**Fixed Bugs**
- Fix _getSpecialTexmap null reference exception (https://github.com/BabylonJS/Exporters/pull/539)

- Fix #472 : add NormalBumpTex support (https://github.com/BabylonJS/Exporters/pull/538)

## v1.0-urbancgi (UrbanCGI fork)
### (2026-09-09)
**Implemented changes**
- Textures are exported by content, not by file extension: a TGA (or DDS, BMP, TIFF, GIF) hiding behind a .png/.jpg name is decoded and re-encoded instead of being embedded verbatim, a JPEG behind a .png name is declared image/jpeg, and every mismatch is logged with a summary at the end of the export. See PLANNER.md.
- Planner naming check: a pre-flight pass over the nodes about to be exported reports objects and groups that the UrbanCGI Planner cannot read or would schedule differently (malformed Ph/St tags, unfiled objects, undated groups, stage mismatches, duplicates, case-only twin groups). Two new options: "Check Planner naming" (on) and "Stop export on naming errors" (off).
- The generator string now ends in v1.0-urbancgi so fork exports are recognisable.
- Unit tests for the shared, Max-free code in SharedProjects/Utilities.Tests.
- Every export writes its log as `<model>.export-log.txt` next to the exported file (dialog and MAXScript exports alike); the Log tab's copy button, which upstream had parked off-screen, is docked at the bottom of the tab and copies the whole tree.
- The naming check no longer reports stray underscores at the edge of a description (the Planner trims them).
- The naming check flags any space in a tagged object's name or a dated group's name (2026-09-14).
- The naming check reads the optional sub-stage in object tags (`St<nn>.<m>`, e.g. `Ph2_St05.2_IN_...`) and sub-numbered groups written with a dash or a dot (`05-1_...`, `05.1_...`; the dotted form needs the underscore, so `2.4 High Hoarding` is still an ordinary group) the way the Planner does: a sub-numbered group owns its sub-stage and plain `St<nn>` objects, a plain group number covers every sub-stage of its stage. Digits are ASCII only, as in the Planner (2026-09-29).
- Installer (v1.8.0): pulls packages from the fork's GitHub Releases instead of upstream's, installs a `Max_<year>.zip` placed next to the program without going online, downloads to the temp folder, stamps installed files with the install time so "up to date" is reported correctly, and no longer crashes when the repository has no release yet.

**Fixed bugs**
- .jpg sources with texture operations, and .jpeg sources in general, fell through the format switches (unsupported-format warning / image/ mime type); both are now normalised to jpg.

## v20261009.5 to v20261015.9 (UrbanCGI fork)
### (2026-10-09 to 2026-10-15)
**Implemented changes**
- Fix phasing names: a "Fix phasing names..." button on the exporter window (Options tab, next to the Planner naming options) lists how the phasing layer and object names in the scene would be corrected, and applies the list in one go after a confirmation: phase and stage IDs written the standard way, split numbers in start-date order, object tags and removal tags following their layers, spaces replaced, the project's spelling fixes, a layer moved into its phase group, and the 3ds Max layers of the same names renamed and re-parented with them. Dates and TBC are never changed; anything that does not read is left as it is and listed to check, as is a corrected name another node already has. Word fixes run again until a name stops changing, and are not used where they would change a date or TBC or leave a name unreadable. In the list a layer's stage can be changed (its ID and the tags follow), the word-fix list is edited and saved with the scene (a scene without one starts with the five HS2 fixes), and the list saves as CSV. Node renames and moves are one undo step; the scene is held first by default (Edit > Fetch restores it, until the next hold). See PLANNER.md.
- The Planner naming check adds one warning when phasing names can be corrected automatically, pointing to the button; it never stops the export.
- MAXScript: `MaxScriptManager.FixPhasingNames false` returns the summary and the list as CSV text; `true` also applies it, holding the scene first.
- Update from inside the exporter: the exporter window shows its release ("Version v20261009.3", or "Development build"), also in the window title and at the top of every export log, and checks GitHub for a newer release when it opens (at most once a day, in the background, silent when offline) and on "Check for updates". "Update" downloads the release's `Max_<year>.zip` and `Installer.zip`, checks them and (not while an export runs, from the window or MAXScript, or while another update is running) asks (saying so when another 3ds Max of the same installation is open, which the update also waits for), lets 3ds Max offer to save the scene, starts the installer (one Windows permission prompt), closes 3ds Max, and the installer installs the package and opens 3ds Max and the scene again. A download that ends in the middle of other 3ds Max work (a MAXScript export, a render, a modal dialog), or with 3ds Max minimised, waits at "Ready to install v..." for the next press. Cancelling any prompt leaves 3ds Max open and unchanged. A scene that still has unsaved changes after the save prompt is only given up after a second question (No by default), and a change made after the prompt gets 3ds Max's own save prompt on closing. The exporter window is disabled while 3ds Max's prompts are open; an installer that stops straight away leaves 3ds Max open; a 3ds Max that does not close is reported after a minute. A release without this 3ds Max's package is not offered, an out-of-date "Update available" ends as "Up to date", and a proxy that asks for a Windows sign-in is given the user's. Exporters installed before this release have no button and are updated once more with the installer. See PLANNER.md.
- The CD workflow works out the release tag once and writes it into the 3ds Max exporter before building it, so each release's exporter knows which release it is. Only `master` publishes a release (a run on another branch is a build check); every Max year and the installer must build and package or nothing is published; runs go one at a time; a run whose commit does not contain the latest release (a re-run of an old run) stops, so an older build is never offered as an update; and a re-run of the latest release's own commit publishes nothing (any new commit on `master` still publishes, also one that only changes documentation).
- Installer (v1.8.1): an update mode for the exporter's button (`--update --max <year> --package <zip> --wait-pid <pid> [--restart <3dsmax.exe>] [--open <scene>]`): waits for 3ds Max to close, installs the package as an offline install does, and opens 3ds Max again. After Cancel or a failure it waits up to five minutes for a 3ds Max that is closing (and reopens it, also while another 3ds Max of that installation stays open), then leaves it as it is. One update per 3ds Max year at a time, released as soon as nothing is left to change or open; a second one changes nothing and leaves the reopening to the first, which reopens only its own 3ds Max. If the update window itself fails, the error is logged and 3ds Max is opened again. Launched without arguments it behaves as before.
- Sub-stages are written with a hyphen: `Ph1_St04-2_IN_...` in object tags and `01_04-2_<name>_<dates>` (or `05-2_...`) in layer and group names. The naming check reads the hyphen everywhere, as the Planner does; the old full stop (`St04.2`, `01_04.2_...`) still reads as the same stage, and the log has one warning for object names and one for group names still written that way, with the count and up to 10 names, saying which ones Fix phasing names converts (the phasing layers and the objects directly in them) and which to rename by hand. Outside the phasing layers a group's full stop counts only after a two-digit stage and not before `_x_`, so a measurement (`2.4_High_Hoarding`, `01_2.4_High_Hoarding_...`, `1.8_x_2.4_Heras_Panel`) is not reported. Fix phasing names writes the hyphen in the layer IDs and tags it corrects (stages with several layers are split -1, -2, ...) and converts full stop sub-stages, also in removal tags and a tag closing an RM object's name, with the reason "hyphen before the sub-stage number, not a full stop"; `02_05.1` and `02_05-1` are the same ID, and a Max layer still written `02_05.1_...` takes the hyphen also when its node already has it. No full stop inside a name (`..._v.02`, `..._2.4_High_Hoarding_...`) and no date is touched. A date is never read as a stage, nor a stage as a date. The parser marks a group name legacy by the Planner's own rule (also after several underscores or a space, `01__04.2_...`), and reads spaces, line ends and trimming as the Planner's JavaScript does (U+FEFF is a space, U+0085 is not) (2026-10-13).
- The naming check follows the convention as Fix phasing names writes it: inside the phasing hierarchy a layer's name is read as its ID, `<PP>_<SS>[-<split>]_<name>` (phase, stage, sub-stage), and an object matches its layer when a tag has the layer's phase and stage. Before, the group's first number (the phase) was taken as its stage, so a scene the fixer had cleaned (HS2 v3_06) gave 122 false "neither tag matches the stage of its group" warnings; it now gives none (only the removal tags with no layer, below). A full stop after a layer's phase (`6.06_...`) is left to the fixer and not reported as a sub-stage; the messages show the hyphen form (`St06-1`). An object linked under another node inside a layer (a Max group, an object), which Fix phasing names leaves as it is, is told to be corrected by hand (2026-10-13).
- Phasing counts, for a quick comparison with the layer / activity matrix: the Max layers in the phasing top layer's tree and the helper nodes (dummies, points, group heads) in the phasing top node's hierarchy, shown after the summary line of the export log and at the top of the Fix phasing names window (which adds the tagged objects); when layers and same-named helpers do not pair up, a note says how many and lists up to 10 names on each side. `MaxScriptManager.FixPhasingNames` returns the counts after its summary, or, when it applies the list, after the outcome, as the fix left the scene (2026-10-13).
- Splines are left out: a shape (a spline such as a RailClone path, whose evaluated object is of the shape class) is not exported, so Fix phasing names no longer lists, renames or counts it, and an untagged spline in a layer no longer gets a "no Ph/St tag" Check line. A shape with something exported linked under it is an empty node in the export and is handled and counted like a helper. The RailClone object itself, and a spline made into geometry by a modifier (Sweep, Extrude), are objects as before (2026-10-13).
- A group name that starts with two numbers is phase then stage, as the Planner now reads it: `02_01-2_Construction_of_Phase1_TM_31-08-26_17-10-26` is phase 2, stage 1-2 (the old `01_04.2_...` phase 1, stage 4-2); each number one or two digits, the stage followed by an underscore, a space or the end (with the old full stop only by an underscore or the end, so `01_2.4 High Hoarding` stays stage 1); a date is never the stage. A name that starts with one number keeps its meaning (the stage, in the phase its objects carry). The parser gives the phase, the name after the ID (`Construction_of_Phase1_TM`) and the label as before (`02_01-2_Construction_of_Phase1_TM`); legacy is a full stop in the stage it reads. The naming check compares an object's tags with such a group's phase and stage anywhere in the scene, not only in the phasing layers (2026-10-13).
- The naming check reports the tags whose phase and stage have no layer in the file, in one warning for removal tags and one for install tags, with the count, the tags and up to 10 object names: the Planner cannot date that work and puts a removal after the end of the works, an install at the start of the project, both TBC (Fix phasing names lists the removal tags as Check lines it cannot correct). On HS2 as Fix phasing names leaves it this is the only warning: eight objects, `Ph1_St06`, `Ph1_St14`, `Ph1_St23`, `Ph1_St26`, `Ph1_St34` and `Ph99_St99` (2026-10-13).
- With a second top-level node with "phasing" in its name, the naming check no longer says Fix phasing names converts the names under it: the fixer corrects only the first (the export asks it which), so the full stops and tag mismatches under any other are to be renamed by hand. Names in a layer whose ID only the fixer reads (`06.01.2_...`) are counted as converted. A removal tag naming a stage with no layer whose spelling the fixer corrects (the phase's zeros, the hyphen) now has the Check line "tag kept, only its spelling corrected" rather than "left as it is", which its Changed line contradicted (2026-10-13).
- The naming check reads which layer the Planner dates each tag from as the Planner does. In a group named with one number (in the phase most of its objects' leading tags with the group's stage carry, or, only when no leading tag has that stage, most of their trailing tags with it), an object whose leading tag has the group's stage in another phase is warned when another dated layer has the leading tag's phase and stage, since the Planner then dates it from that layer while it stays in the group's folder; with no such layer it keeps the group's dates, as meant, and nothing is said. An object whose trailing tag alone has the group's stage, in another phase (`Ph2_St03_IN_..._Ph2_St07_RM` among phase 1 objects in `07_Kerbs_...`), is always warned, worded around that removal tag: the group does not own the removal, and the message says where the Planner dates it and where it puts the leading tag's work, which, when no dated layer has the leading tag's phase and stage, the Planner takes as the group's own work, spread over the group's dates and not listed as unresolved, TBC only when the group is (a TBC group with no dates puts it at the start of the project) (before, nothing was said then). Both warnings say how the group got its phase and, when leading tags set it, name the objects that did; when those are fewer than the objects warned in the group for another phase, the advice says the slip may be theirs. Before, the messages said the group takes "the phase most of its objects carry", which is not the rule: one object whose leading tag has the stage sets it alone, so a removal-only `Ph2_St06-2_RM_Old_Barrier` beside three `Ph0_St00_IN_..._Ph1_St06-2_RM` hoardings got the three hoardings warned and was itself never named. A layer dates another stage's work only when it has a start date and holds a tagged object: a tag whose only layer has no dates yet gets a note of its own (one for removal tags, one for install tags) instead of passing silently, and a group with the tag's phase and stage that holds no tagged object is named in the no-layer warning. An object in a phasing layer whose ID only Fix phasing names reads (`06.01.2_...`) is told that the layer's ID is what the Planner cannot read, and that Fix phasing names corrects it (or which ID to rename it to by hand), rather than that its group needs an order number and dates. In Fix phasing names, a removal tag whose stage has no phasing layer but a layer elsewhere in the scene (at the root, under a second top node) has the Check line "points at <layer>, outside the phasing layers; tag kept - the Planner dates the removal from that group only if the group is exported" rather than "names a stage that has no layer in this file", which was not true; Fix phasing names cannot tell what the export leaves out (hidden nodes, export layers, selected only are the export's settings), so the line says the group counts only if it is exported, which the export's naming check then confirms or warns about (2026-10-13).
- Sub-stages are written with an underscore: `Ph1_St04_2_IN_...` in object tags and `02_01_2_<name>_<dates>` (phase, stage, sub-stage) in layer and group names. Unreal's Datasmith import turns every hyphen in an object's name into an underscore, so objects named with the hyphen of the last release (`Ph1_St04-2_IN_...`) no longer matched their phasing in Unreal. A layer ID is one, two or three numbers, the phase always first: one number is the stage (its phase from its objects) and cannot carry a sub-stage; two are phase and stage; three are phase, stage and sub-stage. Each is one or two digits and the last is never followed by a hyphen, so `02_01_17-08-26_Kerbs` is phase 2, stage 1, dated; a bare number after a two-number ID is now its sub-stage (`02_01_2_Name` is stage 1-2 named `Name`). Dates in layer names keep their hyphens. The hyphen and the full stop (`St04-2`, `St04.2`, `01_04-2_...`, `01_04.2_...`, `06-2_...`, `06.2_...`) still read as the same stage, as the Planner reads them. The naming check has one warning for object names and one for group names still written either way (for objects with a hyphen, saying the names would not match in Unreal), a warning for each one-number group with an old sub-stage saying it needs its phase in front and what it becomes (`06-2_Hoarding_...` with objects tagged phase 3 becomes `03_06_2_Hoarding_...`; with no phase or several among its objects' tags, it says so), and one warning for the tagged objects with any other hyphen in their names. Fix phasing names writes the underscore in the IDs and tags it corrects (reasons "underscore before the sub-stage number, not a hyphen" / "not a full stop"), puts the phase in front of a one-number layer's old sub-stage when the tags of its objects with that stage agree on one (and lists it to rename by hand when they do not), replaces every other hyphen in the names of the objects directly in its layers ("hyphens replaced with underscores, as Unreal's Datasmith import would"; a name that then ends in a tag it did not read before is listed to check), never renames an object into a name another object in the phasing layers keeps (it lists it instead), and renames a Max layer still written with the hyphen along with its node. Fix phasing names reads a layer's sub-stage as the Planner does, also after several underscores or a space (`02_01__2_...`, `02_01_2 ...`, written back `02_01_2_...`), so those objects keep their sub-stage; corrects a tag that a replaced hyphen exposes in the same run (`..._Barrier-Ph2_St05.1_RM` becomes `..._Barrier_Ph2_St05_1_RM`, following its layer), so a second run finds nothing more; gives a layer the review moves alone into a stage split `_1` when its name starts with a number that would otherwise read as its split; and leaves as they are, listed, an old layer ID with a number of three digits or more (`02_02-123_...`, `06-123_...`) and an ID the Planner reads otherwise (`02_123_...`), with the objects directly in any layer it leaves that still need converting. The naming check says why a name is to be renamed by hand where the fixer leaves it (a layer directly under the top node with no phase group of its phase, the objects directly in a layer the fixer cannot read, a name whose underscored form another node already has) rather than promising a conversion, and does not promise the sub-stage of a one-number layer whose stage the fixer numbers again. Released before the Planner that reads the underscore: until that Planner is live, Generate from model there skips objects whose tags carry an underscore sub-stage and misreads three-number layer IDs, so models fixed with this version are generated in the development Planner until then (2026-10-15).
- "Planner": the menu is Planner (it was Babylon) with the item "Planner...", the quad menu is "Planner..." ("Planner Properties", "Planner Animation Groups" and the rest), and the exporter window and the properties windows say Planner in their titles (the exporter window keeps its version and release: "Planner - v1.0-urbancgi - release v..."). On 3ds Max 2022 to 2024 the old "Babylon" menu is taken off the menu bar and the quad on start-up; on 2025 and later it is the same menu, renamed. Nothing saved scenes or scripts rely on changes: the Max2Babylon DLL and namespace, the `babylonjs_*` properties, the MAXScript entry points, the action table, the action ids and the actions' category. The installer is unchanged.
- The exporter window has two tabs: Exporter (everything it held before: the banner, the version and update block, the Options / Advanced / Log tabs, the buttons) and Schedule. The tabs are built when the window opens, the window growing by the tab strip so nothing moves; an export switches to the Exporter tab's log.
- Schedule tab: "Import layer list..." reads the layer list the Planner exports (CSV, one row per layer: Planner ID, Parent ID, Layer, Kind, and the ID, dates, TBC and order for show), checks it (missing columns; missing, repeated or unknown IDs; circles; empty or unusable layer names; names used twice; unknown kinds) and shows it as a tree with each row's status in the scene (New, Rename from..., Move under..., Layer added, Helper added, Helper put on its layer, Up to date, Problem), and the scene's phasing layers that are not in the list (left as they are). The last imported list is kept with the scene and shown again when the window opens.
- "Build layers and helpers..." makes the scene match the list: one Max layer and one same-named helper (a Dummy) per row, the helper on its layer, layers and helpers nested as in the Planner under the phasing root (the existing one, as Fix phasing names finds it, or a new `Work_Phasing`). Rows find their layer by the helper's `planner_id` user property, then by layer ID, then by exact name (outside the phasing root: anything of the name when the scene has no phasing root at all, else only a phasing name or a layer with its helper of the same name, never a Max group of objects, so an ordinary layer of the same name is never pulled in), then, for a phase group renamed in the Planner before the scene had Planner IDs, by its phase number (only a group whose helper has no Planner ID, with a phase number no other row of the list and no other group of the phasing tree has, so a group the Planner removed is listed, never renamed into another; a group made in Max cannot be told from one renamed in the Planner and is taken the same way, which the review shows first, and Add layer refuses a group whose phase number is taken already); their helpers get the Planner ID. A helper whose name has drifted from its layer's (one re-dated by hand) pairs with that layer by their layer ID (never a Max group of objects), and a helper cloned from another (it carries the other's Planner ID) is taken by its own layer's row once the Planner lists it. Existing layers and helpers are renamed (through temporary names), moved and completed, missing ones created; nothing is deleted, and a name the build gives that another layer or node keeps makes the row a Problem, which is skipped (a name that stays as it is only gets a note, in its tooltip, and one line in the tab's notes says how many rows have one). A scene without a phasing root always gets `Work_Phasing`. A review list comes first (one line per change with its reason, Save list...), then Apply holds the scene and runs everything as one MAXScript batch in one undo record.
- "Link objects to their helpers..." links every object on a phasing layer under that layer's helper (its top-most ancestor that is not a phasing helper, so hierarchies and Max groups move whole), keeping world positions; splines the export leaves out are skipped, and an object linked under something on another layer is listed, not moved. Review first, then Apply with Hold and one undo record. A Max group, a Point or a spline with objects under it, once linked, is exported as an empty node, which the export log's pairing note names as a helper without a layer; the review says so beforehand.
- Schedule tab, IN and RM: each layer row has IN and RM columns, the objects its work installs and removes, and a side list shows them (choosing there selects in 3ds Max). With no list imported, the tab shows the scene's own phasing tree. "Fill IN / RM from names" reads every object as the Planner's Generate from model reads the GLB (layer mode): what the export writes (hidden objects and Do not export left out) with the names the GLB gives (a second node called X is written as "X (1)", which the Planner schedules as an object of its own, and so does the tab, the side list saying what it is called in 3ds Max), the layer from the node hierarchy, leading and trailing tags in either order and every older spelling, the phase of one-number layers from their objects, strays, other stages' tags dated by their own layer, removals no dated layer has parked after the works end and installs at the project start (TBC). The project start is the Planner workspace's: "Planner project start" (kept with the scene) takes it when the workspace starts before its works, as Generate from model only ever moves it back to the earliest dated layer; unticked, the earliest dated layer is taken. Objects the Planner does not schedule are listed with why (no tag, a tag that does not read, a tag that is not at the start of the name, not under a dated layer's helper, hidden or Do not export, two nodes the GLB gives one name). An object removed and installed again (`ph1_st02_RM_<object>_ph2_st01_IN`, or the other order) is in the RM of one layer and the IN of the other, wherever it sits.
- "Add selected to IN..." / "Add selected to RM..." give the objects selected in 3ds Max the chosen layer's install or removal tag, and "Remove from the list..." takes the tag out: the tag of the same type is replaced where it sits, a missing one goes on the end as the trailing tag (the leading one for a name with none), the rest of the name stays as written, and a tag the tool writes is `Ph<p>_St<ss>[_<m>]_IN|RM`. The layer is the one the Planner reads on that row (its helper's name, when the Max layer's has drifted from it). A closed group selected whole is edited by its head: its untagged members (and the untagged parts of any hierarchy selected whole) are left out, with a note, as the Planner shows and hides them with it; a member with a tag of its own is a Problem (open the group to choose). An object's split pieces (`Name_2` under `Name`) are renamed with it, so the Planner still reads them as part of it. Nothing is moved or linked. Each edit is checked by reading the scene again with the new names as the Planner would: one that would not land in the column asked for is a Problem, with why (an undated target layer, one with no tagged object yet, another layer dating that stage, an object not under a dated layer's helper, a name taken, a layer's helper), and other objects the renames move are listed, each with why. Review, then Apply in one undo record; Fill then gives back exactly the edited lists.
- "Add layer..." adds a phase group (`_<nn>_<name>`, its phase needed) or a layer (phase, stage, sub-stage, name, dates or TBC) under the chosen phase group's row or the phasing root, as the Planner files every layer in the phase group above it: the name is composed in the convention and must read back with the ID and dates meant and clash with no layer, node, ID, row or, for a group, phase number already in the list or the phasing tree; the Max layer and a same-named Dummy linked under the parent's helper are made with Build's own batch (review, Hold, one undo record). It shows as "Added in Max, not yet in the Planner", is kept with the scene, and a later list from the Planner adopts it (layer ID or name; Planner ID stamped). Taken back with Edit > Undo, deleted or renamed by hand, it is no longer an added layer, and its ID and name are free again.
- Schedule tab, date scrubber: a day slider from the project start to the end of the schedule (a TBC zone after the works end, where removals no dated layer has are parked), with the date, the layers at work and "Select changing this day"; nothing in its row changes size as it moves, so the slider stays under the pointer. "Preview in the viewport" hides what the Planner's 4D view hides at that date (start or end of the day), and what is linked under it, by the nodes' own Hide in one batch with no undo record and one redraw; leaving it (its button, another tab, closing the window, a new scene, reset or open) gives every object its own hidden state back, and every save keeps them in the file. Every export ends it first, from the window or from MAXScript, and while an export runs it does not start and choosing in the lists does not change the selection. Nothing is renamed, moved or linked.

## v20261018.10: naming v2 (UrbanCGI fork)
**Release order: this exporter first, then the Planner.** This release goes out before the Planner release that reads the new names (the next one after 0.9.5). Until that Planner release is live, a cleaned scene's model does not schedule in the Planner.
- Update the exporter with its Update button before a scene is cleaned, so the Schedule tab and the export check read the new names.
- On a live project, such as HRB, do not run Clean up scene graph, and do not export a cleaned scene, until the planning team says to go ahead. Until then, keep exporting the scene as it is: a scene on the old names exports as before.
- Clean up's warning, its tooltip, the Schedule tab's old-names message and the export log's note all say this.

**Implemented changes**
- New names. A layer and its helper are named `<ID>_<Name>` (`02_01_4_Construction_of_Phase1_TM`), with no dates or TBC: the Planner holds every date, and nothing in 3ds Max sets one. An object's tags say when its layer's work installs or removes it: `IN_` or `RM_` at the start (while its own layer runs), `_IN` or `_RM` at the end (at the end of its own layer), and `_IN_<ID>` or `_RM_<ID>` at the end (in another layer). The old `Ph<n>_St<s>` tags are not written any more. See PLANNER.md, section 2.
- "Clean up scene graph..." on the Schedule tab replaces "Fix phasing names..." and "Build layers and helpers...", whose buttons are gone. In one review and one undo record it takes dates and TBC out of layer and helper names, writes IDs the current way, moves layers into their phase groups, renames Max layers with their helpers, links objects under their layers' helpers, converts old tags, turns hyphens in object names into underscores, and numbers clashing own names from `_02`, before any trailing tag. It never deletes anything, never renumbers a stage, and finds nothing to do when run again.
- With the Planner's layer list imported, Clean up also brings the layers and helpers in line with the list: it finds each row's layer by `planner_id`, then layer ID, then exact name; renames, nests and creates layers and helpers as the list has them; writes their Planner IDs; and makes a phasing root when the scene has none. Names never carry dates, even from a list exported before the new names; a group's name is built as the list writes it, as it is the Planner's folder name. A layer whose ID changed in the Planner takes the references to it along, but a reference to an ID that no layer of the scene had, which the list now gives a layer that had another ID, was never written for that layer: it is listed, not rewritten. A layer the list does not have is kept and listed. No two layers may share an ID: a row whose layer ID a layer kept as it is already has, and a second row of the list with the same ID, are listed and left as they are.
- What Clean up cannot convert safely, it asks, with answers to choose: a closing `_RM` or `_IN` with no Ph/St tag, a leading tag naming another layer, a doubled type word, and a trailing tag naming a stage no layer has (by default written as the tag writes it, which the Planner parks TBC, unless the Planner's list now gives that ID to a layer that had another: then there is no default) or several. An object left unanswered keeps its old name. On a scene with the old names it warns first and offers to save a copy (`<scene>_before_v2.max`); it holds the scene first by default, and Edit > Fetch is the sure way back: Clean up is one undo record, but whether Edit > Undo also takes back the Max layers it renamed, moved or made has not been checked in 3ds Max yet, and the report says so when Max layers changed. The project's word fixes are edited in its window. Layers added in Max follow their layers' new names, and their parents', also back on Edit > Undo, and their records are read from the scene first, so a Clean up run from MAXScript in between is kept.
- MAXScript: `MaxScriptManager.CleanUpSceneGraph false` returns Clean up's summary and its review as CSV text; `true` also carries it out after holding the scene, with the default answers only and no copy. `FixPhasingNames` changes nothing and points to Clean up.
- The Schedule tab works on the new names only. On a scene with the old names it says so above the tree and offers Clean up beside the message; Fill, the scrubber, the Gantt, Add to IN / RM and Add layer wait until the scene reads as the new names. Import layer list, Read the scene again, Clean up and Link work as before. A new empty scene works as the new names. The tab reads the scene again after Edit > Undo and Redo, and Fill and Add layer read it first, so a clean-up taken back waits again. At the smallest window the old-names message gives way to one line, the whole of it in its tooltip.
- Fill IN / RM previews the Planner's first Generate on the new names: layers by ID (an empty one included), events filed in the layer their tags name, each layer's work in three groups over the list's dates, end-of-layer events together over the last slot, and a layer with no dates parked on a day of its own, TBC. Checked against the Planner's own code in UTC, Sydney and London time.
- Layer list import: the new `Project start` column sets Planner project start, which never moves on the new names. One date reader serves the preview and the Gantt: ISO dates first, else day first when that cannot be mixed up, else undated with a note. The tool sorts the tree's rows itself, in the order the Planner sorts names in, as the list keeps no particular folder order: its Order column is not used, and layers added in Max sit among their siblings. A second row with a layer ID another row already has is a problem.
- TBC rows: a row whose dates the Planner has not settled is highlighted in its Layer cell, with an orange stripe at its edge: a row the list marks TBC (a layer on its placeholder day, or dated but not confirmed), and a layer of the scene the list does not have (one added in 3ds Max, or left out of the list). The tooltip, the TBC column and the Gantt's hover say which. The list's format does not change.
- Add layer asks for no date: a layer's ID and name, or a phase group's phase and name. Its name is `<ID>_<Name>`, and a name led by a number, ending in a number in brackets (read as a copy number), or holding a date or TBC, is refused, each with its own reason. It reads the scene first, and waits on a scene back on the old names. Its undo record is "Add layer". The layer shows TBC until the Planner's list has it. Records are kept as "PlannerAddedLayers 2"; first-version records are still read, their dates passed over. A layer taken back with Edit > Undo and added again is recorded once, and Edit > Redo brings back its record. The dialog's Under list offers the phasing root and every phase group that can take a layer: it starts on the chosen row's phase group (for a layer's row, the phase group above it, with a note saying why), or on the phasing root with no row chosen.
- Add to IN... / Add to RM... and Remove from list... write the new names' tags: for the object's own layer at the start, or at the end when the start holds the other type; for another layer at the end with its ID, a layer only the Planner's list has included. Remove takes a tag out where it is. Each new name is read back as the Planner reads it, and each edit is checked by filling the preview again. Editing the old Ph/St tags is gone.
- Schedule tab, Gantt (first released here): beside the tree and row for row with it, one column per day (or week or month), each layer's IN days in the install colour and RM days in red, TBC work hatched, group spans, weekends shaded, today and the scrubber's date as lines. Before Fill it shows each layer's dates from the Planner's list. Hovering a day says what happens that day and where the layer's dates come from; clicking a day moves the scrubber. Beside the Gantt the tree hides its Start, Finish and TBC columns, and no date is ever read from a Max layer's name, on the old names either. At the window's smallest size the tree keeps three rows and the side list two. The lists' headers and IN / RM columns fit their text at any display scaling.
- Export check on the new names: it reads the scene as the Planner reads the exported model, every layer node included, and reports what the Planner would list, with its code and severity: names that are not allowed and old tags as errors, references to layers nobody has, shared and nearly matching own names, hyphens in object names and dates in layers' names as warnings (a group's name is the Planner's folder name, and is not pointed to Clean up), and more. The log adds what the Planner reads (layers, objects, installs, removals) and how many names Clean up can convert. See PLANNER.md, section 2.4.
- Export check on the old names: the same rules as before, with one note pointing to Clean up and the live-project rule. It no longer points to Fix phasing names, and its warning that names "can be corrected automatically" is gone. It now reads a closed group's head and a tagged dummy as objects and a split piece as part of its object, as the Planner does, and warns about objects taken away and put back in one stage that sit in another stage's layer, giving the name that retags each for its layer.
- Schedule tab: the tree and the lists below it no longer choose their first row by themselves (on opening, after any reading of the scene, on a header click), so Add layer starts on the phasing root unless the modeller has chosen a row, and showing Not scheduled selects nothing in 3ds Max. A reading keeps the chosen row, in the list it was chosen in. The side list's heading wraps, so a long layer name no longer hides the row's counts or what to do first.

## v20261019.11: Schedule tab tidy-up (UrbanCGI fork)
**Changes**
- "Read the scene again" is now "Read the scene".
- The yellow old-names message above the tree, and its own Clean up button, are gone. On a scene with the old names the "Clean up scene graph..." button at the top is the way in. Fill and Add layer are greyed out until then, and the Gantt, the scrubber and the side list say why.
- Clean up's review: "Layer it sits in" shows where a layer, helper or Max layer is now; "What changes" says where Clean up moves it, also when only a Max layer moves (that move had no row before). The line above the list counts the helpers and Max layers it moves ("2 helpers and 2 Max layers to move"), as the report after Apply does.
- Clean up without the Planner's list: a layer in the group of another phase (`02_05` in `_03_Roads`) is now listed to check, with the group of its own phase to move it into by hand. It is not moved, as the Planner may file it there on purpose; with the list imported, the list's nesting decides, as before.
- Long tooltips in the Planner window (the Clean up button's, a row's details, the notes, the Gantt's hover and the others) are broken into lines of about 80 characters instead of one line across the screen.
- Clean up's review, Answer column: a click (or Enter, F4, Alt+Down, or typing) opens the row's answers in a search list, where every word typed must be in the answer, so an answer naming a layer is found by its ID or name. Ctrl+C copies an answer and Ctrl+V pastes it into the rows chosen; an answer copied here keeps its meaning on each row ("at the end of its own layer" stays that), and the bottom bar says which rows do not offer it. The right-click menu does the same.

**Fixed bugs**
- Edit > Undo then Edit > Redo of a clean-up left the tab unfilled (IN / RM, the Gantt and the scrubber empty until Fill was pressed again). A tab that was filled now fills again by itself once the scene reads as the new names again.
- Add layer: changing Under after its grey note had shown moved the labels beside the wrong fields. Each control now keeps its own place, and the note's row closes up.
- "Preview in the viewport" looked almost the same on and off. While on, it is now filled and edged in blue.
- With the scrubber off, "Select changing this day" kept the last day's count. It now shows no count.
- Clean up's report broke the saved copy's path in the middle of a word. The copy's file name and folder now have lines of their own, after the rest of the report, and a long folder breaks after a backslash. The message when the copy cannot be saved is laid out the same way, with the reason after it.

## Several phasing roots (UrbanCGI fork, next release)
**Changes**
- A scene may hold several phasing roots, one per area of the site, all in one schedule. Every top-level helper with "phasing" in its name that reads as neither a phase group nor a layer is a root, taken in name order, each with the Max layer of exactly its name: no other layer stands in for a root any more. A top-level mesh with "phasing" in its name is no root.
- Clean up scene graph works on every root, in one review and one undo record. A layer straight under a root moves only into a phase group of its own root. A root whose Max layer has another name is paired with one: the layer its groups sit in is renamed to the root's name when it holds nothing else; otherwise a new one is made at the top, the helper put on it and the groups' Max layers moved into it.
- Two roots that share layer IDs (a backup copy) get one question in Clean up's review: which root keeps them. The other root's layers take the next free stages in their phases, their Max layers too, and that root's own tags that name them follow. "Leave it for now" renumbers nothing.
- The Planner's layer list may have rows of Kind Root, one per root, for a project with several. Each finds its root by the root helper's Planner ID (every row by ID first), then its name, then what the rows under it find, leaving out the roots other Root rows have already found (so a renamed root that also holds a group moved in from another root is still renamed, once that root's own row has found it by Planner ID or name); else Clean up makes it, on a Max layer of its name when the scene has one. The Planner's own Root row for a root of its model (Planner ID `root:`) with nothing under it makes no root: the Planner writes one for its model's root after the root folder is renamed, until it reads an export with the new name. A root renamed in the Planner is renamed here, helper and Max layer. A group moved to another root in the Planner moves in Max. A root the list does not name is kept and listed. Top-level rows stay in the root they are in, and a new one goes into the first root the list names, or, in a list without Root rows, the first root the list's other rows are in, else the first the export writes; so a hidden backup copy, or an area not exported yet, never takes it ahead of a root the list's rows are in. A list no longer pulls one root's groups into another. A list with Root rows needs this release: an older tool marks them problems.
- Link objects to their helpers, Add to IN / Add to RM, Add layer, the Schedule tab, the Gantt, Fill, the scrubber and the Not scheduled list cover every root. Add layer lists each root with its groups in Under, and checks IDs and names across all of them; in a root with no Max layer of its name it waits for Clean up. Link never moves an object from one root to another. The tree shows each root as a bold Root row when there are several. Roots are never objects.
- The export check reads every root. New: an error for a layer or a phase group of one name in two roots (the Planner reads one); warnings for a root with no Max layer of its name, two root helpers of one name, and a top-level "phasing" Max layer with no root helper; a note listing the roots. The export log gives one phasing counts line per root, and a total that counts each Max layer once.
