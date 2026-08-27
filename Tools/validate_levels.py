"""Mirror of LevelValidator.cs, run against the level assets on disk.

Same checks, same order, so a clean run here means Unity's console should be clean too.
This is a stand-in for a test suite the project does not have, not a replacement for it.
"""
import io
import re
import sys
from collections import deque

LEVELS_DIR = sys.argv[1]

(BLOCKED, CHECKPOINT, FORBIDDEN, ONEWAY, GATE, MIXED, ARROW, BRIDGE, SPLITTER,
 ROTATOR, PERMIT) = 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11
NAMES = {1: 'Blocked', 2: 'Checkpoint', 3: 'ForbiddenForPair', 4: 'OneWay', 5: 'Gate',
         6: 'Mixed', 7: 'Arrow', 8: 'Bridge', 9: 'Splitter', 10: 'Rotator',
         11: 'AllowedForPairs'}
# Mirror of Block.SecondIdNamesAPair: the types that read secondPairId as "a second pair this
# rule is about" rather than "a second pair this cell is a dot for".
SECOND_ID_NAMES_A_PAIR = (FORBIDDEN, PERMIT)

LEFT, RIGHT, UP, DOWN = 1, 2, 3, 4
DIRNAME = {1: 'Left', 2: 'Right', 3: 'Up', 4: 'Down'}
WALL_BIT = {LEFT: 1, RIGHT: 2, UP: 4, DOWN: 8}
OPPOSITE = {LEFT: RIGHT, RIGHT: LEFT, UP: DOWN, DOWN: UP}
DELTA = {LEFT: (0, -1), RIGHT: (0, 1), UP: (-1, 0), DOWN: (1, 0)}


def unpack(hexs):
    b = bytes.fromhex(hexs)
    return [int.from_bytes(b[i:i + 4], 'little', signed=True) for i in range(0, len(b), 4)]


def column(text, key):
    return [unpack(m.group(1)) for m in
            re.finditer(r'^[ -]+' + key + r': ([0-9a-f]{8,})$', text, re.M)]


def load(path):
    t = io.open(path, encoding='utf-8').read()
    lvl = dict(
        size=int(re.search(r'gridSize: (\d+)', t).group(1)),
        pair_count=int(re.search(r'pairCount: (\d+)', t).group(1)),
        colour=column(t, 'coloum'),
        pair=column(t, 'pairId'),
        btype=column(t, 'blockType'),
        wall=column(t, 'wallMask'),
        entry=column(t, 'requiredEntryDirection'),
        exit=column(t, 'forcedExitDirection'),
        rot=column(t, 'initialRotation'),
        second=column(t, 'secondPairId'),
        third=column(t, 'thirdPairId'),
        fourth=column(t, 'fourthPairId'),
        constraints=[],
    )
    tail = re.search(r'pairConstraints:(.*)$', t, re.S).group(1)
    for m in re.finditer(r'- pairId: (\d+)\s*\n\s*requiredPathLength: (\d+)', tail):
        lvl['constraints'].append((int(m.group(1)), int(m.group(2))))
    for key in ('exit', 'rot', 'second'):
        if not lvl[key]:
            lvl[key] = [[0] * lvl['size'] for _ in range(lvl['size'])]
    # BoardGenerator's pairId fallback: an unset id derives from the cell's colour
    for r in range(lvl['size']):
        for c in range(lvl['size']):
            if lvl['pair'][r][c] == 0:
                lvl['pair'][r][c] = lvl['colour'][r][c]
    return lvl


def normalise_walls(lvl):
    """BoardGenerator.NormalizeWalls: a wall declared on one side applies to both."""
    n = lvl['size']
    for r in range(n):
        for c in range(n):
            for d, bit in WALL_BIT.items():
                if lvl['wall'][r][c] & bit:
                    dr, dc = DELTA[d]
                    nr, nc = r + dr, c + dc
                    if 0 <= nr < n and 0 <= nc < n:
                        lvl['wall'][nr][nc] |= WALL_BIT[OPPOSITE[d]]


def can_step(lvl, fr, fc, tr, tc, d, pair_id):
    if lvl['btype'][tr][tc] == BLOCKED:
        return False
    # both permission rules read the same two ids and differ only in the conclusion
    named = pair_id in (lvl['pair'][tr][tc], lvl['second'][tr][tc])
    if lvl['btype'][tr][tc] == FORBIDDEN and named:
        return False
    if lvl['btype'][tr][tc] == PERMIT and not named:
        return False
    if lvl['colour'][tr][tc] and pair_id not in (lvl['pair'][tr][tc], lvl['second'][tr][tc]):
        return False
    if lvl['wall'][fr][fc] & WALL_BIT[d] or lvl['wall'][tr][tc] & WALL_BIT[OPPOSITE[d]]:
        return False
    required = lvl['entry'][tr][tc]
    if required and required != d:
        return False
    forced = lvl['exit'][tr][tc]
    if lvl['btype'][tr][tc] == ARROW and forced and d == OPPOSITE[forced]:
        return False
    return True


def can_exit_from(lvl, r, c, entry, exit_dir):
    """Mirror of Block.CanExitFromUnderAnyRotation: an arrow forces the exit, a bridge forbids
    turning, and a rotator only ever turns ninety degrees -- in whichever of its four elbows the
    player chooses, which is why the authored rotation is not consulted here."""
    t = lvl['btype'][r][c]
    if t == ARROW:
        forced = lvl['exit'][r][c]
        return not forced or exit_dir == forced
    if t == BRIDGE:
        return entry == 0 or exit_dir == entry
    if t == ROTATOR:
        return entry == 0 or (exit_dir != entry and exit_dir != OPPOSITE[entry])
    return True


def flood(lvl, start, pair_id):
    # states are (cell, arrival direction): an arrow and a bridge both decide where a path may go
    # next from how it got in, so the cell alone is not enough to answer that
    n = lvl['size']
    seen = [[False] * n for _ in range(n)]
    seen_state = set()
    seen[start[0]][start[1]] = True
    seen_state.add((start[0], start[1], 0))
    q = deque([(start[0], start[1], 0)])
    while q:
        r, c, arrived = q.popleft()
        for d, (dr, dc) in DELTA.items():
            if not can_exit_from(lvl, r, c, arrived, d):
                continue
            nr, nc = r + dr, c + dc
            if not (0 <= nr < n and 0 <= nc < n) or (nr, nc, d) in seen_state:
                continue
            if can_step(lvl, r, c, nr, nc, d, pair_id):
                seen[nr][nc] = True
                seen_state.add((nr, nc, d))
                q.append((nr, nc, d))
    return seen


def validate(name, lvl):
    errors = []
    n = lvl['size']
    normalise_walls(lvl)

    dots = {}
    for r in range(n):
        for c in range(n):
            if lvl['colour'][r][c]:
                dots.setdefault(lvl['pair'][r][c], []).append((r, c))
            # a shared destination is a dot for every pair it names -- but a permission cell
            # reuses the SECOND column for a named colour, which is not a dot at all
            if lvl['second'][r][c] and lvl['btype'][r][c] not in SECOND_ID_NAMES_A_PAIR:
                dots.setdefault(lvl['second'][r][c], []).append((r, c))
            for extra in (lvl['third'][r][c], lvl['fourth'][r][c]):
                if extra:
                    dots.setdefault(extra, []).append((r, c))

    splitter_pairs = set()
    for r in range(n):
        for c in range(n):
            if lvl['btype'][r][c] == SPLITTER:
                splitter_pairs.add(lvl['pair'][r][c])

    for pid, cells in sorted(dots.items()):
        expected = 3 if pid in splitter_pairs else 2
        if len(cells) != expected:
            errors.append('pair id %d has %d dot(s), expected exactly %d.' % (pid, len(cells), expected))

    if len(dots) != lvl['pair_count']:
        errors.append('pairCount says %d but the board has %d pairs.' % (lvl['pair_count'], len(dots)))

    for r in range(n):
        for c in range(n):
            t = lvl['btype'][r][c]
            if t in (CHECKPOINT, FORBIDDEN, GATE, SPLITTER, PERMIT):
                pid = lvl['pair'][r][c]
                where = '%s cell at (%d,%d)' % (NAMES[t], r, c)
                if pid == 0:
                    errors.append(where + ' has no pairId, so the rule can never apply.')
                elif pid not in dots:
                    errors.append(where + ' names pair %d, which has no dots on this board.' % pid)
                elif lvl['colour'][r][c]:
                    errors.append(where + ' is also a pair dot.')

                if t in SECOND_ID_NAMES_A_PAIR and lvl['second'][r][c]:
                    sid = lvl['second'][r][c]
                    if sid == pid:
                        errors.append(where + ' names pair %d twice.' % pid)
                    elif sid not in dots:
                        errors.append(where + ' names pair %d as its second colour, which has '
                                              'no dots on this board.' % sid)

            # shared destinations: the same two id columns, read the other way. A permit cell is
            # skipped because its ids name permitted colours, not dots -- the checks above cover it.
            extras = [lvl['second'][r][c] if t not in SECOND_ID_NAMES_A_PAIR else 0,
                      lvl['third'][r][c], lvl['fourth'][r][c]]
            if any(extras):
                where = 'shared destination at (%d,%d)' % (r, c)
                if not lvl['colour'][r][c]:
                    errors.append(where + ' names extra pairs but is not a dot at all.')

                # filled in order, so a gap means a level meant to name a pair and did not
                if not extras[0] and (extras[1] or extras[2]):
                    errors.append(where + ' skips its second pair slot but fills a later one.')
                elif not extras[1] and extras[2]:
                    errors.append(where + ' skips its third pair slot but fills the fourth.')

                named = [lvl['pair'][r][c]] + extras
                for k in range(1, len(named)):
                    if not named[k]:
                        continue
                    if named[k] in named[:k]:
                        errors.append(where + ' names pair %d more than once.' % named[k])
                    elif named[k] not in dots:
                        errors.append(where + ' names pair %d, which has no other dot on this '
                                              'board.' % named[k])

            required = lvl['entry'][r][c]
            if t != ONEWAY and required:
                errors.append('cell (%d,%d) is %s but has requiredEntryDirection %s.'
                              % (r, c, NAMES.get(t, 'Normal'), DIRNAME[required]))
            elif t == ONEWAY and not required:
                errors.append('OneWay cell at (%d,%d) has no requiredEntryDirection.' % (r, c))
            elif t == ONEWAY and lvl['wall'][r][c] & WALL_BIT[OPPOSITE[required]]:
                errors.append('OneWay cell at (%d,%d) has its only entry edge walled.' % (r, c))

    for r in range(n):
        for c in range(n):
            if lvl['btype'][r][c] != ARROW:
                continue
            forced = lvl['exit'][r][c]
            where = 'Arrow cell at (%d,%d)' % (r, c)
            if not forced:
                errors.append(where + ' has no forcedExitDirection.')
                continue
            if lvl['colour'][r][c]:
                errors.append(where + ' is also a pair dot.')
            dr, dc = DELTA[forced]
            tr, tc = r + dr, c + dc
            if not (0 <= tr < n and 0 <= tc < n):
                errors.append(where + ' points %s off the board.' % DIRNAME[forced])
            elif lvl['wall'][r][c] & WALL_BIT[forced] or lvl['wall'][tr][tc] & WALL_BIT[OPPOSITE[forced]]:
                errors.append(where + ' points %s through a wall.' % DIRNAME[forced])
            elif lvl['btype'][tr][tc] == BLOCKED:
                errors.append(where + ' points %s into a blocked cell.' % DIRNAME[forced])

    for r in range(n):
        for c in range(n):
            if lvl['btype'][r][c] != BRIDGE:
                continue
            where = 'Bridge cell at (%d,%d)' % (r, c)
            if lvl['colour'][r][c]:
                errors.append(where + ' is also a pair dot.')

            def side_open(d):
                if lvl['wall'][r][c] & WALL_BIT[d]:
                    return False
                dr, dc = DELTA[d]
                tr, tc = r + dr, c + dc
                if not (0 <= tr < n and 0 <= tc < n):
                    return False
                if lvl['wall'][tr][tc] & WALL_BIT[OPPOSITE[d]]:
                    return False
                return lvl['btype'][tr][tc] != BLOCKED

            horiz = side_open(LEFT) and side_open(RIGHT)
            vert = side_open(UP) and side_open(DOWN)
            if not horiz and not vert:
                errors.append(where + ' has no crossable lane at all.')
            elif not horiz or not vert:
                errors.append(where + ' only has its %s lane open, so it can never hold two paths.'
                              % ('horizontal' if horiz else 'vertical'))

    for r in range(n):
        for c in range(n):
            if lvl['btype'][r][c] != ROTATOR:
                continue
            where = 'Rotator cell at (%d,%d)' % (r, c)
            if lvl['colour'][r][c]:
                errors.append(where + ' is also a pair dot.')

            open_edges = 0
            for d in DELTA:
                if lvl['wall'][r][c] & WALL_BIT[d]:
                    continue
                dr, dc = DELTA[d]
                tr, tc = r + dr, c + dc
                if not (0 <= tr < n and 0 <= tc < n):
                    continue
                if lvl['wall'][tr][tc] & WALL_BIT[OPPOSITE[d]]:
                    continue
                if lvl['btype'][tr][tc] != BLOCKED:
                    open_edges += 1
            if open_edges < 2:
                errors.append(where + ' has %d open edge(s), so no rotation can join two cells.'
                              % open_edges)

    for pid, length in lvl['constraints']:
        if pid not in dots or len(dots[pid]) != 2:
            errors.append('pairConstraint targets pair %d, not a valid pair.' % pid)
            continue
        (r1, c1), (r2, c2) = dots[pid]
        shortest = abs(r1 - r2) + abs(c1 - c2) + 1
        if length < shortest:
            errors.append('pair %d requires %d cells but %d is the shortest possible.' % (pid, length, shortest))
        elif (length - shortest) % 2:
            errors.append('pair %d requires %d cells; wrong parity (shortest is %d).' % (pid, length, shortest))

    for pid, cells in sorted(dots.items()):
        if pid in splitter_pairs:
            # complete only when every dot reaches the junction, so check exactly that
            for r in range(n):
                for c in range(n):
                    if lvl['btype'][r][c] != SPLITTER or lvl['pair'][r][c] != pid:
                        continue
                    for (dr, dc) in cells:
                        if not flood(lvl, (dr, dc), pid)[r][c]:
                            errors.append('pair %d has a dot at (%d,%d) that cannot reach its '
                                          'splitter junction at (%d,%d).' % (pid, dr, dc, r, c))
            continue

        if len(cells) != 2:
            continue
        a, b = cells
        fwd, back = flood(lvl, a, pid), flood(lvl, b, pid)
        if not fwd[b[0]][b[1]] and not back[a[0]][a[1]]:
            errors.append('pair %d has no legal route between its dots.' % pid)
            continue
        for r in range(n):
            for c in range(n):
                if lvl['btype'][r][c] == CHECKPOINT and lvl['pair'][r][c] == pid:
                    if not fwd[r][c] and not back[r][c]:
                        errors.append('pair %d cannot reach its checkpoint at (%d,%d).' % (pid, r, c))
    return errors


if __name__ == '__main__':
    total = 0
    for i in range(1, 100):
        path = '%s/Level_%d.asset' % (LEVELS_DIR, i)
        try:
            lvl = load(path)
        except IOError:
            break
        errs = validate('Level_%d' % i, lvl)
        total += len(errs)
        print('Level_%-2d %dx%d  %d pairs  %s' % (
            i, lvl['size'], lvl['size'], lvl['pair_count'],
            'OK' if not errs else 'ERRORS'))
        for e in errs:
            print('         ! ' + e)
    print('\n%s' % ('all levels validate clean' if total == 0 else '%d error(s)' % total))
