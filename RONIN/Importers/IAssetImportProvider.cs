using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Importers
{
    public interface IAssetImportProvider
    {
        string GetImportExtension();
        bool CanImport(AssetEntry entry);
        bool Import(AssetEntry entry, string inputPath);
    }
}
