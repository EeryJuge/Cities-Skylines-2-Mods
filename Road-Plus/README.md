<p align="center"><img src="Properties/Logo.png" alt="Road+ by Raggend" width="420"></p>

# Road+ by Raggend

Transport Fever 2 style road drawing for **Cities: Skylines II**, plus tools to connect and re-grade existing roads.

Available on Paradox Mods: search for **"Road+ by Raggend"** (Mod ID 166280).
Feedback and bug reports: [forum thread](https://forum.paradoxplaza.com/forum/threads/new-mod-project-for-playing-cities-skylines-2-with-transport-fever-3-mechanics.1948553/) or the Issues tab of this repository.

## Features

**Draw**
- Free curves that keep their direction, like Transport Fever 2
- Height modes: Terrain (smoothed), Graded (fixed grade steps) and Flat
- Raise or lower the end node while drawing; the start stays where you clicked
- Start on a road end to continue that road; end on a road to connect at its height
- Slope limits per road type (highway, highway ramp, rail, other roads)

**Connect** (like Network Multitool)
- Pick two road ends or road points and join them: Straight, Smooth, Arc or Spiral
- Spiral loops as many times as the slope limit needs, leaving room for the road to pass under itself

**Slope** (like Move It)
- Pick the first and last node of an existing road: Even slope, Level or Follow terrain
- Ctrl + Z undoes the last slope

**Shift + R** toggles Road+ while a road is selected. All shortcuts are listed in the tool panel.

## Compatibility

- Draw and Connect build vanilla roads through the game's own systems (`CreationDefinition` + `NetCourse`).
  Nothing is saved into the savegame and there are no Harmony patches; removing the mod leaves all roads as they are.
- The Slope tool edits existing node positions and edge curves directly, the same way Move It does, then lets the game rebuild them.
- Respects the game's error checks. With [Anarchy](https://github.com/yenyang/Anarchy) installed, Road+ registers through Anarchy's public bridge so Anarchy can switch the checks off.
- Works with Road Builder roads, asset roads, train / tram / subway tracks and pathways.

## Source layout

| Path | What |
| --- | --- |
| `src/Core/RoadMath.cs`, `src/Core/ConnectMath.cs` | Game-independent math: curves, arcs, spirals, height profiles, segmenting |
| `tests/` | Unit tests for the math (`dotnet run` in that folder, no game needed) |
| `src/Systems/TF2RoadToolSystem.cs` | The tool: Draw, Connect and Slope modes, preview and building |
| `src/Systems/HeightOverrideSystem.cs` | Keeps the planned heights after the game re-samples courses from the terrain |
| `src/Systems/NetSnap.cs` | Snapping to existing nodes and edges |
| `src/Systems/SlopeEditor.cs` | Path finding, re-grading and undo for the Slope tool |
| `src/Systems/ToolActivatorSystem.cs` | Shift + R and keeping the tool active |
| `src/Systems/AnarchyCompat.cs` | Optional Anarchy registration (reflection, no hard dependency) |
| `src/UI/` | Tool panel |
| `src/Setting.cs` | Options menu |

## Building

Requires the Cities: Skylines II modding toolchain (installed from the game's Options → Modding).

```
dotnet build -c Release
```

The build copies the mod into the game's local mods folder. Close the game first.

## License

Source available, **all rights reserved**. You may read and build the code for personal use and send issues or pull requests,
but you may not copy, reuse or republish it. See [LICENSE](LICENSE).
