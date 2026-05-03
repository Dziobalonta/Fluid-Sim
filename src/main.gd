extends Node2D

func _ready() -> void:
	var screen_rect = get_viewport_rect()
	
	
	for child in get_children():
		if child.has_method("set_boundary"):
			child.set_boundary(screen_rect)
	
