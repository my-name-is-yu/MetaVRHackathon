"""Build the Save Muse! skinned game model and export one Unity FBX.

Run from the project root:
blender -b --factory-startup --python Tools/MuseRigPrototype/generate_muse.py
"""

import argparse
import math
import sys
from functools import partial
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT = Path(__file__).resolve().parents[2]
BASE_OUTPUT = PROJECT / "Assets/Runeveil/Resources/Runeveil/Muse.fbx"
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from muse_artwork import artwork, dark_body, dark_horn, dark_side, white_side, white_surface


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


def oval(name, center, radii, mat=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=20)
    obj = bpy.context.object
    obj.name = name
    rx, ry, rz = radii
    scale = Matrix.Diagonal((rx, rz, ry, 1))
    obj.data.transform(Matrix.Translation(xyz(center)) @ scale)
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


def body_skin_weights(vert, sleeve):
    x, depth, y = vert
    ax = abs(x)
    side = ".L" if x >= 0 else ".R"
    arm_amount = float(sleeve)
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


def body_weights(obj, sleeve):
    groups = {}
    for vert in obj.data.vertices:
        weights = [(name, value) for name, value in body_skin_weights(vert.co, sleeve) if value >= 0.003]
        weights.sort(key=lambda item: item[1], reverse=True)
        weights = weights[:4]
        total = sum(value for _, value in weights)
        for name, value in weights:
            if name not in groups:
                groups[name] = obj.vertex_groups.new(name=name)
            groups[name].add([vert.index], value / total, "REPLACE")


def sculpt_face(vertex, restored):
    """Shape the face and the hood into the same surface."""
    x, front, height = vertex
    forehead = 2.87 if restored else 2.82
    face_radius = math.hypot(x / 0.67, (height - forehead) / 0.54)
    facing = smooth(0.15, 0.40, front)
    if facing == 0 or face_radius >= 1.40:
        return

    # A shallow face with a small fur roll around it reads in profile without
    # introducing the separate face plate from the previous mesh.
    cheek = 0.044 * (1 - smooth(0.15, 0.96, face_radius))
    rim = 0.023 * smooth(0.79, 0.96, face_radius) * (1 - smooth(0.96, 1.28, face_radius))
    vertex.y += facing * (cheek + rim)
    if not restored:
        # The original swept forelock is a soft ridge at the forehead, sculpted
        # into the hood so the edge and face remain one skinned volume.
        ridge = math.exp(-((x + 0.02) / 0.28) ** 2 - ((height - 3.34 - x * 0.37) / 0.15) ** 2)
        vertex.y += facing * ridge * 0.047


def shape_armpit(belly, sleeve_parts, side):
    # Leave a clearance where the closed sleeve passes the flank at rest.
    for obj in sleeve_parts:
        for vert in obj.data.vertices:
            x, _, height = vert.co
            gap = smooth(1.15, 1.30, height) * (1 - smooth(1.63, 1.88, height))
            edge = 1.17 - 0.30 * smooth(1.61, 1.95, height)
            vert.co.x += side * max(0, edge - side * x) * gap
    for vert in belly.data.vertices:
        x, _, height = vert.co
        if x * side <= 0:
            continue
        gap = smooth(1.10, 1.30, height) * (1 - smooth(1.57, 1.80, height))
        vert.co.x -= side * 0.045 * smooth(0.88, 1.05, abs(x)) * gap


def build_shell(parts, name, mat, restored, sleeve=False):
    obj = join(parts, name)
    obj.data.remesh_voxel_size = 0.049
    bpy.ops.object.voxel_remesh()
    smooth_mesh = obj.modifiers.new("Smooth joined shapes", "SMOOTH")
    smooth_mesh.factor = 0.64
    smooth_mesh.iterations = 6
    bpy.ops.object.modifier_apply(modifier=smooth_mesh.name)
    if not sleeve:
        for v in obj.data.vertices:
            # Four centimeters of flat sole make the contact easy to read in side view.
            v.co.z = max(0, v.co.z - 0.028 * (1 - smooth(0.00, 0.080, v.co.z)))
            sculpt_face(v.co, restored)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    body_weights(obj, sleeve)
    return obj


def build_form(restored, mat):
    # Both forms follow the same pear-shaped silhouette, with the hood seated
    # into the broad shoulder cushion and the legs tucked up into the belly.
    width = 1.017 if restored else 1.052
    hood_width = 0.88 if restored else 0.895
    belly = oval("Rounded belly", (0, 1.32, 0.123), (width, 1.035, 0.761))
    parts = [
        belly,
        oval("Hood", (0, 2.79, 0.080), (hood_width, 0.815, 0.688)),
        oval("Shoulder cushion", (0, 2.12, 0.053), (0.795, 0.63, 0.625)),
    ]
    sleeves = []
    arm_width = 0.275 if restored else 0.29
    for side in (-1, 1):
        shoulder = (side * 0.72, 2.04, 0)
        elbow = (side * 1.17, 1.69, -0.06)
        wrist = (side * 1.37, 1.24, -0.20)
        sleeve = [
            oval("Sleeve root", (side * 0.59, 2.035, 0.015), (0.375, 0.30, 0.302)),
            soft_segment("Upper arm", shoulder, elbow, arm_width, arm_width, 0.24),
            soft_segment("Forearm", elbow, wrist, arm_width + 0.008, arm_width + 0.008, 0.21),
            oval("Rounded mitten", (side * 1.39, 1.16, -0.22), (0.295, 0.349, 0.313)),
        ]
        shape_armpit(belly, sleeve, side)
        sleeves.append(build_shell(sleeve, "Closed sleeve", mat, restored, sleeve=True))
        parts.extend([
            oval("Shoulder lining", (side * 0.59, 2.035, 0.027), (0.277, 0.295, 0.290)),
            soft_segment("Thigh", (side * 0.43, 1.02, 0), (side * 0.45, 0.52, -0.08), 0.30, 0.33, 0.23),
            soft_segment("Shin", (side * 0.45, 0.62, -0.08), (side * 0.47, 0.23, -0.01), 0.283, 0.315, 0.21),
            oval("Plush foot", (side * 0.47, 0.193, -0.10), (0.323, 0.216, 0.355)),
        ])

    surface = white_surface if restored else dark_body
    side_surface = white_side if restored else dark_side
    body = build_shell(parts, "Continuous torso", mat, restored)
    artwork(body, surface, side_blend=0.31, side_project=side_surface)
    for sleeve in sleeves:
        artwork(sleeve, partial(surface, sleeve=True), side_blend=0.31, side_project=side_surface)

    # Pack the three closed parts into one skinned renderer. They overlap at the
    # plush seam, with no vertices connecting the sleeve to the moving flank.
    return join((body, *sleeves), "MuseWhite" if restored else "MuseDark")


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
        ((sign * 0.62, 3.22, -0.095), 0.249),
        ((sign * 0.765, 3.44, -0.125), 0.249),
        ((sign * 0.80, 3.65, -0.135), 0.215),
        ((sign * 0.715, 3.84, -0.16), 0.168),
        ((sign * 0.58, 3.94, -0.17), 0.10),
        ((sign * 0.40, 3.94, -0.18), 0.022),
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
            offset = radius * (math.cos(angle) * u + math.sin(angle) * v)
            offset.y *= 0.85
            verts.append(center + offset)

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
    artwork(obj, dark_horn, depth_center=0.15, side_blend=0.155)
    return rigid(obj, marker)


def build_corruption(horn_mat):
    gem = material("MuseCrystal", (0.95, 0.095, 0.38), 0.28)
    rim = material("MuseCrystalRim", (0.25, 0.052, 0.13), 0.58)
    sides = ((1, ".L"), (-1, ".R"))
    horns = [horn(side, horn_mat, "Horn" + suffix) for side, suffix in sides]
    parts = [
        rigid(oval("Crystal seat", (0, 1.61, -0.599), (0.221, 0.262, 0.078), rim), "Core"),
        crystal("Chest crystal", (0, 1.61, -0.68), (0.146, 0.206, 0.095), gem, "Core"),
    ]
    for side, suffix in sides:
        parts.extend([
            crystal("Horn shard", (side * 0.60, 3.45, -0.31), (0.09, 0.114, 0.078), gem, "Horn" + suffix),
            crystal("Paw crystal", (side * 1.40, 1.19, -0.49), (0.106, 0.127, 0.088), gem, "PawGem" + suffix),
        ])
    for part in parts:
        while part.data.uv_layers:
            part.data.uv_layers.remove(part.data.uv_layers[0])
        # ArenaSurface multiplies the imported vertex color by its material.
        # The ornament and crystal facets are unpainted, so retain that material tint.
        colors = part.data.color_attributes.new(name="Facing", type="FLOAT_COLOR", domain="CORNER")
        for color in colors.data:
            color.color = (1.0, 1.0, 1.0, 1.0)
    return join(horns + parts, "MuseCorruption")


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
    plush = material("MuseDarkArtwork", (0.265, 0.257, 0.268))
    ivory = material("MuseWhiteArtwork", (0.81, 0.78, 0.71))
    dark = build_form(False, plush)
    white = build_form(True, ivory)
    attachments = build_corruption(plush)
    objects = (dark, white, attachments)
    for obj in objects:
        skin(obj, rig)
    validate(objects)

    if not args.model_only:
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
