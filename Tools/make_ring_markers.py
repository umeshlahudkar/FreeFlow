"""Generate the ring markers: the permit ring, the forbidden ring, and the shared half arc.

The board's marker language is a set of rings told apart by what sits inside them -- nothing for
Mixed, a crosshair for Checkpoint. The two per-pair permission rules are exact inverses of each
other, so they take inverse glyphs inside the same ring: a check for permitted, an X for forbidden.

THREE FILES, because either rule may name two colours and an Image carries one tint:

  * mark_permit     - the ring plus a check. Tinted to the first permitted colour.
  * mark_forbidden  - the ring plus an X. Tinted to the first forbidden colour. Two crossed
                      strokes rather than the single diagonal slash it used to be, so it reads as
                      the deliberate opposite of the check instead of merely a different angle.
  * mark_ring_half  - the RIGHT half of the same ring, arc only, shared by BOTH rules. Drawn on
                      top and tinted to the cell's second colour, so the ring reads two-tone when
                      the cell names two and is simply absent when it names one.

Both come out of one geometry function, so the half can never drift out of alignment with the arc
it overlays. Same idiom as dot_half over the pair dot on a shared destination.
"""
import io
import math
import os
import struct
import sys
import uuid
import zlib

SIZE = 256
CENTRE = SIZE / 2.0

# Matched to mark_forbidden's ring so the two rules read as a matched pair on the board.
RING_OUTER = 108.0
RING_INNER = 78.0

# The check, as a two-segment polyline with round joins. Kept inside RING_INNER with room for the
# stroke: the farthest point is 53 from the centre and the stroke reaches 13 more, against 78.
CHECK = [(88.0, 130.0), (113.0, 160.0), (170.0, 94.0)]

# The X: two crossed strokes. Corners at +/-40 on both axes sit 56.6 from the centre, and the
# stroke reaches 13 further -- inside RING_INNER with room to spare, same as the check.
CROSS = [[(88.0, 88.0), (168.0, 168.0)], [(168.0, 88.0), (88.0, 168.0)]]

STROKE_HALF_WIDTH = 13.0

SUPERSAMPLE = 3


def png(width, height, rgba_rows):
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = b''.join(b'\x00' + bytes(row) for row in rgba_rows)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def on_ring(x, y):
    d = math.hypot(x - CENTRE, y - CENTRE)
    return RING_INNER <= d <= RING_OUTER


def segment_distance(x, y, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    if length2 == 0.0:
        return math.hypot(x - ax, y - ay)
    t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / length2))
    return math.hypot(x - (ax + t * dx), y - (ay + t * dy))


def on_polyline(x, y, points):
    for i in range(len(points) - 1):
        ax, ay = points[i]
        bx, by = points[i + 1]
        if segment_distance(x, y, ax, ay, bx, by) <= STROKE_HALF_WIDTH:
            return True
    return False


def alpha_permit(x, y):
    return 255 if (on_ring(x, y) or on_polyline(x, y, CHECK)) else 0


def alpha_forbidden(x, y):
    if on_ring(x, y):
        return 255
    return 255 if any(on_polyline(x, y, stroke) for stroke in CROSS) else 0


def alpha_half(x, y):
    """The right half of the ring only -- no glyph, so the overlay recolours the arc and leaves
    whatever is inside the ring in the first colour."""
    return 255 if (x >= CENTRE and on_ring(x, y)) else 0


def rows_for(alpha_at):
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
            row += bytes([255, 255, 255, total // n])
        rows.append(row)
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

if __name__ == '__main__':
    out_dir = sys.argv[1]

    for name, fn in (('mark_permit', alpha_permit),
                     ('mark_forbidden', alpha_forbidden),
                     ('mark_ring_half', alpha_half)):
        path = os.path.join(out_dir, name + '.png')
        io.open(path, 'wb').write(png(SIZE, SIZE, rows_for(fn)))

        # A .meta is written ONCE -- rewriting one mints a new guid and breaks every reference
        # already wired into a prefab or scene.
        meta = path + '.meta'
        if os.path.exists(meta):
            guid = [l.split()[-1] for l in io.open(meta, encoding='utf-8')
                    if l.startswith('guid')][0]
            state = 'kept'
        else:
            guid = uuid.uuid4().hex
            io.open(meta, 'w', encoding='utf-8', newline='\n').write(META % guid)
            state = 'created'
        print('%-17s %dx%d  guid %s (%s)' % (name + '.png', SIZE, SIZE, guid, state))
