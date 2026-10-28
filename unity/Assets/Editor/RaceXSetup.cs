using System.Collections.Generic;
using System.IO;
using System.Linq;
using RaceX.Core;
using RaceX.Track;
using RaceX.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RaceX.EditorTools
{
    /// <summary>Menu "RaceX > Setup Project": URP, cenas, Build Settings e ajustes de plataforma.
    /// Também pode ser chamado via linha de comando: -executeMethod RaceX.EditorTools.RaceXSetup.SetupFromCommandLine</summary>
    public static class RaceXSetup
    {
        const string ScenesDir = "Assets/Scenes";
        const string SettingsDir = "Assets/Settings";

        [MenuItem("RaceX/Setup Project")]
        public static void Setup()
        {
            SetupUrp();
            SetupBaseMaterials();
            SetupPlayer();
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(CreateScene(SceneLoader.MainMenuScene, typeof(MainMenuUI)), true),
            };
            foreach (var track in TrackLibrary.All)
                scenes.Add(new EditorBuildSettingsScene(CreateScene(TrackLibrary.Get(track).sceneName, typeof(RaceBootstrap)), true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("RaceX: setup concluído. Abra Assets/Scenes/MainMenu.unity e aperte Play.");
        }

        public static void SetupFromCommandLine()
        {
            Setup();
            EditorSceneManager.OpenScene($"{ScenesDir}/{SceneLoader.MainMenuScene}.unity");
        }

        public static void BuildWindowsFromCommandLine()
        {
            Setup();
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "../build/Windows/RaceXProto.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("RACEX_BUILD result=" + report.summary.result + " errors=" + report.summary.totalErrors);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static void BuildAndroidFromCommandLine()
        {
            Setup();
            RaceXOptimize.OptimizeAll();
            var jdk = System.Environment.GetEnvironmentVariable("RACEX_JDK");
            var sdk = System.Environment.GetEnvironmentVariable("RACEX_SDK");
            var ndk = System.Environment.GetEnvironmentVariable("RACEX_NDK");
            if (!string.IsNullOrEmpty(jdk)) UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;
            if (!string.IsNullOrEmpty(sdk)) UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            if (!string.IsNullOrEmpty(ndk)) UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTarget.Android);
            try { EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC; }
            catch (System.Exception ex) { Debug.LogWarning("RaceX: não aplicou ASTC (" + ex.Message + ")."); }
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(sc => sc.enabled).Select(sc => sc.path).ToArray(),
                locationPathName = "../build/Android/RaceXProto.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("RACEX_BUILD result=" + report.summary.result + " errors=" + report.summary.totalErrors);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static void SetupBaseMaterials()
        {
            Directory.CreateDirectory("Assets/Resources");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogError("RaceX: shader URP Lit não encontrado."); return; }
            CreateMaterial("Assets/Resources/RaceX_Lit.mat", shader, false);
            CreateMaterial("Assets/Resources/RaceX_LitEmissive.mat", shader, true);
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            CreateParticleMaterial("Assets/Resources/RaceX_Particle.mat", particleShader);
        }

        static void CreateParticleMaterial(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.enableInstancing = true;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            EditorUtility.SetDirty(mat);
        }

        static void CreateMaterial(string path, Shader shader, bool emissive)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.enableInstancing = true;
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
        }

        static void SetupUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            string assetPath = $"{SettingsDir}/RaceX_URP.asset";
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, $"{SettingsDir}/RaceX_URP_Renderer.asset");
                urp = UniversalRenderPipelineAsset.Create(renderer);
                urp.shadowDistance = 60f;
                urp.msaaSampleCount = 2;
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void SetupPlayer()
        {
            PlayerSettings.companyName = "RaceX";
            PlayerSettings.productName = "RaceX Proto";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.racex.proto");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.racex.proto");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // Input System + Input Manager antigo ("Both")
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("RaceX: Active Input Handling = Both (reinicie o editor se ele pedir).");
                }
            }
        }

        static string CreateScene(string name, System.Type bootstrap)
        {
            Directory.CreateDirectory(ScenesDir);
            string path = $"{ScenesDir}/{name}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject(bootstrap.Name).AddComponent(bootstrap);
            new GameObject("GameManager").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }
    }
}
