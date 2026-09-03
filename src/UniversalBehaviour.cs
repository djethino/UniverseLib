using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
#if INTEROP
using Il2CppInterop.Runtime.Injection;
#endif
#if UNHOLLOWER
using UnhollowerRuntimeLib;
#endif

namespace UniverseLib
{
    /// <summary>
    /// Used for receiving Update events and starting Coroutines.
    /// </summary>
    internal class UniversalBehaviour : MonoBehaviour
    {
        internal static UniversalBehaviour Instance { get; private set; }
        internal static volatile bool Quitting;

        internal static void Setup()
        {
#if CPP
            ClassInjector.RegisterTypeInIl2Cpp<UniversalBehaviour>();
#endif

            GameObject obj = new("UniverseLibBehaviour");
            GameObject.DontDestroyOnLoad(obj);
            obj.hideFlags |= HideFlags.HideAndDontSave;
            Instance = obj.AddComponent<UniversalBehaviour>();
        }

        static IEnumerator pendingStartup;

        /// <summary>
        /// Start this coroutine on the first frame the engine runs this behaviour (Unity's
        /// Start), which may be later than now when Init is called before the engine is up.
        /// </summary>
        internal static void RunOnFirstFrame(IEnumerator routine)
        {
            pendingStartup = routine;
        }

        internal void Start()
        {
            IEnumerator routine = pendingStartup;
            pendingStartup = null;
            // Through the runtime provider: on IL2CPP a managed IEnumerator is not Unity's.
            if (routine != null)
                RuntimeHelper.Instance.Internal_StartCoroutine(routine);
        }

        internal void Update()
        {
            Universe.Update();
        }

        internal void OnApplicationQuit()
        {
            Quitting = true;
            StopAllCoroutines();
        }

#if CPP
        public UniversalBehaviour(IntPtr ptr) : base(ptr) { }

        static Delegate queuedDelegate;

        internal static void InvokeDelegate(Delegate method)
        {
            queuedDelegate = method;
            Instance.Invoke(nameof(InvokeQueuedAction), 0f);
        }

        void InvokeQueuedAction()
        {
            try
            {
                Delegate method = queuedDelegate;
                queuedDelegate = null;
                method?.DynamicInvoke();
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"Exception invoking action from IL2CPP thread: {ex}");
            }
        }
#endif
    }
}
