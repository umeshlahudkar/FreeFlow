"""Author FreeFlow level assets from a readable ASCII spec.

Each level teaches exactly one mechanic. Cell tokens:
  .        empty Normal cell
  1..9     pair dot of that pairId (colour = same value)
  B        Blocked
  C:n      Checkpoint for pair n
  F:n      ForbiddenForPair -- pair n may not enter
  F:n+m    ForbiddenForPair -- neither pair n nor pair m may enter
  O:d      OneWay, requiredEntryDirection d (L/R/U/D)
  G:n      Gate depending on pair n
  M        Mixed (shared) cell
  A:d      Arrow, forcedExitDirection d (L/R/U/D)
  X        Bridge (crossing: one lane per axis, no turning)
  S:n      Splitter junction for pair n (that pair has THREE dots)
  R:r      Rotator, initial elbow r (0=Up+Right, then clockwise)
  P:n      AllowedForPairs -- only pair n may enter
  P:n+m    AllowedForPairs -- only pairs n and m may enter
  a+b      one dot that is the destination for pairs a and b
  a+b+c    ... and c; up to four colours may share one destination
Walls are listed separately as (row, col, edge) and authored on BOTH sides of the edge.
"""
import io
import os
import re
import sys
import uuid

SCRIPT_GUID = 'd6ecbb34cd6c56a4fbdf46e52ca3b366'  # SingleLevelDataSO
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))

DIRS = {'L': 1, 'R': 2, 'U': 3, 'D': 4}
WALL_BIT = {'L': 1, 'R': 2, 'U': 4, 'D': 8}
OPPOSITE = {'L': 'R', 'R': 'L', 'U': 'D', 'D': 'U'}
BLOCKTYPE = {'B': 1, 'C': 2, 'F': 3, 'O': 4, 'G': 5, 'M': 6, 'A': 7, 'X': 8, 'S': 9,
             'R': 10, 'P': 11}


def pack(values):
    """Unity serialises int/enum arrays as a little-endian 4-byte-per-entry hex string."""
    return ''.join(int(v).to_bytes(4, 'little', signed=True).hex() for v in values)


def build(spec):
    rows = [r.split() for r in spec['grid'].strip().split('\n')]
    n = len(rows)
    assert all(len(r) == n for r in rows), 'grid must be square'

    coloum, pairid, blocktype, wallmask, reqdir, exitdir, rot = ([], [], [], [], [], [], [])
    second, third, fourth = ([], [], [])
    for r, row in enumerate(rows):
        c_row, p_row, b_row, w_row, d_row, x_row, r_row = ([], [], [], [], [], [], [])
        s_row, t_row, f_row = ([], [], [])
        for cell in row:
            colour = pair = btype = direction = exit_dir = rotation = 0
            second_pair = third_pair = fourth_pair = 0
            if cell == '.':
                pass
            elif '+' in cell and cell[0].isdigit():
                # one cell, several dot identities: the shared destination of up to four pairs
                shared = [int(v) for v in cell.split('+')]
                assert len(shared) <= 4, 'at most four colours can share a destination: %s' % cell
                colour = pair = shared[0]
                if len(shared) > 1:
                    second_pair = shared[1]
                if len(shared) > 2:
                    third_pair = shared[2]
                if len(shared) > 3:
                    fourth_pair = shared[3]
            elif cell.isdigit():
                colour = pair = int(cell)
            else:
                key = cell[0]
                btype = BLOCKTYPE[key]
                arg = cell[2:] if len(cell) > 2 else ''
                if key == 'O':
                    direction = DIRS[arg]
                elif key == 'A':
                    exit_dir = DIRS[arg]
                elif key == 'R':
                    rotation = int(arg)
                elif key in ('P', 'F'):
                    # one or two NAMED pairs -- permitted for P, refused for F -- in the same two
                    # columns a shared destination uses
                    named = arg.split('+')
                    pair = int(named[0])
                    if len(named) > 1:
                        second_pair = int(named[1])
                elif arg:
                    pair = int(arg)
            c_row.append(colour)
            p_row.append(pair)
            b_row.append(btype)
            w_row.append(0)
            d_row.append(direction)
            x_row.append(exit_dir)
            r_row.append(rotation)
            s_row.append(second_pair)
            t_row.append(third_pair)
            f_row.append(fourth_pair)
        coloum.append(c_row)
        pairid.append(p_row)
        blocktype.append(b_row)
        wallmask.append(w_row)
        reqdir.append(d_row)
        exitdir.append(x_row)
        rot.append(r_row)
        second.append(s_row)
        third.append(t_row)
        fourth.append(f_row)

    # a wall belongs to the edge, so author it from both cells that share it
    for (r, c, edge) in spec.get('walls', []):
        wallmask[r][c] |= WALL_BIT[edge]
        dr, dc = {'L': (0, -1), 'R': (0, 1), 'U': (-1, 0), 'D': (1, 0)}[edge]
        nr, nc = r + dr, c + dc
        if 0 <= nr < n and 0 <= nc < n:
            wallmask[nr][nc] |= WALL_BIT[OPPOSITE[edge]]

    dots = {}
    for r in range(n):
        for c in range(n):
            if coloum[r][c]:
                dots[coloum[r][c]] = dots.get(coloum[r][c], 0) + 1
            if second[r][c] and blocktype[r][c] not in (BLOCKTYPE['P'], BLOCKTYPE['F']):
                dots[second[r][c]] = dots.get(second[r][c], 0) + 1
            # the third and fourth are dot identities only -- no rule reads them
            for extra in (third[r][c], fourth[r][c]):
                if extra:
                    dots[extra] = dots.get(extra, 0) + 1

    # a splitter pair has three dots meeting at its junction; everything else has two
    splitter_pairs = set()
    for r in range(n):
        for c in range(n):
            if blocktype[r][c] == BLOCKTYPE['S']:
                splitter_pairs.add(pairid[r][c])
    for pair, count in dots.items():
        expected = 3 if pair in splitter_pairs else 2
        assert count == expected, 'pair %d has %d dots, expected %d' % (pair, count, expected)

    lines = []
    lines.append('%YAML 1.1')
    lines.append('%TAG !u! tag:unity3d.com,2011:')
    lines.append('--- !u!114 &11400000')
    lines.append('MonoBehaviour:')
    lines.append('  m_ObjectHideFlags: 0')
    lines.append('  m_CorrespondingSourceObject: {fileID: 0}')
    lines.append('  m_PrefabInstance: {fileID: 0}')
    lines.append('  m_PrefabAsset: {fileID: 0}')
    lines.append('  m_GameObject: {fileID: 0}')
    lines.append('  m_Enabled: 1')
    lines.append('  m_EditorHideFlags: 0')
    lines.append('  m_Script: {fileID: 11500000, guid: %s, type: 3}' % SCRIPT_GUID)
    lines.append('  m_Name: %s' % spec['name'])
    lines.append('  m_EditorClassIdentifier: Assembly-CSharp::SingleLevelDataSO')
    lines.append('  levelData:')
    lines.append('    gridSize: %d' % n)
    lines.append('    pairCount: %d' % len(dots))
    lines.append('    gridRows:')
    for r in range(n):
        lines.append('    - coloum: %s' % pack(coloum[r]))
        lines.append('      pairId: %s' % pack(pairid[r]))
        lines.append('      blockType: %s' % pack(blocktype[r]))
        lines.append('      wallMask: %s' % pack(wallmask[r]))
        lines.append('      requiredEntryDirection: %s' % pack(reqdir[r]))
        lines.append('      forcedExitDirection: %s' % pack(exitdir[r]))
        lines.append('      initialRotation: %s' % pack(rot[r]))
        lines.append('      secondPairId: %s' % pack(second[r]))
        lines.append('      thirdPairId: %s' % pack(third[r]))
        lines.append('      fourthPairId: %s' % pack(fourth[r]))
    constraints = spec.get('constraints', [])
    if constraints:
        lines.append('    pairConstraints:')
        for pair, length in constraints:
            lines.append('    - pairId: %d' % pair)
            lines.append('      requiredPathLength: %d' % length)
    else:
        lines.append('    pairConstraints: []')
    return '\n'.join(lines) + '\n'


META = """fileFormatVersion: 2
guid: %s
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# ---------------------------------------------------------------- level specs

LEVELS = [
    # 1 - Blocked cell. Pair 1's straight run along the top row is cut, so it has to
    #     drop into row 1 and come back up. Pair 2 has the bottom row to itself.
    dict(name='Level_1', mechanic='Blocked cell', grid="""
1 . B . 1
. . . . .
. . . . .
. . . . .
2 . . . 2
"""),

    # 2 - Wall. The edge between (2,1) and (2,2) is closed, so pair 1 cannot run straight
    #     along its own row and detours through row 3.
    dict(name='Level_2', mechanic='Wall', grid="""
2 . . . 2
. . . . .
1 . . . 1
. . . . .
. . . . .
""", walls=[(2, 1, 'R')]),

    # 3 - One-way. (2,2) may only be entered while moving Down, which is exactly how
    #     pair 2 crosses it and exactly what pair 1 cannot do travelling sideways.
    dict(name='Level_3', mechanic='One-way', grid="""
. . . . .
. . 2 . .
1 . O:D . 1
. . 2 . .
. . . . .
"""),

    # 4 - Forbidden cell. The cell refuses pair 1 AND pair 3 while letting pair 2 run straight
    #     down through it, so the rule reads as a set of colours rather than one exception.
    #     Pair 1 is the refusal the player feels: its own row is cut, and the only way round is
    #     the empty bottom row. Pair 3 never needs the cell -- trying it is the discovery that
    #     the marker's second colour means something.
    dict(name='Level_4', mechanic='Forbidden cell', grid="""
3 . . . 3
. . 2 . .
1 . F:1+3 . 1
. . 2 . .
. . . . .
"""),

    # 5 - Shared cell. Both pairs must cross the middle: without a Mixed cell this board
    #     is a deadlock, which is the point of the mechanic.
    dict(name='Level_5', mechanic='Shared cell', grid="""
. . 2 . .
. . . . .
1 . M . 1
. . . . .
. . 2 . .
"""),

    # 6 - Checkpoint. Pair 1 can reach its partner along its own row, but not without
    #     also visiting the marked cell in the bottom row.
    dict(name='Level_6', mechanic='Checkpoint', grid="""
2 . . . 2
. . . . .
1 . . . 1
. . . . .
. . C:1 . .
"""),

    # 7 - Exact length. Straight across is 5 cells; the constraint asks for 9, so the
    #     player has to deliberately take the long way round.
    dict(name='Level_7', mechanic='Exact length', grid="""
2 . . . 2
. . . . .
1 . . . 1
. . . . .
. . . . .
""", constraints=[(1, 9)]),

    # 8 - Gate. Pair 1's dot is walled in by two cells that both depend on pair 2, so the
    #     board can only be solved in one order. Pair 2's own route never touches them.
    dict(name='Level_8', mechanic='Gate', grid="""
1 G:2 . . 1
G:2 . . . .
. . . . .
. . . . .
2 . . . 2
"""),

    # 9 - Arrow. Both cells next to pair 1's corner dot are arrows, so every route out of it is
    #     one the arrow chooses: the path gets pushed along rather than blocked. Coming back
    #     through one is refused outright -- an arrow cannot be entered head-on.
    dict(name='Level_9', mechanic='Arrow', grid="""
1 A:R . . 1
A:D . . . .
. . . . .
. . . . .
2 . . . 2
"""),

    # 10 - Bridge. Same board as level 5's shared cell, one token different: both pairs still
    #      have to cross the middle, but now each may only pass straight through. Adjacent level
    #      numbers so the permissive and strict versions of sharing can be compared.
    dict(name='Level_10', mechanic='Bridge', grid="""
. . 2 . .
. . . . .
1 . X . 1
. . . . .
. . 2 . .
"""),

    # 11 - Splitter. Pair 1 has three dots instead of two and is complete only when all three
    #      reach the junction. Pair 2 keeps to the far column, out of the way, so the branching
    #      is the only thing to think about.
    dict(name='Level_11', mechanic='Splitter', grid="""
. . 1 . 2
. . . . .
1 . S:1 . .
. . . . .
. . 1 . 2
"""),

    # 12 - Rotator. Both cells beside pair 1's corner dot are rotators, and neither starts joined
    #      to it, so the board has to be turned before a path can leave at all. Every rotation is
    #      an elbow, so whichever one the player opens, the route bends.
    dict(name='Level_12', mechanic='Rotator', grid="""
1 R:0 . . .
R:1 . . . .
. . . . 1
. . . . .
2 . . . 2
"""),

    # 13 - Shared destination. FOUR sources, ONE goal: each colour runs its own path to the same
    #      cell, drawn as four tangent circles, one per colour. No pair has a partner dot of its
    #      own, so the hub is the only place any of them can finish.
    #
    #      Four is the ceiling, and the board shows why: a path that ends in a cell claims the one
    #      edge it arrived through, and the hub has four. Each colour comes in from a different
    #      side, which is the only way all four fit.
    dict(name='Level_13', mechanic='Shared destination', grid="""
1 . . . 2
. . . . .
. . 1+2+3+4 . .
. . . . .
3 . . . 4
"""),

    # 14 - Permitted colours. The mirror of level 4: instead of naming the one colour that may
    #      NOT pass, the cell names the ones that may. Pair 1 is refused mid-drag along its own
    #      row and has to go the long way round the top; pair 2 runs straight down through it.
    #      Pair 3 is the reason the cell needs two slots rather than one -- it is permitted too,
    #      so the marker carries two colours, and the rule is visibly about a set and not a
    #      single exception.
    dict(name='Level_14', mechanic='Permitted colours', grid="""
. . . . . .
. . . 2 . .
1 . . P:2+3 . 1
. . . 2 . .
3 . . . . 3
. . . . . .
"""),
]


if __name__ == '__main__':
    for spec in LEVELS:
        body = build(spec)
        io.open(os.path.join(OUT, spec['name'] + '.asset'), 'w',
                encoding='utf-8', newline='\n').write(body)
        # Levels are reached by path (Resources.Load "Levels/Level_n"), so a fresh guid
        # would break nothing here -- but churning guids for no reason is how references
        # die elsewhere, so a .meta is written once and then left alone.
        meta_path = os.path.join(OUT, spec['name'] + '.asset.meta')
        if not os.path.exists(meta_path):
            io.open(meta_path, 'w',
                    encoding='utf-8', newline='\n').write(META % uuid.uuid4().hex)
        print('%-9s %s' % (spec['name'], spec['mechanic']))
