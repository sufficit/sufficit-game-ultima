"""Build the reference Blender scene and export the neutral GLB."""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Quaternion, Vector


def blender_point(point):
    """Ultima (+Y up, +Z forward) to Blender (+Z up, -Y forward)."""
    x, y, z = point
    return Vector((x, -z, y))


def build_armature(joints):
    data = bpy.data.armatures.new("Ultima Humanoid")
    rig = bpy.data.objects.new("Ultima Humanoid Rig", data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    rig.show_in_front = True
    bpy.ops.object.mode_set(mode="EDIT")
    heads = {"root": Vector((0, 0, 0))}
    heads.update((joint["name"], blender_point((joint["x"], joint["y"], joint["z"]))) for joint in joints)
    children = {}
    for joint in joints:
        children.setdefault(joint["parent"], []).append(joint["name"])
    for name, head in heads.items():
        bone = data.edit_bones.new(name)
        bone.head = head
        end = next((heads[child] for child in children.get(name, ())
                    if (heads[child] - head).length > 1e-5), None)
        bone.tail = end if end is not None else head + Vector((0, 0, 0.025))
    for joint in joints:
        data.edit_bones[joint["name"]].parent = data.edit_bones[joint["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    for joint in joints:
        bone = data.bones[joint["name"]]
        bone["motion"] = joint["motion"]
        if flexion := joint.get("flexion"):
            bone["flexion"] = flexion
    return rig


class Shell:
    def __init__(self):
        self.vertices, self.faces, self.weights, self.materials = [], [], [], []

    def vertex(self, x, y, z, weights):
        index = len(self.vertices)
        self.vertices.append(blender_point((x, y, z)))
        self.weights.append(weights)
        return index

    def ring(self, cx, y, cz, rx, rz, angles, weights):
        return [
            self.vertex(cx + rx * math.sin(angle), y, cz + rz * math.cos(angle),
                        weights(angle) if callable(weights) else weights)
            for angle in angles
        ]

    def bridge(self, a, b, material, openings=()):
        if len(a) != len(b):
            raise ValueError("Ring sizes differ")
        for i in range(len(a)):
            if i in openings:
                continue
            n = (i + 1) % len(a)
            self.face((a[i], a[n], b[n], b[i]), material)

    def face(self, indices, material):
        self.faces.append(indices)
        self.materials.append(material)

    def make_mesh(self, rig):
        mesh = bpy.data.meshes.new("Reference Surface")
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new("Reference Humanoid", mesh)
        bpy.context.collection.objects.link(obj)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        assert all(edge.is_manifold for edge in bm.edges), "The reference surface must have closed edges"
        pending = [next(iter(bm.verts))]
        connected = set(pending)
        while pending:
            vertex = pending.pop()
            for edge in vertex.link_edges:
                neighbor = edge.other_vert(vertex)
                if neighbor not in connected:
                    connected.add(neighbor)
                    pending.append(neighbor)
        assert len(connected) == len(bm.verts), "The sleeves and body must form one connected surface"
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        for name, color in [
            ("Slate trousers", (0.105, 0.19, 0.25)),
            ("Teal tunic", (0.05, 0.38, 0.36)),
            ("Skin", (0.62, 0.34, 0.19)),
            ("Graphite boots", (0.065, 0.085, 0.105)),
        ]:
            material = bpy.data.materials.new(name)
            material.diffuse_color = (*color, 1)
            material.use_nodes = True
            shader = material.node_tree.nodes.get("Principled BSDF")
            shader.inputs["Base Color"].default_value = (*color, 1)
            shader.inputs["Roughness"].default_value = 0.78
            mesh.materials.append(material)
        for polygon, material_index in zip(mesh.polygons, self.materials, strict=True):
            polygon.material_index = material_index

        groups = {}
        for index, weights in enumerate(self.weights):
            for name, weight in weights.items():
                if weight <= 0:
                    continue
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)
                groups[name].add([index], weight, "REPLACE")
        obj.parent = rig
        modifier = obj.modifiers.new("Ultima Skin", "ARMATURE")
        modifier.object = rig
        return obj


def pair(a, b, t):
    return {a: 1 - t, b: t}


def torso_and_legs(shell, joints):
    hip_y = joints["pelvis"]["y"]
    angles = [math.tau * i / 16 for i in range(16)]
    pelvis = shell.ring(0, hip_y - .025, 0, .164, .101, angles, {"pelvis": 1})
    last = pelvis
    shoulders = {}
    shoulder_y = joints["shoulder-right"]["y"]
    sleeve_top = shoulder_y + .005
    for y, rx, rz, front, material, weights in [
        (hip_y + .065, .165, .105, 0, 0, pair("pelvis", "upper-body", .2)),
        (.925, .132, .084, .006, 1, pair("pelvis", "upper-body", .65)),
        (1.105, .143, .096, .005, 1, pair("upper-body", "chest", .5)),
        (shoulder_y - .120, .174, .103, 0, 1, {"chest": 1}),
        (sleeve_top, .190, .081, 0, 1, {"chest": 1}),
        (1.45, .082, .052, 0, 1, pair("chest", "neck-pivot", .3)),
        (1.49, .047, .045, 0, 2, pair("neck-pivot", "head-pivot", .3)),
        (1.515, .069, .065, .018, 2, {"head-pivot": 1}),
        (1.58, .099, .083, .016, 2, {"head-pivot": 1}),
        (1.66, .099, .081, .008, 2, {"head-pivot": 1}),
        (1.72, .070, .060, 0, 2, {"head-pivot": 1}),
        (1.75, .024, .030, 0, 2, {"head-pivot": 1}),
    ]:
        ring = shell.ring(0, y, front, rx, rz, angles, weights)
        openings = ()
        if y == sleeve_top:
            for side, center in (("right", 4), ("left", 12)):
                start = center - 2
                end = center + 3
                lower, upper = last[start:end], ring[start:end]
                shoulders[side] = lower + upper[::-1]
                for vertex in lower + upper:
                    shell.weights[vertex] = pair("chest", "shoulder-" + side, .25)
            openings = (*range(2, 6), *range(10, 14))
        shell.bridge(last, ring, material, openings)
        last = ring
    shell.face(last, 2)

    # The medial seam belongs to both leg loops, giving the pelvis and legs one surface.
    # The rim endpoints are reused, so there is no gap at the crotch.
    seam = [
        shell.vertex(0, hip_y - .025 - .065 * math.sin(math.pi * i / 4),
                     -.101 + .202 * i / 4, {"pelvis": 1})
        for i in range(1, 4)
    ]
    for side, sign, rim in [
        ("right", 1, pelvis[:9] + seam),
        ("left", -1, pelvis[8:] + pelvis[:1] + seam[::-1]),
    ]:
        leg(shell, joints, side, sign, rim)
    return shoulders


def leg(shell, joints, side, sign, last):
    hip, knee, ankle, ball = (f"{name}-{side}" for name in ("hip", "knee", "ankle", "ball"))
    x = joints[hip]["x"]
    ky, ay = joints[knee]["y"], joints[ankle]["y"]
    angles = [math.pi * i / 8 for i in range(9)] + [math.pi + math.pi * i / 4 for i in range(1, 4)]
    if sign < 0:
        angles = [angle + math.pi for angle in angles]

    def foot_weight(angle):
        return pair(ankle, ball, max(0, math.cos(angle)) * .85)

    for y, z, rx, rz, material, weights in [
        (.643, 0, .076, .076, 0, pair("pelvis", hip, .82)),
        (ky + .105, 0, .063, .066, 0, {hip: 1}),
        (ky + .046, 0, .052, .055, 0, pair(hip, knee, .22)),
        (ky, .006, .051, .054, 0, pair(hip, knee, .5)),
        (ky - .046, -.004, .050, .054, 0, pair(hip, knee, .78)),
        (ky - .110, -.010, .052, .053, 0, {knee: 1}),
        (ay + .131, -.003, .043, .045, 0, {knee: 1}),
        (ay + .091, 0, .038, .043, 3, pair(knee, ankle, .18)),
        (ay + .045, .005, .039, .045, 3, pair(knee, ankle, .52)),
        (ay, .040, .043, .080, 3, foot_weight),
        (.019, .064, .051, .114, 3, foot_weight),
        (0, .063, .052, .114, 3, foot_weight),
    ]:
        ring = shell.ring(x, y, z, rx, rz, angles, weights)
        shell.bridge(last, ring, material)
        last = ring
    shell.face(last, 3)


def align_ring(shell, anchor, ring):
    """Choose the closest cyclic correspondence for the sleeve opening."""
    candidates = [seq[i:] + seq[:i] for seq in (ring, ring[::-1]) for i in range(len(ring))]
    return min(candidates, key=lambda indices: sum(
        (shell.vertices[a] - shell.vertices[b]).length_squared
        for a, b in zip(anchor, indices, strict=True)))


def arms(shell, joints, shoulders):
    angles = [math.tau * i / 10 for i in range(10)]
    for side in ("left", "right"):
        shoulder, elbow, wrist = (f"{name}-{side}" for name in ("shoulder", "elbow", "wrist"))
        sy, ey, wy = (joints[joint]["y"] for joint in (shoulder, elbow, wrist))
        x = joints[shoulder]["x"]
        last = shoulders[side]
        for y, rx, rz, material, weights in [
            (sy - .137, .052, .050, 1, {shoulder: 1}),
            (sy - .180, .044, .046, 1, {shoulder: 1}),
            (ey + .045, .037, .039, 2, pair(shoulder, elbow, .2)),
            (ey, .036, .037, 2, pair(shoulder, elbow, .5)),
            (ey - .045, .037, .039, 2, pair(shoulder, elbow, .8)),
            (wy + .035, .027, .030, 2, pair(elbow, wrist, .2)),
            (wy, .026, .027, 2, pair(elbow, wrist, .6)),
            (wy - .055, .034, .024, 2, {wrist: 1}),
            (wy - .090, .021, .017, 2, {wrist: 1}),
        ]:
            ring = shell.ring(x, y, 0, rx, rz, angles, weights)
            ring = align_ring(shell, last, ring)
            shell.bridge(last, ring, material)
            last = ring
        shell.face(last, 2)


def verify_skin(mesh, rig, joints, shell):
    expected = {"root", *joints}
    assert {bone.name for bone in rig.data.bones} == expected
    assert all(abs(sum(weights.values()) - 1) < 1e-6 for weights in shell.weights)
    assert all(1 <= len(vertex.groups) <= 4 for vertex in mesh.data.vertices)
    assert all(abs(sum(group.weight for group in vertex.groups) - 1) < 1e-6
               for vertex in mesh.data.vertices)
    scene = bpy.context.scene
    scene.frame_set(1)
    graph = bpy.context.evaluated_depsgraph_get()
    rest = [v.co.copy() for v in mesh.evaluated_get(graph).data.vertices]
    for name, angle in (("hip-right", .4), ("knee-right", .9), ("ankle-right", .28),
                        ("shoulder-right", .55), ("shoulder-left", .55)):
        bone = rig.pose.bones[name]
        flex = joints[name]["flexion"]
        axis = blender_point((flex["axisX"], flex["axisY"], flex["axisZ"]))
        axis = bone.bone.matrix_local.to_3x3().inverted() @ axis
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = Quaternion(axis, angle)
        bpy.context.view_layer.update()
        moved = [v.co.copy() for v in mesh.evaluated_get(graph).data.vertices]
        blended = [index for index, weights in enumerate(shell.weights)
                   if 0 < weights.get(name, 0) < 1]
        changed = [index for index in blended if (moved[index] - rest[index]).length > .001]
        assert changed, f"{name} did not move any blended vertices"
        assert all((moved[index] - rest[index]).length < 1e-6
                   for index, weights in enumerate(shell.weights) if weights.get("pelvis") == 1)
        bone.rotation_quaternion = Quaternion()
    bpy.context.view_layer.update()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("anatomy", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args(argv)
    joints = json.loads(args.anatomy.read_text())
    by_name = {joint["name"]: joint for joint in joints}
    assert len(joints) == len(by_name) == 73
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    rig = build_armature(joints)
    shell = Shell()
    shoulders = torso_and_legs(shell, by_name)
    arms(shell, by_name, shoulders)
    mesh = shell.make_mesh(rig)
    verify_skin(mesh, rig, by_name, shell)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output.resolve().with_suffix(".blend")), compress=True)
    bpy.ops.export_scene.gltf(filepath=str(args.output.resolve().with_suffix(".glb")),
        export_format="GLB", export_skins=True, export_animations=False, export_armature_object_remove=True, export_yup=True, export_extras=True)
    print(f"Verified {len(rig.data.bones)} bones and {len(mesh.data.vertices)} weighted vertices.")


if __name__ == "__main__":
    main()
