using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Exporters
{
    public interface IAssetExporterProvider
    {
        string GetExportExtension();
        bool CanExport(AssetEntry entry);
        bool Export(AssetEntry entry);
    }
}
