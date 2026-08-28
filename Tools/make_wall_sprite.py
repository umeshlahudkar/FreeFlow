"""Generate the wall edge bars: a flat white bar, one sprite per orientation.

Flat rather than bevelled -- no shading bands, no grooves. `Block.WallColor` tints the whole bar
at runtime, same as before; this file just stopped baking a bevel into it.

WHY STILL TWO FILES even though they're now pixel-identical. A wall Image is pinned to a cell edge
and stretched along it (crushed on the thickness axis, near 1:1 on the length axis), and
`Block.cs` picks `wallSprite` or `wallSpriteVertical` by orientation. Flat art has nothing left for
the two files to disagree on, but collapsing them to one shared sprite is a Block.cs/prefab change,
not an art change -- left alone here so this script's diff is exactly "the art is flat now" and
nothing else.

WHY OPAQUE, WITH THE FLAT VALUE IN RGB (not alpha). `NormalizeWalls` mirrors a wall onto both
cells, so two coincident copies of the bar are drawn. Shading held in alpha would composite twice
and flatten (1-(1-a)^2), and a wall on the board's boundary -- drawn once -- would then not match
an interior one. Opaque greyscale is idempotent: both cases render identically.
"""
import io
import os
import re
import struct
import sys
import zlib

LENGTH = 128      # along the bar; ends up near 1:1 with the cell, so detail here survives
THICK = 32        # across the bar; crushed to ~15 units, so this axis stays to broad bands

# Flat white across the whole bar -- no bevel bands.
BANDS = [
    (1.00, 255),
]

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
                    total += band_value((y + dy) / THICK)
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
    print('flat grey %d, no bevel, no grooves' % BANDS[0][1])
