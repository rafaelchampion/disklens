# DiskLens — Agent Guide

A native Windows WinUI 3 + Win2D treemap explorer and disk cleanup tool. Read
`README.md` for the product overview; this file is the working contract for agents.

## What this is

Find what is eating disk space on Windows, mark paths for removal, review the list, and
remove it — with the volume's free space live on screen the whole time. Two
phases: explore (treemap, breadcrumbs, selection and status lines) and review (the marked list,
the removal mode, confirmation). Marking is never destructive.

The original Rust + GPUI implementation lives in `reference/` for algorithmic and
parity comparison.

## Commands

```powershell
dotnet build src/DiskTree.slnx             # build entire solution
dotnet test src/DiskTree.slnx              # run core tests
dotnet run --project src/DiskTree.App/DiskTree.App.csproj  # launch desktop app
```

## House rules

* **Clean .NET 9 C# code.** Follow modern C# idioms (nullable reference types, pattern matching, records where appropriate).
* **Non-destructive by default.** Never delete or modify files on disk during exploration. Deletion happens strictly through `RemovalEngine` after explicit user review and confirmation.
* **Never delete outside marked root.** Mount points, volume roots, user profile root, and Windows critical system paths are guarded and refused.
* **Separation of Concerns:**
  - `DiskTree.Core`: Pure .NET logic (scanning, aggregation, treemap layout, classification, safety). No UI dependencies.
  - `DiskTree.App`: WinUI 3 presentation, MVVM viewmodels, Win2D Canvas rendering, theme adaptation.

## Where changes belong

| Change | Where |
| --- | --- |
| File categorization, extensions, safety tiers | `src/DiskTree.Core/Classify/` |
| Directory scanning, multi-threading, progress | `src/DiskTree.Core/Scan/` |
| Tree hierarchy, node aggregation, sizing | `src/DiskTree.Core/Tree/` |
| Squarified treemap layout algorithm | `src/DiskTree.Core/Treemap/` |
| Deletion guards and removal execution | `src/DiskTree.Core/Removal/` |
| Volume space and cluster accounting | `src/DiskTree.Core/Space/` |
| Treemap painting, labels, hover rendering | `src/DiskTree.App/Controls/TreemapCanvasView.cs` |
| Color palettes, category hues, age gradients | `src/DiskTree.App/Rendering/DisktreePalette.cs` |
| App state, commands, active selection | `src/DiskTree.App/ViewModels/MainViewModel.cs` |
| Windows, views, XAML layout | `src/DiskTree.App/MainPage.xaml` |
| Original Rust reference implementation | `reference/` |
