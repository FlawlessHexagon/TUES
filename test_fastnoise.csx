using Godot;
var noise = new FastNoiseLite();
noise.Seed = 123;
GD.Print(noise.GetNoise2D(0, 0));
