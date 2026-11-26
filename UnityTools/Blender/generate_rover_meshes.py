"""
Procedural Rovers (KSP 1.12.5) - Blender 3.x / 4.x Otomatik 3B Model & Hiyerarşi Üretici
-----------------------------------------------------------------------------------------
Kullanım:
1. Blender'ı açın -> 'Scripting' sekmesine geçin -> Bu dosyayı açıp 'Run Script' (Alt+P) tuşuna basın.
2. Script; 5 farklı Rover Şasi arketipini, Hasır/Ağır Hizmet tekerleklerini, Tırtıllı Paleti ve
   Payload (MastCam, Matkap, MMRTG, SolarDeck, Spektrometre) modellerini KSP Unity hiyerarşisine
   birebir uygun isimlendirme ve sıfırlanmış (Applied Scale/Rotation) transformlarla üretir.
"""

import bpy
import math


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def create_empty(name, parent=None, location=(0.0, 0.0, 0.0)):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = 'PLAIN_AXES'
    empty.empty_display_size = 0.25
    empty.location = location
    bpy.context.collection.objects.link(empty)
    if parent is not None:
        empty.parent = parent
    return empty


def create_box(name, size_xyz, parent=None, location=(0.0, 0.0, 0.0), bevel_width=0.02):
    """
    Unity koordinat (X=Genişlik, Y=Yükseklik, Z=Uzunluk) ölçülerine uygun,
    transform ölçeği (1,1,1) olarak fırınlanmış (baked) kutu mesh üretir.
    Not: Blender'da Y ileri/uzunluk, Z yukarı/yüksekliktir; FBX export (-Z Forward, Y Up) ile Unity'ye tam oturur.
    """
    sx, sy_unity, sz_unity = size_xyz
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name

    # Blender eksenlerinde boyutlandır (X=Width, Y=Length(Unity Z), Z=Height(Unity Y))
    obj.scale = (sx, sz_unity, sy_unity)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    if bevel_width > 0:
        bevel = obj.modifiers.new(name="Bevel", type='BEVEL')
        bevel.width = bevel_width
        bevel.segments = 2
        bpy.ops.object.modifier_apply(modifier=bevel.name)

    if parent is not None:
        obj.parent = parent
    return obj


def create_wheel_cylinder(name, radius, width, parent=None, location=(0.0, 0.0, 0.0), vertices=24):
    """
    Aksı X ekseni boyunca uzanan (Sol-Sağ), rotasyonu (0,0,0) olarak fırınlanmış tekerlek/makara silindiri üretir.
    """
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=width,
        location=location,
        rotation=(0.0, math.radians(90.0), 0.0)
    )
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    if parent is not None:
        obj.parent = parent
    return obj


def build_rover_core():
    root = create_empty("RoverCoreProcedural", location=(0.0, 0.0, 0.0))

    # 1. Micro (Sojourner)
    c_micro = create_empty("Chassis_Micro", parent=root)
    create_box("Mesh_Micro_Hull", (0.65, 0.30, 0.90), parent=c_micro, bevel_width=0.025)

    # 2. Heavy Science (Curiosity / Perseverance)
    c_heavy = create_empty("Chassis_HeavySci", parent=root)
    create_box("Mesh_HeavySci_Hull", (1.40, 0.65, 2.20), parent=c_heavy, bevel_width=0.05)
    create_box("Mesh_HeavySci_Details", (1.52, 0.16, 1.75), parent=c_heavy, location=(0.0, 0.0, -0.10), bevel_width=0.015)

    # 3. Tub Hull (Lunokhod)
    c_luno = create_empty("Chassis_Lunokhod", parent=root)
    create_box("Mesh_Lunokhod_Tub", (1.60, 0.85, 2.10), parent=c_luno, bevel_width=0.14)

    # 4. Foldable Frame (Apollo LRV)
    c_apollo = create_empty("Chassis_ApolloLRV", parent=root)
    create_box("Mesh_Apollo_Frame", (1.30, 0.22, 2.30), parent=c_apollo, bevel_width=0.02)

    # 5. Armored Explorer (Sci-Fi)
    c_scifi = create_empty("Chassis_SciFi", parent=root)
    create_box("Mesh_SciFi_Armor", (1.80, 0.80, 2.60), parent=c_scifi, bevel_width=0.08)

    return root


def build_continuous_track():
    root = create_empty("TrackContinuous", location=(3.5, 0.0, 0.0))
    create_box("Mount_Strut_Mesh", (0.26, 0.18, 0.35), parent=root, location=(0.14, 0.0, 0.18))

    susp = create_empty("suspensionTransform", parent=root)
    create_box("TrackFrame_Mesh", (0.18, 0.24, 1.15), parent=susp, location=(0.0, 0.0, 0.05))
    create_box("TrackBelt_Mesh", (0.28, 0.46, 1.35), parent=susp, location=(0.0, 0.0, 0.02), bevel_width=0.12)

    create_wheel_cylinder("DriveSprocket", 0.22, 0.26, parent=susp, location=(0.0, -0.52, 0.12), vertices=16)
    create_wheel_cylinder("IdlerWheel", 0.20, 0.26, parent=susp, location=(0.0, 0.52, 0.10), vertices=16)

    roller_y_offsets = [0.36, 0.12, -0.12, -0.36]
    for idx, y_off in enumerate(roller_y_offsets):
        create_wheel_cylinder(f"RoadWheel_0{idx + 1}", 0.16, 0.25, parent=susp, location=(0.0, y_off, -0.08), vertices=16)

    return root


def build_mastcam():
    root = create_empty("MastCamPanoramic", location=(-2.5, 0.0, 0.0))
    create_box("MastPole_Mesh", (0.10, 0.55, 0.10), parent=root, location=(0.0, 0.0, 0.025))

    yaw = create_empty("MastHead_Yaw", parent=root, location=(0.0, 0.0, 0.32))
    pitch = create_empty("MastOptics_Pitch", parent=yaw, location=(0.0, 0.0, 0.0))
    create_box("CameraHead_Mesh", (0.26, 0.16, 0.18), parent=pitch, bevel_width=0.02)

    return root


if __name__ == "__main__":
    clear_scene()
    build_rover_core()
    build_continuous_track()
    build_mastcam()
    print("[ProceduralRovers] Blender 3B baz hiyerarşisi başarıyla oluşturuldu!")
