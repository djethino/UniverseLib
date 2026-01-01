using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Adds hover color effect to UI elements. Works on both Mono and IL2CPP.
    /// For Mono: Uses IPointerEnterHandler/IPointerExitHandler interfaces.
    /// For IL2CPP: Uses registered type with interface implementations.
    /// </summary>
#if MONO
    public class HoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
#else
    public class HoverEffect : MonoBehaviour
#endif
    {
        private Image _targetImage;
        private Color _normalColor;
        private Color _hoverColor;
        private bool _isInitialized;

#if CPP
        public HoverEffect(System.IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<HoverEffect>(new RegisterTypeOptions
                {
                    Interfaces = new System.Type[] { typeof(IPointerEnterHandler), typeof(IPointerExitHandler) }
                });
                Registered = true;
            }
            catch (System.Exception ex)
            {
                Universe.LogWarning($"[HoverEffect] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        /// <summary>
        /// Initialize the hover effect with colors.
        /// </summary>
        public void Initialize(Color normalColor, Color hoverColor)
        {
            _targetImage = GetComponent<Image>();
            if (_targetImage == null)
            {
                Universe.LogWarning("[HoverEffect] No Image component found on GameObject");
                return;
            }

            _normalColor = normalColor;
            _hoverColor = hoverColor;
            _targetImage.color = normalColor;
            _isInitialized = true;
        }

        /// <summary>
        /// Update colors after initialization (e.g., when selection state changes).
        /// </summary>
        public void SetColors(Color normalColor, Color hoverColor)
        {
            _normalColor = normalColor;
            _hoverColor = hoverColor;

            if (_targetImage != null)
            {
                _targetImage.color = normalColor;
            }
        }

        /// <summary>
        /// Set the normal color and apply it immediately.
        /// </summary>
        public void SetNormalColor(Color color)
        {
            _normalColor = color;
            if (_targetImage != null)
            {
                _targetImage.color = color;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_isInitialized && _targetImage != null)
            {
                _targetImage.color = _hoverColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_isInitialized && _targetImage != null)
            {
                _targetImage.color = _normalColor;
            }
        }
    }
}
