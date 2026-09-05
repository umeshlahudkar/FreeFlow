# Graph Report - Script  (2026-09-05)

## Corpus Check
- 42 files · ~108,292 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 947 nodes · 2753 edges · 38 communities (35 shown, 2 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 191 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Block Visual Feedback
- Level Pack Generation
- Block Rendering & State
- Puzzle Grid Data Structures
- Screen & Audio Animation
- Human-Style Puzzle Solver
- Board Layout Generation
- Gameplay UI Controller
- Game Mode & Save Data
- Level Select Button
- Board Topology & Difficulty
- Classic Campaign Building
- Required Mechanic Validation
- Direction & Wall Logic
- Level Canonicalization
- Advanced Pack Building
- Difficulty Ramp Scheduling
- Pair Color Palette
- Difficulty Model & Gates
- Puzzle Solver Core
- Level Validator
- Solver State & Solutions
- Core Gameplay Files
- Daily Challenge Selection
- Level Mechanic Flags
- Relaxation Metrics
- Pack Verification
- Grid Size Enum
- Block Type Enum
- UI & Input Files
- Level Data ScriptableObjects
- Game State Enum
- Save System
- UI Utility Helpers
- Pin State Enum
- Uniqueness Policy Enum
- Solve Status Enum

## God Nodes (most connected - your core abstractions)
1. `Block` - 219 edges
2. `LevelGenerator` - 153 edges
3. `GamePlayController` - 93 edges
4. `Direction` - 76 edges
5. `UIController` - 47 edges
6. `LevelData` - 41 edges
7. `HumanSolver` - 32 edges
8. `SaveData` - 31 edges
9. `FreeFlow.Enums` - 25 edges
10. `SolverOptions` - 25 edges

## Surprising Connections (you probably didn't know these)
- `LevelGenerator` --references--> `Direction`  [EXTRACTED]
  Editor/LevelGenerator.cs → Enums/Direction.cs
- `GeneratedLevel` --references--> `LevelData`  [EXTRACTED]
  Editor/LevelGenerator.cs → ScriptableObject/LevelData.cs
- `Entry` --references--> `LevelData`  [EXTRACTED]
  Editor/LevelGenerator.cs → ScriptableObject/LevelData.cs
- `GridRow` --references--> `BlockType`  [EXTRACTED]
  ScriptableObject/LevelData.cs → Enums/BlockType.cs
- `Block` --references--> `Direction`  [EXTRACTED]
  GamePlay/Block.cs → Enums/Direction.cs

## Import Cycles
- None detected.

## Communities (38 total, 2 thin omitted)

### Community 0 - "Block Visual Feedback"
Cohesion: 0.06
Nodes (21): Func, Color, Coroutine, Dictionary, EventSystem, IEnumerator, Image, List (+13 more)

### Community 1 - "Level Pack Generation"
Cohesion: 0.07
Nodes (14): dc, dr, Func, MenuItem, SolveResult, GeneratedLevel, GenerationSpec, LevelGenerator (+6 more)

### Community 2 - "Block Rendering & State"
Cohesion: 0.06
Nodes (24): BlockType, Color, GameObject, Image, PairColorType, RectTransform, Sprite, Transform (+16 more)

### Community 3 - "Puzzle Grid Data Structures"
Cohesion: 0.15
Nodes (18): A, AFront, AllowedPairId, B, BFront, CheckpointPairId, Dir, Col (+10 more)

### Community 4 - "Screen & Audio Animation"
Cohesion: 0.06
Nodes (21): IEnumerator, List, Transform, ScreenAnimation, TotolTime, AudioClip, AudioManager, BgVolume (+13 more)

### Community 5 - "Human-Style Puzzle Solver"
Cohesion: 0.14
Nodes (18): Cell, PairId, Dictionary, HashSet, List, HumanSolver, Rating, State (+10 more)

### Community 6 - "Board Layout Generation"
Cohesion: 0.07
Nodes (16): Image, RectTransform, Sprite, BoardGenerator, BoardArea, Color, Image, PermissionBorderView (+8 more)

### Community 7 - "Gameplay UI Controller"
Cohesion: 0.09
Nodes (11): Button, GameObject, TextMeshProUGUI, UIController, CurrentLevel, CurrentLevelGoal, CurrentMode, CurrentPackSize (+3 more)

### Community 8 - "Game Mode & Save Data"
Cohesion: 0.11
Nodes (7): GameMode, Advanced, Classic, AudioData, MechanicSkill, PackProgress, SaveData

### Community 9 - "Level Select Button"
Cohesion: 0.11
Nodes (15): Color, Image, RectTransform, TextMeshProUGUI, LevelButton, ThisTransform, Color, Dictionary (+7 more)

### Community 10 - "Board Topology & Difficulty"
Cohesion: 0.10
Nodes (19): DifficultyReport, DifficultyTier, Dictionary, List, BoardTopology, BlockType, Dictionary, List (+11 more)

### Community 11 - "Classic Campaign Building"
Cohesion: 0.30
Nodes (4): SolverOptions, Default, PinState, LevelData

### Community 12 - "Required Mechanic Validation"
Cohesion: 0.16
Nodes (13): Action, BlockType, SolveResult, SolverOptions, RequiredMechanicValidator, RequirementResult, RequirementStatus, Inconclusive (+5 more)

### Community 13 - "Direction & Wall Logic"
Cohesion: 0.13
Nodes (8): Direction, Down, Left, None, Right, Up, List, HintPath

### Community 14 - "Level Canonicalization"
Cohesion: 0.13
Nodes (16): BlockType, Col, Dictionary, Row, CellRecord, LevelCanonicalizer, TransformKind, AntiTranspose (+8 more)

### Community 15 - "Advanced Pack Building"
Cohesion: 0.21
Nodes (5): BlockType, Dictionary, CpuThrottle, MechanicRecipe, MechanicRecipe

### Community 16 - "Difficulty Ramp Scheduling"
Cohesion: 0.14
Nodes (9): CpuThrottle, Data, Deficit, Entry, Entry, Instances, Mechanic, Score (+1 more)

### Community 17 - "Pair Color Palette"
Cohesion: 0.10
Nodes (20): PairColorType, Amber, Blue, Brown, Cyan, Green, Indigo, Lime (+12 more)

### Community 18 - "Difficulty Model & Gates"
Cohesion: 0.13
Nodes (10): DifficultyModel, Profile, Result, Dictionary, SolveResult, Report, StructuralGates, Profile (+2 more)

### Community 19 - "Puzzle Solver Core"
Cohesion: 0.32
Nodes (6): Exception, BudgetExceededException, PuzzleSolver, Queue, SearchContext, SolverState

### Community 20 - "Level Validator"
Cohesion: 0.25
Nodes (3): Dictionary, List, LevelValidator

### Community 21 - "Solver State & Solutions"
Cohesion: 0.19
Nodes (10): PairSolution, Col, Dictionary, List, Row, PairSolution, SearchContext, SolveResult (+2 more)

### Community 23 - "Daily Challenge Selection"
Cohesion: 0.17
Nodes (5): DateTime, DailyChallengeSelector, Pick, Pick, RectTransform

### Community 24 - "Level Mechanic Flags"
Cohesion: 0.16
Nodes (12): LevelMechanics, MechanicFlags, Arrow, Blocked, Bridge, Checkpoint, Forbidden, None (+4 more)

### Community 25 - "Relaxation Metrics"
Cohesion: 0.29
Nodes (5): HashSet, List, PairSolution, RelaxationMetrics, GridRow

### Community 26 - "Pack Verification"
Cohesion: 0.23
Nodes (5): MenuItem, SolveResult, PackVerifier, SolveResult, SolverOptions

### Community 27 - "Grid Size Enum"
Cohesion: 0.18
Nodes (10): GridSize, GridSize_10X10, GridSize_11X11, GridSize_12X12, GridSize_4X4, GridSize_5X5, GridSize_6X6, GridSize_7X7 (+2 more)

### Community 28 - "Block Type Enum"
Cohesion: 0.20
Nodes (9): BlockType, AllowedForPairs, Arrow, Blocked, Bridge, Checkpoint, ForbiddenForPair, Normal (+1 more)

### Community 30 - "UI & Input Files"
Cohesion: 0.39
Nodes (3): FreeFlow.Util, FreeFlow.Input, FreeFlow.UI

### Community 31 - "Level Data ScriptableObjects"
Cohesion: 0.25
Nodes (5): ScriptableObject, LevelDataSO, Color, PairColorData, PairColorDataSO

### Community 32 - "Game State Enum"
Cohesion: 0.33
Nodes (5): GameState, Ending, Paused, Playing, Waiting

### Community 34 - "UI Utility Helpers"
Cohesion: 0.47
Nodes (3): GameObject, UnityAction, UIUtils

### Community 35 - "Pin State Enum"
Cohesion: 0.50
Nodes (4): PinState, Ambiguous, Broken, Unique

### Community 36 - "Uniqueness Policy Enum"
Cohesion: 0.50
Nodes (4): UniquenessPolicy, Ignore, Prefer, Require

### Community 37 - "Solve Status Enum"
Cohesion: 0.50
Nodes (4): SolveStatus, Inconclusive, Solved, Unsolvable

## Knowledge Gaps
- **126 isolated node(s):** `TotolTime`, `BgVolume`, `SFXVolume`, `IsBgMute`, `IsSFXMute` (+121 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 233 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Block` connect `Block Rendering & State` to `Block Visual Feedback`, `Level Pack Generation`, `Human-Style Puzzle Solver`, `Board Layout Generation`, `Board Topology & Difficulty`, `Classic Campaign Building`, `Required Mechanic Validation`, `Direction & Wall Logic`, `Level Canonicalization`, `Advanced Pack Building`, `Puzzle Solver Core`, `Level Validator`, `Solver State & Solutions`, `Core Gameplay Files`, `Pack Verification`, `Pair Connectivity Checks`?**
  _High betweenness centrality (0.423) - this node is a cross-community bridge._
- **Why does `LevelGenerator` connect `Level Pack Generation` to `Puzzle Grid Data Structures`, `Pin State Enum`, `Uniqueness Policy Enum`, `Classic Campaign Building`, `Direction & Wall Logic`, `Advanced Pack Building`, `Difficulty Ramp Scheduling`, `Core Gameplay Files`?**
  _High betweenness centrality (0.199) - this node is a cross-community bridge._
- **Why does `GamePlayController` connect `Block Visual Feedback` to `Block Rendering & State`, `Screen & Audio Animation`, `Game Mode & Save Data`, `Direction & Wall Logic`, `Level Mechanic Flags`, `Pack Verification`, `Pair Connectivity Checks`, `UI & Input Files`, `Level Data ScriptableObjects`?**
  _High betweenness centrality (0.183) - this node is a cross-community bridge._
- **Are the 27 inferred relationships involving `Random` (e.g. with `.BuildAdvancedPack()` and `.BuildSizePack()`) actually correct?**
  _`Random` has 27 INFERRED edges - model-reasoned connections that need verification._
- **What connects `TotolTime`, `BgVolume`, `SFXVolume` to the rest of the system?**
  _126 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Block Visual Feedback` be split into smaller, more focused modules?**
  _Cohesion score 0.06483075157773953 - nodes in this community are weakly interconnected._
- **Should `Level Pack Generation` be split into smaller, more focused modules?**
  _Cohesion score 0.06593406593406594 - nodes in this community are weakly interconnected._