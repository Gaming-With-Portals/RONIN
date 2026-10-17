using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace YakumoLib.Fmt
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MDLHeader
    {
        public uint magic;
        public uint size;
        public float u_8;
        public uint version;
        public uint boneCount;
        public uint u_14;
        public uint boneDataOffset;
        public uint boneNameTable;
        public uint boneInfoCount;
        public uint boneInfoOffset;
        public uint lodCount;
        public uint lodDataOffset;
        public uint loadNameOffset;
        public uint materialCount;
        public uint materialNameOffset;
        public uint meshCount;
        public uint meshNameOffset;
        public uint batchCount;
        public uint batchOffset;
        public uint vertexAttributeCount;
        public uint vertexAttributeDataOffset;
        public uint vertexAttributeNameOffsetMap;
        public uint u_58;
        public uint vertexStridesOffset;
        public uint vertexGroupCount;
        public uint vertexGroupCountOffset;
        public uint vertexPoolOffsetOffset;
        public uint indiceCount;
        public uint indiceOffset;
        public uint boneRemapTableCount;
        public uint boneRemapTableCountOffset;
        public uint boneRemapTableOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MDLBatch
    {
        public uint meshGroupIndex;
        public uint materialIndex;
        public uint boneMapIndex;
        public uint vertexGroupIndex;
        public uint indiceCount;
        public uint indiceStart;
        public uint vertexCount;
    }

    public struct MDLVertexGroupInfo
    {
        public byte[] vertexData;
        public byte[] exVertexData;
        public uint vertexCount;
        public uint bufferOffset;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct MDLVertexAttributeData
    {
        public ushort isEx;
        public ushort format;
        public ushort slotIndex;
        public ushort uvMapIndex;
        public ushort offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MDLBone
    {
        public uint parentId;
        public float a,b,c,d,e,f,g,h,i;
        public float tx, ty, tz;
        public float rx, ry, rz;
        public float sx, sy, sz;
    }


    public struct MDLBoneMap
    {
        public ushort[] remapArray;

        public readonly int GetRemappedBone(int boneIndex)
        {
            return remapArray[boneIndex];
        }

    }



    public class MDLFile
    {
        public MDLHeader Header;
        public MDLBatch[] Batches;
        public MDLVertexGroupInfo[] VertexGroups;
        public MDLBoneMap[] BoneMaps;

        public MDLBone[] Bones;
        public string[] BoneNames;

        public MDLVertexAttributeData[] VertexAttributeData;
        public string[] VertexAttributeNames;

        public string[] MeshNames;
        public string[] MaterialNames;

        public uint VertexStride;
        public uint ExVertexStride;

        public uint TotalVertexCount = 0;

        public uint JointAttributeCount = 0;

        public ushort[] IndicesPool;


        uint VertexBufferOffset = 0;
        uint ExVertexBufferOffset = 0;

        public bool IsStatic()
        {
            return Header.boneCount != 0;
        }


        public MDLFile(ReadOnlySpan<byte> data) {
            Header = MemoryMarshal.Read<MDLHeader>(data);

            Batches = ReadArray<MDLBatch>(data, Header.batchOffset, Header.batchCount);

            VertexGroups = new MDLVertexGroupInfo[Header.vertexGroupCount];

            VertexStride = MemoryMarshal.Read<uint>(data.Slice((int)Header.vertexStridesOffset));
            ExVertexStride = MemoryMarshal.Read<uint>(data.Slice((int)Header.vertexStridesOffset+4));

            VertexBufferOffset = MemoryMarshal.Read<uint>(data.Slice((int)Header.vertexPoolOffsetOffset));
            ExVertexBufferOffset = MemoryMarshal.Read<uint>(data.Slice((int)Header.vertexPoolOffsetOffset + 4));


            for (int i = 0; i < Header.vertexGroupCount; i++)
            {
                VertexGroups[i].vertexCount = MemoryMarshal.Read<uint>(data.Slice((int)(Header.vertexGroupCountOffset)+i*0x4));

                uint vertexDataOffset = VertexBufferOffset + (VertexStride * TotalVertexCount);
                uint exVertexDataOffset = ExVertexBufferOffset + (ExVertexStride * TotalVertexCount);


                TotalVertexCount += VertexGroups[i].vertexCount;

                VertexGroups[i].vertexData = data.Slice((int)vertexDataOffset, (int)(VertexGroups[i].vertexCount * VertexStride)).ToArray();
                VertexGroups[i].exVertexData = data.Slice((int)exVertexDataOffset, (int)(VertexGroups[i].vertexCount * ExVertexStride)).ToArray();

            }

            VertexAttributeData = ReadArray<MDLVertexAttributeData>(data, Header.vertexAttributeDataOffset, Header.vertexAttributeCount);
            VertexAttributeNames = new string[Header.vertexAttributeCount];

            for (int i = 0; i < Header.vertexAttributeCount; i++)
            {
                uint nameOffset = MemoryMarshal.Read<uint>(data.Slice((int)Header.vertexAttributeNameOffsetMap+i*0x4)); // somethin like that

                VertexAttributeNames[i] = ReadNullTerminatedString(data, nameOffset);
                if (VertexAttributeNames[i] == "BIX")
                {
                    JointAttributeCount++;
                }

            }

            MeshNames = new string[Header.meshCount];
            for (int i = 0; i < Header.meshCount; i++)
            {
                uint nameOffset = MemoryMarshal.Read<uint>(data.Slice((int)Header.meshNameOffset + i * 0x4)); // somethin like that

                MeshNames[i] = ReadNullTerminatedString(data, nameOffset);
            }

            MaterialNames = new string[Header.materialCount];
            for (int i = 0; i < Header.materialCount; i++)
            {
                uint nameOffset = MemoryMarshal.Read<uint>(data.Slice((int)Header.materialNameOffset + i * 0x4)); // somethin like that

                MaterialNames[i] = ReadNullTerminatedString(data, nameOffset);
            }

            BoneMaps = new MDLBoneMap[Header.boneRemapTableCount];
            uint runningSize = 0;
            for (int i = 0; i < Header.boneRemapTableCount; i++)
            {
                uint count = MemoryMarshal.Read<uint>(data.Slice((int)(Header.boneRemapTableCountOffset) + i * 0x4));

                BoneMaps[i].remapArray = ReadArray<ushort>(data, Header.boneRemapTableOffset + (runningSize*0x2), count);

                runningSize += count;
            }

            Bones = new MDLBone[Header.boneCount];
            BoneNames = new string[Header.boneCount];

            uint[] boneNameOffsets = new uint[Header.boneCount];

            Bones = ReadArray<MDLBone>(data, Header.boneDataOffset, Header.boneCount);
            boneNameOffsets = ReadArray<uint>(data, Header.boneNameTable, Header.boneCount);

            for (int i = 0; i < Header.boneCount; i++)
            {
                BoneNames[i] = ReadNullTerminatedString(data, boneNameOffsets[i]);
            }
            


            IndicesPool = new ushort[Header.indiceCount];
            IndicesPool = ReadArray<ushort>(data, Header.indiceOffset, Header.indiceCount);





        }



        public byte[] GetBatchVertexData(MDLBatch batch)
        {
            ushort[] batchIndices = IndicesPool
                .Skip((int)batch.indiceStart)
                .Take((int)batch.indiceCount)
                .ToArray();

            uint loIdx = batchIndices.Min();
            uint hiIdx = batchIndices.Max();

            int rangeCount = (int)(hiIdx - loIdx + 1);

            byte[] batchVertexData = VertexGroups[batch.vertexGroupIndex].vertexData
                .Skip((int)loIdx * (int)VertexStride)
                .Take((int)rangeCount * (int)VertexStride)
                .ToArray();

            return batchVertexData;
        }

        public byte[] GetBatchExVertexData(MDLBatch batch)
        {
            ushort[] batchIndices = IndicesPool
                .Skip((int)batch.indiceStart)
                .Take((int)batch.indiceCount)
                .ToArray();

            uint loIdx = batchIndices.Min();
            uint hiIdx = batchIndices.Max();

            int rangeCount = (int)(hiIdx - loIdx + 1);

            byte[] batchVertexData = VertexGroups[batch.vertexGroupIndex].exVertexData
                .Skip((int)loIdx * (int)ExVertexStride)
                .Take((int)rangeCount * (int)ExVertexStride)
                .ToArray();

            return batchVertexData;
        }

        public ushort[] GetBatchIndexData(MDLBatch batch)
        {
            ushort[] batchIndices = IndicesPool
                .Skip((int)batch.indiceStart)
                .Take((int)batch.indiceCount)
                .ToArray();

            ushort loIdx = batchIndices.Min();
            ushort hiIdx = batchIndices.Max();

            return batchIndices.Select(idx => (ushort)(idx - loIdx)).ToArray();
        }

        public bool HasVertexAttribute(string id)
        {
            return VertexAttributeNames.Contains(id);
        }

        public int GetVertexAttributeOffset(string attrib)
        {
            return VertexAttributeData[Array.IndexOf(VertexAttributeNames, attrib)].offset;

        }

        public bool GetVertexAttributeIsEx(string attrib)
        {
            return VertexAttributeData[Array.IndexOf(VertexAttributeNames, attrib)].isEx==1;

        }


        public string GetMeshNameByBatch(MDLBatch batch)
        {
            return MeshNames[batch.meshGroupIndex];
        }

        public MDLBoneMap GetBoneMapByBatch(MDLBatch batch)
        {
            return BoneMaps[batch.boneMapIndex];
        }

        public string GetMaterialNameByBatch(MDLBatch batch)
        {
            return MaterialNames[batch.materialIndex];
        }

        public int GetUVMapCount()
        {
            int hi = 0;
            foreach (MDLVertexAttributeData dat in VertexAttributeData)
            {
                if (dat.uvMapIndex > hi)
                {
                    hi = dat.uvMapIndex;
                }
            }

            return hi + 1;
        }


        static string ReadNullTerminatedString(ReadOnlySpan<byte> data, uint offset)
        {
            var slice = data.Slice((int)offset);
            int nullIndex = slice.IndexOf((byte)0);
            if (nullIndex < 0) nullIndex = slice.Length;

            return Encoding.UTF8.GetString(slice.Slice(0, nullIndex));
        }

        static T[] ReadArray<T>(ReadOnlySpan<byte> data, uint offset, uint count) where T : unmanaged
        {
            var slice = data.Slice((int)offset, (int)count * Unsafe.SizeOf<T>());
            return MemoryMarshal.Cast<byte, T>(slice).ToArray();
        }




    }
}
