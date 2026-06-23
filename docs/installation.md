# Installation

Two paths, depending on whether you want to **just use the tool** or **build
it from source**.

---

## A. Just use the tool (no compiling)

### 1. Download

Go to the [Releases page](https://github.com/sbuchanan01/hanger-layout-for-revit/releases),
find the latest release, and download the ZIP attachment (named something
like `HangerLayout-v1.0.0.zip`).

![Download from Releases](screenshots/install-download.png)

### 2. Extract

Unzip anywhere. Inside you'll find:

```
HangerLayout.dll
HangerLayout.addin
```

### 3. Drop into the Revit Add-ins folder

Press <kbd>Win</kbd>+<kbd>R</kbd> to open the Run dialog, paste this, hit
<kbd>Enter</kbd>:

```
%APPDATA%\Autodesk\Revit\Addins\2026
```

That opens the per-user add-ins folder for Revit 2026.

> If the folder doesn't exist, create it. The structure is:
> `%APPDATA%\Autodesk\Revit\Addins\2026\` (you'll already have an
> `Addins` folder with other version sub-folders if you've installed
> add-ins before).

Copy **both** files into that folder:

![Files dropped in Addins folder](screenshots/install-addins-folder.png)

### 4. Unblock the DLL (Windows quirk)

Windows marks DLLs downloaded from the internet as "blocked" by default —
Revit will refuse to load them. Right-click `HangerLayout.dll` →
**Properties** → at the bottom of the General tab, tick **Unblock** → OK.

![Unblock the DLL](screenshots/install-unblock.png)

If you don't see an Unblock checkbox, the file is already cleared — skip.

### 5. Launch Revit

Start Revit 2026. You'll see a new **Hanger Layout** ribbon tab with a
**Hanger Layout** button.

![Ribbon tab](screenshots/install-ribbon.png)

If the tab doesn't appear, see [Troubleshooting](#troubleshooting) below.

---

## B. Build from source

Prerequisites:

- **Windows 10/11**
- **Revit 2026** (full install — the add-in references DLLs from the
  install folder)
- **.NET 8 SDK** — [download from Microsoft](https://dotnet.microsoft.com/download)
- Git — [download](https://git-scm.com/download/win) or use GitHub Desktop

### 1. Clone the repo

```powershell
git clone https://github.com/sbuchanan01/hanger-layout-for-revit.git
cd hanger-layout-for-revit
```

### 2. Build

```powershell
cd src
dotnet build -c Debug
```

If Revit installed somewhere other than `C:\Program Files\Autodesk\Revit 2026`,
override the path:

```powershell
dotnet build -c Debug /p:RevitInstallPath="D:\Revit 2026"
```

### 3. Auto-deploy

Debug builds automatically copy `HangerLayout.dll` and `HangerLayout.addin`
to `%APPDATA%\Autodesk\Revit\Addins\2026\`. **Close Revit first** — if
Revit is open when you build, the DLL is locked and the copy step is
skipped (the build itself still succeeds; you just need to copy manually).

### 4. Launch Revit

Same as the install path — look for the **Hanger Layout** ribbon tab.

---

## Troubleshooting

### "I installed the files but the ribbon tab doesn't show up"

Check that **both** files are in
`%APPDATA%\Autodesk\Revit\Addins\2026\`:

- `HangerLayout.addin` (the manifest — without it, Revit doesn't know what
  to load)
- `HangerLayout.dll` (the actual add-in)

Open `HangerLayout.addin` in Notepad and confirm it points at
`HangerLayout.dll` (relative path). If you renamed either file, fix the
reference.

### "Revit shows a security warning about the DLL"

Right-click `HangerLayout.dll` → Properties → **Unblock** → OK. Restart
Revit.

### "I get a startup error dialog from the add-in"

The dialog title is "Hanger Layout — startup error". Copy the exception
text and either:
- Search [existing issues](https://github.com/sbuchanan01/hanger-layout-for-revit/issues), or
- File a new issue with the exception text + your Revit version.

### "The icon shows but clicking the button does nothing"

Most likely a missing dependency. Check the Revit journal log under
`%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\` — search
for `HangerLayout` and look for stack traces.

### "Build fails with 'Could not find RevitAPI.dll'"

Your Revit install path differs from the default. Pass `/p:RevitInstallPath`:

```powershell
dotnet build -c Debug /p:RevitInstallPath="D:\My Revit Folder"
```

### "I have an older or newer Revit version"

The add-in is built for Revit 2026 specifically. Cross-version compat
isn't guaranteed. If you want to retarget:

1. Change the install path passed via `RevitInstallPath`.
2. The Revit API surface has changed between versions — you may hit
   compile errors that need source fixes (rare for the APIs this add-in
   uses, but possible).
3. Also update the Addins folder version in the `DeployToRevitAddins`
   target in `src/HangerLayout.csproj`.

---

## Uninstalling

Delete `HangerLayout.dll` and `HangerLayout.addin` from
`%APPDATA%\Autodesk\Revit\Addins\2026\` and restart Revit.

Saved hanger specs persist on the Revit project itself (ExtensibleStorage),
not in the add-in folder — they survive uninstall. If you want to clear
them, reinstall the add-in, open the dialog, delete the rows, and Save.
