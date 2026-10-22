using HelixToolkit;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using RONIN.Formats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using YakumoLib;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public sealed record MaterialData(byte[] DiffuseDds, byte[]? NormalDds);

    public sealed class MeshData
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector3[] Tangents { get; init; }
        public required Vector3[] BiTangents { get; init; }
        public required Vector2[] UVs { get; init; }
        public required uint[] Indices { get; init; }
        public MaterialData? Material { get; init; }
    }

    public sealed class ModelPreviewProvider : IAssetPreviewProvider, IUsesAssetLibrary
    {
        private AssetLibrary? _library;
        public bool CanPreview(AssetEntry entry) => (entry.Type == AssetType.SkeletalMesh || entry.Type == AssetType.StaticMesh);

        public void SetLibrary(AssetLibrary library) => _library = library;

        private async Task<PhongMaterial> BuildMaterialAsync(AssetEntry textureAssetEntry, AssetEntry normalTextureEntry)
        {
            byte[] ddsBytes = await Task.Run(() => DdsTextureLoader.LoadDDSRaw(textureAssetEntry));
            byte[] nrmlDDSBytes = await Task.Run(() => DdsTextureLoader.LoadDDSRaw(normalTextureEntry));

            return new PhongMaterial
            {
                DiffuseColor = new Color4(0.9f, 0.9f, 0.9f, 1f),
                DiffuseMap = new TextureModel(new MemoryStream(ddsBytes)),
                NormalMap = new TextureModel(new MemoryStream(nrmlDDSBytes))
            };
        }

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return null;

            var modelDataHeader = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl"));

            if (modelDataHeader is null)
                return null;

            var materialDataInfo = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("materialmap.bin"));



            MDLHelixBuffers[] buffers = MDLParser.GetPreviewBuffers(AssetExtractor.GetSubBlob(modelDataHeader, entry, modelDataHeader.ContentDirectory));


            uint u = 0;


            MeshGeometryModel3D[] models = new MeshGeometryModel3D[buffers.Count()];
            Dictionary<string, AssetEntry> textures = new Dictionary<string, AssetEntry>();
            Dictionary<string, AssetEntry> normalTextures = new Dictionary<string, AssetEntry>();
            Dictionary<string, PhongMaterial> knownMaterials = new Dictionary<string, PhongMaterial>();

            // parse materials
            BinaryReader br = new BinaryReader(new MemoryStream(AssetExtractor.GetSubBlob(materialDataInfo, entry, materialDataInfo.ContentDirectory)));
            br.BaseStream.Seek(0x10, SeekOrigin.Begin);
            uint baseOffset = br.ReadUInt32();
            uint materialCount = br.ReadUInt32();
            uint u_18 = br.ReadUInt32();

            uint[] offsets = new uint[materialCount];
            for (uint i = 0; i < materialCount; i++)
            {
                offsets[i] = br.ReadUInt32();
            }

            for (uint i = 0; i < materialCount; i++)
            {
                br.BaseStream.Seek(0x4 + baseOffset + offsets[i]+24, SeekOrigin.Begin);

                uint nameLength = br.ReadUInt32()-1;
                string name = Encoding.UTF8.GetString(br.ReadBytes((int)nameLength));
                br.ReadByte();
                UUID id = new UUID
                {
                    a = br.ReadUInt32(),
                    b = br.ReadUInt32(),
                    c = br.ReadUInt32(),
                    d = br.ReadUInt32()
                };

                AssetEntry materialInstance = _library.GetByUUID(id);
                if (materialInstance == null)
                {
                    continue;
                }

                var materialInstanceData = materialInstance.SubEntries.FirstOrDefault(s => s.FileName.Equals("Instance.dat"));
                if (materialInstanceData == null)
                {
                    continue;
                }

                BinaryReader matReader = new BinaryReader(new MemoryStream(AssetExtractor.GetSubBlob(materialInstanceData, materialInstance, materialInstanceData.ContentDirectory)));

                matReader.BaseStream.Seek(40, 0);
                uint textureTableOffset = matReader.ReadUInt32();
                matReader.BaseStream.Seek(textureTableOffset+4, 0);
                uint textureCount = matReader.ReadUInt32();
                matReader.ReadUInt32();
                uint[] textureOffsets = new uint[textureCount];

                for (uint x = 0; x < textureCount; x++)
                {
                    textureOffsets[x] = matReader.ReadUInt32();
                }


                for (uint x = 0; x < textureCount; x++)
                {
                    matReader.BaseStream.Seek(textureTableOffset + 4 + textureOffsets[x] + 24, 0);
                    uint texNameLength = matReader.ReadUInt32() - 1;
                    string texName = Encoding.UTF8.GetString(matReader.ReadBytes((int)texNameLength));
                    matReader.ReadByte();
                    UUID texID = new UUID
                    {
                        a = matReader.ReadUInt32(),
                        b = matReader.ReadUInt32(),
                        c = matReader.ReadUInt32(),
                        d = matReader.ReadUInt32()
                    };

                    if (texName == "BaseColorMap")
                    {
                        var texAsset = _library.GetByUUID(texID);
                        if (texAsset == null)
                        {
                            continue;
                        }
                        textures[name] = texAsset;
                    }
                    else if (texName == "NormalMap")
                    {
                        var texAsset = _library.GetByUUID(texID);
                        if (texAsset == null)
                        {
                            continue;
                        }
                        normalTextures[name] = texAsset;
                    }
                    else
                    {
                        continue;
                    }
                }


                continue;
            }

            foreach (MDLHelixBuffers buf in buffers)
            {
                PhongMaterial tgt;

                if (knownMaterials.ContainsKey(buf.MaterialName))
                {
                    tgt = knownMaterials[buf.MaterialName];
                }
                else
                {
                    tgt = textures.Get(buf.MaterialName) is not null
                    ? await BuildMaterialAsync(textures.Get(buf.MaterialName), normalTextures.Get(buf.MaterialName))
                    : PhongMaterials.White;

                    knownMaterials[buf.MaterialName] = tgt;
                }




                models[u] = ToModel(buf, tgt);
                
                u++;
            }




            ModelPreviewViewModel vm = new ModelPreviewViewModel
            {
                Models = new ObservableCollection<MeshGeometryModel3D>(models)
            } ;

            return (object?)vm;

        }

        public static MeshGeometryModel3D ToModel(MDLHelixBuffers buf, PhongMaterial mat)
        {
            var mesh = new HelixToolkit.SharpDX.MeshGeometry3D
            {
                Positions = new Vector3Collection(buf.Positions),
                Normals = new Vector3Collection(buf.Normals),
                Tangents = new Vector3Collection(buf.Tangents),
                BiTangents = new Vector3Collection(buf.BiTangents),
                TextureCoordinates = new Vector2Collection(buf.UVs),
                Indices = new IntCollection(buf.Indices.Select(i => (int)i))
            };

            

            return new MeshGeometryModel3D
            {
                Geometry = mesh,
                Material = mat
            };
        }

    }
    public sealed class ModelPreviewViewModel()
    {
        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();
        public HelixToolkit.Wpf.SharpDX.Camera Camera { get; } = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
            Position = new Point3D(3, 3, 5),
            LookDirection = new Vector3D(-3, -3, -5),
            UpDirection = new Vector3D(0, 1, 0),
            FarPlaneDistance = 10000,
            NearPlaneDistance = 0.1
        };
        public required ObservableCollection<MeshGeometryModel3D> Models { get; init; }
    }
}
