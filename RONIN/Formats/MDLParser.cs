using SharpDX.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Threading.Tasks;

namespace RONIN.Formats
{
    public struct MDLHelixBuffers
    {
        public Vector3[] Positions;
        public Vector2[] UVs;
        public Vector3[] Normals;
        public Vector3[] Tangents;
        public Vector3[] BiTangents;
        public ushort[] Indices;

        public string MaterialName;
    }

    public struct MDLBatch(BinaryReader reader)
    {
        public uint meshGroupID = reader.ReadUInt32();
        public uint materialID = reader.ReadUInt32();
        public uint boneMapID = reader.ReadUInt32();
        public uint vertexGroupID = reader.ReadUInt32();
        public uint indiceCount = reader.ReadUInt32();
        public uint indiceStart = reader.ReadUInt32();
        public uint vertexCount = reader.ReadUInt32();
    }

    public static class MDLParser
    {
        public static MDLHelixBuffers[] GetPreviewBuffers(byte[] data)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            if (reader.ReadUInt32() != 4998221)
            {
                return []; // not an MDL
            }

            reader.BaseStream.Seek(40, 0);
            uint lodCount = reader.ReadUInt32();
            uint lodDataOffset = reader.ReadUInt32();
            uint lodNameOffset = reader.ReadUInt32();

            uint materialCount = reader.ReadUInt32();
            uint materialNamesOffset = reader.ReadUInt32();

            uint meshNameCount = reader.ReadUInt32();
            uint meshNameOffset = reader.ReadUInt32();
            uint batchCount = reader.ReadUInt32();
            uint batchOffset = reader.ReadUInt32();
            uint vertexPropertyCount = reader.ReadUInt32();
            uint vertexPropertyDataOffset = reader.ReadUInt32();
            uint vertexPropertyNameOffset = reader.ReadUInt32();
            reader.ReadUInt32();

            uint vertexHeaderStart = reader.ReadUInt32();
            uint vertexGroupCount = reader.ReadUInt32();
            uint vertexGroupSizesOffset = reader.ReadUInt32();
            uint vertexPoolOffsetOffsets = reader.ReadUInt32();
            uint indiceCount = reader.ReadUInt32();
            uint indiceOffset = reader.ReadUInt32();

            ushort[] indexBuffer = new ushort[indiceCount];
            MDLHelixBuffers[] coreBuffers = new MDLHelixBuffers[vertexGroupCount]; // main buffers which meshes pull from

            reader.BaseStream.Seek(vertexHeaderStart, 0);
            uint mainStride = reader.ReadUInt32();
            uint exStride = reader.ReadUInt32();

            reader.BaseStream.Seek(vertexPoolOffsetOffsets, 0);
            uint mainPoolOffset = reader.ReadUInt32();
            uint exPoolOffset = reader.ReadUInt32();

            reader.BaseStream.Seek(vertexGroupSizesOffset, 0);
            uint[] vtxPoolSizes = new uint[vertexGroupCount];
            for (uint i = 0; i < vertexGroupCount; i++)
            {
                vtxPoolSizes[i] = reader.ReadUInt32();
            }

            for (uint x = 0; x < vertexGroupCount; x++)
            {
                coreBuffers[x].Positions = new Vector3[vtxPoolSizes[x]];
                coreBuffers[x].UVs = new Vector2[vtxPoolSizes[x]];
                coreBuffers[x].Normals = new Vector3[vtxPoolSizes[x]];
                coreBuffers[x].Tangents = new Vector3[vtxPoolSizes[x]];
                coreBuffers[x].BiTangents = new Vector3[vtxPoolSizes[x]];
            }


            reader.BaseStream.Seek(mainPoolOffset, 0);
            for (uint x = 0; x < vertexGroupCount; x++)
            {
                for (uint y = 0; y < vtxPoolSizes[x]; y++)
                {
                    coreBuffers[x].Positions[y] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); // TOOD: Other properties
                    reader.ReadSingle(); // postions have a w param
                    coreBuffers[x].Normals[y] = new Vector3((float)reader.ReadHalf(), (float)reader.ReadHalf(), (float)reader.ReadHalf());

                    byte[] tangents = new byte[4];
                    tangents[0] = reader.ReadByte();
                    tangents[1] = reader.ReadByte();
                    tangents[2] = reader.ReadByte();
                    tangents[3] = reader.ReadByte(); // always FF

                    coreBuffers[x].Tangents[y] = new Vector3(tangents[0] / 127.0f, tangents[1] / 127.0f, tangents[2] / 127.0f);

                    coreBuffers[x].BiTangents[y] = Vector3.Cross(coreBuffers[x].Normals[y], coreBuffers[x].Tangents[y])*(tangents[3]/127.0f);


                    reader.ReadBytes((int)(mainStride-26));
                }
            }

            reader.BaseStream.Seek(exPoolOffset, 0);
            for (uint x = 0; x < vertexGroupCount; x++)
            {
                for (uint y = 0; y < vtxPoolSizes[x]; y++)
                {
                    coreBuffers[x].UVs[y] = new Vector2((float)reader.ReadHalf(), (float)reader.ReadHalf());
                    reader.ReadBytes((int)(exStride - 4));
                }
            }


            reader.BaseStream.Seek(indiceOffset, 0);
            for (uint x = 0; x < indiceCount; x++)
            {
                indexBuffer[x] = reader.ReadUInt16();
            }


            string[] materialNames = new string[materialCount];
            for (uint x = 0; x < materialCount; x++)
            {
                reader.BaseStream.Seek(materialNamesOffset+x*4, 0);
                uint offset = reader.ReadUInt32();
                reader.BaseStream.Seek(offset, 0);

                using var materialNameBytes = new MemoryStream();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                {
                    materialNameBytes.WriteByte(b);
                }

                materialNames[x] = System.Text.Encoding.UTF8.GetString(materialNameBytes.ToArray());

            }
            




            reader.BaseStream.Seek(batchOffset, 0);
            MDLBatch[] batches = new MDLBatch[batchCount];
            for (uint x = 0; x < batchCount; x++)
            {
                batches[x] = new MDLBatch(reader);
            }

            MDLHelixBuffers[] previewBuffers = new MDLHelixBuffers[batchCount];

            for (int b = 0; b < batchCount; b++)
            {
                var batch = batches[b];
                var source = coreBuffers[batch.vertexGroupID];
                var batchIndices = new ushort[batch.indiceCount];
                Array.Copy(indexBuffer, (int)batch.indiceStart, batchIndices, 0, (int)batch.indiceCount);

                ushort minIndex = batchIndices.Min();
                ushort maxIndex = batchIndices.Max();
                int localCount = maxIndex - minIndex + 1;

                var localPositions = new Vector3[localCount];
                var localUVs = new Vector2[localCount];
                var localNormals = new Vector3[localCount];
                var localTangents = new Vector3[localCount];
                var localBiTangents = new Vector3[localCount];

                Array.Copy(source.Positions, minIndex, localPositions, 0, localCount);
                Array.Copy(source.UVs, minIndex, localUVs, 0, localCount);
                Array.Copy(source.Normals, minIndex, localNormals, 0, localCount);
                Array.Copy(source.Tangents, minIndex, localTangents, 0, localCount);
                Array.Copy(source.BiTangents, minIndex, localBiTangents, 0, localCount);

                var localIndices = new ushort[batchIndices.Length];
                for (int i = 0; i < batchIndices.Length; i++)
                    localIndices[i] = (ushort)(batchIndices[i] - minIndex);

                previewBuffers[b] = new MDLHelixBuffers
                {
                    Positions = localPositions,
                    UVs = localUVs,
                    Normals = localNormals,
                    Tangents = localTangents,
                    BiTangents = localBiTangents,
                    Indices = localIndices,
                    MaterialName = materialNames[batch.materialID]
                };
            }


            return previewBuffers;
        }

    }
}
