# Avatar Asset Nexus v6.6 - Unity Plugin

A lightweight Unity Editor plugin for VRChat / VRM / VTuber avatar asset navigation.

## Main workflow

This tool is Avatar-first. It does not build a heavy global index when the window opens.

1. Open `Tools / Avatar Asset Nexus / Open`.
2. On Dashboard, expand `Asset Folder` only when you need to change the folder used for unused-prefab checks.
3. Click `Select Avatar`, then drag a Project Avatar prefab or a Hierarchy Avatar instance into the Avatar card.
4. Click `Search Current Avatar Prefabs` to refresh this Avatar's remembered prefab list.
5. Open `Current Avatar Prefab List` to see prefabs referenced by that Avatar.
6. Left-click a prefab to select it. Shift-click and Ctrl/Cmd-click support batch selection.
7. Click `View Assets` on a prefab row/card to inspect the prefab's referenced materials, textures, shaders, animations and models.
8. Use `Show Prefabs Not Used By Current Avatar` from Avatar Select when you want to refresh the unused-prefab list. The Prefab List tab displays the saved cache.

## v6.6 changes

- Fixed the IMGUI layout mismatch risk from the previous build by returning to a stable tab layout and removing the Settings tab from the main toolbar.
- Selecting a prefab now also tries to select its scene instance in Hierarchy first. If no scene instance exists, Unity's Inspector switches to the Project asset.
- Right-click `View Details` uses the same Inspector/Hierarchy selection behavior.
- `Show Prefabs Not Used By Current Avatar` in Avatar Select refreshes the unused-prefab list every time and saves it into the Avatar cache.
- The `Unused Prefabs` button in the Prefab List tab no longer rescans; it displays the saved cached list.
- After scanning an Avatar, the tool creates or updates a custom category for that Avatar and fills it with the referenced prefabs. Re-scanning updates the same category without duplicates.
- Empty custom categories now display empty results instead of falling back to all remembered assets.
- Custom categories can be reordered by dragging category rows.
- Booth thumbnail URLs now download through `UnityWebRequestTexture` and update cards/details after the online fill completes.
- Dashboard `Asset Folder` and `Common Settings` sections are collapsible.

## What is stored

The tool stores a lightweight pointer cache:

- Avatar key/path/name
- Referenced prefab paths
- Cached unused prefab paths for that Avatar
- Prefab dependency asset paths
- Analysis timestamps
- Custom categories and tags
- Recent imported asset paths for category assignment
- Local cover image mapping
- Booth metadata for prefab/folder only

Data asset:

`Assets/AvatarAssetNexusData/AvatarAssetNexusDatabase.asset`

Editor settings are persisted with `EditorPrefs`.

## Installation

Delete old copies first, especially accidental nested paths like:

`Assets/Assets/AvatarAssetNexus`

Then place the folder:

`AvatarAssetNexus`

under your Unity project:

`Assets/AvatarAssetNexus`
