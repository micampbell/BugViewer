// ***********************************************************************
// Assembly         : TessellationAndVoxelizationGeometryLibrary
// Author           : matth
// Created          : 04-03-2023
//
// Last Modified By : matth
// Last Modified On : 04-03-2023
// ***********************************************************************
// <copyright file="MinimumSphere.cs" company="Design Engineering Lab">
//     2014
// </copyright>
// <summary></summary>
// ***********************************************************************
using System.Numerics;

internal readonly struct Sphere
{
    internal readonly Vector3 Center;
    internal readonly float RadiusSquared;
    internal readonly float GetRadius() => MathF.Sqrt(RadiusSquared);
    internal Sphere(Vector3 center, float radiusSquared)
    {
        Center = center;
        RadiusSquared = radiusSquared;
    }
    internal static bool AContainsB(Sphere a, Sphere b)
    {
        var centerDistSqd = (a.Center - b.Center).LengthSquared();
        return centerDistSqd + b.RadiusSquared <= a.RadiusSquared;
    }
    internal static bool OnSurface(Sphere a, Vector3 point)
    {
        var centerDistSqd = (a.Center - point).LengthSquared();
        var error = 1e-4 * a.RadiusSquared;
        return Math.Abs(centerDistSqd - a.RadiusSquared) < error;
    }
    internal static bool IsPracticallySame(Sphere a, Sphere b)
    {
        var error = 1e-4 * (a.RadiusSquared + b.RadiusSquared);
        var centerDistSqd = (a.Center - b.Center).LengthSquared();
        if (centerDistSqd > error)
            return false;
        return Math.Abs(b.RadiusSquared - a.RadiusSquared) < error;
    }

}
/// <summary>
/// The MinimumEnclosure class includes static functions for defining smallest enclosures for a
/// tessellated solid. For example: convex hull, minimum bounding box, or minimum bounding sphere.
/// </summary>
internal static class MinimumSphere
{
    const float sphereRadiusError = 1.001f;
    internal static Sphere Run(this IEnumerable<Vector3> pointsInput)
    {
        var points = pointsInput.ToArray();
        var numPoints = points.Length;
        var maxNumStalledIterations = 16;
        if (numPoints == 0)
            return new(Vector3.Zero, 5);
        if (numPoints == 1)
            return new(points[0], 5);
        else if (numPoints == 2)
            return CreateFrom2Points(points[0], points[1]);
        else if (numPoints == 3)
            return FirstSphereWith3Points(points[0], points[1], points[2]);
        var sphere = FirstSphere(points);
        var startIndex = 4;
        var maxDistSqared = sphere.RadiusSquared;
        bool newPointFoundOutsideSphere;
        var stallCounter = 0;
        var indexOfMaxDist = -1;
        var lastIndex = -1;
        do
        {
            newPointFoundOutsideSphere = false;
            for (int i = startIndex; i < numPoints; i++)
            {
                var dist = (points[i] - sphere.Center).LengthSquared();

                if (dist > maxDistSqared)
                {
                    maxDistSqared = dist;
                    indexOfMaxDist = i;
                    newPointFoundOutsideSphere = true;
                }
            }
            if (indexOfMaxDist == lastIndex) stallCounter++;
            else stallCounter = 0;
            if (newPointFoundOutsideSphere)
            {
                var maxPoint = points[indexOfMaxDist];
                Array.Copy(points, 0, points, 1, indexOfMaxDist);
                points[0] = maxPoint;
                sphere = FindSphere(points);
                maxDistSqared = sphereRadiusError * sphere.RadiusSquared;
                startIndex = 5;
                lastIndex = indexOfMaxDist;
            }
        } while (newPointFoundOutsideSphere && stallCounter < maxNumStalledIterations);
        return sphere;
    }

    private static Sphere FirstSphereWith3Points(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        // 0,1 & check 2
        var sphere = CreateFrom2Points(p0, p1);
        if (PointIsInSphere(p2, sphere))
            return sphere;
        // 0,2 & check 1 
        sphere = CreateFrom2Points(p0, p2);
        if (PointIsInSphere(p1, sphere))
            return sphere;
        // 1,2 & check 0 
        sphere = CreateFrom2Points(p1, p2);
        if (PointIsInSphere(p0, sphere))
            return sphere;
        return CreateFrom3Points(p0, p1, p2);
    }

    /// <summary>
    /// Create the smallest sphere from the first four *unordered* points.
    /// This is needed because the main loop will simply call FindSphere 
    /// which makes the assumption that the zeroth point was outside the last sphere
    /// and, hence, must be included in the new sphere.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <returns>A Sphere.</returns>
    private static Sphere FirstSphere(Vector3[] points)
    {
        // first check diametrically opposing points
        // 0,1 & check 2 & 3
        var sphere = CreateFrom2Points(points[0], points[1]);
        if (PointIsInSphere(points[2], sphere) && PointIsInSphere(points[3], sphere))
            return sphere;
        // 0,2 & check 1 & 3
        sphere = CreateFrom2Points(points[0], points[2]);
        if (PointIsInSphere(points[1], sphere) && PointIsInSphere(points[3], sphere))
            return sphere;
        // 0,3 & check 1 & 2
        sphere = CreateFrom2Points(points[0], points[3]);
        if (PointIsInSphere(points[1], sphere) && PointIsInSphere(points[2], sphere))
            return sphere;
        // 1,2 & check 0 & 3
        sphere = CreateFrom2Points(points[1], points[2]);
        if (PointIsInSphere(points[0], sphere) && PointIsInSphere(points[3], sphere))
            return sphere;
        // 1,3 & check 0 & 2
        sphere = CreateFrom2Points(points[1], points[3]);
        if (PointIsInSphere(points[0], sphere) && PointIsInSphere(points[2], sphere))
            return sphere;
        // 2,3 & check 0 & 1
        sphere = CreateFrom2Points(points[2], points[3]);
        if (PointIsInSphere(points[0], sphere) && PointIsInSphere(points[1], sphere))
            return sphere;

        sphere = default;
        var minRadiusSqd = float.PositiveInfinity;
        // now check 3-point spheres. here we need to find the smallest sphere! this wasn't the
        // case for the above diametrically opposed points (since the two defining points of the sphere
        // are farthest apart, but we need to check this here as 3 nearly collinear points will make
        // a huge sphere
        // 0,1,2 & check 3
        var tempSphere3 = CreateFrom3Points(points[0], points[1], points[2]);
        if (PointIsInSphere(points[3], tempSphere3))
        {
            sphere = tempSphere3;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,1,3 & check 2
        var swap3And2 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[1], points[3]);
        if (PointIsInSphere(points[2], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap3And2 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,2,3 & check 1
        var swap3And1 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[2], points[3]);
        if (PointIsInSphere(points[1], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap3And1 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 1,2,3 & check 0
        var swap3And0 = false;
        tempSphere3 = CreateFrom3Points(points[1], points[2], points[3]);
        if (PointIsInSphere(points[0], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap3And0 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        var fourPointIsBest = false;
        var tempSphere4 = CreateFrom4Points(points[0], points[1], points[2], points[3]);
        if (tempSphere4.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere4;
            fourPointIsBest = true;
        }
        if (!fourPointIsBest)
        {
            if (swap3And0) SwapItemsInList(3, 0, points);
            else if (swap3And1) SwapItemsInList(3, 1, points);
            else if (swap3And2) SwapItemsInList(3, 2, points);
        }
        if (sphere.RadiusSquared == 0.0)
            sphere = float.IsNaN(tempSphere3.RadiusSquared) || float.IsInfinity(tempSphere3.RadiusSquared)
                ? tempSphere4 : tempSphere3;
        return sphere;
    }

    private static Sphere FindSphere(Vector3[] points)
    {
        // first check diametrically opposing points: the good news is that the zeroth
        // point must be in the set of points defining the sphere. The bad news is that
        // we need to include cases up to 5 points!
        // 0,1 & check 2, 3 & 4
        var sphere = CreateFrom2Points(points[0], points[1]);
        if (PointIsInSphere(points[2], sphere) && PointIsInSphere(points[3], sphere) && PointIsInSphere(points[4], sphere))
            return sphere;
        // 0,2 & check 1, 3 & 4
        sphere = CreateFrom2Points(points[0], points[2]);
        if (PointIsInSphere(points[1], sphere) && PointIsInSphere(points[3], sphere) && PointIsInSphere(points[4], sphere))
            return sphere;
        // 0,3 & check 1, 2 & 4
        sphere = CreateFrom2Points(points[0], points[3]);
        if (PointIsInSphere(points[1], sphere) && PointIsInSphere(points[2], sphere) && PointIsInSphere(points[4], sphere))
            return sphere;
        // 0,4 & check 1, 2 & 3
        sphere = CreateFrom2Points(points[0], points[4]);
        if (PointIsInSphere(points[1], sphere) && PointIsInSphere(points[2], sphere) && PointIsInSphere(points[3], sphere))
            return sphere;

        sphere = default;
        var minRadiusSqd = float.PositiveInfinity;
        // now check 3-point spheres. here we need to find the smallest sphere! this wasn't the
        // case for the above diametrically opposed points (since the two defining points of the sphere
        // are farthest apart, but we need to check this here as 3 nearly collinear points will make
        // a huge sphere
        // 0,1,2 & check 3 & 4
        var tempSphere3 = CreateFrom3Points(points[0], points[1], points[2]);
        if (PointIsInSphere(points[3], tempSphere3) && PointIsInSphere(points[4], tempSphere3))
        {
            sphere = tempSphere3;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,1,3 & check 2 & 4
        var swap3And2 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[1], points[3]);
        if (PointIsInSphere(points[2], tempSphere3) && PointIsInSphere(points[4], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap3And2 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,1,4 & check 2 & 3
        var swap4And2 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[1], points[4]);
        if (PointIsInSphere(points[2], tempSphere3) && PointIsInSphere(points[3], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap4And2 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,2,3 & check 1 & 4
        var swap3And1 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[2], points[3]);
        if (PointIsInSphere(points[1], tempSphere3) && PointIsInSphere(points[4], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap3And1 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,2,4 & check 1 & 3
        var swap1With4 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[2], points[4]);
        if (PointIsInSphere(points[1], tempSphere3) && PointIsInSphere(points[3], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap1With4 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,3,4 & check 1 & 2
        var swap12With34 = false;
        tempSphere3 = CreateFrom3Points(points[0], points[3], points[4]);
        if (PointIsInSphere(points[1], tempSphere3) && PointIsInSphere(points[2], tempSphere3)
            && tempSphere3.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere3;
            swap12With34 = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // now the 4-point spheres
        var fourPointIsBest = false;
        // 0,1,2,3 & check 4
        var tempSphere4 = CreateFrom4Points(points[0], points[1], points[2], points[3]);
        if (PointIsInSphere(points[4], tempSphere4) && tempSphere4.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere4;
            fourPointIsBest = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,1,2,4 & check 3
        var swap4And3 = false;
        tempSphere4 = CreateFrom4Points(points[0], points[1], points[2], points[4]);
        if (PointIsInSphere(points[3], tempSphere4) && tempSphere4.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere4;
            swap4And3 = fourPointIsBest = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,1,3,4 & check 2
        var swap4With2 = false;
        tempSphere4 = CreateFrom4Points(points[0], points[1], points[3], points[4]);
        if (PointIsInSphere(points[2], tempSphere4) && tempSphere4.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere4;
            swap4With2 = fourPointIsBest = true;
            minRadiusSqd = sphere.RadiusSquared;
        }
        // 0,2,3,4 & check 1
        var swap4With1 = false;
        tempSphere4 = CreateFrom4Points(points[0], points[2], points[3], points[4]);
        if (PointIsInSphere(points[1], tempSphere4) && tempSphere4.RadiusSquared < minRadiusSqd)
        {
            sphere = tempSphere4;
            swap4With1 = fourPointIsBest = true;
            //minRadiusSqd = sphere.RadiusSquared;
            // don't need this anymore...no more checks
        }

        if (fourPointIsBest)
        {
            if (swap4With1) SwapItemsInList(4, 1, points);
            else if (swap4With2) SwapItemsInList(4, 2, points);
            else if (swap4And3) SwapItemsInList(4, 3, points);
        }
        else
        {
            if (swap12With34)
            {
                SwapItemsInList(3, 1, points);
                SwapItemsInList(4, 2, points);
            }
            else if (swap1With4)
                SwapItemsInList(1, 4, points);
            else if (swap3And1) SwapItemsInList(1, 3, points);
            else if (swap4And2) SwapItemsInList(4, 2, points);
            else if (swap3And2) SwapItemsInList(3, 2, points);
        }
        if (sphere.RadiusSquared == 0.0)
            sphere = float.IsNaN(tempSphere3.RadiusSquared) || float.IsInfinity(tempSphere3.RadiusSquared)
                ? tempSphere4 : tempSphere3;
        return sphere;
    }

    private static bool PointIsInSphere(Vector3 point, Sphere s)
        => (point - s.Center).LengthSquared() <= sphereRadiusError * s.RadiusSquared;



    private static Sphere CreateFrom4Points(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        /* see details in Sphere version on this function */
        var x = Solve3x3(
            2 * (p2.X - p1.X), 2 * (p2.Y - p1.Y), 2 * (p2.Z - p1.Z),
            2 * (p3.X - p2.X), 2 * (p3.Y - p2.Y), 2 * (p3.Z - p2.Z),
            2 * (p4.X - p3.X), 2 * (p4.Y - p3.Y), 2 * (p4.Z - p3.Z),
            p2.X * p2.X - p1.X * p1.X + p2.Y * p2.Y - p1.Y * p1.Y + p2.Z * p2.Z - p1.Z * p1.Z,
            p3.X * p3.X - p2.X * p2.X + p3.Y * p3.Y - p2.Y * p2.Y + p3.Z * p3.Z - p2.Z * p2.Z,
            p4.X * p4.X - p3.X * p3.X + p4.Y * p4.Y - p3.Y * p3.Y + p4.Z * p4.Z - p3.Z * p3.Z);
        var center = new Vector3(x.x1, x.x2, x.x3);
        return new Sphere(center, (p1 - center).LengthSquared());
    }

    private static Sphere CreateFrom3Points(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var n1 = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p1));
        var midPoint1 = Vector3.Multiply(0.5f, p1 + p2);
        var d1 = Vector3.Dot(n1, midPoint1);
        var n2 = Vector3.Normalize(p2 - p1);
        var d2 = Vector3.Dot(n2, midPoint1);
        var midPoint2 = Vector3.Multiply(0.5f, p1 + p3);
        var n3 = Vector3.Normalize(p3 - p1);
        var d3 = Vector3.Dot(n3, midPoint2);
        var x = Solve3x3(n1.X, n1.Y, n1.Z, n2.X, n2.Y, n2.Z, n3.X, n3.Y, n3.Z, d1, d2, d3);
        var center = new Vector3(x.x1, x.x2, x.x3);
        return new Sphere(center, Vector3.DistanceSquared(p1, center));
    }
    private static Sphere CreateFrom2Points(Vector3 p1, Vector3 p2)
    {
        var center = Vector3.Multiply(0.5f, (p1 + p2));
        return new Sphere(center, (p1 - center).LengthSquared());
    }
    private static void SwapItemsInList<T>(int i, int j, IList<T> points)
    {
        var temp = points[i];
        points[i] = points[j];
        points[j] = temp;
    }
    private static (float x1, float x2, float x3) Solve3x3(
        float a11, float a12, float a13,
        float a21, float a22, float a23,
        float a31, float a32, float a33,
        float b1, float b2, float b3)
    {
        // Determinant of A
        float detA =
            a11 * (a22 * a33 - a23 * a32) -
            a12 * (a21 * a33 - a23 * a31) +
            a13 * (a21 * a32 - a22 * a31);

        // Precompute cofactors for efficiency
        float c11 = (a22 * a33 - a23 * a32);
        float c12 = (a21 * a33 - a23 * a31);
        float c13 = (a21 * a32 - a22 * a31);

        float c21 = (a12 * a33 - a13 * a32);
        float c22 = (a11 * a33 - a13 * a31);
        float c23 = (a11 * a32 - a12 * a31);

        float c31 = (a12 * a23 - a13 * a22);
        float c32 = (a11 * a23 - a13 * a21);
        float c33 = (a11 * a22 - a12 * a21);

        // Determinants for Cramer's rule
        float detX1 = b1 * c11 - b2 * c21 + b3 * c31;
        float detX2 = -(b1 * c12 - b2 * c22 + b3 * c32);
        float detX3 = b1 * c13 - b2 * c23 + b3 * c33;

        // Single division at the end
        float invDetA = 1f / detA;

        return (detX1 * invDetA, detX2 * invDetA, detX3 * invDetA);
    }

}
