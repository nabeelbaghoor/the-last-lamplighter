using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DreamLayer.SpriteImporter;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lamplighter.EditorTools
{
    /// <summary>
    /// One-command pipeline: configure art/audio importers, import the DreamLayer hero sprite ZIPs
    /// (art/sprites/*.zip) through the DreamLayer importer, build the Animator, and produce the
    /// WebGL build for itch.io.
    ///
    /// Command line (windowed editor; see README):
    ///   Unity -projectPath . -buildTarget WebGL -executeMethod Lamplighter.EditorTools.LamplighterBuild.BuildWebGLFromCli -logFile build.log
    /// </summary>
    public static class LamplighterBuild
    {
        private const string HeroFolder = "Assets/Resources/Hero";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string OutDir = "Builds/WebGL";
        /// <summary>Stamped on configured importers so rebuilds skip assets that are already set up.</summary>
        private const string ImportMarker = "lamplighter-import-v1";

        [MenuItem("Lamplighter/1. Prepare Assets (importers + DreamLayer hero)")]
        public static void PrepareAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextures();
            ConfigureAudio();
            ImportHero();
            EnsureScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Lamplighter/2. Build WebGL")]
        public static void BuildWebGL()
        {
            var report = Build();
            EditorUtility.DisplayDialog("Lamplighter", $"WebGL build {report.summary.result}: {OutDir}", "OK");
        }

        public static void BuildWebGLFromCli()
        {
            int code = 1;
            var log = new List<string>();
            try
            {
                var report = Build();
                log.Add($"result={report.summary.result}");
                log.Add($"size={report.summary.totalSize}");
                log.Add($"seconds={report.summary.totalTime.TotalSeconds:0}");
                log.Add($"errors={report.summary.totalErrors}");
                foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) log.Add("error: " + m.content);
                code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e)
            {
                log.Add("exception: " + e);
            }
            File.WriteAllLines(Path.Combine(ProjectRoot, "Builds", "build-report.txt"), log);
            EditorApplication.Exit(code);
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static BuildReport Build()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Builds"));
            PrepareAssets();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            return BuildPipeline.BuildPlayer(options);
        }

        // ------------------------------------------------------------------ importers

        private static void ConfigureTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null || ti.userData == ImportMarker) continue;
                bool fx = path.Contains("/fx/");
                bool big = path.Contains("/bg/") || path.Contains("/ui/");
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100f;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 2048;
                // Paintings are large: crunch them. Small props and FX stay lossless.
                ti.textureCompression = big ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
                ti.crunchedCompression = big;
                ti.compressionQuality = 75;

                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; // required for tiled draw mode, cheaper for big quads
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                ti.SetTextureSettings(settings);

                if (path.EndsWith("round_rect.png") || path.EndsWith("round_rect_ring.png")) ti.spriteBorder = new Vector4(10, 10, 10, 10);
                if (fx && path.EndsWith("white.png")) ti.filterMode = FilterMode.Point;
                ti.userData = ImportMarker;
                ti.SaveAndReimport();
            }
        }

        private static void ConfigureAudio()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = (AudioImporter)AssetImporter.GetAtPath(path);
                if (ai == null || ai.userData == ImportMarker) continue;
                ai.forceToMono = true;
                ai.loadInBackground = false;
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = path.Contains("drone") || path.Contains("wind") ? 0.45f : 0.6f;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ai.defaultSampleSettings = s;
                ai.userData = ImportMarker;
                ai.SaveAndReimport();
            }
        }

        /// <summary>
        /// Every ZIP in art/sprites is a DreamLayer sprite-sheet export. The importer slices it,
        /// puts the pivot on the feet, normalises the character to the same world height in every
        /// clip, and writes clips with DreamLayer's exact frame timing.
        /// </summary>
        private static void ImportHero()
        {
            string dir = Path.Combine(ProjectRoot, "art", "sprites");
            var zips = Directory.Exists(dir) ? Directory.GetFiles(dir, "hero_*.zip").OrderBy(p => p).ToArray() : new string[0];
            if (zips.Length == 0) throw new Exception("No hero sprite ZIPs in " + dir);
            Directory.CreateDirectory(Path.Combine(ProjectRoot, HeroFolder));
            var clips = new List<AnimationClip>();
            var lines = new List<string>();
            foreach (var zip in zips)
            {
                var result = DreamLayerSpriteZipImporter.Import(zip, HeroFolder, new DreamLayerSpriteZipImporter.Options
                {
                    CharacterHeightUnits = Player.HeroHeight,
                });
                clips.Add(result.Clip);
                lines.Add($"{Path.GetFileName(zip)}: {result.Sprites.Length} frames, {result.Atlas.TotalSeconds:0.###}s, " +
                          $"{(result.Clip.isLooping ? "loop" : "once")}, {result.PixelsPerUnit:0.#} px/unit, pivot {result.Pivot}");
            }
            DreamLayerCharacterBuilder.Build(clips.ToArray(), HeroFolder, "Hero");
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Builds"));
            File.WriteAllLines(Path.Combine(ProjectRoot, "Builds", "hero-import.txt"), lines);
            Debug.Log("Lamplighter: hero imported\n" + string.Join("\n", lines));
        }

        private static void EnsureScene()
        {
            if (File.Exists(Path.Combine(ProjectRoot, ScenePath))) return;
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Assets/Scenes"));
            // The game builds itself at runtime (GameController.Boot), so the scene stays empty.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void ConfigurePlayer()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "Nabeel Hassan";
            PlayerSettings.productName = "The Last Lamplighter";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.WebGL.template = "PROJECT:Lamplighter";
            // itch.io does not send Content-Encoding for pre-compressed files, so ship gzip with the
            // JavaScript decompression fallback: works on any static host.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Low);
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
        }
    }
}
