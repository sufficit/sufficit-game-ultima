namespace Ultima.Models.Core;

/// <summary>Quadros de matrizes de skinning, com uma matriz de identidade ao final de cada paleta.</summary>
public sealed record SkeletalClip(int Version, int Frames, int Fps, int Bones, float[] Matrices)
{
    public static SkeletalClip BakeWalk(double hip, double shoulder)
    {
        const int frames = 32, bones = 5;
        const int palette = (bones + 1) * 16;
        var matrices = new float[frames * palette];
        for (var frame = 0; frame < frames; frame++)
        {
            var stride = Math.Sin(frame * (2 * Math.PI / frames));
            var row = frame * palette;
            Write(row, 0, 0);
            Write(row + 16, hip, stride * 0.28);
            Write(row + 32, hip, -stride * 0.28);
            Write(row + 48, shoulder, -stride * 0.18);
            Write(row + 64, shoulder, stride * 0.18);
            Write(row + 80, 0, 0);
        }
        return new SkeletalClip(1, frames, 32, bones, matrices);

        void Write(int offset, double pivot, double angle)
        {
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            matrices[offset] = matrices[offset + 15] = 1;
            matrices[offset + 5] = matrices[offset + 10] = (float)cos;
            matrices[offset + 6] = (float)sin;
            matrices[offset + 9] = (float)-sin;
            matrices[offset + 13] = (float)(pivot * (1 - cos));
            matrices[offset + 14] = (float)(-pivot * sin);
        }
    }
}
