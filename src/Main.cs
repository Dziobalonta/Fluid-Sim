using Godot;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;


public partial class Main : Node2D
{
	#region Variables
	[Export] public int ParticleCount = 300; 
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

	private float _printTimer = 0f;
	// Grid Optimization Variables
	private Entry[] spatialLookup;
	private int[] startIndices;

	private int[] counts;
	private Entry[] sortBuffer;

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
	#endregion

	#region Game Loops
	public override void _Ready()
	{
		screenRect = GetViewportRect(); 
		float padding = 20.0f; 

		spawnArea = new Rect2(
			padding,
			padding,
			screenRect.Size.X - (padding * 2),
			screenRect.Size.Y - (padding * 2)
		);

		positions = new Vector2[ParticleCount]; 
		densities = new float[ParticleCount];

		spatialLookup = new Entry[ParticleCount];
		startIndices = new int[ParticleCount];

		counts = new int[ParticleCount];
		sortBuffer = new Entry[ParticleCount];
		
		// Pre-calculating once a part of Smooting Function's equation
		volume = (float) ((Math.PI * Math.Pow(SmoothingRadius, 4)) / 6.0f);
		scale = (float) (12 / (Math.Pow(SmoothingRadius,4)) * Math.PI);

		// Spawn all particles
		for (int i = 0; i < ParticleCount; i++)
		{
			Particle p = new Particle();
			
			Random rand = Random.Shared;

			float randX = spawnArea.Position.X + (float)rand.NextDouble() * spawnArea.Size.X;
			float randY = spawnArea.Position.Y + (float)rand.NextDouble() * spawnArea.Size.Y;
			
			positions[i] = new Vector2(randX, randY);

			particles.Add(p); 
		}
		
	}

    public override void _Process(double delta)
    {
		_printTimer += (float)delta;
		if (_printTimer >= 1f)
		{
			GD.Print(Engine.GetFramesPerSecond());
			_printTimer = 0f;
		}
    }


	public override void _PhysicsProcess(double delta)
	{
		// Updating Spatial Grid BEFORE physics calculations
		UpdateSpatialLookup();
		
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
			positions[i] += particles[i].Velocity * (float)delta;
        	ResolveWallCollision(ref positions[i], ref particles[i].Velocity, particles[i].Radius, particles[i].Damping);
		}

		QueueRedraw();
	}
	public override void _Draw()
	{
		for (int i = 0; i < ParticleCount; i++)
		{
			float normalized = Math.Clamp(densities[i] / 20f, 0f, 1f);
			Color c = ParticleGardient?.Sample(normalized) ?? Colors.WhiteSmoke;
			DrawCircle(positions[i], 7f, c);
		}
	}
	#endregion

	private void ResolveWallCollision(ref Vector2 pos, ref Vector2 vel, float radius, float damping)
	{
		// Right Wall
		if (pos.X > screenRect.End.X - radius) {

			pos.X = screenRect.End.X - radius; vel.X *= -damping;
		}
		// Left Wall
		else if (pos.X < screenRect.Position.X + radius) {

			pos.X = screenRect.Position.X + radius; vel.X *= -damping;
		}
		// Bottom
		if (pos.Y > screenRect.End.Y - radius) {
			
			pos.Y = screenRect.End.Y - radius; vel.Y *= -damping;	
		}
		// Top
		else if (pos.Y < screenRect.Position.Y + radius)
		{
			pos.Y = screenRect.Position.Y + radius; vel.Y *= -damping;
		}
	}

	#region Smoothing Functions
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
	#endregion

	#region Density and Pressure
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

			float sqrDst = offset.LengthSquared();

			if (sqrDst > 0)
			{
				float dst = (float)Math.Sqrt(sqrDst);
				Vector2 dir = offset / dst;
				float slope = SmoothingFunctionDerivative(dst, SmoothingRadius);
				float density = densities[otherPart];
				float sharedPressure = CalculateSharedPressure(density, densities[particleIndex]);
				pressureForce += sharedPressure * dir * slope * mass / density;
			}
			else
			{
				Vector2 dir = GetRandomDir();
				float slope = SmoothingFunctionDerivative(0, SmoothingRadius);
				float density = densities[otherPart];
				float sharedPressure = CalculateSharedPressure(density, densities[particleIndex]);
				pressureForce += sharedPressure * dir * slope * mass / density;
			}
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

	#endregion

	Vector2 GetRandomDir()
	{
		return Vector2.FromAngle((float)Random.Shared.NextDouble() * (float) Math.Tau);
	}

	#region Grid Optimization
	public void UpdateSpatialLookup()
	{

		Parallel.For(0, ParticleCount, i =>
		{
			(int cellX, int cellY) = PositionToCellCoord(positions[i], SmoothingRadius);
			uint cellKey = GetKeyFromHash(HashCell(cellX, cellY));
			spatialLookup[i] = new Entry(i, cellKey);
		});

		Array.Clear(counts, 0, ParticleCount);

		// Count entries per key
		for (int i = 0; i < ParticleCount; i++)
			counts[spatialLookup[i].CellKey]++;


		// Empty keys get int.MaxValue so the caller can skip them cheaply
		int total = 0;
		for (int key = 0; key < ParticleCount; key++)
		{
			startIndices[key] = counts[key] > 0 ? total : int.MaxValue;
			int c = counts[key];
			counts[key] = total;
			total += c;
		}

		// Scatter into sorted order — no comparison needed
		for (int i = 0; i < ParticleCount; i++)
			sortBuffer[counts[spatialLookup[i].CellKey]++] = spatialLookup[i];

		// Swap buffers
		(spatialLookup, sortBuffer) = (sortBuffer, spatialLookup);

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
