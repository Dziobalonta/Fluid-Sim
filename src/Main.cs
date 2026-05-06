using Godot;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;


public partial class Main : Node2D
{
	[Export] public PackedScene ParticleScene; 
	[Export] public int ParticleCount = 200; 
	
	[Export] public int SpawnBatchSize = 15; // How many particles in one frame
	[Export] public float mass = 1.0f; 

	private bool isSpawning = true;
	private int spawnedCount = 0;
	private Rect2 screenRect;
	private Rect2 spawnArea;

	public float SmoothingRadius = 30.0f; 

	private List<Particle> particles = new List<Particle>(); 
	private Vector2[] positions; 
	private float[] densities; 

	private float volume; 
	private float scale;

	private float targetDensity = 1.0f;
	private float pressureMultiplier = 20.0f;

	public override void _Ready()
	{
		screenRect = GetViewportRect(); 
		spawnArea = new Rect2(screenRect.Size.X / 2.0f - 150.0f, screenRect.Size.Y / 2.0f, 300.0f, 300.0f); 

		positions = new Vector2[ParticleCount]; 
		densities = new float[ParticleCount]; 
		
		volume = (float) ((Math.PI * Math.Pow(SmoothingRadius, 4)) / 6.0f);
		scale = (float) (12 / (Math.Pow(SmoothingRadius,4)) * Math.PI);
		
	}

	public override void _Process(double delta)
	{
		if (isSpawning)
		{
			SpawnBatch();
		}
	}

	private void SpawnBatch()
	{
		// Calculate batch
		int limit = Math.Min(spawnedCount + SpawnBatchSize, ParticleCount);

		for (int i = spawnedCount; i < limit; i++)
		{
			Particle p = ParticleScene.Instantiate<Particle>(); 
			
			float randX = (float)GD.RandRange(spawnArea.Position.X, spawnArea.End.X); 
			float randY = (float)GD.RandRange(spawnArea.Position.Y, spawnArea.End.Y); 
			
			p.Position = new Vector2(randX, randY); 
			p.SetBoundary(screenRect); 
			
			AddChild(p); 
			particles.Add(p); 
			
			positions[i] = p.Position;
		}

		spawnedCount = limit;

		if (spawnedCount >= ParticleCount)
		{
			isSpawning = false;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Wait for all the particles to spawn
		if (isSpawning) return;

		for (int i = 0; i < ParticleCount; i++) 
		{
			positions[i] = particles[i].Position; 
		}

		Parallel.For(0, ParticleCount, i => 
		{
			densities[i] = CalculateDensity(positions[i]); 
		});

		Parallel.For(0, ParticleCount, i => 
		{
			Vector2 pressureForce = CalculatePressureForce(i);
			Vector2 pressureAcceleration = pressureForce / densities[i];
			particles[i].Velocity += pressureAcceleration * (float)delta;
		}); 

		for (int i = 0; i < ParticleCount; i++) 
		{
			particles[i].Density = densities[i]; 
		}
	}

	public float SmoothingFunction(float dst, float radius) 
	{
		if (dst >= radius) return 0; 
		
		return (radius - dst) * (radius - dst) / volume; 
	}

	public float SmoothingFunctionDerivative(float dst, float radius) 
	{
		if (dst >= radius) return 0; 

		return (dst - radius) * scale;
	}

	public float CalculateDensity(Vector2 samplePoint) 
	{
		float density = 0.0f; 

		for (int i = 0; i < ParticleCount; i++) 
		{
			float dst = (positions[i] - samplePoint).Length(); 
			float influence = SmoothingFunction(dst, SmoothingRadius); 
			density += mass * influence; 
		}

		return density; 
	}

	public float ConvertDensityToPressure(float density)
	{
		float densityError = density - targetDensity;
		float pressure = -densityError * pressureMultiplier;
		return pressure;
	}

	Vector2 CalculatePressureForce(int particleIndex)
	{
		Vector2 pressureForce = Vector2.Zero;

		for (int otherPart = 0; otherPart < ParticleCount; otherPart++)
		{
			if(particleIndex == otherPart) continue;
			Vector2 offset = positions[otherPart] - positions[particleIndex];


			float dst = offset.Length();

			Vector2 dir = (dst  == 0) ?  GetRandomDir() : offset / dst;

			float slope = SmoothingFunctionDerivative(dst, SmoothingRadius);
			float density = densities[otherPart];
			pressureForce += ConvertDensityToPressure(density) * dir * slope * mass / density;

		}

		return pressureForce;
	}

	Vector2 GetRandomDir()
	{
		return Vector2.FromAngle((float)GD.Randf() * Mathf.Tau);
	}
}
