"""Build the runtime preview from Innerscene's CC0 human-base-rigged.glb."""
import sys
from pathlib import Path

import bpy


def main():
    source, destination = map(Path, sys.argv[sys.argv.index("--") + 1:])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source.resolve()))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.find_armature()]
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(meshes) != 1 or len(armatures) != 1 or len(armatures[0].data.bones) != 53:
        raise ValueError("Expected Innerscene's single 53-bone skinned mesh")

    mesh = meshes[0]
    bpy.context.view_layer.objects.active = mesh
    parts = mesh.modifiers.new("Preview triangle budget", "DECIMATE")
    mesh.data.calc_loop_triangles()
    parts.ratio = min(1.0, 18000 / len(mesh.data.loop_triangles))
    bpy.ops.object.modifier_apply(modifier=parts.name)
    mesh.data.calc_loop_triangles()
    if len(mesh.data.loop_triangles) > 18000:
        raise ValueError("Preview exceeds the 18,000 triangle budget")

    # Select the deforming body and rig, excluding Blender's bone display shapes.
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armatures[0].select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(destination.resolve()),
        export_format="GLB",
        export_animations=False,
        use_selection=True,
    )
    print(f"{len(mesh.data.loop_triangles)} triangles, {len(armatures[0].data.bones)} bones")


if __name__ == "__main__":
    main()
