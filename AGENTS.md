# Project Overview

Murmuration is a real-time 3D starling flock simulation inspired by Craig Reynolds' Boids. The shared .NET core drives the simulation and rendering; Avalonia browser and desktop apps display it with WebGPU.

## Where Features Live

- `src/Murmuration.Core/Simulation.cs`: bird state, flock behavior, and simulation updates.
- `src/Murmuration.Core/Rendering.cs`: per-frame orchestration and the shared GPU renderer interface.
- `src/Murmuration.Core/Geometry.cs`, `Camera.cs`, and `Shaders/`: bird/world geometry, camera, and WGSL shader.
- `src/Murmuration.Browser/`: browser app startup and WebGPU renderer.
- `src/Murmuration.Desktop/`: desktop app startup, GPU host, and WebGPU renderer.
- `README.md`: project background and published browser demo.