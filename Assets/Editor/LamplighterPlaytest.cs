using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lamplighter.EditorTools
{
    /// <summary>
    /// Automated playtest: enters play mode, lets the autopilot play the whole level with real
    /// input and physics, and writes Builds/playtest-report.txt. Exits 0 only if the dawn ending
    /// was reached with no errors.
    ///
    ///   Unity -projectPath . -executeMethod Lamplighter.EditorTools.LamplighterPlaytest.RunFromCli -logFile Builds/playtest.log
    /// </summary>
    [InitializeOnLoad]
    public static class LamplighterPlaytest
    {
        private const string ReportKey = "Lamplighter.Playtest.Report";
        private const float TimeoutSeconds = 150f;

        private static readonly List<string> Lines = new List<string>();
        private static bool _sent, _done, _failed;
        private static double _playStart;

        static LamplighterPlaytest()
        {
            // Entering play mode reloads the domain; pick the run back up afterwards.
            if (!string.IsNullOrEmpty(SessionState.GetString(ReportKey, ""))) Hook();
        }

        [MenuItem("Lamplighter/3. Run Autopilot Playtest")]
        public static void RunFromCli()
        {
            SessionState.SetString(ReportKey, Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Builds", "playtest-report.txt"));
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorUtility.audioMasterMute = true;
            Hook();
            EditorApplication.EnterPlaymode();
        }

        private static void Hook()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("LL_BOT")) Lines.Add(message);
            if (message.StartsWith("LL_BOT done")) _done = true;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Lines.Add($"{type}: {message}");
                _failed = true;
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_playStart <= 0) _playStart = EditorApplication.timeSinceStartup;
            double elapsed = EditorApplication.timeSinceStartup - _playStart;

            if (!_sent && elapsed > 1.5)
            {
                var game = GameObject.Find("Game");
                if (game == null) return;
                game.SendMessage("Cmd", "bot");
                _sent = true;
                Lines.Add($"autopilot started (unity {Application.unityVersion})");
            }

            bool timedOut = elapsed > TimeoutSeconds;
            if (!_done && !timedOut) return;

            if (timedOut && !_done) Lines.Add($"TIMEOUT after {TimeoutSeconds}s");
            bool pass = _done && !_failed;
            Lines.Add(pass ? "PASS" : "FAIL");
            string report = SessionState.GetString(ReportKey, "");
            if (!string.IsNullOrEmpty(report))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report) ?? ".");
                File.WriteAllLines(report, Lines);
            }
            SessionState.EraseString(ReportKey);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
