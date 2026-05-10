shader_type canvas_item;

void fragment() {
    vec2 uv = UV - vec2(0.5); // Center the UV coordinates to (0.5, 0.5) in order to create a circular mask
    if (length(uv) > 0.5) discard; // Discard pixels outside the circle
    COLOR = COLOR;
}