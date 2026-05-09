using Godot;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;


public partial class Main : Node2D
{
	[Export] public int ParticleCount = 200; 
	[Export] public float mass = 1.0f;
	[Export] public Gradient ParticleGardient;

	private Rect2 screenRect;
	private Rect2 spawnArea;

	public float SmoothingRadius = 45.0f; 

	private List<Particle> particles = new List<Particle>(); 
	private Vector2[] positions; 
	private float[] densities; 

	private float volume; 
	private float scale;

	private float targetDensity = 1.0f;
	private float pressureMultiplier = 10.0f;

	// Grid Optimization Variables
	private Entry[] spatialLookup;
	private int[] startIndices;
	// The 9 neighbor cells (including the center cell) to search
	private readonly (int x, int y)[] cellOffsets = {
		(-1, 1), (0, 1), (1, 1),
		(-1, 0), (0, 0), (1, 0),
		(-1, -1), (0, -1), (1, -1)
	};

	// Struct to hold particle index and its cell hash key
	public struct Entry : IComparable<Entry>
	{
		public int ParticleIndex;
		public uint CellKey;

		public Entry(int index, uint key)
		{
			ParticleIndex = index;
			CellKey = key;
		}

		// Sort by CellKey so particles in the same cell are contiguous in memory
		public int CompareTo(Entry other)
		{
			return CellKey.CompareTo(other.CellKey);
		}
	}
	public override void _Ready()
	{
		screenRect = GetViewportRect(); 
		spawnArea = new Rect2(screenRect.Size.X / 2.0f - 150.0f, screenRect.Size.Y / 2.0f, 300.0f, 300.0f); 

		positions = new Vector2[ParticleCount]; 
		densities = new float[ParticleCount];

		spatialLookup = new Entry[ParticleCount];
		startIndices = new int[ParticleCount];
		
		// Pre-calculating once a part of Smooting Function's equation
		volume = (float) ((Math.PI * Math.Pow(SmoothingRadius, 4)) / 6.0f);
		scale = (float) (12 / (Math.Pow(SmoothingRadius,4)) * Math.PI);

		// Spawn all particles
		for (int i = 0; i < ParticleCount; i++)
		{
			Particle p = new Particle();
			p.DensityGradient = ParticleGardient;
			
			float randX = (float)GD.RandRange(spawnArea.Position.X, spawnArea.End.X); 
			float randY = (float)GD.RandRange(spawnArea.Position.Y, spawnArea.End.Y); 
			
			p.Position = new Vector2(randX, randY); 
			p.SetBoundary(screenRect); 
			
			AddChild(p); 
			particles.Add(p); 
			
			positions[i] = p.Position;
		}
		
	}

	public override void _PhysicsProcess(double delta)
	{

		for (int i = 0; i < ParticleCount; i++) 
		{
			positions[i] = particles[i].Position; 
		}

		// Updating Spatial Grid BEFORE physics calculations
		UpdateSpacialLookup();
		
		// Caslculating physics
		Parallel.For(0, ParticleCount, i => 
		{
			densities[i] = CalculateDensity(positions[i]); 
		});

		Parallel.For(0, ParticleCount, i => 
		{
			Vector2 pressureForce = ConvertDensityToPressure(i);
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

	float sqrRadius = SmoothingRadius * SmoothingRadius;
	(int centerX, int centerY) = PositionToCellCoord(samplePoint, SmoothingRadius);

		// Only check that 9 neighbor cells
		foreach ((int offsetX, int offsetY) in cellOffsets)
		{
			uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
			int cellStartIndex = startIndices[key];

			if (cellStartIndex == int.MaxValue) continue; // Cell is empty

			for (int i = cellStartIndex; i < spatialLookup.Length; i++)
			{
				if (spatialLookup[i].CellKey != key) break; // Scanned all of this cells particles

				int particleIndex = spatialLookup[i].ParticleIndex;
				float sqrDst = (positions[particleIndex] - samplePoint).LengthSquared();

				if (sqrDst <= sqrRadius)
				{
					float dst = (float) Math.Sqrt(sqrDst);
					float influence = SmoothingFunction(dst, SmoothingRadius); 
					density += mass * influence; 
				}
			}
		}

		return density; 
	}

	public Vector2 ConvertDensityToPressure(int particleIndex)
	{
		Vector2 pressureForce = Vector2.Zero;
		Vector2 samplePoint = positions[particleIndex];
		float sqrRadius = SmoothingRadius * SmoothingRadius;
		(int centerX, int centerY) = PositionToCellCoord(samplePoint, SmoothingRadius);

		// Only check the 9 neighboring cells
		foreach ((int offsetX, int offsetY) in cellOffsets)
		{
			uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
			int cellStartIndex = startIndices[key];

			if (cellStartIndex == int.MaxValue) continue; // Cell is empty

			for (int i = cellStartIndex; i < spatialLookup.Length; i++)
			{
				if (spatialLookup[i].CellKey != key) break;

				int otherPart = spatialLookup[i].ParticleIndex;
				if (particleIndex == otherPart) continue;

				Vector2 offset = positions[otherPart] - positions[particleIndex];
				float sqrDst = offset.LengthSquared();

				if (sqrDst <= sqrRadius)
				{
					float dst = (float) Math.Sqrt(sqrDst);
					Vector2 dir = (dst == 0) ? GetRandomDir() : offset / dst;
					float slope = SmoothingFunctionDerivative(dst, SmoothingRadius);
					float density = densities[otherPart];
					float sharedPressure = CalculateSharedPressure(density, densities[particleIndex]);
					pressureForce += sharedPressure * dir * slope * mass / density;
				}
			}
		}
		return pressureForce;
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
			float sharedPressure = CalculateSharedPressure(density, densities[particleIndex]);
			pressureForce += sharedPressure * dir * slope * mass / density;

		}

		return pressureForce;
	}

	public float ConvertDensityToPressure(float density)
	{
		float densityError = density - targetDensity;
		float pressure = -densityError * pressureMultiplier;
		return pressure;
	}

	// Shared pressure method to simulate 3rd law of Motion
	float CalculateSharedPressure(float densityA, float densityB)
	{
		float pressureA = ConvertDensityToPressure(densityA);
		float pressureB = ConvertDensityToPressure(densityB);

		return (pressureA + pressureB) / 2; 
	}

	Vector2 GetRandomDir()
	{
		return Vector2.FromAngle((float)Random.Shared.NextDouble() * (float) Math.Tau);
	}

	#region Grid Optimization
	public void UpdateSpacialLookup()
	{

		Parallel.For(0, ParticleCount, i =>
		{
			(int cellX, int cellY) = PositionToCellCoord(positions[i], SmoothingRadius);
			uint cellKey = GetKeyFromHash(HashCell(cellX, cellY));
			spatialLookup[i] = new Entry(i, cellKey);
			startIndices[i] = int.MaxValue; // reset start index
		});

		Array.Sort(spatialLookup);
		
		// Calculate start indices for each unique cell key in the spacial lookup
		Parallel.For(0, ParticleCount, i =>
		{
			uint key = spatialLookup[i].CellKey;
			uint keyPrev = i == 0 ? uint.MaxValue : spatialLookup[i-1].CellKey;
			if (key != keyPrev)
			{
				startIndices[key] = i;
			}
		});
	}

	// Convert position of the cell to the coordinates
	public (int x, int y) PositionToCellCoord(Vector2 point, float radius)
	{
		int cellX = (int)(point.X / radius);
		int cellY = (int)(point.Y / radius);
		return (cellX, cellY);
	}

	// Converting cells coordinates to single number
	// Hash collisions will occur, but it is not significant
	public uint HashCell(int cellX, int cellY)
	{
		uint a = (uint) cellX * 15823;
		uint b = (uint) cellY * 9737333;
		return a + b;
	}

	// Reducing the hash values to the length of the Array
	public uint GetKeyFromHash(uint hash)
	{
		return hash % (uint)spatialLookup.Length;
	}
	#endregion
}
