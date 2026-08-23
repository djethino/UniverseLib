using System;
using UnityEngine;
using UnityEngine.UI;
// ⚠ Il2CppInterop is ONE of the two IL2CPP chains; `#if CPP` covers both — see FocusRing.
#if INTEROP
using Il2CppInterop.Runtime.Injection;
#endif
#if UNHOLLOWER
using UnhollowerRuntimeLib;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Makes a wrapping label reserve the vertical space it actually draws, by keeping its
    /// LayoutElement.minHeight at the text's preferred height for the current width.
    ///
    /// Needed because labels here render with <see cref="VerticalWrapMode.Overflow"/> (so a label
    /// slightly too tall for its row is never culled): a value that wraps onto a second line is
    /// drawn past its allotted row and overlaps whatever follows it.
    ///
    /// Use for text that must stay fully readable — long URLs, disclaimers, hints — rather than
    /// trimming it. Add via <see cref="UIFactory.ConfigureAutoHeight"/>.
    /// </summary>
    public class LabelHeightFitter : MonoBehaviour
    {
        /// <summary>Extra space kept below the text, in pixels.</summary>
        public float ExtraHeight { get; set; }

        private Text _label;
        private LayoutElement _layout;
        private string _lastText;
        private float _lastWidth = -1f;
        private float _callerMinHeight = -1f;

#if CPP
        public LabelHeightFitter(IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<LabelHeightFitter>();
                Registered = true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[LabelHeightFitter] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        private void LateUpdate()
        {
            if (_label == null)
            {
                _label = GetComponent<Text>();
                if (_label == null) return;
            }

            if (_layout == null)
            {
                _layout = GetComponent<LayoutElement>();
                if (_layout == null) _layout = gameObject.AddComponent<LayoutElement>();
                _callerMinHeight = _layout.minHeight; // one row: the floor we never go below
            }

            float width = _label.rectTransform.rect.width;
            if (width <= 0f) return;

            if (_label.text == _lastText && Mathf.Approximately(width, _lastWidth)) return;
            _lastText = _label.text;
            _lastWidth = width;

            float needed = _label.preferredHeight + ExtraHeight;
            _layout.minHeight = Mathf.Max(_callerMinHeight, needed);
        }

        /// <summary>Re-evaluate now (e.g. after changing font size).</summary>
        public void Refresh() => _lastWidth = -1f;
    }
}
