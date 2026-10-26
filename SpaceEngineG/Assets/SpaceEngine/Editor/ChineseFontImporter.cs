using System;
using UnityEditor;

namespace SpaceEngine.Editor
{
    /// <summary>Embed the OFL font bytes rather than relying on a font installed on Windows.</summary>
    public sealed class ChineseFontImporter : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if(!assetPath.EndsWith("/NotoSansCJKsc-Regular.otf",StringComparison.Ordinal))return;
            if(assetImporter is TrueTypeFontImporter importer)
            {
                importer.fontTextureCase=FontTextureCase.Dynamic;
                importer.includeFontData=true;
                importer.fontSize=16;
            }
        }
    }
}
