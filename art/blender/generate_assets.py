"""
Generates every low-poly model of the game, plus the shared colour palette texture.

Run (headless):
    blender -b -P art/blender/generate_assets.py -- <models_out_dir> <palette_png_path> [<previews_dir>]

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

    def to_object(self, name):
        # Blender's FBX export maps Blender +Y to Unity -Z, so turn the whole model half a circle:
        # a unit built facing +Y then faces +Z in Unity, and doors built on -Y face the game camera.
        bmesh.ops.rotate(self.bm, verts=self.bm.verts, cent=(0.0, 0.0, 0.0), matrix=Matrix.Rotation(math.pi, 3, "Z"))
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
    previews_dir = argv[2] if len(argv) > 2 else None
    os.makedirs(models_dir, exist_ok=True)
    os.makedirs(os.path.dirname(palette_png), exist_ok=True)
    if previews_dir:
        os.makedirs(previews_dir, exist_ok=True)

    clear_scene()
    write_palette_png(palette_png)
    material = make_palette_material(palette_png)

    for name, build in MODELS.items():
        clear_scene()
        b = Builder()
        build(b)
        obj = b.to_object(name)
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
