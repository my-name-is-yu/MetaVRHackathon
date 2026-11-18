"""Map Runeveil's existing Muse artwork onto the rigged volumes in rest space."""

from mathutils import Vector


ATLAS_SIZE = 1254


def blend(start, end, value):
    t = max(0.0, min(1.0, (value - start) / (end - start)))
    return t * t * (3.0 - 2.0 * t)


def between(value, start, end):
    return max(start, min(end, value))


def interp(value, points):
    if value <= points[0][0]:
        return points[0][1]
    for (start, first), (end, last) in zip(points, points[1:]):
        if value <= end:
            return first + (last - first) * (value - start) / (end - start)
    return points[-1][1]


def artwork(obj, project, depth_center=0.0, side_blend=0.36, side_project=None):
    mesh = obj.data
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    mesh.uv_layers.new(name="FrontArtwork")
    mesh.uv_layers.new(name="BackArtwork")
    front, back = mesh.uv_layers
    side = mesh.uv_layers.new(name="SideArtwork") if side_project else None
    color = mesh.color_attributes.new(name="Facing", type="FLOAT_COLOR", domain="CORNER")
    mesh.color_attributes.active_color = color
    side_normals = [abs(vertex.normal.x) for vertex in mesh.vertices] if side is not None else []
    for loop in mesh.loops:
        location = mesh.vertices[loop.vertex_index].co
        front_px, back_px = project(location)
        front.data[loop.index].uv = (front_px[0] / ATLAS_SIZE, 1 - front_px[1] / ATLAS_SIZE)
        back.data[loop.index].uv = (back_px[0] / ATLAS_SIZE, 1 - back_px[1] / ATLAS_SIZE)
        use_back = 1 - blend(-side_blend, side_blend, location.y - depth_center)
        use_side = 0
        if side is not None:
            x, depth, height = location
            side_px = side_project(depth, height)
            side.data[loop.index].uv = (side_px[0] / ATLAS_SIZE, 1 - side_px[1] / ATLAS_SIZE)
            # The forward curl of the arms shifts their side surface toward the viewer.
            axis = 0.17 * blend(0.88, 1.48, abs(x))
            depth_weight = 1 - blend(0.045, 0.37, abs(depth - axis))
            profile = blend(0.45, 0.98, side_normals[loop.vertex_index])
            face_protection = blend(2.10, 2.55, height) * blend(0.16, 0.42, depth)
            use_side = 0.92 * max(depth_weight, profile * (1 - face_protection))
        color.data[loop.index].color = (use_side, 1.0, 1.0, use_back)


def arm_coordinates(x, height, sleeve):
    # Flatten the lowered arm along the shoulder to paw axis, in the same order as the atlas.
    distance = 0.65 * (x - 0.72) - 0.80 * (height - 2.04)
    across = 0.80 * (x - 0.72) + 0.65 * (height - 2.04)
    return distance, across, blend(0.54, 0.91, x) if sleeve else 0


def dark_body(location, sleeve=False):
    x, _, height = location
    sign = 1 if x >= 0 else -1
    distance, across, arm = arm_coordinates(abs(x), height, sleeve)
    y = interp(height, ((0.0, 1184), (0.26, 1110), (0.70, 1065), (0.97, 1031),
                        (1.61, 783), (2.12, 652), (2.34, 617)))
    # Torso and legs use the mostly symmetrical front silhouette. Let the outer legs reach
    # their own feet rather than painting the transparent split between the feet onto the mesh.
    foot_x = sign * between(141 + (abs(x) - 0.47) * 204, 49, 237)
    lower = 1 - blend(0.18, 0.71, height)
    torso = Vector((627 + x * 324 * (1 - lower) + foot_x * lower, y))
    arm_x = between(878 + distance * 232, 814, 1198)
    arm_y = between(683 - across * 229, 615, 752)
    px = torso.lerp(Vector((627 + sign * (arm_x - 627), arm_y)), arm)
    collar = (blend(1.83, 2.02, height) * (1 - blend(2.15, 2.40, height))
              * blend(0.48, 0.65, abs(x)) * (1 - blend(0.94, 1.15, abs(x))))
    px.x += sign * collar * (abs(x) - 0.77) * 165
    px.y -= collar * (height - 2.08) * 155
    px = px.lerp(Vector(dark_head(location)[0]), blend(2.25, 2.52, height))
    inner_feet = (1 - blend(0.08, 0.25, abs(x))) * (1 - blend(0.36, 0.63, height))
    px = px.lerp(Vector((627 + x * 278, min(px.y, 1040))), inner_feet)
    return px, (ATLAS_SIZE - px.x, px.y)


def dark_head(location):
    x, _, height = location
    px = (627 + x * 350, 429 - (height - 2.83) * 354)
    return px, (ATLAS_SIZE - px[0], px[1])


def dark_horn(location):
    x, _, height = location
    px = (627 + x * 321, 67 + (4.06 - height) * 272)
    return px, (ATLAS_SIZE - px[0], px[1])


def white_surface(location, sleeve=False):
    x, _, height = location
    sign = 1 if x >= 0 else -1
    distance, across, arm = arm_coordinates(abs(x), height, sleeve)
    y = interp(height, ((0.0, 1179), (0.35, 1086), (0.83, 963), (1.50, 815),
                        (2.20, 620), (2.38, 530), (2.88, 363), (3.67, 91)))
    foot_x = sign * between(135 + (abs(x) - 0.47) * 222, 50, 216)
    lower = 1 - blend(0.18, 0.63, height)
    torso = Vector((623 + x * 335 * (1 - lower) + foot_x * lower, y))
    arm_start = Vector((623 + sign * 215, 617 if sign > 0 else 653))
    arm_end = Vector((623 + sign * 375, 372 if sign > 0 else 473))
    arm_axis = arm_end - arm_start
    cross_axis = Vector((arm_axis.y, -arm_axis.x)).normalized() * sign
    arm_pixel = arm_start + arm_axis * between(distance / 1.30, 0, 1.16) + cross_axis * across * 181
    px = torso.lerp(arm_pixel, arm)
    px = px.lerp(Vector(white_head(location)[0]), blend(2.24, 2.52, height))
    inner_feet = (1 - blend(0.08, 0.25, abs(x))) * (1 - blend(0.36, 0.63, height))
    px = px.lerp(Vector((623 + x * 285, min(px.y, 1010))), inner_feet)
    return px, white_back(location)


def white_head(location):
    x, _, height = location
    px = (623 + x * 337, 363 - (height - 2.88) * 335)
    return px, white_back(location)


def white_back(location):
    # The restored portrait only depicts the front. Use a featureless area of the same cloth.
    x, _, height = location
    px = 624 - x * 126
    y = 720 + (3.6 - height) * 71
    return px, y


def dark_side(depth, height):
    # Use the unmarked plush strip on the original back art for grazing faces.
    return 584 + depth * 83, 658 + (3.6 - height) * 87


def white_side(depth, height):
    return 595 + depth * 113, 712 + (3.6 - height) * 91
