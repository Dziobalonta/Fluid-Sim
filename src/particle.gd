extends Node2D
class_name Particle

var velocity: Vector2 = Vector2.ZERO
var radius: float = 10.0
var damping: float = 0.6

func _ready() -> void:
	pass
	
func _physics_process(delta: float) -> void:
	pass
	
func _draw() -> void:
	draw_circle(Vector2.ZERO, radius, Color.WHITE_SMOKE, true)
	
