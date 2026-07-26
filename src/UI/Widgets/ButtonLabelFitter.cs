using System;
using UnityEngine;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Keeps a button wide enough for its label plus a breathing margin, so the text never runs
    /// into the edges. Padding the label alone would only squeeze the text; the button has to be
    /// allowed to grow.
    ///
    /// The width is re-evaluated whenever the label changes, which matters when labels are
    /// translated at runtime — the same button holds a short word in one language and a long one
    /// in another.
    ///
    /// Only ever grows past the minWidth the caller asked for, never below it.
    /// </summary>
    public class ButtonLabelFitter : MonoBehaviour
    {
        /// <summary>Horizontal breathing room on each side of the label, in pixels.</summary>
        public float Padding { get; set; } = UIFactory.ButtonLabelPadding;

        private Text _label;
        private LayoutElement _layout;
        private string _lastText;
        private float _callerMinWidth = -1f;

#if CPP
        public ButtonLabelFitter(IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ButtonLabelFitter>();
                Registered = true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[ButtonLabelFitter] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        private void LateUpdate()
        {
            if (_label == null)
            {
                _label = GetComponentInChildren<Text>();
                if (_label == null) return;
            }

            // The caller adds its LayoutElement after the button is built, so pick it up lazily and
            // remember its minWidth once — reading it back later would ratchet the button wider
            // every time we grow it.
            if (_layout == null)
            {
                _layout = GetComponent<LayoutElement>();
                if (_layout == null) return; // width not layout-driven: nothing to widen
            }
            if (_callerMinWidth < 0f) _callerMinWidth = _layout.minWidth;

            if (_label.text == _lastText) return;
            _lastText = _label.text;

            // Two widths, not one. The breathing margin goes to preferredWidth, which a layout
            // group may give up when the row runs out of room; minWidth only guarantees the label
            // itself fits. Pushing the padded width into minWidth made it a hard floor, so a row
            // of translated buttons overflowed its parent instead of tightening — and a
            // HorizontalLayoutGroup always overflows to the right, leaving the last button glued
            // to the window edge while the first kept its margin.
            float labelWidth = _label.preferredWidth;
            float floor = Mathf.Max(_callerMinWidth, labelWidth);
            _layout.minWidth = floor;
            _layout.preferredWidth = Mathf.Max(floor, labelWidth + (Padding * 2f));
        }

        /// <summary>Re-evaluate now (e.g. after changing font size).</summary>
        public void Refresh() => _lastText = null;
    }
}
