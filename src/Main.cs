using Godot;
using System;
using System.Threading.Tasks;

public partial class Main : Node2D
{
	[ExportGroup("UI Controls")]
	[Export] public SpinBox TargetDSlider;
    [Export] public SpinBox PressureMSlider;
	[Export] public SpinBox NearPressureMSlider;
    [Export] public SpinBox GravitySlider;
    [Export] public SpinBox DampingSlider;
    [Export] public SpinBox SmoothingFunSlider;
	[Export] public SpinBox ViscositySlider;


	#region Variables
	[Export] public int ParticleCount = 3000; 
	[Export] public float mass = 1.0f;
	[Export] public Gradient ParticleGardient;
	public float targetDensity = 2.5f;
	public float pressureMultiplier = 200.0f;
	public float nearPressureMultiplier = 1.5f;
	private Rect2 screenRect;
	private Rect2 spawnArea;

	public float SmoothingRadius = 10.0f; 

	private Vector2[] positions;
	private Vector2[] predictedPositions;
	private float[] densities;
	private float[] nearDensities;
	private Vector2[] velocities;
	private float particleRadius = 4f;
	public float particleDamping = 0.5f;

	private float volume;
	private float nearVolume;

	private float scale;

	public float viscosityStrength = 3.5f;
	
	public float gravity = 350.0f;

	private float printTimer = 0f;
	private float dt;

	private float mouseForceRadius = 125f;
	private float mouseForceStrength = 2500f;

	private Color[] cachedGradient = new Color[256];

	private Vector2[] randomDirs;
	Vector2 GetRandomDir(int seed) => randomDirs[seed & 255]; // faster way of doing seed % 256

	private MultiMesh multiMesh;
	private MultiMeshInstance2D multiMeshInstance;

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
		InitializeUI();

		screenRect = GetViewportRect(); 
		float padding = 20.0f; 

		spawnArea = new Rect2(
			padding,
			padding,
			screenRect.Size.X - (padding * 2),
			screenRect.Size.Y - (padding * 2)
		);

		positions = new Vector2[ParticleCount];
		predictedPositions = new Vector2[ParticleCount];
		velocities = new Vector2[ParticleCount];
		densities = new float[ParticleCount];
		nearDensities = new float[ParticleCount];

		spatialLookup = new Entry[ParticleCount];
		startIndices = new int[ParticleCount];

		counts = new int[ParticleCount];
		sortBuffer = new Entry[ParticleCount];

        multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            InstanceCount = ParticleCount,
            Mesh = new QuadMesh
            {
                Size = new Vector2(particleRadius * 2, particleRadius * 2)  // 2x particle radius - diameter
            }
        };

        multiMeshInstance = new MultiMeshInstance2D {
            Multimesh = multiMesh
        };
        AddChild(multiMeshInstance);

		var shader = GD.Load<Shader>("res://shaders/particle.gdshader");
		var mat = new ShaderMaterial();
		mat.Shader = shader;
		multiMeshInstance.Material = mat;

		// Pre-cache the gradient
		for (int i = 0; i < 256; i++) {
        	cachedGradient[i] = ParticleGardient.Sample(i / 255f);
    	} 
		
		// Pre-calculating once a part of Smoothing Functions equation
		RecalculateSmoothingConstants();

		// Pre-bake random directions
		randomDirs = new Vector2[256];
		for (int i = 0; i < 256; i++)
			randomDirs[i] = Vector2.FromAngle(i / 256f * MathF.Tau);

		// Spawn all particles
		Random rand = Random.Shared;
		for (int i = 0; i < ParticleCount; i++)
		{		
			float randX = spawnArea.Position.X + (float) rand.NextDouble() * spawnArea.Size.X;
			float randY = spawnArea.Position.Y + (float) rand.NextDouble() * spawnArea.Size.Y;
			
			positions[i] = new Vector2(randX, randY);
			velocities[i] = Vector2.Zero;
		}
		
	}

    public override void _Process(double delta)
    {
		printTimer += (float) delta;
		if (printTimer >= 1f)
		{
			GD.Print(Engine.GetFramesPerSecond());
			// float avg = 0f;
			// for (int i = 0; i < ParticleCount; i++) avg += densities[i];
    		// 	GD.Print("avg density: " + avg / ParticleCount);
			printTimer = 0f;
		}
    }


	public override void _PhysicsProcess(double delta)
	{
		dt = (float) delta;

		// Calculate position predictions
		Parallel.For(0, ParticleCount, i =>
		{
			velocities[i].Y += gravity * dt; // Gravity
			predictedPositions[i] = positions[i] + velocities[i] * dt;
		});

		// Updating Spatial Grid BEFORE physics calculations
		UpdateSpatialLookup();
		
		// Calculating physics
		Parallel.For(0, ParticleCount, i => 
		{
			var result = CalculateDensity(predictedPositions[i]);
			
			densities[i] = result.density;
			nearDensities[i] = result.nearDensity;
		});

		Parallel.For(0, ParticleCount, i => 
		{
			if (densities[i] < float.Epsilon) return; // skip if no neighbors
			
			Vector2 pressureForce = ConvertDensityToPressure(i);
			Vector2 pressureAcceleration = pressureForce / densities[i];
			velocities[i] += pressureAcceleration * dt;
		});

		 Parallel.For(0, ParticleCount, i =>
		{
			velocities[i] += CalculateViscosityForce(i) * dt;
		});

		if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right))
		{
			Vector2 mousePos = GetGlobalMousePosition();
			float strength = Input.IsMouseButtonPressed(MouseButton.Left) ? mouseForceStrength : -mouseForceStrength;

			for (int i = 0; i < ParticleCount; i++)
			{
				velocities[i] += InteractionForce(mousePos, mouseForceRadius, strength, i) * dt;
			}
		}

		// Update positions
		for (int i = 0; i < ParticleCount; i++)
		{
			velocities[i] = velocities[i].LimitLength(2000f); // cap max speed

			positions[i] += velocities[i] * dt;
        	ResolveWallCollision(ref positions[i], ref velocities[i], particleRadius, particleDamping);
		}

		for (int i = 0; i < ParticleCount; i++)
		{
			float speed = velocities[i].Length();
			int colorIndex = (int) Math.Clamp((speed / 500f) * 255f, 0, 255);
			multiMesh.SetInstanceTransform2D(i, new Transform2D(0, positions[i]));
			multiMesh.SetInstanceColor(i, cachedGradient[colorIndex]);
		}
	}
	#endregion

	#region Collisions
	Vector2 InteractionForce(Vector2 inputPos, float radius, float strength, int particleIndex)
	{
		Vector2 interactionForce = Vector2.Zero;
		Vector2 offset = inputPos - positions[particleIndex];
		float sqrDst = offset.Dot(offset);

		if (sqrDst < radius * radius)
		{
			float dst =  MathF.Sqrt(sqrDst);
			Vector2 dirToInputPoint = dst <= float.Epsilon ? Vector2.Zero : offset / dst;
			float centreT = 1 - dst / radius;
			interactionForce += (dirToInputPoint * strength - velocities[particleIndex]) * centreT;
		}

		return interactionForce;
	}
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
	#endregion

	#region Smoothing Functions
	public float SmoothingFunction(float dst, float radius) 
	{
		// if (dst >= radius) return 0; // caller already checked
		
		return (radius - dst) * (radius - dst) / volume; 
	}

	public float SmoothingFunctionDerivative(float dst, float radius) 
	{
		// if (dst >= radius) return 0; // caller already checked

		return (dst - radius) * scale;
	}

		public float NearSmoothingFunction(float dst, float radius) 
	{
		float diff = radius - dst;
		return diff * diff * diff / nearVolume; 
	}

	public float NearSmoothingFunctionDerivative(float dst, float radius) 
	{
		float diff = radius - dst;
		// Pochodna z (R-r)^3 to -3(R-r)^2
		return -3.0f * diff * diff / nearVolume;
	}

	public float ViscositySmoothingFunction(float dst, float radius) 
	{
		// if (dst >= radius) return 0; // caller already checked
		
		return (radius - dst) * (radius - dst) / volume; 
	}
	#endregion

	#region Density and Pressure
	public (float density, float nearDensity) CalculateDensity(Vector2 samplePoint) 
	{
		float density = 0.0f;
		float nearDensity = 0.0f;

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
				float sqrDst = (predictedPositions[particleIndex] - samplePoint).LengthSquared();

				if (sqrDst <= sqrRadius)
				{
					float dst =  MathF.Sqrt(sqrDst);
					float influence = SmoothingFunction(dst, SmoothingRadius); 
					density += mass * influence;
					nearDensity += mass * NearSmoothingFunction(dst, SmoothingRadius); // Dodane
				}
			}
		}

		return (density, nearDensity); 
	}

	public Vector2 ConvertDensityToPressure(int particleIndex)
	{
		Vector2 pressureForce = Vector2.Zero;
		Vector2 samplePoint = predictedPositions[particleIndex];
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

				Vector2 offset = predictedPositions[otherPart] - predictedPositions[particleIndex];
				float sqrDst = offset.LengthSquared();

				if (sqrDst <= sqrRadius)
				{
					float dst = MathF.Sqrt(sqrDst);
					Vector2 dir = (dst == 0) ? GetRandomDir(otherPart) : offset / dst;

					float density = densities[otherPart];
					float nearDensity = nearDensities[otherPart];

					// Dividing by zero case
					if (density < float.Epsilon) density = float.Epsilon;
					if (nearDensity < float.Epsilon) nearDensity = float.Epsilon;


					float slope = SmoothingFunctionDerivative(dst, SmoothingRadius);
					float nearSlope = NearSmoothingFunctionDerivative(dst, SmoothingRadius);

					var shared = CalculateSharedPressure(density, nearDensity, densities[particleIndex], nearDensities[particleIndex]);
					
					pressureForce += shared.sharedPressure * dir * slope * mass / density;
					pressureForce += shared.sharedNearPressure * dir * nearSlope * mass / nearDensity;
				}
			}
		}
		return pressureForce;
	}

	public (float pressure, float nearPressure) ConvertDensityToPressure(float density, float nearDensity)
	{
		float densityError = density - targetDensity;
		float pressure = densityError * pressureMultiplier;
		float nearPressure = nearDensity * nearPressureMultiplier * 50000;
		return (pressure, nearPressure);
	}

	// Shared pressure method to simulate 3rd law of Motion
	(float sharedPressure, float sharedNearPressure) CalculateSharedPressure(float densityA, float nearDensityA, float densityB, float nearDensityB)
	{
		var pressureA = ConvertDensityToPressure(densityA, nearDensityA);
		var pressureB = ConvertDensityToPressure(densityB, nearDensityB);

		return (
			(pressureA.pressure + pressureB.pressure) / 2f, 
			(pressureA.nearPressure + pressureB.nearPressure) / 2f
		); 
	}

	public Vector2 CalculateViscosityForce(int particleIndex)
	{
		Vector2 viscosityForce = Vector2.Zero;
		Vector2 samplePoint = predictedPositions[particleIndex];
		float sqrRadius = SmoothingRadius * SmoothingRadius;
		(int centerX, int centerY) = PositionToCellCoord(samplePoint, SmoothingRadius);

		foreach ((int offsetX, int offsetY) in cellOffsets)
		{
			uint key = GetKeyFromHash(HashCell(centerX + offsetX, centerY + offsetY));
			int cellStartIndex = startIndices[key];

			if (cellStartIndex == int.MaxValue) continue;

			for (int i = cellStartIndex; i < spatialLookup.Length; i++)
			{
				if (spatialLookup[i].CellKey != key) break;

				int otherIndex = spatialLookup[i].ParticleIndex;
				if (otherIndex == particleIndex) continue;

				float sqrDst = (predictedPositions[otherIndex] - samplePoint).LengthSquared();

				if (sqrDst <= sqrRadius)
				{
					float dst = MathF.Sqrt(sqrDst);
					float influence = ViscositySmoothingFunction(dst, SmoothingRadius);
					// Get the neighbors density
					float neighborDensity = densities[otherIndex];
					
					// Safety check to prevent dividing by zero
					if (neighborDensity <= float.Epsilon) continue;

					// Multiply by mass and divide by the neighbors density
					viscosityForce += (velocities[otherIndex] - velocities[particleIndex]) * influence * mass / neighborDensity;
				}
			}
		}

		return viscosityForce * viscosityStrength;
	}

	#endregion

	#region Grid Optimization
	public void UpdateSpatialLookup()
	{

		Parallel.For(0, ParticleCount, i =>
		{
			(int cellX, int cellY) = PositionToCellCoord(predictedPositions[i], SmoothingRadius);
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

	#region Updating values via UI
	public void RecalculateSmoothingConstants()
	{
		volume = (MathF.PI * MathF.Pow(SmoothingRadius, 4)) / 6.0f;
		scale  =  12 / (MathF.Pow(SmoothingRadius, 4)) * MathF.PI;
		nearVolume = (MathF.PI * MathF.Pow(SmoothingRadius, 5)) / 10.0f;
	}

	public void InitializeUI()
	{
		if (TargetDSlider != null)
        {
            TargetDSlider.Value = targetDensity;
            TargetDSlider.ValueChanged += (value) => targetDensity = (float)value;
        }

        if (PressureMSlider != null)
        {
            PressureMSlider.Value = pressureMultiplier;
            PressureMSlider.ValueChanged += (value) => pressureMultiplier = (float)value;
        }

		if (NearPressureMSlider != null)
        {
            NearPressureMSlider.Value = nearPressureMultiplier;
            NearPressureMSlider.ValueChanged += (value) => nearPressureMultiplier = (float)value;
        }

        if (GravitySlider != null)
        {
            GravitySlider.Value = gravity;
            GravitySlider.ValueChanged += (value) => gravity = (float)value;
        }

        if (DampingSlider != null)
        {
            DampingSlider.Value = particleDamping;
            DampingSlider.ValueChanged += (value) => particleDamping = (float)value;
        }

        if (SmoothingFunSlider != null)
        {
            SmoothingFunSlider.Value = SmoothingRadius;
            SmoothingFunSlider.ValueChanged += (value) => 
            {
                SmoothingRadius = (float)value;
                RecalculateSmoothingConstants(); // Need to recaculate!
            };
        }
		if (ViscositySlider != null)
        {
            ViscositySlider.Value = viscosityStrength;
            ViscositySlider.ValueChanged += (value) => viscosityStrength = (float)value;
        }
	}
	#endregion
}

