# Graph Report - Script  (2026-09-06)

## Corpus Check
- 1 files · ~113,080 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1055 nodes · 2815 edges · 80 communities (43 shown, 37 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 146 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Block Cell Runtime State
- UI Reflection Setters
- Levels Screen Stage Paging
- Level Cell Grid Model
- Human Solver Pathfinding
- Level Completion & Save
- Screen Controller Navigation
- Level Pack Building
- Level Generation Range Building
- Difficulty Model Blending
- Campaign Pack Building
- Required Mechanic Validator
- Level Canonicalization
- Difficulty Report Model
- Generation Scheduler State
- Direction Enum
- Level Generation Scheduling
- Block Region Connectivity
- Puzzle Solver Reachability
- Pair Color Palette
- Level Validator Checks
- Menu Screen Animation
- Block Occupant Management
- Script File Nodes
- Level Mechanics Identification
- Level Button Script File
- Board Generator Rendering
- UI Controller Script Files
- Daily Challenge Screen UI
- Screen Animation Coroutines
- Game Mode & Daily Challenge
- Grid Size Enum
- Menu Screen Controller
- Input Manager Debounce
- Singleton Base Pattern
- Pack Verifier Solving
- Block Type Enum
- UIController Navigation Flow
- Permission Border Rendering
- Level Complete Screen UI
- Board Topology Coverage
- Pair Color Data Assets
- Game State Enum
- Object Pool Lifecycle
- Pin State Enum
- Uniqueness Policy Enum
- Solve Status Enum
- Color Type Reference
- Coroutine Type Reference
- Dictionary Type Reference
- EventSystem Type Reference
- IEnumerator Type Reference
- List Type Reference
- PairColorType Type Reference
- Vector3 Type Reference
- GameState Type Reference
- LevelScreenController Type Reference
- Button Type Reference
- Color Type Reference (Alt)
- GameObject Type Reference
- Image Type Reference
- List Type Reference (Alt)
- RectTransform Type Reference
- Sprite Type Reference
- TextMeshProUGUI Type Reference
- GameMode Type Reference
- TextMeshProUGUI Type Reference (Alt)
- Image Type Reference (Alt)
- RectTransform Type Reference (Alt)
- Sprite Type Reference (Alt)
- List Type Reference (Alt2)
- TextMeshProUGUI Type Reference (Alt2)
- Button Type Reference (Alt2)
- GameMode Type Reference (Alt2)
- GameObject Type Reference (Alt3)
- LevelData Type Reference (Alt)
- RectTransform Type Reference (Alt3)
- TextMeshProUGUI Type Reference (Alt3)
- GameObject Type Reference (Alt2)
- UnityAction Type Reference (Alt)

## God Nodes (most connected - your core abstractions)
1. `Block` - 186 edges
2. `LevelGenerator` - 153 edges
3. `GamePlayController` - 92 edges
4. `Direction` - 67 edges
5. `UIController` - 61 edges
6. `LevelData` - 37 edges
7. `SaveData` - 34 edges
8. `HumanSolver` - 32 edges
9. `LevelsScreenController` - 29 edges
10. `SolverOptions` - 25 edges

## Surprising Connections (you probably didn't know these)
- `GeneratedLevel` --references--> `LevelData`  [EXTRACTED]
  Editor/LevelGenerator.cs → ScriptableObject/LevelData.cs
- `LevelGenerator` --references--> `Direction`  [EXTRACTED]
  Editor/LevelGenerator.cs → Enums/Direction.cs
- `CellRecord` --references--> `Direction`  [EXTRACTED]
  GamePlay/LevelCanonicalizer.cs → Enums/Direction.cs
- `DifficultyAnalyzer` --references--> `Direction`  [EXTRACTED]
  GamePlay/DifficultyAnalyzer.cs → Enums/Direction.cs
- `Entry` --references--> `LevelData`  [EXTRACTED]
  Editor/LevelGenerator.cs → ScriptableObject/LevelData.cs

## Import Cycles
- None detected.

## Communities (80 total, 37 thin omitted)

### Community 0 - "Block Cell Runtime State"
Cohesion: 0.07
Nodes (22): Block, Color, Coroutine, Dictionary, Direction, EventSystem, Image, GamePlayController (+14 more)

### Community 1 - "UI Reflection Setters"
Cohesion: 0.06
Nodes (23): BlockType, Color, GameObject, Image, RectTransform, Sprite, Transform, Block (+15 more)

### Community 2 - "Levels Screen Stage Paging"
Cohesion: 0.08
Nodes (18): GameObject, Image, LevelButton, ObjectPool, RectTransform, TextMeshProUGUI, SettingScreen, Slider (+10 more)

### Community 3 - "Level Cell Grid Model"
Cohesion: 0.13
Nodes (18): A, AFront, AllowedPairId, B, BFront, CheckpointPairId, Dir, Col (+10 more)

### Community 4 - "Human Solver Pathfinding"
Cohesion: 0.14
Nodes (18): Cell, PairId, Dictionary, HashSet, List, HumanSolver, Rating, State (+10 more)

### Community 5 - "Level Completion & Save"
Cohesion: 0.09
Nodes (8): LevelData, AudioData, GameMode, MechanicSkill, PackProgress, SaveData, SavingSystem, Singleton

### Community 6 - "Screen Controller Navigation"
Cohesion: 0.06
Nodes (16): BoardGenerator, Button, DailyChallengeScreenController, LevelCompleteScreenController, LevelData, LevelsScreenController, PackSelectScreenController, SingleLevelDataSO (+8 more)

### Community 7 - "Level Pack Building"
Cohesion: 0.09
Nodes (10): dc, dr, SolveResult, GeneratedLevel, GenerationSpec, LevelGenerator, MaxDistinctColors, GenerationSpec (+2 more)

### Community 8 - "Level Generation Range Building"
Cohesion: 0.17
Nodes (5): Func, HashSet, MenuItem, GeneratedLevel, SingleLevelDataSO

### Community 9 - "Difficulty Model Blending"
Cohesion: 0.10
Nodes (14): DifficultyModel, Profile, HashSet, List, PairSolution, RelaxationMetrics, Result, Dictionary (+6 more)

### Community 10 - "Campaign Pack Building"
Cohesion: 0.26
Nodes (5): SolverOptions, Default, PinState, GridRow, LevelData

### Community 11 - "Required Mechanic Validator"
Cohesion: 0.16
Nodes (13): Action, BlockType, SolveResult, SolverOptions, RequiredMechanicValidator, RequirementResult, RequirementStatus, Inconclusive (+5 more)

### Community 12 - "Level Canonicalization"
Cohesion: 0.13
Nodes (16): BlockType, Col, Dictionary, Row, CellRecord, LevelCanonicalizer, TransformKind, AntiTranspose (+8 more)

### Community 13 - "Difficulty Report Model"
Cohesion: 0.12
Nodes (16): DifficultyReport, DifficultyTier, BlockType, Dictionary, List, PairSolution, SolveResult, DifficultyAnalyzer (+8 more)

### Community 14 - "Generation Scheduler State"
Cohesion: 0.18
Nodes (4): BlockType, CpuThrottle, MechanicRecipe, MechanicRecipe

### Community 15 - "Direction Enum"
Cohesion: 0.13
Nodes (8): Direction, Down, Left, None, Right, Up, List, HintPath

### Community 16 - "Level Generation Scheduling"
Cohesion: 0.14
Nodes (9): CpuThrottle, Data, Deficit, Entry, Entry, Instances, Mechanic, Score (+1 more)

### Community 17 - "Block Region Connectivity"
Cohesion: 0.16
Nodes (10): PairSolution, Col, Dictionary, List, Row, PairSolution, SearchContext, SolveResult (+2 more)

### Community 18 - "Puzzle Solver Reachability"
Cohesion: 0.27
Nodes (6): Exception, BudgetExceededException, PuzzleSolver, Queue, SearchContext, SolverState

### Community 19 - "Pair Color Palette"
Cohesion: 0.10
Nodes (20): PairColorType, Amber, Blue, Brown, Cyan, Green, Indigo, Lime (+12 more)

### Community 20 - "Level Validator Checks"
Cohesion: 0.25
Nodes (3): Dictionary, List, LevelValidator

### Community 21 - "Menu Screen Animation"
Cohesion: 0.14
Nodes (8): AudioClip, AudioManager, BgVolume, IsBgMute, IsSFXMute, SFXVolume, Coroutine, AudioSource

### Community 24 - "Level Mechanics Identification"
Cohesion: 0.15
Nodes (12): LevelMechanics, MechanicFlags, Arrow, Blocked, Bridge, Checkpoint, Forbidden, None (+4 more)

### Community 25 - "Level Button Script File"
Cohesion: 0.13
Nodes (13): Button, Color, GameObject, Image, RectTransform, Sprite, TextMeshProUGUI, LevelButton (+5 more)

### Community 26 - "Board Generator Rendering"
Cohesion: 0.24
Nodes (5): Image, RectTransform, Sprite, BoardGenerator, BoardArea

### Community 27 - "UI Controller Script Files"
Cohesion: 0.18
Nodes (4): FreeFlow.UI, PackCard, Transform, PackSelectScreenController

### Community 28 - "Daily Challenge Screen UI"
Cohesion: 0.23
Nodes (4): GameObject, RectTransform, TextMeshProUGUI, DailyChallengeScreenController

### Community 29 - "Screen Animation Coroutines"
Cohesion: 0.22
Nodes (7): IEnumerator, List, Transform, ScreenAnimation, TotolTime, IEnumerator, WaitForSeconds

### Community 30 - "Game Mode & Daily Challenge"
Cohesion: 0.27
Nodes (6): DateTime, GameMode, Advanced, Classic, DailyChallengeSelector, Pick

### Community 31 - "Grid Size Enum"
Cohesion: 0.18
Nodes (10): GridSize, GridSize_10X10, GridSize_11X11, GridSize_12X12, GridSize_4X4, GridSize_5X5, GridSize_6X6, GridSize_7X7 (+2 more)

### Community 32 - "Menu Screen Controller"
Cohesion: 0.29
Nodes (5): GameMode, SaveData, TextMeshProUGUI, MenuScreenController, UIController

### Community 33 - "Input Manager Debounce"
Cohesion: 0.36
Nodes (4): Coroutine, EventSystem, IEnumerator, InputManager

### Community 34 - "Singleton Base Pattern"
Cohesion: 0.20
Nodes (5): FreeFlow.Util, FreeFlow.Input, MonoBehaviour, Singleton, Instance

### Community 35 - "Pack Verifier Solving"
Cohesion: 0.29
Nodes (5): MenuItem, SolveResult, PackVerifier, SolveResult, SolverOptions

### Community 36 - "Block Type Enum"
Cohesion: 0.20
Nodes (9): BlockType, AllowedForPairs, Arrow, Blocked, Bridge, Checkpoint, ForbiddenForPair, Normal (+1 more)

### Community 38 - "Permission Border Rendering"
Cohesion: 0.31
Nodes (4): Color, Image, PermissionBorderView, Material

### Community 39 - "Level Complete Screen UI"
Cohesion: 0.28
Nodes (5): GameObject, Image, RectTransform, TextMeshProUGUI, LevelCompleteScreenController

### Community 40 - "Board Topology Coverage"
Cohesion: 0.36
Nodes (4): Dictionary, Func, List, BoardTopology

### Community 41 - "Pair Color Data Assets"
Cohesion: 0.25
Nodes (5): ScriptableObject, LevelDataSO, Color, PairColorData, PairColorDataSO

### Community 42 - "Game State Enum"
Cohesion: 0.33
Nodes (5): GameState, Ending, Paused, Playing, Waiting

### Community 44 - "Pin State Enum"
Cohesion: 0.50
Nodes (4): PinState, Ambiguous, Broken, Unique

### Community 45 - "Uniqueness Policy Enum"
Cohesion: 0.50
Nodes (4): UniquenessPolicy, Ignore, Prefer, Require

### Community 46 - "Solve Status Enum"
Cohesion: 0.50
Nodes (4): SolveStatus, Inconclusive, Solved, Unsolvable

## Knowledge Gaps
- **130 isolated node(s):** `CheckpointCellCount`, `FilledCellCount`, `GameState`, `HintAvailable`, `SatisfiedCheckpointCount` (+125 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 293 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **37 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Block` connect `UI Reflection Setters` to `Human Solver Pathfinding`, `Level Pack Building`, `Level Generation Range Building`, `Campaign Pack Building`, `Required Mechanic Validator`, `Level Canonicalization`, `Difficulty Report Model`, `Generation Scheduler State`, `Direction Enum`, `Block Region Connectivity`, `Puzzle Solver Reachability`, `Level Validator Checks`, `Block Occupant Management`, `Script File Nodes`, `Board Generator Rendering`, `Singleton Base Pattern`, `Pack Verifier Solving`, `Permission Border Rendering`, `Board Topology Coverage`?**
  _High betweenness centrality (0.452) - this node is a cross-community bridge._
- **Why does `LevelGenerator` connect `Level Pack Building` to `UI Reflection Setters`, `Level Cell Grid Model`, `Level Generation Range Building`, `Campaign Pack Building`, `Pin State Enum`, `Uniqueness Policy Enum`, `Generation Scheduler State`, `Direction Enum`, `Level Generation Scheduling`, `Puzzle Solver Reachability`, `Script File Nodes`?**
  _High betweenness centrality (0.192) - this node is a cross-community bridge._
- **Why does `GamePlayController` connect `Block Cell Runtime State` to `UI Controller Script Files`, `Level Completion & Save`?**
  _High betweenness centrality (0.139) - this node is a cross-community bridge._
- **What connects `CheckpointCellCount`, `FilledCellCount`, `GameState` to the rest of the system?**
  _130 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Block Cell Runtime State` be split into smaller, more focused modules?**
  _Cohesion score 0.07431168136861802 - nodes in this community are weakly interconnected._
- **Should `UI Reflection Setters` be split into smaller, more focused modules?**
  _Cohesion score 0.061367621274108705 - nodes in this community are weakly interconnected._
- **Should `Levels Screen Stage Paging` be split into smaller, more focused modules?**
  _Cohesion score 0.07541478129713423 - nodes in this community are weakly interconnected._