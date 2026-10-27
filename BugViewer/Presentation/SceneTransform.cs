using System.Numerics;

namespace BugViewer;

/// <summary>A row-vector 4x4 transform with double precision. A nullable transform represents a step barrier.</summary>
public readonly record struct SceneTransform(
    double M11, double M12, double M13, double M14,
    double M21, double M22, double M23, double M24,
    double M31, double M32, double M33, double M34,
    double M41, double M42, double M43, double M44)
{
    /// <summary>The identity transform.</summary>
    public static SceneTransform Identity => new(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);

    /// <summary>Applies the transform in double precision and returns float coordinates for rendering.</summary>
    public Vector3 Transform(ScenePoint point)
    {
        var w = point.X * M14 + point.Y * M24 + point.Z * M34 + M44;
        return new((float)((point.X * M11 + point.Y * M21 + point.Z * M31 + M41) / w),
            (float)((point.X * M12 + point.Y * M22 + point.Z * M32 + M42) / w),
            (float)((point.X * M13 + point.Y * M23 + point.Z * M33 + M43) / w));
    }

    internal double[] Values => [M11,M12,M13,M14, M21,M22,M23,M24, M31,M32,M33,M34, M41,M42,M43,M44];

    // Compute once per mesh, before transforming its normals. Partial pivoting handles
    // rotations, nonuniform scales, shear, and projective matrices without a TVGL dependency.
    internal bool TryNormalTransform(out SceneTransform normalTransform)
    {
        Span<double> rows = stackalloc double[32];
        rows.Clear();
        var values = Values;
        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 4; col++) rows[row * 8 + col] = values[row * 4 + col];
            rows[row * 8 + 4 + row] = 1;
        }
        for (var col = 0; col < 4; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < 4; row++)
                if (Math.Abs(rows[row * 8 + col]) > Math.Abs(rows[pivot * 8 + col])) pivot = row;
            if (rows[pivot * 8 + col] == 0 || !double.IsFinite(rows[pivot * 8 + col]))
            {
                normalTransform = default;
                return false;
            }
            for (var j = 0; j < 8; j++)
                (rows[col * 8 + j], rows[pivot * 8 + j]) = (rows[pivot * 8 + j], rows[col * 8 + j]);
            var divisor = rows[col * 8 + col];
            for (var j = 0; j < 8; j++) rows[col * 8 + j] /= divisor;
            for (var row = 0; row < 4; row++)
            {
                if (row == col) continue;
                var factor = rows[row * 8 + col];
                for (var j = 0; j < 8; j++) rows[row * 8 + j] -= factor * rows[col * 8 + j];
            }
        }
        normalTransform = new(rows[4],rows[12],rows[20],rows[28],
            rows[5],rows[13],rows[21],rows[29], rows[6],rows[14],rows[22],rows[30],
            rows[7],rows[15],rows[23],rows[31]);
        return normalTransform.Values.All(double.IsFinite);
    }

    internal Vector3 TransformNormal(float[] normal)
    {
        var x = normal[0] * M11 + normal[1] * M21 + normal[2] * M31;
        var y = normal[0] * M12 + normal[1] * M22 + normal[2] * M32;
        var z = normal[0] * M13 + normal[1] * M23 + normal[2] * M33;
        var length = Math.Sqrt(x*x + y*y + z*z);
        return !double.IsFinite(length) || length <= 1e-10 ? Vector3.Zero
            : new((float)(x/length), (float)(y/length), (float)(z/length));
    }
}
