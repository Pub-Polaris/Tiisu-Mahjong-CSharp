import bpy, os, math

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "table.png")
scene = bpy.context.scene
scene.render.engine = scene.render.engine
scene.render.resolution_x = 2048
scene.render.resolution_y = 2048
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.filepath = out

# top-down orthographic camera
cam_data = bpy.data.cameras.get("SkinCam") or bpy.data.cameras.new("SkinCam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 1.02
cam = bpy.data.objects.get("SkinCamObj")
if cam is None:
    cam = bpy.data.objects.new("SkinCamObj", cam_data)
    scene.collection.objects.link(cam)
cam.data = cam_data
cam.location = (0, 0, 3.0)
cam.rotation_euler = (0, 0, 0)
scene.camera = cam

# hide preview helpers and the code-drawn center plate placeholder
for name in ("PreviewGround", "center_sq"):
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_render = True

print("engine", scene.render.engine, "samples", getattr(scene, "cycles", None) and scene.cycles.samples)
bpy.ops.render.render(write_still=True)
print("rendered", out)
