using System;
using System.Collections.Generic;
using System.Text;

namespace BugViewer
{
    /// <summary>
    /// Cardinal directions for camera positioning.
    /// </summary>
    public enum CardinalDirection
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }


    /// <summary>
    /// Specifies how a mesh should be colored.
    /// </summary>
    public enum MeshColoring
    {
        /// <summary>Colors the entire mesh with a single uniform color.</summary>
        UniformColor,
        /// <summary>Assigns a color to each vertex of the mesh.</summary>
        PerVertex,
        /// <summary>Assigns a color to each triangle of the mesh.</summary>
        PerTriangle
    }

    /// <summary>
    /// Specifies how mesh faces are displayed.
    /// </summary>
    public enum MeshFaceDisplay
    {
        /// <summary>Uses one flat normal for each triangle.</summary>
        Triangles,
        /// <summary>Uses interpolated normals supplied by primitive surfaces.</summary>
        Surfaces,
        /// <summary>Hides mesh faces.</summary>
        None
    }

    /// <summary>
    /// Defines when an automatic update should be triggered.
    /// </summary>
    public enum UpdateTypes
    {
        /// <summary>The viewer will not automatically update.</summary>
        Never = 0,
        /// <summary>The viewer will update whenever data is added or removed.</summary>
        OnDataChange = 1,
        /// <summary>The viewer will update only when the bounding sphere changes.</summary>
        SphereChange = 2,
    }
}
