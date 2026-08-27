"""Generate a hazard-hatch sprite for BlockType.Blocked, and a preview of how it composites.

The sprite is white with everything carried in ALPHA, like the rest of the set: Block tints it a
single flat colour (BlockedColor), so alpha is the only lever for contrast *inside* the cell. The
tint is therefore a ceiling -- alpha can only take a pixel from the tint down toward the board
behind it, never brighter. So the stripes sit at full alpha and everything else at zero.

Slanted at 45 degrees, so it survives the cell being any size: the sprite is square and the cell is
square (BoardGenerator snaps one blockSize for both axes), so nothing shears.
"""
import io
import math
import os
import struct
import sys
import uuid
import zlib

SIZE = 256

# The hatch, measured PERPENDICULAR to the stripes -- the direction that reads as "width" and
# "gap" on screen.
#
# The period is DERIVED, not chosen. Stepping one cell to the right moves the perpendicular
# coordinate by SIZE/sqrt(2); unless the period divides that exactly, a stripe entering a
# neighbouring blocked cell restarts out of phase and a group of blocked cells shows a seam at
# every boundary. Fitting a whole number of periods into that step makes the stripes run
# continuously across any run of blocked cells, in both directions -- one hazard field rather than
# a mosaic of tiles.
STRIPES_PER_CELL_STEP = 4
STRIPE_PERIOD = SIZE / math.sqrt(2.0) / STRIPES_PER_CELL_STEP
STRIPE_WIDTH = 18.0

STRIPE_ALPHA = 255   # the stripes get the tint in full
SLAB_ALPHA = 0       # nothing between them: the board shows through, so it is stripes and only
                     # stripes -- no outline, no plate behind.

SUPERSAMPLE = 3      # 3x3 per pixel; diagonals alias badly without it


def png(width, height, rgba_rows):
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = b''.join(b'\x00' + bytes(row) for row in rgba_rows)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def stripe_distance(x, y):
    """Distance from the nearest stripe centre line, perpendicular to the stripes."""
    # x - y is constant along a "\" line; dividing by sqrt(2) makes the units perpendicular pixels
    s = (x - y) / math.sqrt(2.0)
    t = s % STRIPE_PERIOD
    return min(t, STRIPE_PERIOD - t)


def alpha_at(x, y):
    """Alpha for one sample point: on a stripe, or between stripes.

    Edge to edge -- no inset and no rounding. The sprite is the full cell, so the stripes reach the
    cell boundary and, being in phase across it, continue straight into the next blocked cell.
    """
    return STRIPE_ALPHA if stripe_distance(x, y) <= STRIPE_WIDTH / 2.0 else SLAB_ALPHA


def hatch_rows():
    step = 1.0 / SUPERSAMPLE
    offsets = [(i + 0.5) * step for i in range(SUPERSAMPLE)]
    n = SUPERSAMPLE * SUPERSAMPLE

    rows = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            total = 0
            for dy in offsets:
                for dx in offsets:
                    total += alpha_at(x + dx, y + dy)
            row += b'\xff\xff\xff' + bytes([total // n])
        rows.append(row)
    return rows


# ---------------------------------------------------------------- preview
# Composites the sprite the way Unity will: tint * alpha over the board's cell colour, with the
# board's white grid lines around it. Purely for looking at -- never imported.
BOARD = (10, 12, 16)        # the dark board behind everything
CELL = (26, 28, 34)         # a cell interior, board grid interior alpha over the board
LINE = (150, 155, 165)      # a grid line


def composite_preview(rows, tint, cell_px=104):
    """Draws a 4x4 patch of board with some cells blocked, so the board's own grid lines are
    plainly the board's and not mistaken for part of the sprite. The L of adjacent blocked cells
    is what checks the derived stripe period: every cell samples the sprite from its own origin, so
    if the period did not divide the cell step the stripes would visibly kink at each boundary."""
    n, line, pad = 4, 4, 14
    blocked = {(1, 1), (1, 2), (2, 1)}

    w = h = pad * 2 + cell_px * n
    out = []
    for _ in range(h):
        r = bytearray()
        for _ in range(w):
            r += bytes(BOARD) + bytes([255])
        out.append(r)

    for row in range(n):
        for col in range(n):
            x0, y0 = pad + col * cell_px, pad + row * cell_px
            for cy in range(cell_px):
                for cx in range(cell_px):
                    on_line = cx < line or cy < line
                    if row == n - 1 and cy >= cell_px - line: on_line = True
                    if col == n - 1 and cx >= cell_px - line: on_line = True

                    base = LINE if on_line else CELL
                    a = 0.0
                    if (row, col) in blocked and not on_line:
                        sx = min(SIZE - 1, int(cx * SIZE / cell_px))
                        sy = min(SIZE - 1, int(cy * SIZE / cell_px))
                        a = rows[sy][sx * 4 + 3] / 255.0

                    px = tuple(int(round(base[c] * (1 - a) + tint[c] * 255 * a)) for c in range(3))
                    o = (x0 + cx) * 4
                    out[y0 + cy][o:o + 3] = bytes(px)

    return png(w, h, out)


META = """fileFormatVersion: 2
guid: %s
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spritePivot: {x: 0.5, y: 0.5}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

if __name__ == '__main__':
    out_dir = sys.argv[1]
    preview_path = sys.argv[2] if len(sys.argv) > 2 else None

    rows = hatch_rows()

    path = os.path.join(out_dir, 'cell_blocked_hatch.png')
    io.open(path, 'wb').write(png(SIZE, SIZE, rows))

    # A .meta is written ONCE. Rewriting one mints a new guid and silently breaks every reference
    # already wired into a scene or prefab.
    meta = path + '.meta'
    if os.path.exists(meta):
        guid = [l.split()[-1] for l in io.open(meta, encoding='utf-8') if l.startswith('guid')][0]
        print('meta kept, guid %s' % guid)
    else:
        guid = uuid.uuid4().hex
        io.open(meta, 'w', encoding='utf-8', newline='\n').write(META % guid)
        print('meta written, guid %s' % guid)

    print('cell_blocked_hatch.png  %dx%d  stripes %.0fpx every %.2fpx perpendicular '
          '(%d per cell step, so adjacent blocked cells stay in phase)'
          % (SIZE, SIZE, STRIPE_WIDTH, STRIPE_PERIOD, STRIPES_PER_CELL_STEP))
    for cell in (60, 100, 140):
        k = cell / float(SIZE)
        print('   at a %3dpx cell: stripe %.1fpx, gap %.1fpx'
              % (cell, STRIPE_WIDTH * k, (STRIPE_PERIOD - STRIPE_WIDTH) * k))

    if preview_path:
        io.open(preview_path, 'wb').write(composite_preview(rows, (0.42, 0.42, 0.42)))
        print('preview: %s  (4x4 board patch, tint 0.42)' % preview_path)
