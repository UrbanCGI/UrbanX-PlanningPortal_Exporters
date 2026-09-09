# UrbanCGI Planner additions to the 3ds Max exporter

This fork of [BabylonJS/Exporters](https://github.com/BabylonJS/Exporters) adds two pre-flight
safeguards for models that are exported to the UrbanCGI Planner (planning.urbancgi.co.uk). Everything
else is upstream. Exports made with this build identify themselves in the GLB's `asset.generator` as
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
(`Ph1_St01_IN_Sheet_Pile_1`, `Ph1_St00_IN_RC_Hoardings_Ph2_St06_RM`). Objects sit inside a group whose
name carries the order number, activity name and dates: `N_<Activity>_DD-MM-YY[_DD-MM-YY]`, or
`N_<Activity>_TBC` while dates are unknown (`01_Sheet_Piling_SA&DS3_17-08-26_18-09-26`,
`35_Service_Road_Setback_TBC`). The group owns the segment whose `St` equals its order number. The
Planner reads the exported node hierarchy, so it is the *group* that matters, not the 3ds Max layer.

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
| Warning | neither tag matches the group's stage (`Ph1_St06_RM_...` inside `00_...`) |
| Warning | group has neither dates nor TBC; group has an order number but no name |
| Warning | groups whose labels differ only by letter case (`..._Inst` / `..._inst`) |
| Warning | empty description, or a stray underscore at its edge (`..._C_`) |
| Warning | two groups share a name |
| Note | single-digit stage (`St6`); untagged object inside a dated group; a date more than 3 years in the past or 10 in the future |

Files: `SharedProjects/Utilities/Planner/PlannerNaming.cs`, `PlannerNamingValidator.cs` (rules, Max-free),
`Max2Babylon/Exporter/BabylonExporter.PlannerChecks.cs` (collects the scene nodes, reports, stops).

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

## Installing on a modeller's machine

With 3ds Max closed, copy the DLLs listed in `Max2Babylon/OnPostBuild.bat` (Max2Babylon, Newtonsoft.Json,
SharpDX, SharpDX.Mathematics, GDImageLibrary, TargaImage, TQ.Texture, the three Microsoft.WindowsAPICodePack
assemblies) from `bin/Release/<year>/` into `C:\Program Files\Autodesk\3ds Max <year>\bin\assemblies`.
On a machine where Max is installed the post-build event does this automatically (it reads the
`ADSK_3DSMAX_x64_<year>` environment variable Max sets up). The build must be repeated for each Max
version in use, and reinstalled after every change.

To confirm the install: the exporter log shows "Checking Planner naming convention" at the start of an
export, and the GLB's generator string ends in `v1.0-urbancgi`.

## Tests

```bat
dotnet test SharedProjects/Utilities.Tests/Utilities.Tests.csproj
```

The test project compiles the shared sources directly (as Max2Babylon does) and needs no Max. Two tests
run only when pointed at real data and print their report:

- `PLANNER_REAL_TGA=<file>`: a mislabelled texture, e.g. the image bytes extracted from a broken GLB;
  proves the bytes round-trip to a decodable PNG.
- `PLANNER_NODES_FIXTURE=<json>`: `{ "nodes": [ { "name", "parent" | null, "isMesh" } ] }` extracted from
  a GLB; prints the naming report for a real hierarchy.
