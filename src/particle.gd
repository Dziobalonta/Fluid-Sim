extends Node2D
class_name Particle

var velocity: Vector2 = Vector2.ZERO
var radius: float = 10.0
var damping: float = 0.6
var boundary: Rect2
var gravity: Vector2 = Vector2(0, 981.0)
var density: float = 0.0

@export var density_gradient: Gradient

func set_boundary(new_boundary: Rect2):
	boundary = new_boundary
	
func _draw() -> void:
	var current_color: Color
	if density_gradient != null:
		var normalized_density = clamp(density / 20.0 ,0.0, 1.0)
		
		current_color = density_gradient.sample(normalized_density)
		
	else:
		# Emergency color
		current_color = Color.WHITE_SMOKE
	
	draw_circle(Vector2.ZERO, radius, current_color, true)

func _ready() -> void:
	pass
	
func _physics_process(delta: float) -> void:
	velocity += gravity * delta
	position += velocity * delta
	
	check_boundary()
	queue_redraw()
	
func check_boundary():
	if boundary == Rect2(): return

	# Right wall
	if position.x > boundary.end.x - radius:
		position.x = boundary.end.x - radius
		velocity.x *= -damping
		
	# Left wall
	elif position.x < boundary.position.x + radius:
		position.x = boundary.position.x + radius
		velocity.x *= -damping
		
	# Bottom
	if position.y > boundary.end.y - radius:
		position.y = boundary.end.y - radius
		velocity.y *= -damping
		
	# Top
	elif position.y < boundary.position.y + radius:
		position.y = boundary.position.y + radius
		velocity.y *= -damping
