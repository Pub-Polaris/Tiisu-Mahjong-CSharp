import bpy, math, os
from mathutils import Vector

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "preview-4x3.png")
scene = bpy.context.scene
scene.render.resolution_x = 800
scene.render.resolution_y = 600
scene.render.resolution_percentage = 100
scene.render.film_transparent = False
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = out

# world background: dark studio; keep existing lights
world = scene.world or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.02, 0.025, 0.03, 1)
    bg.inputs[1].default_value = 0.35

target = Vector((0, 0, 0.78))
elev = math.radians(25.0)
dist = 1.0
cam = bpy.data.objects.get("AngleCamObj")
if cam is None:
    cd = bpy.data.cameras.new("AngleCam")
    cam = bpy.data.objects.new("AngleCamObj", cd)
    scene.collection.objects.link(cam)
cam.data.type = 'PERSP'
cam.data.sensor_fit = 'VERTICAL'
cam.data.angle = math.radians(55.0)
cam.location = target + Vector((0, -dist * math.cos(elev), dist * math.sin(elev)))
look = target - cam.location
cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
scene.camera = cam

for name in ("PreviewGround", "PreviewCamObj", "center_sq"):
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_render = True

bpy.ops.render.render(write_still=True)
print("rendered", out)
