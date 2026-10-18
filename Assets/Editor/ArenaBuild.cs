using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Editor
{
    public static class ArenaBuild
    {
        const string ScenePath = "Assets/Scenes/Arena.unity";
        [InitializeOnLoadMethod]
        static void Initialize() { EditorApplication.delayCall += EnsureScene; }

        [MenuItem("方块斗兽场/打开试玩场景")]
        public static void OpenScene()
        {
            EnsureScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        static void EnsureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath,true) };
        }

        public static void Configure()
        {
            PlayerSettings.companyName = "BlockArena";
            PlayerSettings.productName = "方块斗兽场";
            PlayerSettings.bundleVersion = "0.4.0";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.defaultScreenWidth = 1440; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 75;
            QualitySettings.vSyncCount = 1;
            GraphicsSettings.defaultRenderPipeline = null;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]{GraphicsDeviceType.Direct3D11});
            AssetDatabase.SaveAssets();
        }

        [MenuItem("方块斗兽场/构建 Windows 试玩版")]
        public static void BuildWindows()
        {
            EnsureScene(); Configure();
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/BlockArena.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows构建失败："+report.summary.result);
            Debug.Log("ARENA_BUILD_OK "+report.summary.totalSize+" bytes");
        }

        public static void Verify()
        {
            EnsureScene(); Configure();
            var game = new Core.ArenaGame("verify-host","编译验证",123);
            game.Apply("verify-host",new Core.GameCommand{type="start",bots=true});
            if(game.State.players.Count!=4||game.State.phase!="recruit")throw new InvalidOperationException("核心规则自检失败");
            var codec = new Runtime.UnityWireCodec();
            string json=codec.Encode(new Networking.WirePacket{type="state",state=game.SnapshotFor("verify-host")});
            var decoded=codec.Decode(json);
            if(decoded.state.players.Count!=4||decoded.state.players[0].units.Count!=2)throw new InvalidOperationException("JsonUtility编解码自检失败");
            Debug.Log("ARENA_VERIFY_OK Unity "+Application.unityVersion+" | C# rules + JsonUtility");
        }
    }
}
