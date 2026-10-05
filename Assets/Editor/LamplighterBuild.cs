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
        /// Every ZIP in art/sprites is a DreamLayer sprite-sheet export. The importer slices it, puts the
        /// pivot on the feet and normalises the character to the same world height in every sheet.
        /// art/sprites/clips.json then cuts the game's clips (idle, run, jump) out of those sheets: a
        /// frame range (sprite jobs open with an intro) and a length (exports follow video sampling).
        /// One ZIP can feed several clips, and is imported only once.
        /// </summary>
        private static void ImportHero()
        {
            string dir = Path.Combine(ProjectRoot, "art", "sprites");
            var specs = LoadClipSpecs(Path.Combine(dir, "clips.json"));
            if (specs.Length == 0)
                specs = Directory.GetFiles(dir, "hero_*.zip").Select(z => new ClipSpec { name = Path.GetFileNameWithoutExtension(z), zip = Path.GetFileName(z) }).ToArray();
            if (specs.Length == 0) throw new Exception("No hero sprite ZIPs in " + dir);

            // Start clean: everything under Resources ships in the build.
            if (AssetDatabase.IsValidFolder(HeroFolder))
                foreach (var guid in AssetDatabase.FindAssets("", new[] { HeroFolder }))
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, HeroFolder));

            var sheets = new Dictionary<string, DreamLayerSpriteZipImporter.Result>();
            var clips = new List<AnimationClip>();
            var lines = new List<string>();
            foreach (var spec in specs)
            {
                if (!sheets.TryGetValue(spec.zip, out var sheet))
                {
                    sheet = DreamLayerSpriteZipImporter.Import(Path.Combine(dir, spec.zip), HeroFolder,
                        new DreamLayerSpriteZipImporter.Options { CharacterHeightUnits = Player.HeroHeight });
                    sheets[spec.zip] = sheet;
                    AssetDatabase.DeleteAsset(sheet.ClipPath); // the full-sheet clip; the game uses the cut clips below
                    lines.Add($"{spec.zip}: {sheet.Sprites.Length} frames, exported {sheet.Atlas.TotalSeconds:0.###}s, " +
                              $"{sheet.PixelsPerUnit:0.#} px/unit, pivot {sheet.Pivot}");
                }
                int first = Mathf.Clamp(spec.first, 0, sheet.Sprites.Length - 1);
                int count = spec.count > 0 ? Mathf.Min(spec.count, sheet.Sprites.Length - first) : sheet.Sprites.Length - first;
                var sprites = sheet.Sprites.Skip(first).Take(count).ToArray();
                var durations = sheet.Atlas.FrameDurationsMs.Skip(first).Take(count).Select(d => (float)d).ToArray();
                if (spec.seconds > 0f)
                {
                    float k = spec.seconds * 1000f / durations.Sum();
                    durations = durations.Select(d => d * k).ToArray();
                }
                bool loop = spec.loop >= 0 ? spec.loop == 1 : sheet.Atlas.Loops;
                var clip = DreamLayerSpriteZipImporter.BuildClip(sprites, durations, loop);
                clip.name = spec.name;
                AssetDatabase.CreateAsset(clip, $"{HeroFolder}/{spec.name}.anim");
                clips.Add(clip);
                lines.Add($"  {spec.name}: frames {first + 1}-{first + count}, {clip.length:0.###}s, {(loop ? "loop" : "once")}");
            }
            AssetDatabase.SaveAssets();
            DreamLayerCharacterBuilder.Build(clips.ToArray(), HeroFolder, "Hero");
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Builds"));
            File.WriteAllLines(Path.Combine(ProjectRoot, "Builds", "hero-import.txt"), lines);
            Debug.Log("Lamplighter: hero imported\n" + string.Join("\n", lines));
        }

        /// <summary>Per-clip overrides for DreamLayer sprite ZIPs (art/sprites/clips.json).</summary>
        [Serializable]
        private class ClipSpec
        {
            public string name;
            public string zip;
            public int first;
            public int count;
            public float seconds;
            public int loop = -1; // -1 follows the atlas, 0 once, 1 loop
        }

        [Serializable]
        private class ClipSpecList
        {
            public ClipSpec[] clips = new ClipSpec[0];
        }

        private static ClipSpec[] LoadClipSpecs(string path) =>
            File.Exists(path) ? JsonUtility.FromJson<ClipSpecList>(File.ReadAllText(path)).clips ?? new ClipSpec[0] : new ClipSpec[0];

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
