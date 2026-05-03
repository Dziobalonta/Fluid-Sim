extends Node2D

@onready var fluid_container: ReferenceRect = $FluidContainer

@export var particle_scene: PackedScene
@export var particle_count: int = 200

func _ready() -> void:
	var screen_rect = get_viewport_rect()
	#var container_rect = Rect2(fluid_container.global_position,fluid_container.size)
	
	spawn_particles(screen_rect)
	
func spawn_particles(bounding_box: Rect2):
	var spawn_area_w = 300.0
	var spawn_area_h = 300.0
	var spawn_area = Rect2(bounding_box.size.x / 2.0 - spawn_area_w / 2.0, bounding_box.size.y/2.0, spawn_area_w, spawn_area_h)
	
	for i in range(particle_count):
		# Create an instance of a particle
		var p = particle_scene.instantiate()
		
		var rand_x = randf_range(spawn_area.position.x, spawn_area.end.x)
		var rand_y = randf_range(spawn_area.position.y, spawn_area.end.y)
		
		p.position = Vector2(rand_x, rand_y)
		
		if p.has_method("set_boundary"):
			p.set_boundary(bounding_box)
		
		# Adding a particle to the scene	
		add_child(p)
		
		
	
