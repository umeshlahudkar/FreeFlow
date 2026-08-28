"""Generate mark_checkpoint: a planted pennant flag, replacing the old crosshair reticle.

The ring-plus-four-ticks-plus-dot glyph it replaces reads as a rifle scope -- "aim here" -- which
fights what the rule actually means: a pair's path must pass through this cell, in any direction,
and keep going. A flag is the closest thing to a universal "waypoint" symbol (every racing game
uses one) and, unlike the reticle, does not imply a direction or a stop.

Same white-alpha-only convention as the ring markers this sits alongside (mark_permit,
mark_forbidden): the shape carries no colour of its own, so Block.ShowSpecialMarker's tint to the
checkpoint's pair colour is a plain multiply, same as every other special marker.
"""
import io
import math
import os
import re
import struct
import sys
import zlib

SIZE = 256
SUPERSAMPLE = 4

# Pole: a capsule (segment + radius), which gives rounded caps for free -- same idiom as the
# ring markers' stroked polylines. Sized to fill most of the marker's half-cell footprint, same
# visual weight as the reticle it replaces rather than a small icon floating in empty space.
POLE_TOP = (100.0, 46.0)
POLE_BOTTOM = (100.0, 214.0)
POLE_RADIUS = 15.0

# Finial: a small ball atop the pole, the detail that reads as "flagpole" rather than "stick".
FINIAL_CENTER = (100.0, 30.0)
FINIAL_RADIUS = 18.0

# Flag: a solid pennant, flat edge against the pole, tapering to a point. Points right -- the
# mechanic has no direction, so this is a legibility choice, not a rule.
FLAG = [(108.0, 58.0), (108.0, 128.0), (222.0, 93.0)]


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


def in_pole(x, y):
    ax, ay = POLE_TOP
    bx, by = POLE_BOTTOM
    return segment_distance(x, y, ax, ay, bx, by) <= POLE_RADIUS


def in_finial(x, y):
    cx, cy = FINIAL_CENTER
    return math.hypot(x - cx, y - cy) <= FINIAL_RADIUS


def in_flag(x, y):
    (ax, ay), (bx, by), (cx, cy) = FLAG

    def sign(px, py, qx, qy, rx, ry):
        return (px - rx) * (qy - ry) - (qx - rx) * (py - ry)

    d1 = sign(x, y, ax, ay, bx, by)
    d2 = sign(x, y, bx, by, cx, cy)
    d3 = sign(x, y, cx, cy, ax, ay)
    has_neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    has_pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    return not (has_neg and has_pos)


def covered(x, y):
    return in_pole(x, y) or in_finial(x, y) or in_flag(x, y)


def render():
    step = 1.0 / SUPERSAMPLE
    offsets = [(i + 0.5) * step for i in range(SUPERSAMPLE)]
    n = SUPERSAMPLE * SUPERSAMPLE

    rows = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            hits = 0
            for dy in offsets:
                for dx in offsets:
                    if covered(x + dx, y + dy):
                        hits += 1
            row += bytes([255, 255, 255, round(255 * hits / n)])
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
    silently breaks every reference already wired into a scene or prefab."""
    meta = path + '.meta'
    if os.path.exists(meta):
        text = io.open(meta, encoding='utf-8').read()
        guid = re.search(r'^guid: (\S+)', text, re.M).group(1)
        return guid, 'kept'

    import uuid
    guid = uuid.uuid4().hex
    io.open(meta, 'w', encoding='utf-8', newline='\n').write(META % guid)
    return guid, 'created'


if __name__ == '__main__':
    out_dir = sys.argv[1]

    path = os.path.join(out_dir, 'mark_checkpoint.png')
    io.open(path, 'wb').write(png(SIZE, SIZE, render()))
    guid, how = ensure_meta(path)
    print('%-19s %dx%d  guid %s (%s)' % ('mark_checkpoint.png', SIZE, SIZE, guid, how))
