using Godot;
using System;

public partial class Particle : Node2D
{
	public Vector2 Velocity = Vector2.Zero;
	public float Radius = 10f;
	public float Damping = 0.6f;
	public Rect2 Boundary;
	public Vector2 Gravity = new Vector2(0f, 981f);

	public float NormalizedDensity = 0f;

	// Normalization happens once, not in _Draw
	public float Density { set => NormalizedDensity = Math.Clamp(value / 20f, 0f, 1f); }

	[Export] public Gradient DensityGradient;

	public Vector2 PressureAcceleration = Vector2.Zero;


	public void SetBoundary(Rect2 newBoundary) => Boundary = newBoundary;

	public override void _Draw()
	{
		Color c = DensityGradient != null
			? DensityGradient.Sample(NormalizedDensity)
			: Colors.WhiteSmoke;

		DrawCircle(Vector2.Zero, Radius, c);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		Velocity += PressureAcceleration * dt;
		// Velocity += Gravity * dt;
		Position += Velocity * dt;

		CheckBoundary();
		QueueRedraw();
	}

	private void CheckBoundary()
	{
		if (Boundary == default) return;

		Vector2 pos = Position;
		Vector2 vel = Velocity;

		// Right wall
		if (pos.X > Boundary.End.X - Radius) { 

			pos.X = Boundary.End.X - Radius;
			vel.X *= -Damping;
		// Left wall
		} else if (pos.X < Boundary.Position.X + Radius) {

			pos.X = Boundary.Position.X + Radius;
			vel.X *= -Damping; 
		}
		// Bottom
		if (pos.Y > Boundary.End.Y - Radius) {

			pos.Y = Boundary.End.Y - Radius;
			vel.Y *= -Damping;
		// Top
		} else if (pos.Y < Boundary.Position.Y + Radius) {

			pos.Y = Boundary.Position.Y + Radius;
			vel.Y *= -Damping;
		}

		Position = pos;
		Velocity = vel;
	}
}
