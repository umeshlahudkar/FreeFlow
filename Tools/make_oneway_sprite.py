"""Generate edge_oneway: the one-way entry bar, drawn as a colour-fade motion trail.

Two identical chevrons on a flat tint reads as a repeating pattern (wallpaper) rather than a
single directional signal, and every pixel being flat white means OneWayColor's tint lands on the
whole shape at one uniform brightness.

This version bakes THREE brightness tiers into the grey channel: a dim lip at the outer edge, then
two chevrons growing both bigger and brighter as they approach the inner edge -- the edge the path
actually crosses. Multiplied by the same single OneWayColor tint at runtime, dim-to-bright grey
becomes dark-to-bright green, so recolouring the mechanic is still a one-line tint change; only the
SHAPE of the fade is baked here, not a fixed palette. Two mirrored trails (not one centred) so a
full-width bar stays legible rather than leaving the outer thirds empty.

WHY GREY LEVEL RATHER THAN ALPHA FOR THE FADE. Alpha-only fading (transparent trailing chevrons)
risks disappearing into a dark cell background -- exactly the kind of board this sits on. Baking
the fade into RGB keeps every tier fully opaque and legible; only alpha is used for shape/edge
antialiasing, same idiom as the wall bar's opaque bevel bands.

WHY SIMPLE, NOT SLICED. The sprite this replaces was imported as a 9-slice (spriteBorder
96,58,96,0) despite nothing about a directional chevron trail wanting fixed-size caps -- likely a
leftover default rather than an intentional choice, and at this project's high spritePixelsToUnits
the sliced caps rendered as a near-invisible sliver while almost the whole bar came from a heavily
stretched few source pixels. Block.ShowOneWayMarker sets the bar's size directly every time, so
nothing needs 9-slicing; the fix is switching the Image component to Simple (done in the prefab)
so the whole texture maps linearly onto the bar, matching every other edge-bar sprite in the
project (see make_wall_sprite.py).
"""
import io
import math
import os
import re
import struct
import sys
import zlib

WIDTH = 256
HEIGHT = 80

SUPERSAMPLE = 4

# The lip: a dim anchor strip at the outer edge (y=0 is outer/away from the cell, y=HEIGHT is
# inner/toward the cell interior -- this sprite is authored for the TOP edge, chevrons pointing
# down into the cell, same convention as the art it replaces).
LIP_Y = (0.0, 9.0)
LIP_X_MARGIN = 3.0
LIP_LEVEL = 75

# Two mirrored trails so a full-width bar doesn't leave its outer thirds empty.
CLUSTER_X = (70.0, 186.0)

# Each tier: (apex_y, arm_y, half_width, stroke_half_width, grey level). Growing in every
# dimension -- size, stroke weight, AND brightness -- toward the inner edge, so the trail reads as
# one thing approaching rather than three unrelated marks.
TIERS = [
    (24.0, 13.0, 15.0, 4.5, 95),
    (45.0, 31.0, 21.0, 6.5, 170),
    (68.0, 51.0, 28.0, 9.5, 255),
]


def png(width, height, rows):
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = b''.join(b'\x00' + bytes(r) for r in rows)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def segment_distance(x, y, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    if length2 == 0.0:
        return math.hypot(x - ax, y - ay)
    t = max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / length2))
    return math.hypot(x - (ax + t * dx), y - (ay + t * dy))


def chevron_points(cx, apex_y, arm_y, half_width):
    return [(cx - half_width, arm_y), (cx, apex_y), (cx + half_width, arm_y)]


def level_at(x, y):
    """The grey level at a point, or None if outside every shape."""
    if (LIP_X_MARGIN <= x <= WIDTH - LIP_X_MARGIN) and (LIP_Y[0] <= y <= LIP_Y[1]):
        return LIP_LEVEL

    for cx in CLUSTER_X:
        for apex_y, arm_y, half_width, stroke_half_width, level in TIERS:
            points = chevron_points(cx, apex_y, arm_y, half_width)
            for i in range(len(points) - 1):
                ax, ay = points[i]
                bx, by = points[i + 1]
                if segment_distance(x, y, ax, ay, bx, by) <= stroke_half_width:
                    return level
    return None


def render():
    step = 1.0 / SUPERSAMPLE
    offsets = [(i + 0.5) * step for i in range(SUPERSAMPLE)]
    n = SUPERSAMPLE * SUPERSAMPLE

    rows = []
    for y in range(HEIGHT):
        row = bytearray()
        for x in range(WIDTH):
            hits = 0
            level_sum = 0
            for dy in offsets:
                for dx in offsets:
                    level = level_at(x + dx, y + dy)
                    if level is not None:
                        hits += 1
                        level_sum += level
            if hits == 0:
                row += bytes([0, 0, 0, 0])
            else:
                grey = round(level_sum / hits)
                alpha = round(255 * hits / n)
                row += bytes([grey, grey, grey, alpha])
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


def ensure_meta(path):
    """Returns the guid, creating the .meta only if absent -- rewriting one mints a new guid and
    silently breaks every reference already wired into a scene or prefab. Also zeroes a leftover
    spriteBorder: this art is meant to stretch linearly across the bar (see the module docstring),
    and a stale 9-slice border would fight that the moment anyone re-imports it as Sliced."""
    meta = path + '.meta'
    if os.path.exists(meta):
        text = io.open(meta, encoding='utf-8').read()
        guid = re.search(r'^guid: (\S+)', text, re.M).group(1)

        fixed = re.sub(r'spriteBorder: \{x: \d+, y: \d+, z: \d+, w: \d+\}',
                       'spriteBorder: {x: 0, y: 0, z: 0, w: 0}', text)
        if fixed != text:
            io.open(meta, 'w', encoding='utf-8', newline='\n').write(fixed)
            print('  spriteBorder zeroed (this art stretches linearly, not 9-sliced)')
        return guid, 'kept'

    import uuid
    guid = uuid.uuid4().hex
    io.open(meta, 'w', encoding='utf-8', newline='\n').write(META % guid)
    return guid, 'created'


if __name__ == '__main__':
    out_dir = sys.argv[1]

    path = os.path.join(out_dir, 'edge_oneway.png')
    io.open(path, 'wb').write(png(WIDTH, HEIGHT, render()))
    guid, how = ensure_meta(path)
    print('%-15s %dx%d  guid %s (%s)' % ('edge_oneway.png', WIDTH, HEIGHT, guid, how))
