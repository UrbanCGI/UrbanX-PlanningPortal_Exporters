# How to install
Here are the steps to follow to use the installer locally.

## Copy
Copy the executable from the zip file on your local drive.

## Admin Mode
In order to use the installer, as it will have to modify files in you program files folder, you need to run the application in admin mode.

To run in administrator mode, simply right click the exe, navigate to the compatibility tab and select the run this program as an administrator checkbox.

I know this might sound scary but this is the only way we can deploy the required dll in you program files folder. In case you prefer to deploy manually, you can still find all the built libraries in the Github releases assets.

# How to use
You can now launch the tool \o/

Once launched, it will show you for both Max/Maya and their different respective versions, the currently installed exporter version.

You can then easily install the latest available version or uninstall the plugin.

## Plugin not found
If the software (3ds Max or Maya) was not automatically found, you can easily relocate the installation folder by pressing the (re)locate button. Just locate the root of the installation. At this point, the software will automatically search for the exporter.

## Quick Update
The update all button is the way to go and will install / update the exporter for all the applications found on your system at once.

# Updating from inside 3ds Max (installer 1.8.1 and later)
From this release on, the exporter window in 3ds Max has an Update button, so there is no need to come back to this installer for each new version. One press downloads the new version, asks you to save your scene, closes 3ds Max, runs this installer in update mode (Windows asks for permission once, because the exporter lives in Program Files) and then opens 3ds Max again with the same scene. It does not start while another update is still running, and it says so first when another 3ds Max of the same version is open.

Exporters installed before this release have no Update button: update them once more with this installer, as before.

In update mode the installer only shows a small window, "Updating the exporter for 3ds Max <year>...":

- It waits for 3ds Max to close. If another 3ds Max of the same version is still open, it asks you to close that one too; a loaded exporter cannot be replaced.
- Cancel stops the update at any point before the new files are written. Nothing is changed and 3ds Max opens again once it has closed, also when another 3ds Max of the same version stays open. If 3ds Max is still open five minutes after Cancel, it is left open as it is and the window says it is not opened again. Close stops that wait sooner, and 3ds Max is then not opened again.
- Only one update per 3ds Max version runs at a time. A second one that starts anyway changes nothing and says so; it still waits for its 3ds Max to close, which the first update does not open again (it reopens only the 3ds Max that started it). The exporter checks for a running update first, so this takes two near-simultaneous updates.
- If this window itself runs into an unexpected problem, the problem is written to the log and 3ds Max is opened again.
- The new files are installed exactly as the Update button in this window installs them.
- If the new files cannot be written, the previous version is put back, 3ds Max opens again and a message says what went wrong. Update by hand with this installer, as before.
- 3ds Max is opened again with your normal Windows rights, not the installer's administrator rights, so mapped network drives and dragging files in from Explorer keep working.
- Every update is written to `%LOCALAPPDATA%\UrbanCGI\Exporter\update.log`. The last line of each run says `RESULT: updated`, `RESULT: cancelled` or `RESULT: failed: <reason>`.

For support only: the exporter starts the installer with this command line (you never need to type it).

```
BabylonJS_Exporters.exe --update --max <year> --package "<full path to Max_<year>.zip>" --wait-pid <3ds Max process id>
  [--restart "<full path to 3dsmax.exe>"] [--open "<full path to the scene>"]
```

Started without `--update`, the installer opens its usual window.
