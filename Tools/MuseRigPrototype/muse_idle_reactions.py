"""Author a planted idle and short, bone-local impact reactions."""

import bpy

from muse_motion import (
    FPS,
    IDENTITY,
    PAW_L_HOME,
    PAW_R_HOME,
    PELVIS_HOME,
    Motion,
    Pose,
    build_actions,
    key,
    rotation,
    sample,
)


IDLE = Motion(
    "MuseIdle",
    120,
    hips=(
        key(0, *PELVIS_HOME), key(22, -.044, -.033, .019),
        key(49, -.031, -.017, -.015), key(76, .042, -.030, .012),
        key(100, .028, -.010, -.016), key(120, *PELVIS_HOME),
    ),
    hip_rotation=(
        key(0, 0, 0, 0), key(24, -.7, 1.3, 1.5),
        key(53, .8, .4, .7), key(82, -.7, -1.1, -1.5),
        key(103, .5, -.7, -.6), key(120, 0, 0, 0),
    ),
    spine=(
        key(0, 0, 0, 0), key(27, -1.6, -.6, 1),
        key(55, 1.1, -.1, -.3), key(85, -1.3, .6, -1.1),
        key(103, .8, .2, -.3), key(120, 0, 0, 0),
    ),
    chest=(
        key(0, 0, 0, 0), key(32, 2.8, -1.3, -.9),
        key(58, -1.8, -.4, .5), key(88, 2.1, 1.2, .7),
        key(108, -.9, .3, .2), key(120, 0, 0, 0),
    ),
    neck=(
        key(0, 0, 0, 0), key(34, -1.1, 1, -.5),
        key(60, 1.2, .4, .5), key(91, -1, -1, .6), key(120, 0, 0, 0),
    ),
    head=(
        key(0, 0, 0, 0), key(19, -.8, -2.8, -.9),
        key(39, .4, -3.7, .4), key(65, 1.4, -.6, .7),
        key(82, -.7, 2.5, .8), key(105, .5, 1.2, -.6), key(120, 0, 0, 0),
    ),
    paw_left=(
        key(0, *PAW_L_HOME), key(29, 1.337, 1.217, -.213),
        key(58, 1.351, 1.252, -.235), key(87, 1.389, 1.239, -.205),
        key(105, 1.380, 1.258, -.222), key(120, *PAW_L_HOME),
    ),
    paw_right=(
        key(0, *PAW_R_HOME), key(36, -1.393, 1.238, -.228),
        key(62, -1.366, 1.253, -.211), key(90, -1.337, 1.216, -.218),
        key(106, -1.363, 1.254, -.226), key(120, *PAW_R_HOME),
    ),
    paw_left_rotation=(key(0, 0, 0, 0), key(43, 2, -3, 2), key(91, -1, 2, -1), key(120, 0, 0, 0)),
    paw_right_rotation=(key(0, 0, 0, 0), key(46, -1, 1, 1), key(94, 2, -2, -2), key(120, 0, 0, 0)),
)


HIT_END = 9
REST = (0, 0, 0)


def impact(*poses):
    return (key(0, *REST), *poses, key(HIT_END, *REST))


BODY = {
    "Spine": impact(key(2, 9, -3, 6), key(4, 6, -2, 4), key(7, -.8, .4, -.6)),
    "Chest": impact(key(2, 4, 1, -1), key(4, 5, 3, -2), key(7, 1, 0, 0)),
    "Head": impact(key(2, -3, 1, -2), key(4, 4, 2, -3), key(7, 1, 0, -1)),
}

CORE = {
    "Spine": impact(key(2, 6, 0, 0), key(4, 4, 0, 0), key(7, -1, 0, 0)),
    "Chest": impact(key(2, 13, 0, 0), key(4, 8, 0, 0), key(7, -2, 0, 0)),
    "Neck": impact(key(2, -6, 0, 0), key(4, -2, 0, 0), key(7, 2, 0, 0)),
    "Head": impact(key(2, -6, 0, 0), key(4, 2, 0, 0), key(7, 2, 0, 0)),
}


def horn_recoil(side, sign):
    # Carry the deflection through the head with a smaller delayed neck response.
    return {
        "Neck": impact(key(2, 2, sign * 5, sign * 4), key(4, 1, sign * 4, sign * 5)),
        "Head": impact(
            key(2, 4, sign * 15, sign * 14),
            key(4, 4, sign * 10, sign * 9),
            key(7, 0, -sign * 2, -sign),
        ),
        f"Horn.{side}": impact(key(2, 1, 0, sign * 4), key(4, 0, 0, -sign * 2)),
    }


def paw_recoil(side, sign):
    # Keep the opposite arm still. The struck elbow folds before the mitten settles.
    return {
        f"UpperArm.{side}": impact(key(2, 8, -sign * 12, -sign * 5), key(4, 5, -sign * 7, -sign * 2)),
        f"Forearm.{side}": impact(
            key(2, -18, -sign * 9, sign * 19),
            key(4, -12, -sign * 6, sign * 12),
            key(7, 2, 0, -sign * 2),
        ),
        f"Paw.{side}": impact(key(2, 11, sign * 7, sign * 6), key(4, 13, sign * 5, sign * 8)),
    }


REACTIONS = {
    "MuseHitBody": BODY,
    "MuseHitCore": CORE,
    "MuseHitHornL": horn_recoil("L", 1),
    "MuseHitHornR": horn_recoil("R", -1),
    "MuseHitPawL": paw_recoil("L", 1),
    "MuseHitPawR": paw_recoil("R", -1),
}


def build_reaction_actions(armature):
    """Bake rotation-only deltas so hits can be layered onto any base action."""
    pose = Pose(armature.pose.bones)
    armature.animation_data_create()
    bpy.context.scene.render.fps = FPS
    actions = []
    for name, tracks in REACTIONS.items():
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        armature.animation_data.action = action
        for bone_name, track in tracks.items():
            bone = armature.pose.bones[bone_name]
            bone.rotation_mode = "QUATERNION"
            for frame in range(HIT_END + 1):
                pose.bind_axes(bone_name, rotation(*sample(track, frame)))
                bone.rotation_quaternion = pose.rotations[bone_name]
                bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone_name)
            bone.rotation_quaternion = IDENTITY
        actions.append(action)
    armature.animation_data.action = None
    bpy.context.view_layer.update()
    return actions


def build_idle_and_reactions(armature):
    return build_actions(armature, motions=(IDLE,)) + build_reaction_actions(armature)
