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

## Unreleased (UrbanCGI fork)
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
