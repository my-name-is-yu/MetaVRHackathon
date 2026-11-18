"""Build the Save Muse! skinned studies from geometry and export one Unity FBX.

Run from the project root:
blender -b --factory-startup --python Tools/MuseRigPrototype/generate_muse.py
"""

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT = Path(__file__).resolve().parents[2]
BASE_OUTPUT = PROJECT / "Assets/Runeveil/Prototype/MusePrototype.fbx"


def xyz(coords):
    # Work in meters with Unity's negative Z facing the viewer.
    x, y, z = coords
    return Vector((x, -z, y))


def smooth(start, end, value):
    t = max(0.0, min(1.0, (value - start) / (end - start)))
    return t * t * (3 - 2 * t)


def select_only(*objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def material(name, shade, roughness=0.84):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*shade, 1)
    result.use_nodes = True
    surface = result.node_tree.nodes.get("Principled BSDF")
    surface.inputs["Base Color"].default_value = (*shade, 1)
    surface.inputs["Roughness"].default_value = roughness
    return result


def oval(name, center, radii, mat=None, turn=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=20)
    obj = bpy.context.object
    obj.name = name
    rx, ry, rz = radii
    scale = Matrix.Diagonal((rx, rz, ry, 1))
    tilt = Matrix.Rotation(math.radians(turn), 4, "Y")
    obj.data.transform(Matrix.Translation(xyz(center)) @ tilt @ scale)
    if mat is not None:
        obj.data.materials.append(mat)
    for face in obj.data.polygons:
        face.use_smooth = True
    return obj


def soft_segment(name, start, end, width, depth, cap):
    a, b = xyz(start), xyz(end)
    axis = b - a
    obj = oval(name, (0, 0, 0), (1, 1, 1))
    rot = axis.to_track_quat("Z", "Y").to_matrix().to_4x4()
    shape = Matrix.Diagonal((width, depth, axis.length * 0.5 + cap, 1))
    obj.data.transform(Matrix.Translation((a + b) * 0.5) @ rot @ shape)
    return obj


def join(parts, name):
    select_only(*parts)
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name
    return obj


def build_rig():
    data = bpy.data.armatures.new("MuseSkeleton")
    rig = bpy.data.objects.new("MuseRig", data)
    bpy.context.collection.objects.link(rig)
    rig.show_in_front = True
    select_only(rig)
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, parent, head, tail):
        segment = data.edit_bones.new(name)
        segment.head = xyz(head)
        segment.tail = xyz(tail)
        if parent is not None:
            segment.parent = data.edit_bones[parent]
            segment.use_connect = (segment.parent.tail - segment.head).length < 0.0001

    bone("Root", None, (0, 0, 0), (0, 0.37, 0))
    bone("Pelvis", "Root", (0, 0.98, 0), (0, 1.32, 0))
    bone("Spine", "Pelvis", (0, 1.32, 0), (0, 1.83, 0))
    bone("Chest", "Spine", (0, 1.83, 0), (0, 2.12, 0))
    bone("Neck", "Chest", (0, 2.12, 0), (0, 2.34, 0))
    bone("Head", "Neck", (0, 2.34, 0), (0, 3.04, 0))
    bone("Core", "Chest", (0, 1.61, -0.68), (0, 1.84, -0.68))
    for suffix, side in ((".L", 1), (".R", -1)):
        shoulder = (side * 0.72, 2.04, 0)
        elbow = (side * 1.17, 1.69, -0.06)
        wrist = (side * 1.37, 1.24, -0.20)
        hip = (side * 0.42, 0.94, 0)
        knee = (side * 0.45, 0.57, -0.11)
        ankle = (side * 0.47, 0.26, -0.01)
        bone("Clavicle" + suffix, "Chest", (side * 0.34, 2.05, 0), shoulder)
        bone("UpperArm" + suffix, "Clavicle" + suffix, shoulder, elbow)
        bone("Forearm" + suffix, "UpperArm" + suffix, elbow, wrist)
        bone("Paw" + suffix, "Forearm" + suffix, wrist, (side * 1.44, 1.03, -0.25))
        bone("Thigh" + suffix, "Pelvis", hip, knee)
        bone("Shin" + suffix, "Thigh" + suffix, knee, ankle)
        bone("Foot" + suffix, "Shin" + suffix, ankle, (side * 0.47, 0.14, -0.34))
        bone("Horn" + suffix, "Head", (side * 0.60, 3.45, -0.31), (side * 0.60, 3.66, -0.31))
        bone("PawGem" + suffix, "Paw" + suffix, (side * 1.40, 1.19, -0.49), (side * 1.40, 1.33, -0.49))
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def skin(obj, rig):
    obj.parent = rig
    deformer = obj.modifiers.new("Muse skeleton", "ARMATURE")
    deformer.object = rig


def rigid(obj, name):
    group = obj.vertex_groups.new(name=name)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    return obj


def body_skin_weights(vert):
    x, depth, y = vert
    ax = abs(x)
    side = ".L" if x >= 0 else ".R"
    # Keep the belly on the torso while allowing a continuous blend at the armpit.
    arm_start = 0.87 - 0.24 * smooth(1.57, 1.92, y)
    arm_amount = smooth(arm_start, 1.035, ax) * (1 - smooth(2.14, 2.36, y)) * smooth(0.60, 0.77, y)
    # At the crotch the center of the body follows the pelvis. Lower down each
    # sole belongs entirely to its own foot so a planted foot stays on the floor.
    inner_hip = smooth(0.45, 0.86, y) * (1 - smooth(0.04, 0.31, ax))
    leg_amount = (1 - arm_amount) * (1 - inner_hip) * (1 - smooth(0.83, 1.21, y))
    center_amount = 1 - arm_amount - leg_amount

    shoulder = Vector((0.72, 0, 2.04))
    wrist = Vector((1.37, 0.20, 1.24))
    reach = wrist - shoulder
    arm_progress = (Vector((ax, depth, y)) - shoulder).dot(reach) / reach.length_squared
    shoulder_blend = smooth(-0.17, 0.20, arm_progress)
    elbow_blend = smooth(0.45, 0.62, arm_progress)
    wrist_blend = smooth(0.83, 1.04, arm_progress)

    knee_blend = smooth(0.44, 0.69, y)
    ankle_blend = smooth(0.17, 0.35, y)
    low_torso = smooth(1.16, 1.54, y)
    chest = smooth(1.63, 2.00, y)
    neck = smooth(2.10, 2.33, y)
    head = smooth(2.23, 2.50, y)
    return (
        ("Clavicle" + side, arm_amount * (1 - shoulder_blend)),
        ("UpperArm" + side, arm_amount * shoulder_blend * (1 - elbow_blend)),
        ("Forearm" + side, arm_amount * elbow_blend * (1 - wrist_blend)),
        ("Paw" + side, arm_amount * wrist_blend),
        ("Thigh" + side, leg_amount * knee_blend),
        ("Shin" + side, leg_amount * (1 - knee_blend) * ankle_blend),
        ("Foot" + side, leg_amount * (1 - ankle_blend)),
        ("Pelvis", center_amount * (1 - low_torso)),
        ("Spine", center_amount * low_torso * (1 - chest)),
        ("Chest", center_amount * chest * (1 - neck)),
        ("Neck", center_amount * neck * (1 - head)),
        ("Head", center_amount * head),
    )


def body_weights(obj):
    groups = {}
    for vert in obj.data.vertices:
        weights = [(name, value) for name, value in body_skin_weights(vert.co) if value >= 0.003]
        weights.sort(key=lambda item: item[1], reverse=True)
        weights = weights[:4]
        total = sum(value for _, value in weights)
        for name, value in weights:
            if name not in groups:
                groups[name] = obj.vertex_groups.new(name=name)
            groups[name].add([vert.index], value / total, "REPLACE")


def build_body(restored, mat):
    if restored:
        parts = [
            oval("Cream body", (0, 1.44, 0.02), (0.83, 0.86, 0.58)),
            oval("Cream hood", (0, 2.85, 0.02), (0.84, 0.78, 0.68)),
            oval("Cream throat", (0, 2.20, 0.04), (0.58, 0.51, 0.47)),
        ]
    else:
        parts = [
            oval("Dark body", (0, 1.49, 0.07), (0.93, 0.92, 0.63)),
            oval("Dark hood", (0, 2.84, 0.04), (0.92, 0.80, 0.70)),
            oval("Dark throat", (0, 2.17, 0.04), (0.67, 0.49, 0.49)),
        ]
    arm_width = 0.245 if restored else 0.27
    foot_width = 0.285 if restored else 0.305
    for side in (-1, 1):
        shoulder = (side * 0.72, 2.04, 0)
        elbow = (side * 1.17, 1.69, -0.06)
        wrist = (side * 1.37, 1.24, -0.20)
        parts.extend([
            oval("Shoulder cushion", (side * 0.70, 1.99, 0.00), (0.34, 0.34, 0.33)),
            soft_segment("Upper arm", shoulder, elbow, arm_width, arm_width, 0.22),
            soft_segment("Forearm", elbow, wrist, arm_width, arm_width, 0.19),
            oval("Mitten", (side * 1.39, 1.16, -0.22), (0.27, 0.34, 0.29)),
            soft_segment("Thigh", (side * 0.42, 1.03, 0), (side * 0.45, 0.57, -0.11), 0.27, 0.32, 0.19),
            soft_segment("Shin", (side * 0.45, 0.62, -0.11), (side * 0.47, 0.24, -0.01), 0.26, 0.29, 0.17),
            oval("Foot", (side * 0.47, 0.195, -0.13), (foot_width, 0.215, 0.38)),
        ])

    obj = join(parts, "White Plush" if restored else "Dark Plush")
    obj.data.remesh_voxel_size = 0.044
    bpy.ops.object.voxel_remesh()
    smooth_mesh = obj.modifiers.new("Smooth joined shapes", "SMOOTH")
    smooth_mesh.factor = 0.64
    smooth_mesh.iterations = 7
    bpy.ops.object.modifier_apply(modifier=smooth_mesh.name)
    for v in obj.data.vertices:
        # Four centimeters of flat sole make the contact easy to read in side view.
        v.co.z = max(0, v.co.z - 0.028 * (1 - smooth(0.00, 0.080, v.co.z)))
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    body_weights(obj)
    return obj


def smile_arc(name, points, radius, mat):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    curve.resolution_u = 8
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for knot, pos in zip(spline.bezier_points, points):
        knot.co = xyz(pos)
        knot.handle_left_type = "AUTO"
        knot.handle_right_type = "AUTO"
    path = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(path)
    path.data.materials.append(mat)
    select_only(path)
    bpy.ops.object.convert(target="MESH")
    return bpy.context.object


def build_faces(body, restored, face, eyes):
    plane = rigid(oval("Face", (0, 2.83, -0.55), (0.68, 0.56, 0.22), face), "Head")
    parts = [body, plane]
    for side in (-1, 1):
        if restored:
            eye = oval("Kind eye", (side * 0.235, 2.89, -0.755), (0.072, 0.092, 0.046), eyes)
        else:
            eye = oval("Dark eye", (side * 0.237, 2.93, -0.755), (0.155, 0.086, 0.042), eyes, turn=-side * 24)
        parts.append(rigid(eye, "Head"))
    if restored:
        mouth = smile_arc("Restored smile", ((-0.125, 2.73, -0.762), (0, 2.66, -0.771), (0.125, 2.73, -0.762)), 0.023, eyes)
    else:
        mouth = oval("Dark open mouth", (0, 2.61, -0.749), (0.172, 0.188, 0.035), eyes)
    parts.append(rigid(mouth, "Head"))
    return join(parts, "MuseWhite" if restored else "MuseDark")


def crystal(name, center, radii, mat, sort):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1)
    obj = bpy.context.object
    obj.name = name
    rx, ry, rz = radii
    obj.data.transform(Matrix.Translation(xyz(center)) @ Matrix.Diagonal((rx, rz, ry, 1)))
    obj.data.materials.append(mat)
    return rigid(obj, sort)


def horn(sign, material_, marker):
    path = (
        ((sign * 0.63, 3.24, -0.08), 0.21),
        ((sign * 0.78, 3.47, -0.13), 0.19),
        ((sign * 0.81, 3.69, -0.15), 0.155),
        ((sign * 0.71, 3.86, -0.16), 0.094),
        ((sign * 0.54, 3.96, -0.18), 0.012),
    )
    corners = 7
    centers = [xyz(p) for p, _ in path]
    verts = []
    for i, (center, (_, radius)) in enumerate(zip(centers, path)):
        tangent = centers[min(i + 1, len(centers) - 1)] - centers[max(0, i - 1)]
        u = tangent.normalized().cross(Vector((0, 1, 0))).normalized()
        v = tangent.normalized().cross(u)
        for j in range(corners):
            angle = 2 * math.pi * (j / corners + (i % 2) * 0.025)
            verts.append(center + radius * (math.cos(angle) * u + math.sin(angle) * v))

    faces = [tuple(reversed(range(corners)))]
    for level in range(len(centers) - 1):
        for j in range(corners):
            a = level * corners + j
            b = level * corners + (j + 1) % corners
            faces.extend([(a, b, b + corners), (a, b + corners, a + corners)])
    faces.append(tuple(range((len(centers) - 1) * corners, len(centers) * corners)))
    mesh = bpy.data.meshes.new("Faceted horn")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(marker, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material_)
    return rigid(obj, marker)


def build_corruption():
    horn_mat = material("MuseHornStone", (0.17, 0.16, 0.17), 0.58)
    gem = material("MuseCrystal", (0.67, 0.65, 0.66), 0.28)
    rim = material("MuseCrystalRim", (0.19, 0.18, 0.19), 0.58)
    parts = [
        rigid(oval("Crystal seat", (0, 1.61, -0.61), (0.32, 0.31, 0.13), rim), "Core"),
        crystal("Chest crystal", (0, 1.61, -0.68), (0.21, 0.245, 0.15), gem, "Core"),
    ]
    for side, suffix in ((1, ".L"), (-1, ".R")):
        parts.extend([
            horn(side, horn_mat, "Horn" + suffix),
            crystal("Horn shard", (side * 0.60, 3.45, -0.31), (0.11, 0.13, 0.10), gem, "Horn" + suffix),
            crystal("Paw crystal", (side * 1.40, 1.19, -0.49), (0.125, 0.14, 0.083), gem, "PawGem" + suffix),
        ])
    return join(parts, "MuseCorruption")


def validate(objects):
    for obj in objects:
        for vertex in obj.data.vertices:
            total = sum(weight.weight for weight in vertex.groups)
            if not math.isclose(total, 1.0, abs_tol=0.0001):
                raise ValueError(f"{obj.name} vertex {vertex.index} has weight total {total}")
        verts = obj.data.vertices
        print(f"MUSE ASSET {obj.name}: {len(verts)} vertices, {len(obj.data.polygons)} faces, "
              f"Y up in FBX from {min(v.co.z for v in verts):.3f} to {max(v.co.z for v in verts):.3f}")


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--output", type=Path, default=BASE_OUTPUT)
    cli.add_argument("--model-only", action="store_true")
    args = cli.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    bpy.context.scene.render.fps = 60

    rig = build_rig()
    ink = material("MuseInk", (0.027, 0.024, 0.025), 0.62)
    plush = material("MuseDarkPlush", (0.265, 0.257, 0.268))
    mask = material("MuseDarkMask", (0.49, 0.475, 0.48))
    ivory = material("MuseIvoryPlush", (0.81, 0.78, 0.71))
    ivory_face = material("MuseIvoryFace", (0.93, 0.89, 0.80))
    dark = build_faces(build_body(False, plush), False, mask, ink)
    white = build_faces(build_body(True, ivory), True, ivory_face, ink)
    attachments = build_corruption()
    objects = (dark, white, attachments)
    for obj in objects:
        skin(obj, rig)
    validate(objects)

    if not args.model_only:
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from muse_idle_reactions import build_idle_and_reactions
        from muse_motion import build_actions

        actions = build_actions(rig) + build_idle_and_reactions(rig)
        print(f"MUSE ACTIONS: {actions}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    select_only(rig, *objects)
    bpy.ops.export_scene.fbx(
        filepath=str(args.output.resolve()),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=False,
        use_armature_deform_only=True,
        add_leaf_bones=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        bake_anim=not args.model_only,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
    )


if __name__ == "__main__":
    main()
