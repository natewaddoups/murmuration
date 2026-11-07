using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Murmuration;

[StructLayout(LayoutKind.Sequential)]
public struct GpuBird
{
    public Vector4 Position;
    public Quaternion Orientation;
    public Vector4 DesiredDirection;
    public Vector4 Flight;
    public uint Climb;
    public float DirectionX;
    public float DirectionY;
    public float DirectionZ;

    public static GpuBird FromBird(in Bird bird) => new()
    {
        Position = new Vector4(bird.Position, 0f),
        Orientation = bird.Orientation,
        DesiredDirection = new Vector4(bird.DesiredDirection, 0f),
        Flight = new Vector4(bird.Speed, bird.PreferredSpeed, bird.Mass, bird.ClimbStrength),
        Climb = (uint)bird.Climb,
        DirectionX = bird.Direction.X,
        DirectionY = bird.Direction.Y,
        DirectionZ = bird.Direction.Z,
    };

    public readonly Bird ToBird() => new()
    {
        Position = new Vector3(Position.X, Position.Y, Position.Z),
        Orientation = Orientation,
        DesiredDirection = new Vector3(DesiredDirection.X, DesiredDirection.Y, DesiredDirection.Z),
        Speed = Flight.X,
        PreferredSpeed = Flight.Y,
        Mass = Flight.Z,
        ClimbStrength = Flight.W,
        Climb = (ClimbDirection)Climb,
    };

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields, typeof(GpuBird))]
    public static string WgslDeclaration()
    {
        var source = new StringBuilder("struct GpuBird {\n");
        foreach (FieldInfo field in typeof(GpuBird).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            string type = field.FieldType == typeof(Vector4) || field.FieldType == typeof(Quaternion)
                ? "vec4<f32>"
                : field.FieldType == typeof(uint) ? "u32"
                : field.FieldType == typeof(float) ? "f32"
                : throw new InvalidOperationException($"Unsupported GPU bird field type: {field.FieldType}.");
            source.Append("    ").Append(field.Name).Append(": ").Append(type).Append(",\n");
        }
        return source.Append("};\n").ToString();
    }
}