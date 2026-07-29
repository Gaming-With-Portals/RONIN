using RONIN.Exporters;
using RONIN.Formats;
using RONIN.Preview;
using SharpGLTF.Schema2;
using System.IO;
using YakumoLib.Assets;


namespace RONIN.Exporters
{
    public sealed class ModelPreviewProvider : IAssetExporterProvider
    {
        public bool CanExport(AssetEntry entry) => (entry.Type == AssetType.SkeletalMesh || entry.Type == AssetType.StaticMesh); // TODO: Statci mesh

        public string GetExportExtension() => ".gltf";
        public bool Export(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return false; // FAIL

            var modelDataHeader = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl"));
            if(modelDataHeader == null)
            {
                return false;
            }


            BinaryReader r = new BinaryReader(new MemoryStream(AssetExtractor.GetSubBlob(modelDataHeader, entry, modelDataHeader.ContentDirectory)));

            var model = ModelRoot.CreateModel();
            var scene = model.UseScene("default");






            return true; // SUCCESS
        }


    }
}