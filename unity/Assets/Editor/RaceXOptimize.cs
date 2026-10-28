using UnityEditor;
using UnityEngine;

namespace RaceX.EditorTools
{
    /// <summary>Otimizações de assets para acelerar carregamento e reduzir o APK:
    /// compressão de malha dos FBX, sem blend shapes/anims/câmeras/luzes embutidas; céus HDR limitados a 1024px.
    /// Pode ser chamado via linha de comando: -executeMethod RaceX.EditorTools.RaceXOptimize.OptimizeAll</summary>
    public static class RaceXOptimize
    {
        public static void OptimizeAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/CarModels" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool changed = false;
                if (imp.meshCompression != ModelImporterMeshCompression.Medium) { imp.meshCompression = ModelImporterMeshCompression.Medium; changed = true; }
                if (imp.importBlendShapes) { imp.importBlendShapes = false; changed = true; }
                if (imp.importCameras) { imp.importCameras = false; changed = true; }
                if (imp.importLights) { imp.importLights = false; changed = true; }
                if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
                if (changed)
                {
                    imp.SaveAndReimport();
                    Debug.Log("[RaceXOptimize] Modelo otimizado: " + path);
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Skyboxes" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (imp.maxTextureSize > 1024)
                {
                    imp.maxTextureSize = 1024;
                    imp.SaveAndReimport();
                    Debug.Log("[RaceXOptimize] Céu reduzido para 1024px: " + path);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RaceXOptimize] Otimização de assets concluída.");
        }
    }
}
