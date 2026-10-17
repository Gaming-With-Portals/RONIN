using RONIN.Exporters;
using RONIN.Formats;
using RONIN.Preview;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using YakumoLib.Assets;
using YakumoLib.Fmt;

// Credit to Yequ (https://github.com/yequ172672/RONIN_ng4edit/blob/ng4pack/YakumoLib/Formats/MdlToGltfConverter.cs) for SharpGLTF sections.

namespace RONIN.Exporters
{
    public sealed class ModelExportProvider : IAssetExporterProvider
    {
        public static Matrix4x4 SanitizeAffine(Matrix4x4 value)
        {
            value.M14 = 0; value.M24 = 0; value.M34 = 0; value.M44 = 1;
            return value;
        }

        public static Matrix4x4 ToGltfBoneWorldTransform(MDLBone bone)// Straight from Yequ
        {
            Matrix4x4 mdlWorld = Matrix4x4.CreateScale(new Vector3(bone.sx, bone.sy, bone.sz)) *
                Matrix4x4.CreateFromYawPitchRoll(bone.ry, bone.rx, bone.rz) *
                Matrix4x4.CreateTranslation(new Vector3(bone.tx, bone.ty, bone.tz));
            return SanitizeAffine(mdlWorld);
        }

        private static NodeBuilder[] BuildSkeleton(MDLFile model, NodeBuilder root) // Straight from Yequ
        {
            if (model.Bones.Length == 0)
            {
                var staticRoot = new NodeBuilder("static")
                {
                    Extras = new JsonObject { ["static"] = true }
                };
                root.AddNode(staticRoot);
                return [staticRoot];
            }
            var nodes = new NodeBuilder[model.Bones.Length];
            var worldTransforms = new Matrix4x4[model.Bones.Length];

            for (int i = 0; i < model.Bones.Length; i++)
            {
                MDLBone bone = model.Bones[i];
                var node = new NodeBuilder(model.BoneNames[i]);
                node.Extras = new JsonObject
                {
                    ["roninBoneIndex"] = i,
                    ["roninOriginalName"] = model.BoneNames[i],
                    ["roninRotationX"] = bone.rx,
                    ["roninRotationY"] = bone.ry,
                    ["roninRotationZ"] = bone.rz
                };

                Matrix4x4 world = ToGltfBoneWorldTransform(bone);
                nodes[i] = node;
                worldTransforms[i] = world;
            }

            for (int i = 0; i < model.Bones.Length; i++)
            {
                MDLBone bone = model.Bones[i];
                NodeBuilder parent = bone.parentId < model.Bones.Length && bone.parentId != i
                    ? nodes[bone.parentId]
                    : root;
                parent.AddNode(nodes[i]);
                Matrix4x4 local = worldTransforms[i];
                if (bone.parentId < model.Bones.Length && bone.parentId != i)
                {
                    if (!Matrix4x4.Invert(worldTransforms[bone.parentId], out Matrix4x4 inverseParent))
                        throw new InvalidDataException($"Bone {i} '{model.BoneNames[i]}' has a non-invertible parent transform.");
                    local = SanitizeAffine(worldTransforms[i] * inverseParent);
                }
                nodes[i].LocalTransform = local;
            }

            return nodes;
        }

        public bool CanExport(AssetEntry entry) => (entry.Type == AssetType.SkeletalMesh || entry.Type == AssetType.StaticMesh); // TODO: Statci mesh

        public string GetExportExtension() => ".glb";
        public bool Export(AssetEntry entry, string targetPath)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return false; // FAIL

            var modelDataHeader = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl"));
            if(modelDataHeader == null)
            {
                return false;
            }


            byte[] data = AssetExtractor.GetSubBlob(modelDataHeader, entry, modelDataHeader.ContentDirectory);
            MDLFile mdlFile = new MDLFile(data);

            



            var scene = new SceneBuilder("MDL");

 
            var skeletonRoot = new NodeBuilder("Armature");

            // Build bone data
            NodeBuilder[] boneNodes = BuildSkeleton(mdlFile, skeletonRoot);




            Dictionary<string, int> batchIndexTicker = new();

            int uvMapCount = mdlFile.GetUVMapCount();

            for (uint i = 0; i < mdlFile.Header.batchCount; i++)
            {
                string meshName = mdlFile.GetMeshNameByBatch(mdlFile.Batches[i]);

                if (!batchIndexTicker.ContainsKey(meshName))
                {
                    batchIndexTicker[meshName] = 0;
                }
                else
                {
                    batchIndexTicker[meshName] += 1;
                }

                string stylizedName = $"{meshName}_{batchIndexTicker[meshName]}";



                byte[] vertexData = mdlFile.GetBatchVertexData(mdlFile.Batches[i]);
                byte[] exVertexData = mdlFile.GetBatchExVertexData(mdlFile.Batches[i]);
                ushort[] indexData = mdlFile.GetBatchIndexData(mdlFile.Batches[i]);

                var batchMesh = new MeshBuilder<VertexPositionNormalTangent, VertexColor2Texture3, VertexJoints8>(stylizedName)
                {
                    Extras = new JsonObject
                    {
                        ["roninMaterial"] = mdlFile.GetMaterialNameByBatch(mdlFile.Batches[i]),
                    }
                };
                PrimitiveBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexColor2Texture3, VertexJoints8> primitive = batchMesh.UsePrimitive(MaterialBuilder.CreateDefault());

                int vertexCount = vertexData.Length / (int)mdlFile.VertexStride;


                // Create Vertex Geometry Data
                var positions = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var tangents = new Vector4[vertexCount];

                int positionBase = mdlFile.GetVertexAttributeOffset("POS"); // No support for these attributes as ExVertices...
                int normalBase = mdlFile.GetVertexAttributeOffset("NML");
                int tangentBase = mdlFile.GetVertexAttributeOffset("TGT");

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = v * (int)mdlFile.VertexStride;


                    float x = BitConverter.ToSingle(vertexData, offset + positionBase + 0);
                    float y = BitConverter.ToSingle(vertexData, offset + positionBase + 4);
                    float z = BitConverter.ToSingle(vertexData, offset + positionBase + 8);
                    positions[v] = new Vector3(x, y, z);

                    float nx = (float)BitConverter.ToHalf(vertexData, offset + normalBase + 0); // no one ever complains about *more* percision...
                    float ny = (float)BitConverter.ToHalf(vertexData, offset + normalBase + 2);
                    float nz = (float)BitConverter.ToHalf(vertexData, offset + normalBase + 4);
                    normals[v] = new Vector3(nx, ny, nz); // TODO: Fix... more?

                    sbyte txRaw = (sbyte)vertexData[offset + tangentBase + 0];
                    sbyte tyRaw = (sbyte)vertexData[offset + tangentBase + 1];
                    sbyte tzRaw = (sbyte)vertexData[offset + tangentBase + 2];
                    sbyte twRaw = (sbyte)vertexData[offset + tangentBase + 3];

                    Vector3 tangentDir = new Vector3(txRaw / 127.0f, tyRaw / 127.0f, tzRaw / 127.0f);

                    if (tangentDir.LengthSquared() < 1e-12f)
                        tangentDir = Vector3.UnitX; // Failsafe, probably not useful though.
                    else
                        tangentDir = Vector3.Normalize(tangentDir);

                    float bitangentSign = twRaw < 0 ? -1.0f : 1.0f;
                    tangents[v] = new Vector4(tangentDir, bitangentSign);

                }

                // Vertex Color/Tex Data

                bool hasColor1 = mdlFile.HasVertexAttribute("colorSet1");
                bool hasColor2 = mdlFile.HasVertexAttribute("colorSet2");

                bool hasMap1 = mdlFile.HasVertexAttribute("map1");
                bool hasMap2 = mdlFile.HasVertexAttribute("map2");
                bool hasMap3 = mdlFile.HasVertexAttribute("map3");

                int color1Base = hasColor1 ? mdlFile.GetVertexAttributeOffset("colorSet1") : -1;
                int color2Base = hasColor2 ? mdlFile.GetVertexAttributeOffset("colorSet2") : -1;

                int map1Base = hasMap1 ? mdlFile.GetVertexAttributeOffset("map1") : -1;
                int map2Base = hasMap2 ? mdlFile.GetVertexAttributeOffset("map2") : -1;
                int map3Base = hasMap3 ? mdlFile.GetVertexAttributeOffset("map3") : -1;


                bool color1IsEx = hasColor1 ? mdlFile.GetVertexAttributeIsEx("colorSet1") : false;
                bool color2IsEx = hasColor2 ? mdlFile.GetVertexAttributeIsEx("colorSet2") : false;

                bool map1Ex = hasMap1 ? mdlFile.GetVertexAttributeIsEx("map1") : false;
                bool map2Ex = hasMap2 ? mdlFile.GetVertexAttributeIsEx("map2") : false;
                bool map3Ex = hasMap3 ? mdlFile.GetVertexAttributeIsEx("map3") : false;


                var colors1 = new Vector4[vertexCount];
                var colors2 = new Vector4[vertexCount];
                var uv1s = new Vector2[vertexCount];
                var uv2s = new Vector2[vertexCount];
                var uv3s = new Vector2[vertexCount];

                Vector4 ReadColor(byte[] data, int baseOffset, bool isEx, bool has, int off, int offEx)
                {
                    if (!has) return Vector4.One;
                    int o = (isEx ? offEx : off) + baseOffset;
                    return new Vector4(data[o + 0] / 255f, data[o + 1] / 255f, data[o + 2] / 255f, data[o + 3] / 255f);
                }

                Vector2 ReadUV(byte[] data, int baseOffset, bool isEx, bool has, int off, int offEx)
                {
                    if (!has) return Vector2.Zero;
                    int o = (isEx ? offEx : off) + baseOffset;
                    return new Vector2((float)BitConverter.ToHalf(data, o + 0), (float)BitConverter.ToHalf(data, o + 2));
                }


                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = v * (int)mdlFile.VertexStride;
                    int offsetEx = v * (int)mdlFile.ExVertexStride;

                    colors1[v] = ReadColor(color1IsEx ? exVertexData : vertexData, color1Base, color1IsEx, hasColor1, offset, offsetEx);
                    colors2[v] = ReadColor(color2IsEx ? exVertexData : vertexData, color2Base, color2IsEx, hasColor2, offset, offsetEx);

                    uv1s[v] = ReadUV(map1Ex ? exVertexData : vertexData, map1Base, map1Ex, hasMap1, offset, offsetEx);
                    uv2s[v] = ReadUV(map2Ex ? exVertexData : vertexData, map2Base, map2Ex, hasMap2, offset, offsetEx);
                    uv3s[v] = ReadUV(map3Ex ? exVertexData : vertexData, map3Base, map3Ex, hasMap3, offset, offsetEx);
                }

                // Skinning data
                bool hasJoint4 = mdlFile.JointAttributeCount >= 1;
                bool hasJoint8 = mdlFile.JointAttributeCount >= 2;

                int BIXBase = hasJoint4 ? mdlFile.GetVertexAttributeOffset("BIX") : -1;
                int BWTBase = hasJoint4 ? mdlFile.GetVertexAttributeOffset("BWT") : -1;
                bool BIXEx = hasJoint4 ? mdlFile.GetVertexAttributeIsEx("BIX") : false; // This should always be core propreties, but okay
                bool BWTEx = hasJoint4 ? mdlFile.GetVertexAttributeIsEx("BWT") : false;

                var weightData = new (int, float)[vertexCount][];

                MDLBoneMap batchBoneMap = mdlFile.GetBoneMapByBatch(mdlFile.Batches[i]);

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = v * (int)mdlFile.VertexStride;
                    int offsetEx = v * (int)mdlFile.ExVertexStride;

                    var joints = new (int, float)[8];

                    if (hasJoint4)
                    {
                        byte[] bixSrc = BIXEx ? exVertexData : vertexData;
                        byte[] bwtSrc = BWTEx ? exVertexData : vertexData;
                        int bixOff = (BIXEx ? offsetEx : offset) + BIXBase;
                        int bwtOff = (BWTEx ? offsetEx : offset) + BWTBase;

                        for (int j = 0; j < 4; j++)
                            joints[j] = (batchBoneMap.GetRemappedBone(bixSrc[bixOff + j]), bwtSrc[bwtOff + j] / 255f);
                    }

                    if (hasJoint8)
                    {
                        byte[] bixSrc2 = BIXEx ? exVertexData : vertexData;
                        byte[] bwtSrc2 = BWTEx ? exVertexData : vertexData;
                        int bixOff2 = (BIXEx ? offsetEx : offset) + (BIXBase+0x8);
                        int bwtOff2 = (BWTEx ? offsetEx : offset) + (BWTBase+0x8);

                        for (int j = 0; j < 4; j++)
                            joints[4 + j] = (batchBoneMap.GetRemappedBone(bixSrc2[bixOff2 + j]), bwtSrc2[bwtOff2 + j] / 255f);
                    }

                    if (!hasJoint4)
                        joints[0] = (0, 1f);

                    weightData[v] = joints;

                }

                // Build
                var vertexBuilders = new VertexBuilder<VertexPositionNormalTangent, VertexColor2Texture3, VertexJoints8>[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                    vertexBuilders[v] = new VertexBuilder<VertexPositionNormalTangent, VertexColor2Texture3, VertexJoints8>(
                        new VertexPositionNormalTangent(positions[v], normals[v], tangents[v]),
                        new VertexColor2Texture3(colors1[v], colors2[v], uv1s[v], uv2s[v], uv3s[v]),
                        new VertexJoints8(weightData[v])
                        );

                for (int tri = 0; tri + 2 < indexData.Length; tri += 3)
                {
                    var a = vertexBuilders[indexData[tri + 0]];
                    var b = vertexBuilders[indexData[tri + 1]];
                    var c = vertexBuilders[indexData[tri + 2]];

                    primitive.AddTriangle(a, c, b);
                }


                if (hasJoint4)
                {
                    scene.AddSkinnedMesh(batchMesh, Matrix4x4.Identity, boneNodes);
                }
                else
                {
                    scene.AddRigidMesh(batchMesh, Matrix4x4.Identity);
                }


            }




            scene.ToGltf2().SaveGLB(targetPath);

            return true; // SUCCESS
        }


    }
}