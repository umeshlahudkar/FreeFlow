"""Generate the wall edge bars: a bevelled masonry bar, one sprite per orientation.

WHY TWO SPRITES. A wall Image is pinned to a cell edge and stretched along it, so the sprite meets
the screen crushed on one axis and near 1:1 on the other -- for a 100-unit cell the bar is about
100 long and 15 thick. Shading that reads as a solid object has to run across the THICKNESS, and
thickness is the y axis for the Up/Down bars but the x axis for Left/Right. One sprite cannot serve
both: any single-axis gradient is correct for one pair of edges and smeared along the other.

The alternatives were considered and rejected:

  * A gradient symmetric in x and y (distance to the nearest edge) does serve both orientations,
    but the same band width lands on both axes -- and the axes differ by ~7x. A rim thick enough
    to see across a 15-unit thickness becomes a 13-unit dark region at each END of the bar, which
    is exactly the fading cap the bar is not supposed to have.
  * 9-slicing pins the rim to a fixed unit size, which sounds right until the board resizes: bar
    thickness is a fraction of the cell, so on a small board a fixed rim eats the whole bar and on
    a large one it thins to a line. A proportional bevel is what survives every board size.

So: `edge_wall` carries the horizontal bar and `edge_wall_vertical` is its exact transpose. The
transpose is what makes the lighting agree -- the horizontal bar's bright band sits near its top
edge, and transposing puts the vertical bar's bright band near its left edge, both consistent with
one light source up and to the left.

WHY OPAQUE, WITH THE SHADING IN RGB. `NormalizeWalls` mirrors a wall onto both cells, so two
coincident copies of the bar are drawn. Shading held in alpha would composite twice and flatten
(1-(1-a)^2), and a wall on the board's boundary -- drawn once -- would then not match an interior
one. Opaque greyscale is idempotent: both cases render identically, and `Block.WallColor` still
tints the whole bar at runtime.
"""
import io
import os
import re
import struct
import sys
import zlib

LENGTH = 128      # along the bar; ends up near 1:1 with the cell, so detail here survives
THICK = 32        # across the bar; crushed to ~15 units, so this axis stays to broad bands

# Bands across the thickness, top edge -> bottom edge: (fraction of thickness, grey 0-255).
# A dark lip, a highlight, the body, shading, a darker lip. The lips are what stop the bar reading
# as a fatter grid line: a flat bar has no edges, a bevelled one does.
BANDS = [
    (0.06, 130),
    (0.25, 255),
    (0.64, 228),
    (0.84, 168),
    (1.00, 105),
]

# Grooves perpendicular to the bar, so it reads as courses of masonry rather than one long slab.
# Safe to put detail here: this is the axis that is NOT crushed.
GROOVES = [0.25, 0.5, 0.75]
GROOVE_HALF_WIDTH = 1.6      # in LENGTH texels
GROOVE_DARKEN = 0.58

SUPERSAMPLE = 4


def png(width, height, rows):
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = b''.join(b'\x00' + bytes(r) for r in rows)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def band_value(t):
    """Grey at fractional position t across the thickness."""
    for edge, value in BANDS:
        if t <= edge:
            return value
    return BANDS[-1][1]


def groove_factor(along):
    """1.0 away from a groove, GROOVE_DARKEN on one, antialiased between."""
    half = GROOVE_HALF_WIDTH / LENGTH
    for centre in GROOVES:
        d = abs(along - centre)
        if d <= half:
            return GROOVE_DARKEN
        if d <= half * 2.0:
            k = (d - half) / half
            return GROOVE_DARKEN + (1.0 - GROOVE_DARKEN) * k
    return 1.0


def horizontal_grid():
    """[y][x] greys for the horizontal bar: thickness down y, length along x."""
    step = 1.0 / SUPERSAMPLE
    offsets = [(i + 0.5) * step for i in range(SUPERSAMPLE)]
    n = SUPERSAMPLE * SUPERSAMPLE

    grid = []
    for y in range(THICK):
        row = []
        for x in range(LENGTH):
            total = 0.0
            for dy in offsets:
                for dx in offsets:
                    total += band_value((y + dy) / THICK) * groove_factor((x + dx) / LENGTH)
            row.append(int(round(total / n)))
        grid.append(row)
    return grid


def transpose(grid):
    return [[grid[y][x] for y in range(len(grid))] for x in range(len(grid[0]))]


def to_rows(grid):
    """Opaque RGBA rows from greys."""
    rows = []
    for row in grid:
        r = bytearray()
        for v in row:
            r += bytes([v, v, v, 255])
        rows.append(r)
    return rows


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


def ensure_meta(path):
    """Returns the guid, creating the .meta only if absent -- rewriting one mints a new guid and
    silently breaks every reference already wired into a scene or prefab."""
    meta = path + '.meta'
    if os.path.exists(meta):
        text = io.open(meta, encoding='utf-8').read()
        guid = re.search(r'^guid: (\S+)', text, re.M).group(1)

        fixed = re.sub(r'spriteBorder: \{x: \d+, y: \d+, z: \d+, w: \d+\}',
                       'spriteBorder: {x: 0, y: 0, z: 0, w: 0}', text)
        if fixed != text:
            io.open(meta, 'w', encoding='utf-8', newline='\n').write(fixed)
            print('  spriteBorder zeroed (bevel is proportional, so slicing would fight it)')
        return guid, 'kept'

    import uuid
    guid = uuid.uuid4().hex
    io.open(meta, 'w', encoding='utf-8', newline='\n').write(META % guid)
    return guid, 'created'


if __name__ == '__main__':
    out_dir = sys.argv[1]

    horizontal = horizontal_grid()
    vertical = transpose(horizontal)

    for name, grid in (('edge_wall', horizontal), ('edge_wall_vertical', vertical)):
        path = os.path.join(out_dir, name + '.png')
        io.open(path, 'wb').write(png(len(grid[0]), len(grid), to_rows(grid)))
        guid, how = ensure_meta(path)
        print('%-19s %3dx%-3d  guid %s (%s)' % (name + '.png', len(grid[0]), len(grid), guid, how))

    print()
    print('bands across the thickness, at a 15-unit bar:')
    prev = 0.0
    for edge, value in BANDS:
        print('   grey %3d over %4.1f units' % (value, (edge - prev) * 15.0))
        prev = edge
    print('grooves: %d perpendicular, %.1f texels wide (~%.1f units on a 100-unit edge)'
          % (len(GROOVES), GROOVE_HALF_WIDTH * 2, GROOVE_HALF_WIDTH * 2 * 100.0 / LENGTH))
