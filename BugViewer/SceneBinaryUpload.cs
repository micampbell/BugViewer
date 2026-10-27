using System.Buffers.Binary;

namespace BugViewer;

// Experimental internal transport; the public viewer continues to use JSON.
internal sealed record BinaryMeshUpload(string Id, int VertexOffset, int VertexCount,
    int ColorOffset, int ColorCount, int NormalOffset, int NormalCount,
    int IndexOffset, int IndexCount, bool Index32, bool SingleColor);
internal sealed record BinaryLineUpload(string Id, int Offset, int Count, float[] Center);
internal sealed record BinarySceneUpload(BinaryMeshUpload[] Meshes, BinaryLineUpload[] Lines, byte[] Bytes)
{
    internal static BinarySceneUpload Create(MeshUpload[] meshes, LineUpload[] lines)
    {
        // Calculate once and allocate once. The experiment intentionally shares
        // MeshData.Pack rather than maintaining a second geometry-expansion algorithm.
        var index32 = meshes.Select(mesh => mesh.Indices.Any(index => index > ushort.MaxValue)).ToArray();
        var byteCount = 0;
        for (var i = 0; i < meshes.Length; i++)
        {
            var mesh = meshes[i];
            byteCount = checked(byteCount + checked((mesh.Vertices.Length + mesh.Colors.Length + mesh.PrimitiveSurfaceNormals.Length) * 4));
            byteCount = checked(byteCount + checked(mesh.Indices.Length * (index32[i] ? 4 : 2)));
            byteCount = checked(byteCount + 3) & ~3;
        }
        foreach (var line in lines) byteCount = checked(byteCount + checked(line.Segments.Length * 4));
        var bytes = new byte[byteCount];
        var position = 0;
        int WriteFloats(float[] values)
        {
            var offset = position;
            foreach (var value in values)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(position), value);
                position += 4;
            }
            return offset;
        }

        var meshMetadata = new BinaryMeshUpload[meshes.Length];
        for (var i = 0; i < meshes.Length; i++)
        {
            var mesh = meshes[i];
            var vertexOffset = WriteFloats(mesh.Vertices);
            var colorOffset = WriteFloats(mesh.Colors);
            var normalOffset = WriteFloats(mesh.PrimitiveSurfaceNormals);
            var indexOffset = position;
            foreach (var index in mesh.Indices)
            {
                if (index < 0) throw new InvalidOperationException("Mesh indices must be nonnegative.");
                if (index32[i]) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), (uint)index);
                else BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(position), (ushort)index);
                position += index32[i] ? 4 : 2;
            }
            position = checked(position + 3) & ~3;
            meshMetadata[i] = new(mesh.Id, vertexOffset, mesh.Vertices.Length,
                colorOffset, mesh.Colors.Length, normalOffset, mesh.PrimitiveSurfaceNormals.Length,
                indexOffset, mesh.Indices.Length, index32[i], mesh.SingleColor);
        }
        var lineMetadata = new BinaryLineUpload[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            lineMetadata[i] = new(lines[i].Id, WriteFloats(lines[i].Segments), lines[i].Segments.Length, lines[i].Center);
        return new(meshMetadata, lineMetadata, bytes);
    }
}
