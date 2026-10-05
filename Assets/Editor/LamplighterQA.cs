using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lamplighter.EditorTools
{
    /// <summary>
    /// Automated QA: enters play mode and plays through every mechanic with real input and physics
    /// (jumps, the first crates, embers, falls, gloom deaths, wisps, flares, the locked final lamp,
    /// pause/restart/quit, mute, the dawn ending, a second run, and a full no-cheat playthrough).
    /// Writes Builds/qa-report.txt and exits 0 only if every check passes.
    ///
    ///   Unity -projectPath . -executeMethod Lamplighter.EditorTools.LamplighterQA.RunFromCli -logFile Builds/qa.log
    /// </summary>
    [InitializeOnLoad]
    public static class LamplighterQA
    {
        private const string ReportKey = "Lamplighter.QA.Report";

        [Serializable]
        private class State
        {
            public string mode;
            public bool paused, dying;
            public float x, y, flame;
            public int[] lit = new int[0];
            public int wispsAlive, embers, deaths, burned;
            public float fps, timeScale, nearestWisp;
        }

        private static readonly List<string> Lines = new List<string>();
        private static int _pass, _fail, _errors;
        private static State _last;
        private static bool _botDone;
        private static string _botDoneLine;
        private static readonly Stack<IEnumerator> _run = new Stack<IEnumerator>();
        private static bool _started;
        private static double _waitUntil;
        private static GameObject _game;

        static LamplighterQA()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(ReportKey, ""))) Hook();
        }

        [MenuItem("Lamplighter/4. Run QA Suite")]
        public static void RunFromCli()
        {
            SessionState.SetString(ReportKey, Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Builds", "qa-report.txt"));
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
            if (message.StartsWith("LL_STATE ")) _last = JsonUtility.FromJson<State>(message.Substring(9).Trim());
            else if (message.StartsWith("LL_BOT done")) { _botDone = true; _botDoneLine = message; }
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                Lines.Add($"ERROR {type}: {message}");
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (!_started)
            {
                _started = true;
                _run.Push(Suite());
                _waitUntil = EditorApplication.timeSinceStartup + 1.5;
            }
            if (EditorApplication.timeSinceStartup < _waitUntil) return;
            // Step the innermost coroutine; nested IEnumerators run to completion before their parent resumes.
            while (_run.Count > 0)
            {
                var top = _run.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    Lines.Add("FAIL suite crashed: " + e);
                    _fail++;
                    _run.Clear();
                    break;
                }
                if (!more)
                {
                    _run.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    _run.Push(nested);
                    continue;
                }
                _waitUntil = top.Current is float secs ? EditorApplication.timeSinceStartup + secs : 0;
                return;
            }
            Finish();
        }

        private static void Finish()
        {
            Lines.Add($"errors logged during play: {_errors}");
            bool ok = _fail == 0 && _errors == 0;
            Lines.Insert(0, $"{(ok ? "PASS" : "FAIL")}  {_pass} passed, {_fail} failed, {_errors} errors  (unity {Application.unityVersion})");
            string report = SessionState.GetString(ReportKey, "");
            if (!string.IsNullOrEmpty(report))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report) ?? ".");
                File.WriteAllLines(report, Lines);
            }
            SessionState.EraseString(ReportKey);
            EditorApplication.update -= Tick;
            Autopilot.Active = false;
            Autopilot.Manual = false;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // ------------------------------------------------------------------ helpers

        private static void Cmd(string c) => _game.SendMessage("Cmd", c);

        private static State S()
        {
            Cmd("state");
            return _last;
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++;
            else _fail++;
            Lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
        }

        private static void SetInput(int dir, bool jump)
        {
            Autopilot.Active = true;
            Autopilot.Manual = true;
            Autopilot.Dir = dir;
            Autopilot.Jump = jump;
        }

        private static void ReleaseInput()
        {
            Autopilot.Dir = 0;
            Autopilot.Jump = false;
            Autopilot.Active = false;
            Autopilot.Manual = false;
        }

        private static string Lit(State s) => string.Concat(s.lit.Select(v => v.ToString()));

        /// <summary>Highest point (smallest design y) reached while jumping from rest.</summary>
        private static IEnumerator MeasureJump(float hold, float[] result)
        {
            float ground = S().y;
            float minY = ground;
            SetInput(0, true);
            double end = EditorApplication.timeSinceStartup + 1.4;
            double release = EditorApplication.timeSinceStartup + hold;
            while (EditorApplication.timeSinceStartup < end)
            {
                if (EditorApplication.timeSinceStartup >= release) Autopilot.Jump = false;
                minY = Mathf.Min(minY, S().y);
                yield return null;
            }
            ReleaseInput();
            result[0] = ground - minY;
        }

        // ------------------------------------------------------------------ the suite

        private static IEnumerator Suite()
        {
            // Boot
            for (int i = 0; i < 200 && (_game = GameObject.Find("Game")) == null; i++) yield return 0.05f;
            for (int i = 0; i < 100 && (S() == null || S().mode != "Title"); i++) yield return 0.05f;
            Check("boot", _last != null && _last.mode == "Title", "title screen up");

            // ---- start a run
            Cmd("key space");
            yield return 2.2f;
            var s = S();
            Check("start", s.mode == "Playing" && Mathf.Abs(s.x - 170) < 2 && Lit(s) == "000000" && s.wispsAlive == 10 && s.deaths == 0,
                $"mode {s.mode}, x {s.x:0}, lit {Lit(s)}, wisps {s.wispsAlive}");
            Check("standing on the street", Mathf.Abs(s.y - 640) < 3, $"feet at y {s.y:0.0} (street top 640)");

            // ---- the flame drains in the dark
            float f0 = S().flame;
            yield return 2f;
            float f1 = S().flame;
            Check("flame drains in the dark", f0 - f1 > 0.04f && f0 - f1 < 0.08f, $"{f0:0.000} -> {f1:0.000} over 2 s");

            // ---- jump heights
            var jr = new float[1];
            yield return MeasureJump(0.7f, jr);
            float full = jr[0];
            Check("full jump height", full > 125 && full < 145, $"{full:0} px (design 136)");
            yield return 0.8f;
            yield return MeasureJump(0.06f, jr);
            Check("tap jump is a short hop", jr[0] > 25 && jr[0] < full - 30, $"{jr[0]:0} px");

            // ---- the first crates (now 80 px) with a held jump, then the ember on top
            Cmd("warp 760 600");
            yield return 1.6f;
            s = S();
            Check("first lamp lights on touch", s.lit[0] == 1, $"lit {Lit(s)}");
            SetInput(1, false);
            for (int i = 0; i < 300 && S().x < 872; i++) yield return null;
            Autopilot.Jump = true;
            bool onCrate = false;
            int embersBefore = S().embers;
            double until = EditorApplication.timeSinceStartup + 2.2;
            double releaseAt = EditorApplication.timeSinceStartup + 0.35;
            while (EditorApplication.timeSinceStartup < until)
            {
                if (EditorApplication.timeSinceStartup > releaseAt) Autopilot.Jump = false;
                s = S();
                if (s.x > 945 && s.x < 1125 && Mathf.Abs(s.y - 560) < 3) onCrate = true;
                if (s.x > 1250) break;
                yield return null;
            }
            ReleaseInput();
            Check("held jump lands on the first crates", onCrate, "feet on the crate top (y 560)");
            Check("ember on the crates is collected", S().embers > embersBefore, $"embers {embersBefore} -> {S().embers}");

            // ---- falling into the canal
            int d0 = S().deaths;
            Cmd("warp 2060 560");
            yield return 2.6f;
            s = S();
            Check("canal fall costs a life", s.deaths == d0 + 1, $"deaths {d0} -> {s.deaths}");
            Check("respawn at the last lamp", Mathf.Abs(s.x - 650) < 15 && !s.dying, $"x {s.x:0} (checkpoint 650)");

            // ---- the gloom takes you when the flame runs out
            Cmd("warp 1450 600");
            yield return 0.8f;
            d0 = S().deaths;
            Cmd("flame 0.01");
            yield return 2.6f;
            s = S();
            Check("flame out = gloom death", s.deaths == d0 + 1, $"deaths {d0} -> {s.deaths}");
            Check("respawn rekindles the lantern", s.flame >= 0.55f && Mathf.Abs(s.x - 650) < 15, $"flame {s.flame:0.00}, x {s.x:0}");

            // ---- wisps hunt and hurt; a flare burns them
            Cmd("warp 1640 600");
            yield return 0.3f;
            bool hurt = false;
            float prev = S().flame;
            until = EditorApplication.timeSinceStartup + 8;
            while (EditorApplication.timeSinceStartup < until && !hurt)
            {
                float f = S().flame;
                if (prev - f > 0.15f) hurt = true;
                prev = f;
                yield return null;
            }
            Check("a wisp hunts the lantern and hurts", hurt, hurt ? "lost ~0.22 flame on contact" : "no hit within 8 s");
            // It must back off instead of clinging: no second hit for 1.8 s (invulnerability alone
            // covers 1.1 s), and it drifts away from the lantern.
            float afterHit = S().flame;
            bool hitAgain = false;
            float far = 0f;
            until = EditorApplication.timeSinceStartup + 1.8;
            while (EditorApplication.timeSinceStartup < until)
            {
                s = S();
                if (afterHit - s.flame > 0.15f) hitAgain = true;
                far = Mathf.Max(far, s.nearestWisp);
                yield return null;
            }
            Check("the wisp recoils after a hit", !hitAgain && far > 90f, $"second hit {hitAgain}, backed off to {far:0} px");
            int b0 = S().burned;
            Cmd("flare");
            yield return 0.7f;
            s = S();
            Check("flare burns the wisp", s.burned > b0, $"burned {b0} -> {s.burned}");

            // ---- the ends of the town are walls, not drops
            d0 = S().deaths;
            Cmd("warp 6300 600");
            yield return 0.6f;
            SetInput(1, false);
            yield return 1.8f;
            float rightX = S().x;
            Cmd("warp 120 600");
            yield return 0.6f;
            SetInput(-1, false);
            yield return 1.8f;
            float leftX = S().x;
            ReleaseInput();
            s = S();
            Check("world edges are walls", rightX <= 6400 && leftX >= 0 && s.deaths == d0 && Mathf.Abs(s.y - 640) < 3,
                $"stopped at x {rightX:0} (right) and {leftX:0} (left), deaths {d0} -> {s.deaths}");

            // ---- the bell-tower lamp stays dark until every other lamp is lit
            Cmd("light 5");
            yield return 1.4f;
            Check("final lamp refuses early", S().lit[5] == 0, $"lit {Lit(S())}");

            // ---- pause
            Cmd("key esc");
            yield return 0.4f;
            s = S();
            float px = s.x;
            Check("pause", s.paused && s.timeScale == 0, $"paused {s.paused}, timeScale {s.timeScale}");
            yield return 1f;
            Check("nothing moves while paused", Mathf.Abs(S().x - px) < 0.01f, $"x {px:0.0} -> {S().x:0.0}");
            Cmd("key esc");
            yield return 0.4f;
            s = S();
            Check("resume", !s.paused && s.timeScale == 1, $"paused {s.paused}, timeScale {s.timeScale}");

            // ---- R: back to the last lamp, not a death
            d0 = S().deaths;
            Cmd("key r");
            yield return 2.2f;
            s = S();
            Check("R returns to the last lamp", Mathf.Abs(s.x - 650) < 15 && s.deaths == d0, $"x {s.x:0}, deaths {d0} -> {s.deaths}");

            // ---- mute
            Cmd("key m");
            yield return 0.2f;
            bool muted = GameAudio.I.Muted && AudioListener.volume == 0f;
            Cmd("key m");
            yield return 0.2f;
            Check("M mutes and unmutes", muted && !GameAudio.I.Muted && AudioListener.volume == 1f, "listener volume 0 then 1");

            // ---- light the town, then the bell tower: dawn and the end card
            foreach (int i in new[] { 1, 2, 3, 4 })
            {
                Cmd("light " + i);
                yield return 1.6f;
            }
            s = S();
            Check("lamps 1-5 lit", Lit(s) == "111110", $"lit {Lit(s)}");
            Cmd("light 5");
            yield return 1.2f;
            s = S();
            Check("bell tower lamp starts the dawn", s.mode == "Ending" && s.lit[5] == 1, $"mode {s.mode}, lit {Lit(s)}");
            yield return 9f;
            s = S();
            Check("end card", s.mode == "End" && s.wispsAlive == 0, $"mode {s.mode}, wisps left {s.wispsAlive}");

            // ---- Space on the end card: back to the title, then a clean second run
            yield return 1f;
            Cmd("key space");
            yield return 1.6f;
            Check("end card -> title", S().mode == "Title", $"mode {S().mode}");
            Cmd("key space");
            yield return 2.2f;
            s = S();
            Check("second run starts fresh", s.mode == "Playing" && Lit(s) == "000000" && s.deaths == 0 && s.embers == 0 && s.wispsAlive == 10 &&
                                             Mathf.Abs(s.x - 170) < 2 && s.flame > 0.8f,
                $"lit {Lit(s)}, deaths {s.deaths}, embers {s.embers}, wisps {s.wispsAlive}, x {s.x:0}, flame {s.flame:0.00}");

            // ---- Esc then Q quits to the title
            Cmd("key esc");
            yield return 0.3f;
            Cmd("key q");
            yield return 1.6f;
            Check("pause + Q quits to the title", S().mode == "Title" && S().timeScale == 1, $"mode {S().mode}");
            Cmd("key space");
            yield return 2.2f;
            Check("third run starts", S().mode == "Playing" && Lit(S()) == "000000", $"mode {S().mode}, lit {Lit(S())}");

            // ---- a full playthrough with no god mode: real flame, real wisps. The autopilot only runs
            // forward, so a wisp that knocks it off the rooftops makes it skip a lamp it never goes back
            // for (a person would). It gets a second attempt from the title; failing both means trouble.
            bool reached = false;
            int attempts = 0;
            while (attempts < 2 && !reached)
            {
                attempts++;
                if (attempts > 1)
                {
                    Cmd("bot");
                    Cmd("key esc");
                    yield return 0.3f;
                    Cmd("key q");
                    yield return 1.6f;
                    Cmd("key space");
                    yield return 2.2f;
                }
                _botDone = false;
                Cmd("bot nogod");
                double start = EditorApplication.timeSinceStartup, lastProgress = start;
                string lastLit = "";
                while (!_botDone && EditorApplication.timeSinceStartup - start < 150)
                {
                    s = S();
                    if (Lit(s) != lastLit)
                    {
                        lastLit = Lit(s);
                        lastProgress = EditorApplication.timeSinceStartup;
                    }
                    if (EditorApplication.timeSinceStartup - lastProgress > 45) break;
                    yield return 0.5f;
                }
                reached = _botDone;
                s = S();
                Lines.Add($"      autopilot attempt {attempts}: " + (reached ? _botDoneLine : $"stalled at x {s.x:0}, lit {Lit(s)}, deaths {s.deaths}"));
            }
            Check("full no-cheat playthrough reaches the dawn", reached, $"{(reached ? _botDoneLine : "no attempt reached the dawn")} (attempt {attempts} of 2)");
            yield return 2f;
        }
    }
}
