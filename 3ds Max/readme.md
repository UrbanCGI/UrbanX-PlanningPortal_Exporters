3DS Max to Babylon.js exporter
==============================

Documentation: https://doc.babylonjs.com/features/featuresDeepDive/Exporters/3DSMax

UrbanCGI fork: the texture content check and the Planner naming check are described in [PLANNER.md](PLANNER.md),
together with build and install steps that need no 3ds Max installation.

# How to contribute:
## Requirements:
* Install Visual Studio (community editon works)
* Install 3dsmax.

## Develop:
Use "RaiseMessage/Warning" methods to see if your code is working
* Close all running 3DS Max instances.
* Build project with Admin-elevated instance of Visual Studio.
* 3DS Max should be relaunched by the build system, run the exporter, the messages will be displayed in the exporter form.
