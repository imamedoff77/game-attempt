"""
Generates every low-poly model of the game, plus the shared colour palette texture.

Run (headless):
    blender -b -P art/blender/generate_assets.py -- <models_out_dir> <palette_png_path> [<previews_dir>] [--only=<name suffix>]

Conventions
  * 1 Blender unit = 1 map cell. Models stand on z = 0, centred on x/y = 0.
  * Models are DESIGNED facing +Y with doors on -Y; to_object() turns them 180 degrees so that
    in Unity units face +Z and doors face -Z (towards the game camera).
  * Every face gets its colour by UV: the whole game uses ONE material with ONE small palette texture.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- palette
PALETTE = [
    ("sand_wall", "#D9B779"), ("sand_light", "#E8D3A2"), ("sand_dark", "#B58F55"), ("concrete", "#A8A39A"),
    ("team", "#2F6FD1"), ("team_dark", "#1E4A99"), ("olive", "#6B7A3A"), ("olive_dark", "#4A5527"),
    ("tire", "#2A2A2E"), ("black", "#151518"), ("metal", "#7C828A"), ("metal_light", "#B5BBC2"),
    ("wood", "#7A5230"), ("wood_dark", "#583A22"), ("orange", "#E8872B"), ("red", "#C23B2E"),
    ("yellow", "#EFC63A"), ("glass", "#7FB9D9"), ("skin", "#D9A877"), ("white", "#EDEDE8"),
    ("rock", "#8A7B6B"), ("rock_light", "#A8977F"), ("rock_dark", "#6B5E50"), ("green", "#4F8A3A"),
    ("green_dark", "#386629"), ("dry", "#9C8B4A"), ("khaki", "#A89A6A"), ("crate", "#8E6B3E"),
    ("container_red", "#B2432F"), ("container_blue", "#3C6E9E"), ("container_green", "#4C8452"), ("flag", "#E7E7E7"),
]
COLS = 8
CELL_PX = 4
PAL_INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}


def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def palette_uv(name):
    i = PAL_INDEX[name]
    rows = (len(PALETTE) + COLS - 1) // COLS
    return ((i % COLS + 0.5) / COLS, (i // COLS + 0.5) / rows)


def write_palette_png(path):
    rows = (len(PALETTE) + COLS - 1) // COLS
    w, h = COLS * CELL_PX, rows * CELL_PX
    img = bpy.data.images.new("palette", w, h, alpha=False)
    img.colorspace_settings.name = "sRGB"
    px = []
    for y in range(h):
        for x in range(w):
            idx = (y // CELL_PX) * COLS + (x // CELL_PX)
            r, g, b = hex_to_rgb(PALETTE[idx][1]) if idx < len(PALETTE) else (1, 0, 1)
            px.extend((r, g, b, 1.0))
    img.pixels = px
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


# ---------------------------------------------------------------- geometry builder
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()

    def _finish(self, faces, color):
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        u, v = palette_uv(color)
        for f in faces:
            for loop in f.loops:
                loop[self.uv].uv = (u, v)

    def _poly(self, verts_xyz, face_indices, color):
        vs = [self.bm.verts.new(p) for p in verts_xyz]
        faces = [self.bm.faces.new([vs[i] for i in f]) for f in face_indices]
        self._finish(faces, color)

    @staticmethod
    def _xform(points, center, yaw=0.0, pitch=0.0, roll=0.0, pivot=None):
        """Rotate local points (roll about Y, pitch about X, yaw about Z) then move to center."""
        m = Matrix.Rotation(yaw, 3, "Z") @ Matrix.Rotation(pitch, 3, "X") @ Matrix.Rotation(roll, 3, "Y")
        return [tuple(Vector(center) + m @ Vector(p)) for p in points]

    # size = (sx, sy, sz), center = middle of the box
    def box(self, center, size, color, yaw=0.0, pitch=0.0, roll=0.0, top_front_drop=0.0):
        sx, sy, sz = (s / 2.0 for s in size)
        pts = [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
               (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz - top_front_drop * 2), (-sx, sy, sz - top_front_drop * 2)]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        self._poly(self._xform(pts, center, yaw, pitch, roll), faces, color)

    # Box standing on the ground: z is the base height
    def block(self, x, y, base_z, sx, sy, sz, color, yaw=0.0):
        self.box((x, y, base_z + sz / 2.0), (sx, sy, sz), color, yaw=yaw)

    # Frustum / cylinder along an axis ('x', 'y' or 'z'); center = middle
    def cyl(self, center, r, length, color, seg=8, axis="z", r2=None, yaw=0.0):
        r2 = r if r2 is None else r2
        h = length / 2.0
        ring0 = [(r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), -h) for i in range(seg)]
        ring1 = [(r2 * math.cos(2 * math.pi * i / seg), r2 * math.sin(2 * math.pi * i / seg), h) for i in range(seg)]
        pts = ring0 + ring1
        faces = [tuple(range(seg - 1, -1, -1)), tuple(range(seg, 2 * seg))]
        for i in range(seg):
            j = (i + 1) % seg
            faces.append((i, j, seg + j, seg + i))
        if axis == "x":
            pts = [(p[2], p[1], p[0]) for p in pts]
        elif axis == "y":
            pts = [(p[0], p[2], p[1]) for p in pts]
        self._poly(self._xform(pts, center, yaw), faces, color)

    # Gabled roof: width along x, ridge along y
    def gable(self, center, size, color, yaw=0.0, overhang=0.0):
        sx, sy, sz = size
        x, y = sx / 2.0 + overhang, sy / 2.0 + overhang
        pts = [(-x, -y, 0), (x, -y, 0), (x, y, 0), (-x, y, 0), (0, -y, sz), (0, y, sz)]
        faces = [(0, 3, 2, 1), (0, 1, 4), (2, 3, 5), (1, 2, 5, 4), (3, 0, 4, 5)]
        self._poly(self._xform(pts, center, yaw), faces, color)

    # Half-cylinder arch: width along x, length along y, rises sz above the base
    def arch(self, center, width, length, height, color, seg=8, yaw=0.0):
        rx, h = width / 2.0, height
        ring = [(rx * math.cos(math.pi - math.pi * i / seg), 0, h * math.sin(math.pi * i / seg)) for i in range(seg + 1)]
        y = length / 2.0
        pts = [(p[0], -y, p[2]) for p in ring] + [(p[0], y, p[2]) for p in ring]
        n = seg + 1
        faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
        for i in range(seg):
            faces.append((i, i + 1, n + i + 1, n + i))
        faces.append((n - 1, 0, n, 2 * n - 1))  # flat underside
        self._poly(self._xform(pts, center, yaw), faces, color)

    def sphere(self, center, radius, color, squash=1.0, subdiv=1, jitter=0.0, rng=None, two_tone=None):
        before = set(self.bm.faces)
        bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=radius)
        new_faces = [f for f in self.bm.faces if f not in before]
        verts = {v for f in new_faces for v in f.verts}
        for v in verts:
            v.co.z *= squash
            if jitter and rng:
                v.co += Vector((rng.uniform(-jitter, jitter), rng.uniform(-jitter, jitter), rng.uniform(-jitter, jitter))) * radius
            v.co += Vector(center)
        bmesh.ops.recalc_face_normals(self.bm, faces=new_faces)
        for f in new_faces:
            name = color
            if two_tone and f.normal.z > 0.45:
                name = two_tone
            u, v = palette_uv(name)
            for loop in f.loops:
                loop[self.uv].uv = (u, v)

    def to_object(self, name, scale=1.0):
        # Blender's FBX export maps Blender +Y to Unity -Z, so turn the whole model half a circle:
        # a unit built facing +Y then faces +Z in Unity, and doors built on -Y face the game camera.
        bmesh.ops.rotate(self.bm, verts=self.bm.verts, cent=(0.0, 0.0, 0.0), matrix=Matrix.Rotation(math.pi, 3, "Z"))
        if scale != 1.0:
            bmesh.ops.scale(self.bm, verts=self.bm.verts, vec=(scale, scale, scale), space=Matrix.Identity(4))
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------- models
def rifleman(b, rocket=False):
    legs = "olive_dark"
    for sx in (-0.1, 0.1):
        b.block(sx, 0, 0.0, 0.13, 0.15, 0.32, legs)
    torso_w = 0.42 if rocket else 0.36
    b.block(0, 0, 0.32, torso_w, 0.22, 0.34, "team")
    b.block(-torso_w / 2 - 0.05, 0, 0.36, 0.09, 0.12, 0.26, "team_dark")
    b.block(torso_w / 2 + 0.05, 0, 0.36, 0.09, 0.12, 0.26, "team_dark")
    b.sphere((0, 0, 0.77), 0.12, "skin", subdiv=1)
    b.block(0, 0, 0.82, 0.27, 0.27, 0.1, "olive")
    b.box((0.19, 0.2, 0.5), (0.05, 0.55, 0.06), "black")  # rifle / launcher grip
    if rocket:
        b.block(0, -0.19, 0.34, 0.3, 0.14, 0.3, "olive_dark")                      # backpack
        b.cyl((-0.2, 0.05, 0.72), 0.075, 0.8, "olive", seg=6, axis="y")           # tube on the shoulder
        b.cyl((-0.2, 0.5, 0.72), 0.095, 0.1, "orange", seg=6, axis="y", r2=0.05)  # warhead
        b.cyl((-0.2, -0.35, 0.72), 0.095, 0.08, "metal", seg=6, axis="y")


def jeep(b):
    for sx in (-0.5, 0.5):
        for sy in (-0.5, 0.5):
            b.cyl((sx, sy, 0.2), 0.2, 0.16, "tire", seg=8, axis="x")
    b.block(0, 0, 0.18, 0.86, 1.55, 0.22, "team_dark")                      # chassis
    b.block(0, 0.45, 0.4, 0.8, 0.55, 0.2, "team")                          # hood
    b.block(0, -0.15, 0.4, 0.8, 0.55, 0.12, "team")                        # floor
    b.block(0, 0.14, 0.52, 0.76, 0.06, 0.3, "glass")                       # windscreen
    for sx in (-0.38, 0.38):
        b.block(sx, -0.45, 0.52, 0.05, 0.05, 0.38, "metal")                # roll bar
    b.block(0, -0.45, 0.88, 0.8, 0.05, 0.05, "metal")
    b.block(0, -0.55, 0.52, 0.3, 0.3, 0.2, "olive_dark")                   # cargo / gun mount
    b.cyl((0, -0.1, 0.88), 0.05, 0.5, "black", seg=6, axis="y")             # machine gun


def tank(b):
    for sx in (-0.7, 0.7):
        b.block(sx, 0, 0.0, 0.42, 2.1, 0.42, "tire")                      # tracks
        b.block(sx, 0, 0.42, 0.46, 2.1, 0.06, "olive_dark")               # track guard
        for sy in (-0.7, -0.25, 0.25, 0.7):
            b.cyl((sx + (0.23 if sx > 0 else -0.23), sy, 0.21), 0.17, 0.05, "metal", seg=8, axis="x")
    b.box((0, 0, 0.62), (1.1, 1.9, 0.34), "team", top_front_drop=0.12)    # hull with sloped front
    b.block(0, -0.6, 0.79, 0.9, 0.5, 0.08, "team_dark")                   # engine deck
    b.cyl((0, -0.05, 0.93), 0.46, 0.3, "team", seg=8)                      # turret
    b.cyl((0, 0.95, 0.96), 0.075, 1.2, "metal", seg=6, axis="y")            # barrel
    b.cyl((0, 1.55, 0.96), 0.1, 0.12, "black", seg=6, axis="y")             # muzzle brake
    b.block(-0.18, -0.2, 1.08, 0.22, 0.22, 0.1, "team_dark")              # hatch
    b.cyl((0.22, -0.3, 1.2), 0.025, 0.4, "black", seg=4)                    # antenna


def windows(b, x0, y, z, count, step, w=0.2, h=0.2, color="glass"):
    for i in range(count):
        b.block(x0 + i * step, y, z, w, 0.05, h, color)


def hq(b):
    # Footprint 4 x 4 cells: everything stays within +-1.95.
    b.block(0, 0, 0, 3.5, 3.5, 1.0, "sand_wall")
    for sx in (-1.75, 1.75):
        for sy in (-1.75, 1.75):
            b.block(sx, sy, 0, 0.3, 0.3, 1.15, "sand_dark")
    b.block(0, 0, 1.0, 3.7, 3.7, 0.12, "concrete")                          # floor slab
    b.block(0, 0.1, 1.12, 2.4, 2.4, 0.8, "sand_light")                       # upper tier
    b.block(0, 0.1, 1.92, 2.6, 2.6, 0.14, "team_dark")                       # roof
    b.block(0, -1.78, 0.0, 0.8, 0.1, 0.75, "black")                          # door
    b.block(0, -1.8, 0.75, 1.0, 0.1, 0.12, "team")                           # sign over door
    windows(b, -1.15, -1.78, 0.45, 2, 0.4)
    windows(b, 0.75, -1.78, 0.45, 2, 0.4)
    windows(b, -0.9, -1.12, 1.4, 4, 0.6, 0.28, 0.3)
    b.cyl((0.9, 0.7, 2.6), 0.05, 1.2, "metal", seg=6)                        # antenna mast
    b.cyl((-0.9, 0.8, 2.3), 0.38, 0.12, "metal_light", seg=8, axis="y", r2=0.1)  # radar dish
    b.cyl((-1.5, -1.5, 1.7), 0.04, 1.4, "metal", seg=4)                      # flag pole
    b.block(-1.3, -1.5, 2.1, 0.5, 0.03, 0.3, "team")                         # flag
    for sx in (-1.4, -0.9, 0.9, 1.4):
        b.block(sx, -1.85, 0, 0.45, 0.2, 0.28, "sand_dark")                  # sandbags


def power_plant(b):
    b.block(0, 0, 0, 1.8, 1.8, 0.14, "concrete")
    b.block(-0.35, 0.1, 0.14, 0.9, 1.1, 0.7, "team")                         # generator housing
    b.block(-0.35, 0.1, 0.84, 1.0, 1.2, 0.08, "team_dark")
    for sx in (-0.35, 0.05):
        b.block(sx, -0.5, 0.35, 0.18, 0.05, 0.18, "black")                   # vents
    b.cyl((0.55, 0.45, 0.65), 0.28, 1.0, "metal_light", seg=8)               # tank
    b.cyl((0.55, -0.35, 0.55), 0.24, 0.8, "metal", seg=8)                    # tank 2
    b.cyl((0.55, 0.45, 1.2), 0.3, 0.1, "metal", seg=8, r2=0.2)
    b.cyl((0.0, 0.45, 1.15), 0.09, 0.9, "tire", seg=6)                       # chimney
    b.cyl((0.0, 0.45, 1.65), 0.12, 0.08, "red", seg=6)
    b.block(0.0, -0.78, 0.14, 0.5, 0.05, 0.5, "yellow")                      # hazard sign
    b.block(0.0, -0.8, 0.3, 0.12, 0.05, 0.25, "black")                       # bolt


def supply_center(b):
    b.block(0, 0.55, 0, 2.7, 1.5, 0.75, "sand_wall")                         # warehouse
    b.gable((0, 0.55, 0.75), (2.7, 1.5, 0.55), "sand_dark", overhang=0.08)
    b.block(-0.7, -0.12, 0, 0.9, 0.05, 0.6, "black")                         # big door
    b.block(0.6, -0.12, 0.25, 0.5, 0.05, 0.3, "glass")
    cols = ["container_red", "container_blue", "container_green"]
    for i, c in enumerate(cols):                                             # containers
        b.block(-0.9 + i * 0.95, -0.95, 0, 0.85, 0.55, 0.45, c)
    b.block(-0.45, -0.95, 0.45, 0.85, 0.55, 0.45, "container_blue")
    b.block(1.0, -0.95, 0.45, 0.85, 0.55, 0.45, "container_red")
    for i in range(3):
        b.block(1.15, 0.0 + i * 0.0, 0, 0.35, 0.35, 0.35 + i * 0.0, "crate")
    b.block(1.15, 0.45, 0, 0.35, 0.35, 0.35, "crate")
    b.block(1.15, 0.45, 0.35, 0.3, 0.3, 0.3, "wood")
    b.cyl((-1.2, 1.15, 0.45), 0.2, 0.9, "yellow", seg=6)                     # fuel drums
    b.cyl((-0.85, 1.15, 0.45), 0.2, 0.9, "orange", seg=6)


def barracks(b):
    b.block(0, 0.15, 0, 2.7, 1.3, 0.65, "sand_wall")
    b.gable((0, 0.15, 0.65), (2.7, 1.3, 0.5), "team", overhang=0.1)
    b.block(0, -0.52, 0, 0.5, 0.05, 0.5, "black")                            # door
    windows(b, -1.0, -0.52, 0.3, 2, 0.4, 0.22, 0.22)
    windows(b, 0.6, -0.52, 0.3, 2, 0.4, 0.22, 0.22)
    b.cyl((1.15, -0.75, 0.7), 0.035, 1.4, "metal", seg=4)                    # flag pole
    b.block(1.3, -0.75, 1.15, 0.3, 0.03, 0.2, "flag")
    for sx in (-1.0, -0.5, 0.5, 1.0):
        b.block(sx, -0.85, 0, 0.4, 0.22, 0.24, "sand_dark")                  # sandbags
    b.block(-1.05, 0.78, 0, 0.5, 0.3, 0.25, "olive")                         # supplies


def vehicle_factory(b):
    # Footprint 4 x 3 cells: x within +-1.95, y within +-1.45.
    b.block(0, 0.1, 0, 3.6, 2.2, 0.55, "concrete")
    b.arch((0, 0.1, 0.55), 3.6, 2.2, 1.05, "metal", seg=8)
    b.block(0, -0.99, 0.0, 1.9, 0.08, 1.0, "black")                          # hangar door
    b.block(0, -1.02, 1.0, 2.1, 0.08, 0.1, "yellow")
    b.block(1.55, 0.6, 0.55, 0.4, 0.7, 0.45, "sand_dark")                    # annex on the roof edge
    b.cyl((1.55, 0.6, 1.35), 0.1, 0.9, "tire", seg=6)                        # chimney
    b.block(-1.75, -0.95, 0.0, 0.3, 0.3, 0.55, "metal_light")                # pillars / lights
    b.block(1.75, -0.95, 0.0, 0.3, 0.3, 0.55, "metal_light")
    b.block(0, -1.3, 0.0, 1.8, 0.35, 0.04, "concrete")                       # apron
    for sx in (-0.5, 0, 0.5):
        b.block(sx, -1.3, 0.04, 0.2, 0.3, 0.01, "yellow")


def rock(b, seed, size):
    rng = random.Random(seed)
    b.sphere((0, 0, size * 0.3), size, "rock", squash=0.65, subdiv=1, jitter=0.22, rng=rng, two_tone="rock_light")


def palm(b):
    for i in range(4):
        b.cyl((0.04 * i, 0, 0.22 + i * 0.4), 0.11 - i * 0.012, 0.45, "wood", seg=6, r2=0.1 - i * 0.012)
    top = (0.14, 0, 1.7)
    for i in range(7):
        yaw = i * (2 * math.pi / 7)
        b.box((top[0] + 0.4 * math.sin(yaw), top[2] * 0 + 0.4 * math.cos(yaw) * -1 + top[1], top[2] - 0.05),
              (0.2, 0.9, 0.03), "green", yaw=yaw, pitch=0.5)
    b.sphere((top[0], top[1], top[2] - 0.05), 0.09, "wood_dark", subdiv=0)


def cactus(b):
    b.cyl((0, 0, 0.4), 0.11, 0.8, "green", seg=6)
    b.cyl((0.2, 0, 0.45), 0.06, 0.22, "green", seg=6, axis="x")
    b.cyl((0.3, 0, 0.6), 0.06, 0.3, "green", seg=6)
    b.cyl((-0.18, 0, 0.35), 0.06, 0.2, "green", seg=6, axis="x")
    b.cyl((-0.27, 0, 0.5), 0.06, 0.28, "green", seg=6)
    b.sphere((0, 0, 0.82), 0.08, "orange", subdiv=0)


def bush(b):
    b.sphere((0, 0, 0.12), 0.28, "dry", squash=0.5, subdiv=1, jitter=0.25, rng=random.Random(3))



# ---------------------------------------------------------------- detailed models (art style test, Faza 0)
# Same palette, same conventions, 4-6x the triangles of the simple models. Used to measure what the
# "more realistic low-poly" direction costs on a phone before the final art is made.
def rifleman_hd(b, rocket=False):
    cloth, cloth_dark, gear = "olive", "olive_dark", "khaki"
    for sx in (-0.095, 0.095):
        b.block(sx, 0.0, 0.0, 0.12, 0.2, 0.09, "black")                        # boot
        b.block(sx, -0.01, 0.09, 0.105, 0.12, 0.22, cloth_dark)                 # shin
        b.block(sx, 0.0, 0.31, 0.125, 0.14, 0.2, cloth_dark)                    # thigh
        b.block(sx, 0.0, 0.2, 0.13, 0.15, 0.04, "khaki")                        # knee pad
    b.block(0, 0, 0.5, 0.34, 0.2, 0.12, cloth_dark)                             # hips + belt
    b.block(0, 0.0, 0.52, 0.35, 0.21, 0.03, "black")
    torso_w = 0.4 if rocket else 0.36
    b.block(0, 0, 0.62, torso_w, 0.22, 0.3, cloth)                              # chest
    b.block(0, 0.12, 0.66, torso_w * 0.8, 0.05, 0.2, gear)                      # armour plate
    for sx in (-0.11, 0.0, 0.11):
        b.block(sx, 0.14, 0.58, 0.085, 0.05, 0.1, "olive_dark")                 # magazine pouches
    for sx in (-1, 1):
        x = sx * (torso_w / 2 + 0.05)
        b.block(x, 0.02, 0.7, 0.09, 0.11, 0.2, cloth)                           # upper arm
        b.block(x * 1.02, 0.1, 0.62, 0.08, 0.2, 0.08, cloth)                    # forearm
        b.sphere((x * 1.02, 0.2, 0.64), 0.04, "skin", subdiv=0)                 # hand
    b.block(0, 0, 0.92, 0.1, 0.1, 0.05, "skin")                                 # neck
    b.sphere((0, 0, 1.02), 0.1, "skin", subdiv=2)                               # head
    b.sphere((0, -0.005, 1.07), 0.125, cloth_dark, squash=0.7, subdiv=2)        # helmet shell
    b.block(0, 0.02, 1.0, 0.27, 0.27, 0.025, cloth_dark)                        # helmet brim
    b.block(0, 0.1, 1.03, 0.17, 0.03, 0.05, "black")                            # goggles
    b.block(0, -0.2, 0.52, 0.28, 0.14, 0.34, cloth_dark)                        # backpack
    b.cyl((0, -0.22, 0.88), 0.07, 0.3, "khaki", seg=8, axis="x")                # bedroll
    if rocket:
        b.cyl((-0.2, 0.0, 0.88), 0.075, 0.95, "olive", seg=10, axis="y")        # launch tube
        b.cyl((-0.2, -0.5, 0.88), 0.1, 0.1, "metal", seg=10, axis="y", r2=0.08)
        b.cyl((-0.2, 0.52, 0.88), 0.1, 0.12, "orange", seg=10, axis="y", r2=0.045)
        b.block(-0.2, 0.0, 0.94, 0.05, 0.2, 0.05, "black")                      # sight
    else:
        b.box((0.12, 0.28, 0.66), (0.06, 0.5, 0.09), "black")                   # rifle body
        b.cyl((0.12, 0.62, 0.67), 0.018, 0.34, "metal", seg=8, axis="y")        # barrel
        b.block(0.12, 0.2, 0.52, 0.04, 0.07, 0.15, "black")                     # magazine
        b.box((0.12, 0.02, 0.64), (0.05, 0.16, 0.1), "wood_dark")               # stock
        b.block(0.12, 0.3, 0.7, 0.04, 0.16, 0.05, "metal")                      # scope


def tank_hd(b):
    for sx in (-0.7, 0.7):
        out = 1 if sx > 0 else -1
        b.block(sx, 0, 0.1, 0.46, 2.2, 0.3, "tire")                            # track body
        b.block(sx, 0, 0.4, 0.5, 2.25, 0.05, "olive_dark")                     # upper track run
        b.block(sx + out * 0.2, 0, 0.45, 0.12, 2.1, 0.22, "olive_dark")        # side skirt
        for i in range(7):
            b.cyl((sx + out * 0.24, -0.88 + i * 0.29, 0.22), 0.17, 0.06, "metal", seg=12, axis="x")   # road wheels
            b.cyl((sx + out * 0.265, -0.88 + i * 0.29, 0.22), 0.07, 0.03, "tire", seg=8, axis="x")    # hubs
        b.cyl((sx + out * 0.24, 1.08, 0.3), 0.2, 0.08, "metal_light", seg=12, axis="x")               # idler
        b.cyl((sx + out * 0.24, -1.08, 0.3), 0.2, 0.08, "metal_light", seg=12, axis="x")              # sprocket
        for i in range(4):
            b.cyl((sx, -0.6 + i * 0.4, 0.46), 0.05, 0.4, "metal", seg=6, axis="x")                    # return rollers
        for i in range(10):
            b.block(sx, -1.0 + i * 0.22, 0.0, 0.5, 0.1, 0.06, "black")                                 # track pads
    b.box((0, -0.05, 0.62), (1.14, 2.0, 0.34), "team", top_front_drop=0.12)    # hull
    b.block(0, 0.95, 0.45, 1.1, 0.25, 0.1, "team_dark")                        # glacis plate
    b.block(0, -0.62, 0.79, 0.95, 0.62, 0.07, "team_dark")                     # engine deck
    for i in range(7):
        b.block(-0.36 + i * 0.12, -0.62, 0.86, 0.06, 0.5, 0.025, "black")      # grille slats
    b.block(0, 0.05, 0.79, 0.8, 0.2, 0.05, "olive_dark")                       # turret ring
    b.cyl((0, -0.08, 0.98), 0.5, 0.3, "team", seg=12)                          # turret
    b.cyl((0, -0.08, 1.14), 0.42, 0.04, "team_dark", seg=12)                   # turret roof
    b.block(0, 0.4, 0.88, 0.5, 0.25, 0.26, "team_dark")                        # gun mantlet
    b.cyl((0, 1.0, 1.0), 0.075, 1.3, "metal", seg=10, axis="y")                # barrel
    b.cyl((0, 1.55, 1.0), 0.1, 0.15, "black", seg=10, axis="y")                # muzzle brake
    b.cyl((0, 0.45, 1.0), 0.11, 0.2, "metal_light", seg=10, axis="y")          # recoil sleeve
    b.cyl((-0.2, -0.2, 1.2), 0.11, 0.07, "team_dark", seg=10)                  # commander hatch
    b.cyl((0.2, -0.2, 1.17), 0.1, 0.05, "team_dark", seg=10)                   # loader hatch
    b.cyl((0.25, 0.1, 1.22), 0.03, 0.22, "black", seg=6, axis="y")             # machine gun
    for i in range(4):
        b.cyl((0.45, 0.2 - i * 0.07, 0.99 + i * 0.01), 0.025, 0.12, "black", seg=6, axis="x")         # smoke launchers
    b.block(-0.5, -0.45, 1.12, 0.4, 0.5, 0.2, "khaki")                         # turret stowage
    b.block(0.35, -0.95, 0.79, 0.3, 0.2, 0.18, "crate")                        # rear crate
    b.cyl((0.27, -0.4, 1.35), 0.02, 0.5, "black", seg=4)                       # antenna
    b.block(-0.55, 0.5, 0.74, 0.18, 0.55, 0.05, "metal")                       # tool rack


def barracks_hd(b):
    b.block(0, 0.15, 0, 2.8, 1.4, 0.12, "concrete")                            # foundation
    b.block(0, 0.15, 0.12, 2.6, 1.2, 0.58, "sand_wall")                        # walls
    for i in range(7):
        b.block(-1.25 + i * 0.42, -0.45, 0.12, 0.06, 0.04, 0.58, "sand_dark")  # wall ribs
    b.block(0, 0.15, 0.7, 2.7, 1.3, 0.06, "sand_dark")                         # eaves
    b.gable((0, 0.15, 0.76), (2.7, 1.3, 0.5), "team", overhang=0.1)            # roof
    b.block(0, 0.15, 1.24, 2.75, 0.07, 0.05, "team_dark")                      # ridge cap
    for i in range(4):
        b.block(-1.1 + i * 0.7, 0.55, 0.85, 0.22, 0.2, 0.07, "metal")          # roof vents
    b.cyl((1.0, 0.4, 1.05), 0.1, 0.5, "sand_dark", seg=8)                      # chimney
    b.block(1.0, 0.4, 1.3, 0.28, 0.28, 0.05, "concrete")
    b.block(0, -0.5, 0.12, 0.56, 0.06, 0.52, "black")                          # door
    b.block(0, -0.52, 0.64, 0.7, 0.06, 0.06, "wood_dark")                      # door lintel
    for sx in (-0.31, 0.31):
        b.block(sx, -0.52, 0.12, 0.06, 0.06, 0.56, "wood_dark")                # door posts
    for i in range(3):
        b.block(0, -0.62 - i * 0.1, 0.12 - i * 0.04, 0.8, 0.1, 0.04 * (i + 1), "concrete")            # steps
    for cx in (-1.0, -0.6, 0.6, 1.0):
        b.block(cx, -0.5, 0.3, 0.24, 0.05, 0.26, "glass")                      # windows
        b.block(cx, -0.52, 0.27, 0.3, 0.05, 0.03, "wood_dark")                 # sills
        for sx in (-0.15, 0.15):
            b.block(cx + sx, -0.53, 0.3, 0.05, 0.04, 0.26, "wood")             # shutters
    for cx in (-1.3, 1.3):
        b.block(cx, 0.15, 0.3, 0.05, 0.24, 0.26, "glass")                      # side windows
    b.cyl((1.15, -0.8, 0.6), 0.03, 1.5, "metal", seg=6)                        # flag pole
    b.block(1.32, -0.8, 1.2, 0.34, 0.03, 0.22, "flag")
    b.block(1.32, -0.8, 1.2, 0.34, 0.035, 0.07, "team")
    for row in range(2):
        for i in range(9):
            off = 0.0 if row == 0 else 0.22
            x = -1.4 + i * 0.35 + off
            if abs(x) > 1.45 or abs(x) < 0.3 and False:
                continue
            b.block(x, -0.9, row * 0.14, 0.34, 0.2, 0.14, "sand_dark")         # sandbag wall
    for i in range(4):
        b.block(-1.2 + (i % 2) * 0.3, 0.95 + (i // 2) * 0.28, 0, 0.26, 0.24, 0.24, "crate")           # crates
    b.block(-1.05, 1.0, 0.24, 0.24, 0.22, 0.2, "wood")
    for i in range(3):
        b.cyl((0.1 + i * 0.28, 0.98, 0.23), 0.11, 0.46, "olive_dark", seg=8)  # barrels
    for sx in (-1.45, 1.45):
        b.cyl((sx, -0.35, 0.6), 0.025, 1.2, "black", seg=4)                    # lamp posts
        b.block(sx, -0.35, 1.2, 0.12, 0.1, 0.06, "yellow")
    b.block(-1.1, 0.78, 0.7, 0.3, 0.2, 0.22, "metal_light")                    # air conditioner
    b.cyl((-0.4, 0.5, 1.0), 0.02, 0.9, "metal", seg=4)                         # radio mast
    for i in range(3):
        b.block(-0.4, 0.5, 1.05 + i * 0.22, 0.28, 0.02, 0.02, "metal")



# ---------------------------------------------------------------- detailed set, part 2 (Faza 2 art pass)
def jeep_hd(b):
    for sx in (-0.52, 0.52):
        out = 1 if sx > 0 else -1
        for sy in (-0.52, 0.52):
            b.cyl((sx, sy, 0.22), 0.22, 0.18, "tire", seg=10, axis="x")                 # tyre
            b.cyl((sx + out * 0.1, sy, 0.22), 0.1, 0.04, "metal_light", seg=8, axis="x")  # hub
        b.block(sx + out * 0.05, 0.52, 0.34, 0.3, 0.5, 0.05, "team_dark")              # front fender
        b.block(sx + out * 0.05, -0.52, 0.34, 0.3, 0.5, 0.05, "team_dark")             # rear fender
    b.block(0, 0, 0.16, 0.8, 1.7, 0.2, "black")                                          # underbody
    b.block(0, 0, 0.3, 0.9, 1.65, 0.12, "team_dark")                                     # chassis
    b.box((0, 0.5, 0.52), (0.84, 0.7, 0.3), "team", top_front_drop=0.04)                # hood
    b.block(0, 0.86, 0.36, 0.78, 0.06, 0.26, "metal")                                    # grille
    for i in range(4):
        b.block(-0.27 + i * 0.18, 0.89, 0.4, 0.05, 0.03, 0.18, "black")
    for sx in (-0.3, 0.3):
        b.block(sx, 0.88, 0.58, 0.14, 0.05, 0.1, "yellow")                              # headlights
    b.block(0, -0.2, 0.42, 0.84, 0.9, 0.1, "team")                                       # cabin floor
    b.block(0, 0.12, 0.56, 0.8, 0.07, 0.34, "glass")                                     # windscreen
    b.block(0, 0.12, 0.9, 0.84, 0.09, 0.05, "team_dark")                                 # screen frame
    for sx in (-0.2, 0.2):
        b.block(sx, -0.12, 0.52, 0.28, 0.3, 0.1, "olive_dark")                           # seats
        b.block(sx, -0.3, 0.6, 0.26, 0.07, 0.28, "olive_dark")
    for sx in (-0.42, 0.42):
        b.block(sx, -0.55, 0.52, 0.05, 0.05, 0.46, "metal")                              # roll bar
    b.block(0, -0.55, 0.96, 0.9, 0.05, 0.05, "metal")
    b.block(0, -0.7, 0.42, 0.7, 0.4, 0.1, "olive_dark")                                  # cargo bed
    b.block(0.2, -0.75, 0.52, 0.28, 0.28, 0.2, "crate")
    b.cyl((-0.2, -0.75, 0.6), 0.1, 0.35, "olive", seg=8)                                 # jerrycan drum
    b.cyl((0, -0.88, 0.5), 0.2, 0.06, "metal", seg=8, axis="y")                          # spare wheel hub
    b.cyl((0, -0.92, 0.5), 0.22, 0.1, "tire", seg=10, axis="y")
    b.block(0, -0.1, 0.96, 0.05, 0.05, 0.28, "metal")                                    # gun post
    b.cyl((0, 0.12, 1.25), 0.05, 0.6, "black", seg=6, axis="y")                          # machine gun
    b.block(0, -0.12, 1.2, 0.16, 0.2, 0.1, "metal")                                      # receiver
    b.cyl((0.45, -0.8, 1.1), 0.015, 0.8, "black", seg=4)                                 # antenna
    b.block(0, 0.55, 0.78, 0.4, 0.05, 0.03, "team_dark")                                 # hood stripe
    b.block(-0.55, 0.3, 0.55, 0.05, 0.2, 0.18, "black")                                  # mirror


def sniper_hd(b):
    """Rifleman body in a darker kit, long rifle, bipod, ghillie scarf: a thin tall silhouette."""
    rifleman_hd(b, False)
    b.block(0, 0.02, 1.06, 0.3, 0.3, 0.05, "dry")                                        # camo cover over the helmet
    b.block(0, -0.12, 0.7, 0.4, 0.06, 0.3, "dry")                                        # camo cape
    b.block(0, -0.2, 0.46, 0.34, 0.06, 0.34, "dry")
    b.cyl((0.12, 0.85, 0.67), 0.022, 0.6, "metal", seg=8, axis="y")                      # long barrel
    b.cyl((0.12, 1.16, 0.67), 0.034, 0.1, "black", seg=8, axis="y")                      # suppressor
    b.cyl((0.12, 0.35, 0.76), 0.04, 0.34, "black", seg=8, axis="y")                      # big scope
    for sx in (-0.05, 0.05):
        b.block(0.12 + sx, 0.9, 0.5, 0.015, 0.015, 0.18, "metal")                        # bipod legs


def armored_car_hd(b):
    for sx in (-0.62, 0.62):
        out = 1 if sx > 0 else -1
        for sy in (-0.7, 0.0, 0.7):
            b.cyl((sx, sy, 0.26), 0.26, 0.22, "tire", seg=10, axis="x")
            b.cyl((sx + out * 0.12, sy, 0.26), 0.12, 0.04, "metal_light", seg=8, axis="x")
    b.block(0, 0, 0.18, 1.1, 2.4, 0.18, "black")                                         # underbody
    b.box((0, 0, 0.64), (1.3, 2.3, 0.62), "team", top_front_drop=0.1)                    # hull
    b.block(0, 1.02, 0.46, 1.2, 0.2, 0.34, "team_dark")                                  # sloped nose plate
    for i in range(3):
        b.block(-0.35 + i * 0.35, 1.14, 0.58, 0.28, 0.03, 0.05, "black")                 # vision slits
    for sx in (-0.5, 0.5):
        b.block(sx, 1.15, 0.5, 0.14, 0.05, 0.09, "yellow")                               # headlights
    for sx in (-0.66, 0.66):
        b.block(sx, 0.2, 0.74, 0.06, 0.9, 0.22, "team_dark")                             # side armour
        b.block(sx, 0.2, 0.9, 0.04, 0.5, 0.05, "black")                                  # firing port
    b.block(0, -0.9, 0.98, 1.0, 0.5, 0.08, "team_dark")                                  # engine deck
    for i in range(5):
        b.block(-0.36 + i * 0.18, -0.9, 1.04, 0.08, 0.38, 0.025, "black")                # grille slats
    b.cyl((0, 0.1, 1.08), 0.44, 0.1, "team_dark", seg=10)                                # turret ring
    b.cyl((0, 0.1, 1.28), 0.38, 0.3, "team", seg=10)                                     # turret
    b.cyl((0, 0.1, 1.44), 0.3, 0.04, "team_dark", seg=10)
    b.cyl((0, 0.85, 1.3), 0.06, 1.0, "metal", seg=8, axis="y")                           # autocannon
    b.cyl((0, 1.38, 1.3), 0.085, 0.12, "black", seg=8, axis="y")
    b.cyl((0.14, 0.7, 1.36), 0.03, 0.6, "black", seg=6, axis="y")                        # coax gun
    b.cyl((-0.22, -0.05, 1.5), 0.11, 0.06, "team_dark", seg=8)                           # hatch
    b.block(0.28, 0.35, 1.52, 0.2, 0.08, 0.1, "glass")                                   # sight
    b.cyl((0.3, -0.5, 1.75), 0.015, 0.7, "black", seg=4)                                 # antenna
    b.block(0, -1.2, 0.5, 0.9, 0.1, 0.12, "metal")                                       # rear bumper
    b.block(0.5, -1.15, 0.9, 0.32, 0.2, 0.2, "crate")                                    # rear stowage


def hq_hd(b):
    # Footprint 4 x 4 cells: everything stays within +-1.95. Door on the -Y side (towards the camera).
    b.block(0, 0, 0, 3.8, 3.8, 0.12, "concrete")                                         # foundation
    b.block(0, 0, 0.12, 3.4, 3.4, 0.95, "sand_wall")                                     # ground floor
    for i in range(9):
        b.block(-1.6 + i * 0.4, -1.72, 0.12, 0.08, 0.06, 0.95, "sand_dark")              # wall ribs
    for sx in (-1.75, 1.75):
        for sy in (-1.75, 1.75):
            b.block(sx, sy, 0.0, 0.34, 0.34, 1.3, "sand_dark")                           # corner towers
            b.block(sx, sy, 1.3, 0.42, 0.42, 0.07, "concrete")
    b.block(0, 0, 1.07, 3.7, 3.7, 0.1, "concrete")                                       # slab
    b.block(0, 0.1, 1.17, 2.5, 2.5, 0.85, "sand_light")                                  # upper tier
    for i in range(6):
        b.block(-1.0 + i * 0.4, -1.17, 1.17, 0.07, 0.05, 0.85, "sand_wall")              # upper ribs
    b.block(0, 0.1, 2.02, 2.7, 2.7, 0.14, "team_dark")                                   # roof
    b.block(0, 0.1, 2.16, 2.4, 2.4, 0.05, "team")                                        # roof inlay
    b.block(0, -1.75, 0.12, 0.9, 0.08, 0.8, "black")                                     # door
    b.block(0, -1.78, 0.92, 1.1, 0.1, 0.14, "team")                                      # sign
    for sx in (-0.5, 0.5):
        b.block(sx, -1.78, 0.12, 0.08, 0.08, 0.84, "wood_dark")                          # door posts
    for i in range(3):
        b.block(0, -1.88 - i * 0.12, 0.12 - i * 0.04, 1.3, 0.12, 0.04 * (i + 1), "concrete")  # steps
    for cx in (-1.3, -0.85, 0.85, 1.3):
        b.block(cx, -1.76, 0.45, 0.26, 0.06, 0.34, "glass")                              # ground windows
        b.block(cx, -1.78, 0.4, 0.32, 0.06, 0.04, "wood_dark")
    for i in range(5):
        b.block(-1.0 + i * 0.5, -1.2, 1.4, 0.3, 0.05, 0.36, "glass")                     # upper windows
        b.block(-1.0 + i * 0.5, -1.22, 1.35, 0.36, 0.05, 0.04, "sand_dark")
    for sy in (-0.8, 0.0, 0.8):
        b.block(-1.72, sy, 0.5, 0.06, 0.3, 0.34, "glass")                                # side windows
        b.block(1.72, sy, 0.5, 0.06, 0.3, 0.34, "glass")
    b.cyl((1.0, 0.8, 2.75), 0.05, 1.3, "metal", seg=6)                                   # mast
    for i in range(3):
        b.block(1.0, 0.8, 2.4 + i * 0.3, 0.4, 0.03, 0.03, "metal")
    b.cyl((-0.9, 0.9, 2.3), 0.4, 0.12, "metal_light", seg=10, axis="y", r2=0.1)          # radar dish
    b.cyl((-0.9, 0.9, 2.25), 0.07, 0.3, "metal", seg=6)
    for sx in (-1.0, 1.0):
        b.block(sx, 0.9, 2.21, 0.4, 0.4, 0.3, "metal")                                   # roof machinery
    b.cyl((-1.55, -1.95, 0.9), 0.04, 1.8, "metal", seg=6)                                # flag pole
    b.block(-1.32, -1.95, 1.7, 0.5, 0.03, 0.34, "flag")
    b.block(-1.32, -1.95, 1.7, 0.5, 0.035, 0.1, "team")
    for row in range(2):
        for i in range(10):
            x = -1.8 + i * 0.4 + (0.2 if row else 0.0)
            if abs(x) < 0.75 or abs(x) > 1.9:
                continue
            b.block(x, -2.15, row * 0.14, 0.38, 0.22, 0.14, "sand_dark")                 # sandbags either side of the door
    for i in range(3):
        b.block(1.35 + (i % 2) * 0.3, 1.85 + 0.0, 0.12 + (i // 2) * 0.3, 0.26, 0.24, 0.28, "crate")
    b.cyl((-1.4, 1.85, 0.4), 0.16, 0.56, "olive_dark", seg=8)                            # barrels
    b.cyl((-1.05, 1.85, 0.4), 0.16, 0.56, "olive", seg=8)


def power_plant_hd(b):
    # Footprint 2 x 2 cells: within +-0.95.
    b.block(0, 0, 0, 1.9, 1.9, 0.14, "concrete")
    b.block(-0.38, 0.12, 0.14, 0.95, 1.1, 0.72, "team")                                  # generator housing
    b.block(-0.38, 0.12, 0.86, 1.05, 1.2, 0.07, "team_dark")
    for i in range(4):
        b.block(-0.7 + i * 0.22, -0.45, 0.3, 0.08, 0.04, 0.42, "black")                  # louvres
    b.block(-0.38, -0.5, 0.14, 0.9, 0.05, 0.12, "metal")                                 # skid
    b.block(-0.38, 0.12, 0.93, 0.5, 0.4, 0.2, "metal_light")                             # roof unit
    b.cyl((-0.38, 0.12, 1.2), 0.1, 0.18, "metal", seg=8)
    b.cyl((0.58, 0.42, 0.7), 0.3, 1.1, "metal_light", seg=10)                            # storage tank
    b.cyl((0.58, 0.42, 1.28), 0.3, 0.1, "metal", seg=10, r2=0.18)
    for i in range(5):
        b.cyl((0.58, 0.42, 0.3 + i * 0.22), 0.31, 0.03, "metal", seg=10)                 # tank bands
    b.cyl((0.6, -0.4, 0.55), 0.24, 0.8, "metal", seg=10)                                 # tank 2
    b.cyl((0.6, -0.4, 0.97), 0.2, 0.06, "red", seg=10)
    b.cyl((0.1, 0.55, 1.2), 0.1, 1.1, "tire", seg=8)                                     # chimney
    b.cyl((0.1, 0.55, 1.78), 0.13, 0.08, "red", seg=8)
    b.block(0.1, -0.1, 0.14, 0.14, 0.9, 0.04, "tire")                                    # pipe run
    b.cyl((0.35, -0.1, 0.5), 0.04, 0.8, "metal", seg=6, axis="y")
    b.block(0.0, -0.9, 0.14, 0.5, 0.05, 0.5, "yellow")                                   # hazard sign
    b.block(0.0, -0.93, 0.3, 0.1, 0.05, 0.26, "black")
    b.block(-0.85, -0.7, 0.14, 0.18, 0.2, 0.22, "crate")
    b.cyl((0.8, -0.85, 0.3), 0.04, 0.6, "black", seg=4)                                  # lamp
    b.block(0.8, -0.85, 0.6, 0.12, 0.1, 0.05, "yellow")


def supply_center_hd(b):
    # Footprint 3 x 3 cells: within +-1.45.
    b.block(0, 0, 0, 2.9, 2.9, 0.1, "concrete")
    b.block(0, 0.55, 0.1, 2.7, 1.6, 0.75, "sand_wall")                                   # warehouse
    for i in range(6):
        b.block(-1.15 + i * 0.46, -0.27, 0.1, 0.06, 0.04, 0.75, "sand_dark")
    b.gable((0, 0.55, 0.85), (2.7, 1.6, 0.5), "sand_dark", overhang=0.1)                 # roof
    b.block(0, 0.55, 1.33, 2.75, 0.07, 0.05, "team_dark")                                # ridge
    for i in range(3):
        b.block(-0.8 + i * 0.8, 0.9, 1.0, 0.3, 0.24, 0.06, "metal")                      # roof vents
    b.block(-0.7, -0.26, 0.1, 0.95, 0.06, 0.62, "black")                                 # roller door
    for i in range(5):
        b.block(-0.7, -0.29, 0.2 + i * 0.1, 0.95, 0.03, 0.02, "metal")
    b.block(0.62, -0.26, 0.3, 0.5, 0.06, 0.32, "glass")
    b.block(0.62, -0.28, 0.27, 0.56, 0.06, 0.03, "wood_dark")
    b.block(1.0, -0.26, 0.1, 0.4, 0.06, 0.55, "team")                                    # sign panel
    cols = ["container_red", "container_blue", "container_green"]
    for i, c in enumerate(cols):                                                         # container row
        b.block(-1.0 + i * 0.95, -0.95, 0.1, 0.88, 0.58, 0.46, c)
        b.block(-1.0 + i * 0.95, -0.95, 0.56, 0.88, 0.04, 0.03, "metal")
    b.block(-0.55, -0.95, 0.56, 0.88, 0.58, 0.46, "container_blue")
    b.block(0.95, -0.95, 0.56, 0.88, 0.58, 0.46, "container_red")
    for i in range(4):
        b.block(1.2, 0.0 + (i % 2) * 0.4, 0.1 + (i // 2) * 0.34, 0.36, 0.36, 0.34, "crate")
    b.block(1.2, 0.2, 0.78, 0.3, 0.3, 0.28, "wood")
    for i in range(3):
        b.cyl((-1.2 + i * 0.36, 1.2, 0.4), 0.17, 0.6, ["yellow", "orange", "olive"][i], seg=8)   # drums
    b.cyl((1.1, 1.15, 0.55), 0.03, 1.1, "metal", seg=4)                                  # lamp post
    b.block(1.1, 1.15, 1.1, 0.14, 0.1, 0.06, "yellow")
    b.block(0, 1.5, 0.1, 1.4, 0.24, 0.55, "sand_dark")                                   # loading dock
    b.block(-0.9, -1.35, 0.1, 0.5, 0.2, 0.2, "olive")                                    # pallets
    b.block(0.35, -1.35, 0.1, 0.5, 0.2, 0.2, "olive_dark")


def vehicle_factory_hd(b):
    # Footprint 4 x 3 cells: x within +-1.95, y within +-1.45.
    b.block(0, 0.05, 0, 3.8, 2.7, 0.1, "concrete")
    b.block(0, 0.15, 0.1, 3.6, 2.2, 0.55, "concrete")                                    # plinth
    b.arch((0, 0.15, 0.65), 3.4, 2.2, 1.0, "metal", seg=10)                              # hangar roof
    for i in range(6):
        b.block(-1.6 + i * 0.64, -0.9, 0.65, 0.08, 0.06, 0.7, "metal_light")             # roof ribs front
    b.block(0, -0.97, 0.1, 2.0, 0.08, 1.05, "black")                                     # hangar door
    for i in range(6):
        b.block(0, -1.0, 0.2 + i * 0.16, 2.0, 0.03, 0.02, "metal")                       # door slats
    b.block(0, -1.0, 1.15, 2.2, 0.1, 0.1, "yellow")                                      # hazard header
    for sx in (-1.15, 1.15):
        b.block(sx, -1.0, 0.1, 0.12, 0.1, 1.15, "yellow")
    b.block(1.55, 0.7, 0.65, 0.5, 0.8, 0.55, "sand_dark")                                # annex
    b.block(1.55, 0.7, 1.2, 0.6, 0.9, 0.06, "concrete")
    b.cyl((1.55, 0.9, 1.7), 0.1, 1.0, "tire", seg=8)                                     # chimney
    b.cyl((1.55, 0.9, 2.2), 0.13, 0.07, "red", seg=8)
    b.cyl((-1.55, 0.7, 1.15), 0.22, 0.5, "metal_light", seg=8)                           # fan housing
    b.cyl((-1.55, 0.7, 1.42), 0.18, 0.04, "black", seg=8)
    for sx in (-1.85, 1.85):
        b.block(sx, -1.0, 0.1, 0.26, 0.26, 0.7, "metal_light")                           # lamp pillars
        b.block(sx, -1.0, 0.8, 0.34, 0.3, 0.07, "yellow")
    b.block(0, -1.35, 0.1, 2.0, 0.3, 0.03, "concrete")                                   # apron
    for sx in (-0.6, 0, 0.6):
        b.block(sx, -1.35, 0.13, 0.18, 0.26, 0.01, "yellow")
    b.block(0, -0.97, 1.3, 1.0, 0.06, 0.2, "team")                                       # sign
    for i in range(3):
        b.cyl((-1.3 + i * 0.3, -1.25, 0.28), 0.12, 0.34, "olive_dark", seg=8)            # tyres/drums
    b.block(1.2, -1.2, 0.1, 0.4, 0.3, 0.3, "crate")
    b.cyl((1.0, 0.1, 1.9), 0.015, 0.9, "black", seg=4)                                   # antenna
    b.block(-0.5, 1.2, 0.65, 0.5, 0.1, 0.4, "metal")                                     # rear panel


def repair_station_hd(b):
    # Footprint 2 x 2 cells: within +-0.95. An open gantry over a vehicle bay.
    b.block(0, 0, 0, 1.9, 1.9, 0.08, "concrete")
    b.block(0, 0.1, 0.08, 1.5, 1.4, 0.03, "metal")                                       # repair pad
    for i in range(4):
        b.block(-0.55 + i * 0.37, 0.1, 0.11, 0.08, 1.2, 0.01, "yellow")                  # bay stripes
    for sx in (-0.78, 0.78):
        for sy in (-0.7, 0.55):
            b.block(sx, sy, 0.08, 0.12, 0.12, 1.15, "metal_light")                       # gantry posts
    for sy in (-0.7, 0.55):
        b.block(0, sy, 1.18, 1.7, 0.14, 0.12, "team")                                    # cross beams
    for sx in (-0.78, 0.78):
        b.block(sx, -0.08, 1.18, 0.14, 1.4, 0.12, "team_dark")                           # side beams
    b.block(0, -0.08, 1.12, 0.3, 0.4, 0.2, "metal")                                      # crane trolley
    b.cyl((0, -0.08, 0.85), 0.02, 0.5, "black", seg=4)                                   # cable
    b.block(0, -0.08, 0.62, 0.16, 0.12, 0.1, "yellow")                                   # hook block
    b.block(-0.55, 0.75, 0.08, 0.5, 0.3, 0.55, "sand_dark")                              # tool shed
    b.block(-0.55, 0.75, 0.63, 0.56, 0.36, 0.05, "team_dark")
    b.block(-0.55, 0.58, 0.12, 0.26, 0.04, 0.4, "black")
    b.block(0.5, 0.78, 0.08, 0.4, 0.3, 0.3, "crate")                                     # parts
    b.cyl((0.82, 0.8, 0.2), 0.12, 0.4, "olive_dark", seg=8)
    b.block(0, -0.85, 0.08, 0.5, 0.05, 0.5, "white")                                     # medical-style sign
    b.block(0, -0.88, 0.18, 0.34, 0.05, 0.1, "red")
    b.block(0, -0.88, 0.1, 0.1, 0.05, 0.34, "red")
    b.cyl((0.8, -0.8, 0.5), 0.025, 1.0, "metal", seg=4)                                  # lamp
    b.block(0.8, -0.8, 1.0, 0.14, 0.1, 0.06, "yellow")


def tower_hd(b):
    # Footprint 2 x 2 cells: within +-0.95. A sandbagged guard tower with a turret gun.
    b.block(0, 0, 0, 1.9, 1.9, 0.12, "concrete")
    for row in range(2):
        for i in range(5):
            b.block(-0.85 + i * 0.4 + row * 0.2, -0.85, 0.12 + row * 0.14, 0.38, 0.2, 0.14, "sand_dark")   # front sandbags
    b.block(0, 0, 0.12, 1.0, 1.0, 0.5, "sand_wall")                                      # base
    b.block(0, 0, 0.62, 0.8, 0.8, 0.9, "sand_wall")                                      # shaft
    for i in range(4):
        b.block(0, -0.41, 0.7 + i * 0.2, 0.5, 0.03, 0.04, "sand_dark")                   # brickwork bands
    b.block(0, -0.41, 0.12, 0.34, 0.04, 0.4, "black")                                    # door
    b.block(0, 0, 1.52, 1.3, 1.3, 0.1, "concrete")                                       # platform
    for sx in (-0.62, 0.62):
        b.block(sx, 0, 1.62, 0.07, 1.3, 0.34, "sand_dark")                               # parapet
    for sy in (-0.62, 0.62):
        b.block(0, sy, 1.62, 1.3, 0.07, 0.34, "sand_dark")
    b.block(0, -0.64, 1.72, 0.7, 0.05, 0.08, "black")                                    # firing slit
    for sx in (-0.55, 0.55):
        b.block(sx, 0.4, 1.62, 0.25, 0.25, 0.34, "crate")                                # ammo boxes
    b.cyl((0, 0, 1.72), 0.3, 0.14, "team_dark", seg=10)                                  # turret base
    b.cyl((0, 0, 1.9), 0.26, 0.24, "team", seg=10)                                       # turret
    b.cyl((0, 0.55, 1.92), 0.045, 0.9, "metal", seg=8, axis="y")                         # barrel
    b.cyl((0, 1.0, 1.92), 0.07, 0.1, "black", seg=8, axis="y")
    b.block(0, 0.1, 2.06, 0.2, 0.2, 0.06, "glass")                                       # sight
    b.cyl((0.45, -0.45, 2.0), 0.02, 0.9, "black", seg=4)                                 # antenna
    b.cyl((-0.5, -0.5, 1.8), 0.03, 1.0, "metal", seg=4)                                  # flag pole
    b.block(-0.34, -0.5, 2.15, 0.34, 0.03, 0.22, "team")
    b.block(0, 0.9, 0.12, 0.5, 0.2, 0.18, "olive")


# The detailed infantry is drawn a bit taller than the simple one; shrink it to the same size for a fair comparison.
HD_SCALE = {"unit_rifleman_hd": 0.8, "unit_rocket_hd": 0.8, "unit_sniper_hd": 0.8}

MODELS = {
    "unit_rifleman": lambda b: rifleman(b, False),
    "unit_rocket": lambda b: rifleman(b, True),
    "unit_jeep": jeep,
    "unit_tank": tank,
    "bld_hq": hq,
    "bld_power": power_plant,
    "bld_supply": supply_center,
    "bld_barracks": barracks,
    "bld_factory": vehicle_factory,
    "unit_rifleman_hd": lambda b: rifleman_hd(b, False),
    "unit_rocket_hd": lambda b: rifleman_hd(b, True),
    "unit_tank_hd": tank_hd,
    "bld_barracks_hd": barracks_hd,
    "unit_jeep_hd": jeep_hd,
    "unit_sniper_hd": sniper_hd,
    "unit_armoredcar_hd": armored_car_hd,
    "bld_hq_hd": hq_hd,
    "bld_power_hd": power_plant_hd,
    "bld_supply_hd": supply_center_hd,
    "bld_factory_hd": vehicle_factory_hd,
    "bld_repair_hd": repair_station_hd,
    "bld_tower_hd": tower_hd,
    "prop_rock_a": lambda b: rock(b, 11, 0.62),
    "prop_rock_b": lambda b: rock(b, 23, 0.5),
    "prop_rock_c": lambda b: rock(b, 37, 0.72),
    "prop_palm": palm,
    "prop_cactus": cactus,
    "prop_bush": bush,
}

# ---------------------------------------------------------------- pipeline
def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for me in list(bpy.data.meshes):
        bpy.data.meshes.remove(me)


def make_palette_material(png_path):
    mat = bpy.data.materials.new("Palette")
    mat.use_nodes = True
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tex = tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(png_path)
    tex.interpolation = "Closest"
    tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def render_preview(obj, path, size):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 420
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w") if not scene.world else scene.world
    scene.world = world
    world.color = (0.55, 0.62, 0.7)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    # Looks down at 55 degrees from the -Y side, like the in-game camera, aimed at the middle of the model.
    d = size * 2
    elevation = math.radians(55)
    target = Vector((0.0, 0.0, obj.dimensions.z / 2.0))
    cam.location = target + Vector((0.0, d * math.cos(elevation), d * math.sin(elevation)))
    cam.rotation_euler = (math.radians(90) - elevation, 0.0, math.radians(180))  # x = 90 looks horizontally, x = 0 straight down
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    models_dir, palette_png = argv[0], argv[1]
    only = [a[len("--only="):] for a in argv if a.startswith("--only=")]   # e.g. --only=_hd: export just those models
    argv = [a for a in argv if not a.startswith("--only=")]
    previews_dir = argv[2] if len(argv) > 2 else None
    os.makedirs(models_dir, exist_ok=True)
    os.makedirs(os.path.dirname(palette_png), exist_ok=True)
    if previews_dir:
        os.makedirs(previews_dir, exist_ok=True)

    clear_scene()
    write_palette_png(palette_png)
    material = make_palette_material(palette_png)

    for name, build in MODELS.items():
        if only and not any(name.endswith(o) for o in only):
            continue
        clear_scene()
        b = Builder()
        build(b)
        obj = b.to_object(name, HD_SCALE.get(name, 1.0))
        obj.data.materials.append(material)

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(models_dir, name + ".fbx"),
            use_selection=True, axis_forward="Z", axis_up="Y",
            apply_scale_options="FBX_SCALE_ALL", global_scale=1.0,
            mesh_smooth_type="FACE", add_leaf_bones=False, bake_space_transform=True,
        )
        if previews_dir:
            dims = obj.dimensions
            render_preview(obj, os.path.join(previews_dir, name + ".png"), max(dims.x, dims.y, dims.z) * 1.6)

        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"MODEL {name}: {len(obj.data.polygons)} faces, {tris} tris, size=({obj.dimensions.x:.2f}, {obj.dimensions.y:.2f}, {obj.dimensions.z:.2f})")


main()
