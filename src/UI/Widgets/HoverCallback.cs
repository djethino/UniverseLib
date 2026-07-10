using UnityEngine;
using UnityEngine.EventSystems;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Reports pointer enter/exit to static events, keyed by the GameObject's instance ID.
    /// Consumers map the IDs to their own data (e.g. contextual help texts).
    /// Works on both Mono and IL2CPP (same dual-build pattern as HoverEffect).
    /// </summary>
#if MONO
    public class HoverCallback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
#else
    public class HoverCallback : MonoBehaviour
#endif
    {
        public static event System.Action<int> PointerEntered;
        public static event System.Action<int> PointerExited;

#if CPP
        public HoverCallback(System.IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<HoverCallback>(new RegisterTypeOptions
                {
                    Interfaces = new System.Type[] { typeof(IPointerEnterHandler), typeof(IPointerExitHandler) }
                });
                Registered = true;
            }
            catch (System.Exception ex)
            {
                Universe.LogWarning($"[HoverCallback] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        public void OnPointerEnter(PointerEventData eventData)
        {
            PointerEntered?.Invoke(gameObject.GetInstanceID());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            PointerExited?.Invoke(gameObject.GetInstanceID());
        }
    }
}
