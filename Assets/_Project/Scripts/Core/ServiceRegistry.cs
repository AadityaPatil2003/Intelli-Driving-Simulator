using System;
using System.Collections.Generic;
using UnityEngine;

namespace IDS.Core
{
    /// <summary>
    /// Minimal service locator. Exists so that a test scene containing only ONE
    /// stream's components still runs: anything missing simply resolves to null
    /// and the caller degrades gracefully.
    ///
    /// This is deliberately not a DI framework. Register in Awake, resolve in
    /// Start — never the other way round.
    ///
    /// OWNER: Aaditya.
    /// </summary>
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> Services = new();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            Services[typeof(T)] = service;
        }

        public static T Resolve<T>() where T : class
            => Services.TryGetValue(typeof(T), out var s) ? s as T : null;

        /// Resolve, or log a clear message naming who owns the missing piece.
        public static T Require<T>(string ownedBy) where T : class
        {
            var s = Resolve<T>();
            if (s == null)
                Debug.LogWarning($"[ServiceRegistry] {typeof(T).Name} not registered " +
                                 $"(owner: {ownedBy}). Running in degraded mode.");
            return s;
        }

        public static void Clear() => Services.Clear();
    }
}
