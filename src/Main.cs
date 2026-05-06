using Godot;
using System.Collections.Generic;
using System;
using System.Threading.Tasks; 

public partial class Main : Node2D
{
	[Export] public PackedScene ParticleScene; 
	[Export] public int ParticleCount = 200; 
	
	[Export] public int SpawnBatchSize = 15; // How many particles in one frame
	private bool isSpawning = true;
	private int spawnedCount = 0;
	private Rect2 screenRect;
	private Rect2 spawnArea;

	public float SmoothingRadius = 60.0f; 

	private List<Particle> particles = new List<Particle>(); 
	private Vector2[] positions; 
	private float[] densities; 

	private float volume; 
	private float radiusSq; 

	public override void _Ready()
	{
		screenRect = GetViewportRect(); 
		spawnArea = new Rect2(screenRect.Size.X / 2.0f - 150.0f, screenRect.Size.Y / 2.0f, 300.0f, 300.0f); 

		positions = new Vector2[ParticleCount]; 
		densities = new float[ParticleCount]; 
		
		radiusSq = SmoothingRadius * SmoothingRadius; 
		volume = (float) ((Math.PI * Math.Pow(SmoothingRadius, 8)) / 4.0f); 
		
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

		for (int i = 0; i < ParticleCount; i++) 
		{
			particles[i].Density = densities[i]; 
		}
	}

	public float SmoothingFunction(float dstSq) 
	{
		if (dstSq >= radiusSq) return 0; 

		float value = radiusSq - dstSq; 
		
		return (value * value * value) / volume; 
	}

	public float CalculateDensity(Vector2 samplePoint) 
	{
		float density = 0.0f; 
		const float mass = 1.0f; 

		for (int i = 0; i < ParticleCount; i++) 
		{
			float dstSq = (positions[i] - samplePoint).LengthSquared(); 
			float influence = SmoothingFunction(dstSq); 
			density += mass * influence; 
		}

		return density; 
	}
}
