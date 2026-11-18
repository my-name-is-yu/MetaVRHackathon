"""Author Muse's prototype animation actions and bake the planted limbs in Blender."""

from bisect import bisect_right
from dataclasses import dataclass
from math import radians, sqrt

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector


FPS = 30
IDENTITY = Quaternion((1.0, 0.0, 0.0, 0.0))
UNIT_SCALE = Vector((1.0, 1.0, 1.0))
ZERO = Vector((0.0, 0.0, 0.0))
UNITY_TO_BLENDER = Quaternion((1.0, 0.0, 0.0), radians(90))


def position(x, y, z):
    """Map the game's authoring axes to the Blender scene axes."""
    return Vector((x, -z, y))


def rotation(x, y, z):
    authored = Euler(tuple(map(radians, (x, y, z))), "XYZ").to_quaternion()
    return UNITY_TO_BLENDER @ authored @ UNITY_TO_BLENDER.inverted()


def key(frame, x, y, z):
    return (frame, (x, y, z))


def sample(keys, frame):
    index = bisect_right(keys, frame, key=lambda entry: entry[0]) - 1
    if index < 0:
        return keys[0][1]
    frame_start, start = keys[index]
    if index == len(keys) - 1:
        return start
    frame_end, end = keys[index + 1]
    t = (frame - frame_start) / (frame_end - frame_start)
    eased = t * t * (3.0 - 2.0 * t)
    return tuple(a + (b - a) * eased for a, b in zip(start, end))


# Positions and Euler rotations are expressed in Unity authoring axes.
# Separate timing for the chest, head and wrists gives each action a clear lead and settle.
PELVIS_HOME = (0.0, 0.0, 0.0)
PAW_L_HOME = (1.37, 1.24, -0.20)
PAW_R_HOME = (-1.37, 1.24, -0.20)


@dataclass(frozen=True)
class Motion:
    name: str
    end: int
    hips: tuple
    hip_rotation: tuple
    spine: tuple
    chest: tuple
    neck: tuple
    head: tuple
    paw_left: tuple
    paw_right: tuple
    paw_left_rotation: tuple
    paw_right_rotation: tuple


# A compact guard keeps the short strike distinct from the heavy's rearward load.
# The raised paw holds through the release window, then travels in a shallow diagonal.
NORMAL = Motion(
    "MuseNormal",
    86,
    hips=(
        key(0, *PELVIS_HOME), key(14, .045, -.042, .027), key(24, .063, -.027, .038),
        key(50, .049, -.038, .031), key(72, .061, -.041, .039),
        key(74, .015, -.066, -.045), key(76, -.031, -.087, -.121),
        key(79, -.047, -.079, -.092), key(86, *PELVIS_HOME),
    ),
    hip_rotation=(
        key(0, 0, 0, 0), key(18, 1, -6, -3), key(72, 2, -7, -3),
        key(75, -4, 9, 2), key(79, -5, 11, 3), key(86, 0, 0, 0),
    ),
    spine=(
        key(0, 0, 0, 0), key(20, 3, -5, -3), key(72, 4, -6, -3),
        key(75, -7, 7, 2), key(79, -8, 12, 4), key(86, 0, 0, 0),
    ),
    chest=(
        key(0, 0, 0, 0), key(21, 4, -6, -2), key(72, 4, -7, -1),
        key(75, -5, 6, 1), key(79, -8, 13, 3), key(86, 0, 0, 0),
    ),
    neck=(
        key(0, 0, 0, 0), key(23, -4, 7, 0), key(72, -5, 8, 0),
        key(76, 0, -5, -2), key(81, 8, -9, -1), key(86, 0, 0, 0),
    ),
    head=(
        key(0, 0, 0, 0), key(24, -2, 3, 1), key(72, -3, 4, 2),
        key(76, 1, -4, 0), key(81, 6, -6, -1), key(86, 0, 0, 0),
    ),
    paw_left=(
        key(0, *PAW_L_HOME), key(9, 1.30, 1.73, -.10), key(20, 1.32, 2.35, -.17),
        key(37, 1.29, 2.38, -.19), key(59, 1.33, 2.32, -.16),
        key(72, 1.31, 2.37, -.16), key(74, 1.26, 2.17, -.58),
        key(76, 1.07, 1.80, -1.02), key(78, .93, 1.59, -1.04),
        key(81, 1.16, 1.33, -.64), key(86, *PAW_L_HOME),
    ),
    paw_right=(
        key(0, *PAW_R_HOME), key(18, -1.26, 1.35, -.36),
        key(72, -1.26, 1.38, -.32), key(77, -1.26, 1.37, -.13),
        key(86, *PAW_R_HOME),
    ),
    paw_left_rotation=(
        key(0, 0, 0, 0), key(20, 52, 2, -12), key(72, 54, 3, -12),
        key(76, 65, -17, -13), key(80, 43, -13, -7), key(86, 0, 0, 0),
    ),
    paw_right_rotation=(
        key(0, 0, 0, 0), key(18, -4, 6, -8), key(72, -5, 8, -7),
        key(79, 9, -6, 4), key(86, 0, 0, 0),
    ),
)


# Hold the charge at frame 72 (2.4s) until the game resolves the attack.
# The shoulder leads the paw between frames 73 and 77.
HEAVY = Motion(
    "MuseHeavy",
    96,
    hips=(
        key(0, *PELVIS_HOME), key(11, .183, -.185, .16), key(22, .132, -.15, .125),
        key(34, .074, -.046, .067), key(46, .094, -.074, .082),
        key(65, .085, -.061, .081), key(72, .122, -.085, .10),
        key(74, .039, -.137, .018), key(78, -.106, -.212, -.179),
        key(82, -.134, -.210, -.149), key(88, -.091, -.126, -.110),
        key(96, *PELVIS_HOME),
    ),
    hip_rotation=(
        key(0, 0, 0, 0), key(11, 3, -14, -9), key(32, 4, -10, -5),
        key(65, 4, -9, -5), key(72, 4, -13, -7), key(75, -10, 12, 5),
        key(78, -15, 20, 11), key(86, -10, 10, 6), key(96, 0, 0, 0),
    ),
    spine=(
        key(0, 0, 0, 0), key(10, 8, -10, -7), key(29, 6, -6, -6),
        key(66, 6, -6, -6), key(72, 7, -8, -7), key(75, -5, 8, 2),
        key(78, -17, 16, 7), key(85, -11, 8, 3), key(96, 0, 0, 0),
    ),
    chest=(
        key(0, 0, 0, 0), key(14, 8, -14, -5), key(33, 7, -8, 2),
        key(69, 6, -8, 2), key(72, 5, -7, 2), key(76, -10, 9, 4),
        key(80, -16, 17, 5), key(87, -11, 9, 2), key(96, 0, 0, 0),
    ),
    neck=(
        key(0, 0, 0, 0), key(16, -7, 9, 3), key(38, -6, 4, 1),
        key(72, -8, 7, 2), key(77, 3, -7, -5), key(85, 18, -9, -5),
        key(96, 0, 0, 0),
    ),
    head=(
        key(0, 0, 0, 0), key(17, -7, 8, 4), key(39, -4, 5, 5),
        key(72, -4, 6, 5), key(77, 1, -5, -2), key(83, 13, -10, -5),
        key(89, 8, -5, -2), key(96, 0, 0, 0),
    ),
    paw_left=(
        key(0, *PAW_L_HOME), key(13, 1.20, 1.38, .43), key(22, 1.26, 2.12, .43),
        key(34, 1.23, 2.73, .045), key(47, 1.24, 2.70, .020),
        key(67, 1.23, 2.74, .045), key(72, 1.23, 2.73, .04),
        key(74, 1.30, 2.70, -.45), key(77, 1.00, 1.80, -1.04),
        key(80, .89, 1.37, -1.09), key(86, 1.07, 1.23, -.76),
        key(96, *PAW_L_HOME),
    ),
    paw_right=(
        key(0, *PAW_R_HOME), key(8, -1.15, 1.48, -.20),
        key(15, -1.02, 1.54, -.35), key(33, -1.04, 1.38, -.44),
        key(72, -1.04, 1.39, -.40),
        key(78, -1.24, 1.48, .19), key(86, -1.32, 1.30, .04),
        key(96, *PAW_R_HOME),
    ),
    paw_left_rotation=(
        key(0, 0, 0, 0), key(17, 13, -12, 18), key(32, 73, 0, -8),
        key(72, 75, 0, -8), key(77, 68, -25, -20),
        key(82, 50, -25, -12), key(96, 0, 0, 0),
    ),
    paw_right_rotation=(
        key(0, 0, 0, 0), key(15, -9, 0, -9), key(33, -5, 10, -7),
        key(72, -7, 12, -7), key(81, 21, -16, 7), key(96, 0, 0, 0),
    ),
)


# Keep the hips between the soles as the torso reaches toward the bracing paw.
# The paw's world position and rotation stay fixed through the three-second hold.
LIGHTNING = Motion(
    "MuseLightningDown",
    107,
    hips=(
        key(0, 0, -.016, .01), key(3, .025, -.035, .066), key(6, -.075, -.160, .043),
        key(12, -.230, -.400, .020), key(17, -.280, -.450, -.010),
        key(30, -.270, -.440, .000), key(55, -.271, -.447, -.010),
        key(72, -.275, -.436, .004), key(87, -.278, -.440, -.002),
        key(90, -.275, -.423, -.001), key(97, -.166, -.244, .043),
        key(107, *PELVIS_HOME),
    ),
    hip_rotation=(
        key(0, 0, 0, 0), key(3, 9, -7, -5), key(7, -5, 3, 8),
        key(16, -11, 9, 11), key(32, -10, 10, 12),
        key(90, -10, 10, 12), key(101, -4, 4, 6), key(107, 0, 0, 0),
    ),
    spine=(
        key(0, 0, 0, 0), key(4, 10, -6, -6), key(10, -16, -1, 9),
        key(17, -19, -6, 15), key(30, -16, -7, 13), key(89, -16, -7, 13),
        key(100, -9, -3, 7), key(107, 0, 0, 0),
    ),
    chest=(
        key(0, 0, 0, 0), key(5, 9, -3, -6), key(12, -16, -4, 7),
        key(18, -20, -4, 11), key(31, -15, -4, 9),
        key(61, -17, -4, 9), key(92, -15, -2, 8), key(107, 0, 0, 0),
    ),
    neck=(
        key(0, 0, 0, 0), key(5, 7, -6, -6), key(15, -7, -1, 12),
        key(32, 22, -4, -11), key(81, 19, -3, -9),
        key(94, 14, -2, -7), key(107, 0, 0, 0),
    ),
    head=(
        key(0, 0, 0, 0), key(6, 7, -4, -3), key(17, -3, 4, 6),
        key(35, 13, -2, -12), key(63, 9, -2, -10), key(90, 12, -2, -11),
        key(98, 15, 0, -7), key(107, 0, 0, 0),
    ),
    paw_left=(
        key(0, 1.24, 2.60, -.18), key(4, 1.24, 2.63, -.08),
        key(8, 1.31, 1.77, -.42), key(16, .72, 1.27, -.48),
        key(28, .73, 1.26, -.37), key(89, .74, 1.26, -.37),
        key(99, 1.08, 1.29, -.36), key(107, *PAW_L_HOME),
    ),
    paw_right=(
        key(0, *PAW_R_HOME), key(3, -1.22, 1.42, -.20),
        key(5, -1.14, 1.26, -.34),
        key(11, -1.30, .58, -.62), key(16, -1.22, .40, -.69),
        key(90, -1.22, .40, -.69), key(97, -1.30, .64, -.48),
        key(107, *PAW_R_HOME),
    ),
    paw_left_rotation=(
        key(0, 74, 0, -8), key(5, 80, 8, -9), key(14, 17, 13, -10),
        key(30, 9, 4, -7), key(88, 9, 4, -7), key(107, 0, 0, 0),
    ),
    paw_right_rotation=(
        key(0, 0, 0, 0), key(15, 5, -7, -33), key(90, 5, -7, -33),
        key(102, 1, -4, -10), key(107, 0, 0, 0),
    ),
)


# The renderers share the bow at the 1.15s reveal. The head
# notices the player first, followed by the spine and relieved paws.
CLEANSE = Motion(
    "MuseCleanse",
    78,
    hips=(
        key(0, *PELVIS_HOME), key(8, .019, -.05, -.041),
        key(23, -.036, -.187, -.081), key(35, -.027, -.177, -.068),
        key(44, -.02, -.127, -.053), key(58, -.008, -.021, -.016),
        key(67, -.004, -.03, -.009), key(78, *PELVIS_HOME),
    ),
    hip_rotation=(
        key(0, 0, 0, 0), key(10, 1, 3, -4), key(24, -4, 2, 3),
        key(35, -4, 1, 2), key(53, -1, -1, 1), key(75, 0, 0, 0),
    ),
    spine=(
        key(0, 0, 0, 0), key(24, -10, 3, 5), key(35, -9, 2, 4),
        key(46, -5, 2, 3), key(61, 1, -2, 1), key(78, 0, 0, 0),
    ),
    chest=(
        key(0, 0, 0, 0), key(8, 5, -1, 0), key(25, -13, 1, 5),
        key(35, -12, 1, 4), key(45, -8, -1, 3), key(57, 4, -3, 0),
        key(68, 1, -1, -1), key(78, 0, 0, 0),
    ),
    neck=(
        key(0, 0, 0, 0), key(25, -9, 5, 2), key(35, -8, 4, 2),
        key(44, 2, 0, -1), key(51, 8, -3, -2), key(70, 2, -1, 0),
        key(78, 0, 0, 0),
    ),
    head=(
        key(0, 0, 0, 0), key(10, -3, 0, 4), key(25, -18, -4, -6),
        key(35, -15, -3, -6), key(43, 4, -3, -3), key(50, 13, 0, 2),
        key(60, 6, 5, 7), key(69, 2, 2, 3), key(78, 0, 0, 0),
    ),
    paw_left=(
        key(0, *PAW_L_HOME), key(10, 1.20, 1.31, -.48), key(26, 1.08, 1.01, -.27),
        key(35, 1.12, 1.02, -.26), key(46, 1.24, 1.12, -.34),
        key(58, 1.34, 1.42, -.47), key(65, 1.37, 1.41, -.43),
        key(78, *PAW_L_HOME),
    ),
    paw_right=(
        key(0, *PAW_R_HOME), key(9, -1.25, 1.30, -.37), key(24, -1.18, .81, -.25),
        key(35, -1.20, .83, -.26), key(49, -1.28, 1.01, -.27),
        key(65, -1.38, 1.20, -.23), key(78, *PAW_R_HOME),
    ),
    paw_left_rotation=(
        key(0, 0, 0, 0), key(10, -7, -6, 0), key(28, 15, -12, -10),
        key(37, 13, -11, -8), key(58, 23, -14, 7), key(78, 0, 0, 0),
    ),
    paw_right_rotation=(
        key(0, 0, 0, 0), key(15, -2, -6, 5), key(29, 10, 8, 7),
        key(39, 8, 7, 5), key(59, 3, -1, -3), key(78, 0, 0, 0),
    ),
)


CORRUPTION = {
    "Core": (
        (key(0, 0, 0, 0), key(24, 0, 0, 0), key(35, 0, .08, -.17)),
        (key(0, 0, 0, 0), key(24, 0, 0, 0), key(35, 12, 19, -5)),
    ),
    "Horn.L": (
        (key(0, 0, 0, 0), key(25, 0, 0, 0), key(35, .11, .13, .015)),
        (key(0, 0, 0, 0), key(25, 0, 0, 0), key(35, 2, 3, -17)),
    ),
    "Horn.R": (
        (key(0, 0, 0, 0), key(25, 0, 0, 0), key(35, -.13, .15, .025)),
        (key(0, 0, 0, 0), key(25, 0, 0, 0), key(35, 4, -3, 19)),
    ),
}


class Pose:
    """Forward-evaluate Blender's bone basis to solve fixed-length two-bone chains."""

    def __init__(self, bones):
        self.bones = bones
        self.rest = {}
        for bone in bones:
            rest = bone.bone.matrix_local
            if bone.parent:
                rest = bone.parent.bone.matrix_local.inverted() @ rest
            self.rest[bone.name] = rest
        self.reset()

    def reset(self):
        self.world = {}
        self.rotations = {}
        self.translations = {}

    def matrix(self, name):
        if name not in self.world:
            parent = self.bones[name].parent
            rest_world = self.matrix(parent.name) @ self.rest[name] if parent else self.rest[name]
            self.world[name] = rest_world @ Matrix.LocRotScale(
                self.translations.get(name, ZERO), self.rotations.get(name, IDENTITY), UNIT_SCALE,
            )
        return self.world[name]

    def set_rotation(self, name, value):
        self.rotations[name] = value
        self.world.clear()

    def bind_axes(self, name, value):
        bind = self.bones[name].bone.matrix_local.to_quaternion()
        self.set_rotation(name, bind.inverted() @ value @ bind)

    def bind_offset(self, name, value):
        bind = self.bones[name].bone.matrix_local.to_quaternion()
        self.translations[name] = bind.inverted() @ value
        self.world.clear()

    def world_rotation(self, name, value):
        parent = self.bones[name].parent
        basis = self.matrix(parent.name) @ self.rest[name] if parent else self.rest[name]
        self.set_rotation(name, basis.to_quaternion().inverted() @ value)

    def aim(self, name, destination):
        local = self.matrix(name)
        current = local.to_quaternion()
        axis = current @ Vector((0.0, 1.0, 0.0))
        target_axis = (destination - local.translation).normalized()
        aim = axis.rotation_difference(target_axis)
        self.world_rotation(name, aim @ current)

    def reach(self, upper, lower, tip, destination, bend_hint):
        start = self.matrix(upper).translation
        axis = (destination - start).normalized()
        a = self.bones[upper].bone.length
        b = self.bones[lower].bone.length
        distance = (destination - start).length
        distance = max(abs(a - b) + .00005, min(distance, a + b - .00005))
        projection = (a * a - b * b + distance * distance) / (2.0 * distance)
        bend = bend_hint - axis * bend_hint.dot(axis)
        mid = start + axis * projection + bend.normalized() * sqrt(max(0.0, a * a - projection * projection))
        self.aim(upper, mid)
        self.aim(lower, start + axis * distance)
        return (self.matrix(tip).translation - destination).length

    def frame(self, motion, frame):
        self.reset()
        self.bind_offset("Pelvis", position(*sample(motion.hips, frame)))
        for name, track in (
            ("Pelvis", motion.hip_rotation),
            ("Spine", motion.spine),
            ("Chest", motion.chest),
            ("Neck", motion.neck),
            ("Head", motion.head),
        ):
            self.bind_axes(name, rotation(*sample(track, frame)))
        if motion is CLEANSE:
            for name, (path, tilt) in CORRUPTION.items():
                self.bind_offset(name, position(*sample(path, frame)))
                self.bind_axes(name, rotation(*sample(tilt, frame)))
        shoulder_angle = min(1.0, max(0.0, (sample(motion.paw_left, frame)[1] - 1.7) / 1.0))
        self.bind_axes("Clavicle.L", rotation(0.0, 0.0, 32.0 * shoulder_angle))
        errors = {}
        for side, sign in (("L", 1.0), ("R", -1.0)):
            foot = f"Foot.{side}"
            errors[foot] = self.reach(
                f"Thigh.{side}", f"Shin.{side}", foot, self.bones[foot].bone.head_local,
                position(sign * .18, .04, -1.0),
            )
            self.world_rotation(foot, self.bones[foot].bone.matrix_local.to_quaternion())

        for side, sign, target, orientation in (
            ("L", 1.0, motion.paw_left, motion.paw_left_rotation),
            ("R", -1.0, motion.paw_right, motion.paw_right_rotation),
        ):
            paw = f"Paw.{side}"
            wrist = position(*sample(target, frame))
            # Elbows stay outside the abdomen during the charge and under the paw at contact.
            errors[paw] = self.reach(
                f"UpperArm.{side}", f"Forearm.{side}", paw, wrist,
                position(sign * 1.0, -.25, .35),
            )
            facing = rotation(*sample(orientation, frame))
            self.world_rotation(paw, facing @ self.bones[paw].bone.matrix_local.to_quaternion())
        return errors


def build_actions(armature, motions=None):
    """Return explicit named actions for FBX export on the prototype armature."""
    armature.animation_data_create()
    bpy.context.scene.render.fps = FPS
    pose = Pose(armature.pose.bones)
    animated = [
        bone for bone in armature.pose.bones
        if bone.name in {"Pelvis", "Spine", "Chest", "Neck", "Head"}
        or bone.name in CORRUPTION
        or bone.name.split(".")[0] in {"Clavicle", "UpperArm", "Forearm", "Paw", "Thigh", "Shin", "Foot"}
    ]
    translated = [armature.pose.bones[name] for name in ("Pelvis", *CORRUPTION)]
    for bone in animated:
        bone.rotation_mode = "QUATERNION"
    actions = []
    for motion in (NORMAL, HEAVY, LIGHTNING, CLEANSE) if motions is None else motions:
        action = bpy.data.actions.new(motion.name)
        action.use_fake_user = True
        armature.animation_data.action = action
        previous = {}
        maximum = {}
        for frame in range(motion.end + 1):
            errors = pose.frame(motion, frame)
            for name, error in errors.items():
                maximum[name] = max(maximum.get(name, (0.0, 0)), (error, frame))
            for bone in animated:
                value = pose.rotations.get(bone.name, IDENTITY).normalized()
                if bone.name in previous and previous[bone.name].dot(value) < 0:
                    value.negate()
                previous[bone.name] = value
                bone.rotation_quaternion = value
                bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=bone.name)
            for bone in translated:
                bone.location = pose.translations.get(bone.name, ZERO)
                bone.keyframe_insert(data_path="location", frame=frame, group=bone.name)
        print(f"{motion.name} max target distance (m, frame): {maximum}")
        actions.append(action)
    armature.animation_data.action = None
    for bone in animated:
        bone.rotation_quaternion = IDENTITY
    for bone in translated:
        bone.location = ZERO
    bpy.context.view_layer.update()
    return actions
