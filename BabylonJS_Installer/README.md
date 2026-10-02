# Planner Exporters installer (Urban CGI)
`Planner_Exporters.exe` installs the Planner Exporters, Urban CGI's build of the Babylon.js exporters for 3ds Max and Maya, from the fork's GitHub releases (https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases). Up to version 1.8.0 the program was called `BabylonJS_Exporters.exe`. Do not use the stock Babylon.js installer alongside it: it would replace the Planner build with the stock exporter.

# How to install
Here are the steps to follow to use the installer locally.

## Copy
Copy `Planner_Exporters.exe` from the zip file to your local drive.

## Admin Mode
In order to use the installer, as it will have to modify files in your Program Files folder, you need to run the application in admin mode.

To run in administrator mode, right-click the exe, open the Compatibility tab and tick "Run this program as an administrator".

This is the only way the required DLLs can be put into your Program Files folder. If you prefer to deploy by hand, all the built libraries are in the GitHub release assets.

## Offline
A `Max_<year>.zip` placed next to `Planner_Exporters.exe` is installed instead of downloading one, so a build can be tested before a release exists and machines without GitHub access can still install.

# How to use
Once launched, it shows, for 3ds Max and Maya and each of their versions, the exporter currently installed.

You can then install the latest available version or uninstall the plugin. Uninstalling also removes the "Planner" menu from 3ds Max, and any "Babylon" menu an earlier build left behind, the next time 3ds Max starts.

## Plugin not found
If the software (3ds Max or Maya) was not found automatically, relocate the installation folder with the (re)locate button: pick the root of the installation and the installer searches it for the exporter.

## Quick Update
The Update all button installs or updates the exporter for every application found on your system at once.
