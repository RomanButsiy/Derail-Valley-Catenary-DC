// Deliberately small lifetime/event doubles, NOT a Unity/game simulation.
using System;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object? a, Object? b)
        {
            if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) || b.Destroyed;
            if (ReferenceEquals(b, null)) return a.Destroyed;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
        public static void Destroy(Object obj) => obj.Destroyed = true;
    }
    public struct Vector3 { public float x; public Vector3(float x) { this.x = x; } }
    public class Transform : Object
    {
        private Vector3 _position;
        public Vector3 position
        {
            get { if (Destroyed) throw new NullReferenceException("Destroyed Transform.position"); return _position; }
            set { _position = value; }
        }
    }
    public class Component : Object
    {
        private readonly Transform _transform = new();
        public Transform transform
        {
            get { if (Destroyed) throw new NullReferenceException("Destroyed Component.get_transform"); return _transform; }
        }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class Camera : Component { }
    public class GameObject : Object
    {
        private readonly Component _component;
        public GameObject(string name, Type type) => _component = (Component)Activator.CreateInstance(type, nonPublic: true)!;
        public T GetComponent<T>() where T : Component
        {
            if (Destroyed) throw new NullReferenceException("Destroyed GameObject.GetComponent");
            return (T)_component;
        }
        internal void DestroyNative()
        {
            Destroyed = true;
            _component.Destroyed = true;
        }
    }
}

public static class PlayerManager
{
    public static UnityEngine.Camera? ActiveCamera;
    public static UnityEngine.Transform? PlayerTransform;
    public static event Action? PlayerTeleportStarted, PlayerTeleportFinished, PlayerChanged;
    public static int Listeners => (PlayerTeleportStarted?.GetInvocationList().Length ?? 0) +
        (PlayerTeleportFinished?.GetInvocationList().Length ?? 0) + (PlayerChanged?.GetInvocationList().Length ?? 0);
    public static void TeleportStart() => PlayerTeleportStarted?.Invoke();
    public static void TeleportFinish() => PlayerTeleportFinished?.Invoke();
    public static void ChangePlayer() => PlayerChanged?.Invoke();
    public static void Reset()
    {
        ActiveCamera = null; PlayerTransform = null;
        PlayerTeleportStarted = null; PlayerTeleportFinished = null; PlayerChanged = null;
    }
}

namespace CatenaryDC.Api
{
    public static class CatenaryApi
    {
        public static Action? Step;
        public static int Steps;
        public static void RaiseSimulationStep() { Steps++; Step?.Invoke(); }
    }
}
