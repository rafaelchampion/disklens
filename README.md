# DiskLens

> **A fast, native Windows disk space visualizer and cleanup tool.**  
> Built with C#, .NET 9, WinUI 3, and Win2D.

![Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![WinUI 3](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D7)
![License](https://img.shields.io/badge/License-MIT-blue.svg)

---

## What is DiskLens?

**DiskLens** helps you find what is eating your drives, safely mark space for reclamation, and clean up with complete confidence. 

Inspired by the treemap concepts of [disktree](reference/README.md) (originally written in Rust with GPUI), DiskLens is built from the ground up as a first-class, high-performance native Windows client. It combines the visual clarity of squarified treemaps with Windows-specific disk intelligence, safety tiers, and modern fluent design.

### Two-Phase Workflow: Never Accidental Deletions

1. **Explore**:
   - **Squarified Treemap**: Every file and folder rendered as a nested mosaic sized by actual disk allocation (`st_blocks` / cluster sizes).
   - **Deep Zoom & Breadcrumbs**: Navigate subtrees with fluid mouse clicks or keyboard navigation.
   - **Category & Heuristic Coloring**: Immediate visual classification of media, dev environments, games, documents, and system files.
   - **Live Volume Gauge**: Watch free space and projected savings live as you explore.

2. **Review & Clean**:
   - **Non-Destructive Marking**: Marking files or directories for removal never deletes immediately.
   - **Safety Guards**: Mount points, root directories, system trees, and critical OS files are strictly guarded and cannot be marked.
   - **Audit List & Commitment**: Review your cleanup candidate list, verify projected space reclaimed against actual volume clusters, and execute with confirmation.

---

## Architecture & Project Structure

```
disktree/
├── src/                          # Active .NET 9 / WinUI 3 codebase
│   ├── DiskTree.App/             # WinUI 3 desktop client (XAML + Win2D)
│   │   ├── Controls/             # TreemapCanvasView & custom canvas controls
│   │   ├── Rendering/            # Color palettes, HSL conversions & brush caches
│   │   ├── ViewModels/           # MVVM state management
│   │   └── MainPage.xaml         # Explorer view, sidebar, and breadcrumbs
│   ├── DiskTree.Core/            # Platform-independent core engine
│   │   ├── Classify/             # File categorization & reclaim rules
│   │   ├── Insights/             # Disk space insights & large consumers
│   │   ├── Removal/              # Safe path guards and removal engine
│   │   ├── Scan/                 # High-throughput multi-threaded directory scanner
│   │   ├── Space/                # Volume statistics & disk geometry
│   │   ├── Tree/                 # Tree aggregation & weight computation
│   │   └── Treemap/              # Squarified layout algorithm & tile geometry
│   ├── DiskTree.Core.Tests/      # Unit & integration test suite
│   └── DiskTree.slnx             # Solution file
│
├── reference/                    # Original Rust + GPUI implementation
│   ├── crates/                   # disktree-core & disktree-app (Rust)
│   ├── packaging/                # Linux & macOS packaging specs
│   └── README.md                 # Upstream disktree documentation
│
└── README.md                     # This file
```

---

## Getting Started

### Prerequisites

- **OS**: Windows 10 version 1809 (Build 17763) or Windows 11
- **SDK**: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or latest .NET SDK)
- Visual Studio 2022 (v17.12+) or VS Code with C# Dev Kit
- *Windows App SDK Workload* included with .NET / Visual Studio

### Building and Running

1. **Clone the repository:**
   ```powershell
   git clone https://github.com/rafaelchampion/disklens.git
   cd disklens
   ```

2. **Build the solution:**
   ```powershell
   dotnet build src/DiskTree.slnx
   ```

3. **Run the desktop app:**
   ```powershell
   dotnet run --project src/DiskTree.App/DiskTree.App.csproj
   ```

4. **Run the automated tests:**
   ```powershell
   dotnet test src/DiskTree.slnx
   ```

---

## Roadmap

Development is tracked through GitHub issues across defined phases:

- [x] **Repository Setup & Baseline**: Forked from upstream, remotes configured, .NET 9 WinUI client imported.
- [ ] **Phase 1: Core UX Enhancements**
  - [Issue #1](https://github.com/rafaelchampion/disklens/issues/1): Port original category hue map to WinUI palette
  - [Issue #2](https://github.com/rafaelchampion/disklens/issues/2): Scan progress bar with volume percentage indication
  - [Issue #3](https://github.com/rafaelchampion/disklens/issues/3): Full solution rename to DiskLens
- [ ] **Phase 2: Windows Heuristics & Safety Tiers**
  - [Issue #4](https://github.com/rafaelchampion/disklens/issues/4): Expand Windows-specific reclaimable items (Green/Yellow/Red tiers)
  - [Issue #5](https://github.com/rafaelchampion/disklens/issues/5): Add Gaming, Browser, and Windows Component categories
- [ ] **Phase 3: Cleanup Advisor**
  - [Issue #6](https://github.com/rafaelchampion/disklens/issues/6): Dedicated Cleanup Advisor side panel with 1-click recommendations
  - [Issue #7](https://github.com/rafaelchampion/disklens/issues/7): Collapsible Expander sections for side panel
- [ ] **Phase 4: Visual Analytics**
  - [Issue #8](https://github.com/rafaelchampion/disklens/issues/8): Category breakdown interactive donut chart
  - [Issue #9](https://github.com/rafaelchampion/disklens/issues/9): Top 10 largest files list
  - [Issue #10](https://github.com/rafaelchampion/disklens/issues/10): File extension breakdown with bar charts
  - [Issue #11](https://github.com/rafaelchampion/disklens/issues/11): Enhanced per-drive summary dropdown
- [ ] **Phase 5: Advanced Visualizations**
  - [Issue #12](https://github.com/rafaelchampion/disklens/issues/12): "Color by Age" visualization mode

---

## Credits & License

- **DiskLens** is developed and maintained by [Rafael Champion](https://github.com/rafaelchampion).
- Inspired by **disktree** created by [Tobi Lütke](https://github.com/tobi/disktree) and contributors under the MIT License.
- Licensed under the [MIT License](LICENSE).
