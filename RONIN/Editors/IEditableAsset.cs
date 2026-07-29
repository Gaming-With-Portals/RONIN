using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using YakumoLib.Assets;

namespace RONIN.Editors
{
    public interface IEditableAsset
    {
        AssetEntry ParentEntry { get; }
        string SubFileName { get; }
        byte[] Serialize();
        event EventHandler? Modified;

    }
}
