extends Node2D

@onready var fluid_container: ReferenceRect = $FluidContainer

@export var particle_scene: PackedScene
@export var particle_count: int = 200
var particles_list: Array[Particle] = []

var smoothing_radius: float = 60.0

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
		particles_list.append(p)

func _physics_process(delta: float) -> void:
	for p in particles_list:
			p.density = calculate_density(p.position)
	
func smoothing_function(distance: float, radius: float) -> float:
	var value = max(0, radius * radius - distance * distance)
	return value * value * value
	
func calculate_density(sample_point: Vector2) -> float:
	var density: float = 0.0
	const mass: float = 1.0

	var radius_sq = smoothing_radius * smoothing_radius
	
	for p in particles_list:
		var dst_sq = p.position.distance_squared_to(sample_point)
		
		if dst_sq >= radius_sq:
			continue
		
		
		var value = radius_sq - dst_sq # Smoothing function
		var influence = value * value * value
		
		density += mass * influence
			
	return density
