using System;
using UnityEngine;
using CatenaryDC.Api;
using CatenaryDC.catenary;

// The production ticker and subscription/teardown helper are linked unchanged.
// Only the rest of the world and Unity's native object API are test doubles.
namespace CatenaryDC.catenary
{
    public partial class overhead_equipment
    {
        private static overhead_equipment? _system;
        private bool _ready;
        private GameObject? _OCS_ticker;
        public int VisibilityCalls, AuthoringCalls, LoadCalls;
        public float LastPosition;
        internal void authoring_tick() => AuthoringCalls++;
        internal void handle_scenery_visibility(Vector3 p) { VisibilityCalls++; LastPosition = p.x; }
        private void simulate_all_substations_load() => LoadCalls++;
        public static overhead_equipment Start(bool ready = true)
        {
            var w = new overhead_equipment(); _system = w;
            w.start_player_tracker(); w._ready = ready; return w;
        }
        public void Ready(bool value) => _ready = value;
        public void Stop() { _ready = false; stop_player_tracker(); if (ReferenceEquals(_system, this)) _system = null; }
        public void DestroyHost() => _OCS_ticker!.DestroyNative();
        public Action Capture(string name)
        {
            var t = _player_tracker!;
            var m = typeof(OCS_ticker).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            return () =>
            {
                try { m.Invoke(t, null); }
                catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException!; }
            };
        }
        public void UpdateForTest() => Capture("Update")();
        public void FixedForTest() => Capture("FixedUpdate")();
    }
}

internal static class Program
{
    private static int Passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action test)
    {
        PlayerManager.Reset(); CatenaryApi.Step = null; CatenaryApi.Steps = 0;
        test(); Passed++; Console.WriteLine("PASS " + name);
    }
    private static Transform Player(float x = 7) => new() { position = new Vector3(x) };
    public static void Main()
    {
        Run("old null-conditional reproduces destroyed-camera exception", () => {
            var camera = new Camera(); UnityEngine.Object.Destroy(camera);
            Check(!ReferenceEquals(camera, null) && camera == null, "Lifetime double invalid");
            bool failed = false;
            try { var oldView = camera?.transform; }
            catch (NullReferenceException) { failed = true; }
            Check(failed, "Old expression must fail");
        });
        Run("live camera preferred", () => {
            var w = overhead_equipment.Start(); var c = new Camera(); c.transform.position = new Vector3(42);
            PlayerManager.ActiveCamera = c; PlayerManager.PlayerTransform = Player();
            w.UpdateForTest(); Check(w.VisibilityCalls == 1 && w.LastPosition == 42, "Camera precedence"); w.Stop();
        });
        Run("missing camera falls back to live player", () => {
            var w = overhead_equipment.Start(); PlayerManager.PlayerTransform = Player();
            w.UpdateForTest(); Check(w.LastPosition == 7, "Fallback"); w.Stop();
        });
        Run("destroyed camera falls back without get_transform", () => {
            var w = overhead_equipment.Start(); PlayerManager.ActiveCamera = new Camera();
            UnityEngine.Object.Destroy(PlayerManager.ActiveCamera); PlayerManager.PlayerTransform = Player();
            w.UpdateForTest(); Check(w.VisibilityCalls == 1 && w.LastPosition == 7, "Destroyed fallback"); w.Stop();
        });
        Run("destroyed camera transform falls back", () => {
            var w = overhead_equipment.Start(); PlayerManager.ActiveCamera = new Camera();
            UnityEngine.Object.Destroy(PlayerManager.ActiveCamera.transform); PlayerManager.PlayerTransform = Player();
            w.UpdateForTest(); Check(w.LastPosition == 7, "Destroyed transform fallback"); w.Stop();
        });
        Run("absent view skips frame and recovers next frame", () => {
            var w = overhead_equipment.Start(); w.UpdateForTest(); Check(w.VisibilityCalls == 0, "No view");
            PlayerManager.PlayerTransform = Player(); w.UpdateForTest(); Check(w.VisibilityCalls == 1, "Recovery"); w.Stop();
        });
        Run("both destroyed views skip without position access", () => {
            var w = overhead_equipment.Start(); PlayerManager.ActiveCamera = new Camera(); PlayerManager.PlayerTransform = Player();
            UnityEngine.Object.Destroy(PlayerManager.ActiveCamera); UnityEngine.Object.Destroy(PlayerManager.PlayerTransform);
            w.UpdateForTest(); Check(w.VisibilityCalls == 0, "Destroyed view"); w.Stop();
        });
        Run("teleport suspends and resumes visibility", () => {
            var w = overhead_equipment.Start(); PlayerManager.PlayerTransform = Player(); PlayerManager.TeleportStart();
            w.UpdateForTest(); Check(w.VisibilityCalls == 0, "Suspend"); PlayerManager.TeleportFinish();
            w.UpdateForTest(); Check(w.VisibilityCalls == 1, "Resume"); w.Stop();
        });
        Run("unready world does no work", () => {
            var w = overhead_equipment.Start(false); PlayerManager.PlayerTransform = Player(); w.UpdateForTest(); w.FixedForTest();
            Check(w.VisibilityCalls + w.AuthoringCalls + w.LoadCalls + CatenaryApi.Steps == 0, "Unready work");
            w.Ready(true); w.UpdateForTest(); w.FixedForTest(); Check(w.VisibilityCalls == 1 && w.LoadCalls == 1, "Ready recovery"); w.Stop();
        });
        Run("deferred old ticker cannot tick replacement world", () => {
            var old = overhead_equipment.Start(); var update = old.Capture("Update"); var tick = old.Capture("FixedUpdate");
            old.Stop(); var current = overhead_equipment.Start(); PlayerManager.PlayerTransform = Player();
            update(); tick(); Check(current.VisibilityCalls + current.AuthoringCalls + current.LoadCalls + CatenaryApi.Steps == 0, "Stale tick");
            current.UpdateForTest(); Check(current.VisibilityCalls == 1, "Current ticker"); current.Stop();
        });
        Run("owner mismatch blocks old ticker even before stop", () => {
            var old = overhead_equipment.Start(); var current = overhead_equipment.Start(); PlayerManager.PlayerTransform = Player();
            old.UpdateForTest(); old.FixedForTest(); Check(old.AuthoringCalls + current.AuthoringCalls + CatenaryApi.Steps == 0, "Owner isolation");
            old.Stop(); current.UpdateForTest(); Check(current.VisibilityCalls == 1, "New world retained"); current.Stop();
        });
        Run("destroyed tracker still unsubscribes all delegates", () => {
            var w = overhead_equipment.Start(); Check(PlayerManager.Listeners == 3, "Subscriptions");
            w.DestroyHost(); PlayerManager.ChangePlayer(); w.Stop(); w.Stop(); Check(PlayerManager.Listeners == 0, "Leaked listeners");
        });
        Run("twenty load-unload cycles retain only current listeners", () => {
            for (int i = 0; i < 20; i++) {
                var w = overhead_equipment.Start(); Check(PlayerManager.Listeners == 3, "Duplicate listeners");
                PlayerManager.PlayerTransform = Player(i); PlayerManager.ChangePlayer(); w.UpdateForTest();
                Check(w.VisibilityCalls == 1 && w.LastPosition == i, "Reload view");
                w.Stop(); Check(PlayerManager.Listeners == 0, "Unsubscribe");
            }
        });
        Run("simulation callback can unload safely", () => {
            var w = overhead_equipment.Start(); CatenaryApi.Step = w.Stop; w.FixedForTest();
            Check(CatenaryApi.Steps == 1 && w.LoadCalls == 0, "Disposed simulation");
        });
        Run("simulation callback cannot transfer tick to new world", () => {
            var old = overhead_equipment.Start(); overhead_equipment? next = null;
            CatenaryApi.Step = () => { old.Stop(); next = overhead_equipment.Start(); };
            old.FixedForTest(); Check(old.LoadCalls == 0 && next!.LoadCalls == 0, "Cross-world simulation"); next!.Stop();
        });
        Console.WriteLine($"{Passed} player-tracking tests passed (Unity lifetime doubles; not an in-game test).");
    }
}
