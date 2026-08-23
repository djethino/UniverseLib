using System;
using UnityEngine;
using UnityEngine.UI;
// ⚠ Il2CppInterop is ONE of the two IL2CPP chains; `#if CPP` covers both. Naming it here is what
// stopped the Unhollower configuration compiling — see FocusRing for the full account.
#if INTEROP
using Il2CppInterop.Runtime.Injection;
#endif
#if UNHOLLOWER
using UnhollowerRuntimeLib;
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
            // 🔴 **Whatever shares the row with the label takes room the label cannot have.**
            // This class guarantees the label fits; measuring the label alone made that guarantee
            // false the moment a caller laid anything beside it — icons, a scope strip, a counter.
            // The button was then sized to the TEXT while the text only got what the neighbours
            // left, and it ran straight out of the coloured rectangle. Adding a fixed allowance in
            // the caller cannot fix it either: these two lines overwrite that allowance on the
            // next label change, which is exactly when a long translated word needs it most.
            float labelWidth = _label.preferredWidth + NeighboursWidth();
            float floor = Mathf.Max(_callerMinWidth, labelWidth);
            _layout.minWidth = floor;
            _layout.preferredWidth = Mathf.Max(floor, labelWidth + (Padding * 2f));
        }

        /// <summary>
        /// How much of the row is taken by things that are not the label.
        ///
        /// Zero for an ordinary button, where the label is the only child — so nothing changes for
        /// the buttons that have always worked. It only ever reports what a horizontal layout group
        /// is actually arranging: without one, children are anchored rather than laid out in a row,
        /// and they take no room from the label.
        ///
        /// ⚠ Reads each child through <see cref="LayoutUtility"/> rather than its painted width:
        /// this runs before the first arrangement, when a rect is still zero and a preferred size
        /// is already known.
        /// </summary>
        private float NeighboursWidth()
        {
            HorizontalLayoutGroup row = GetComponent<HorizontalLayoutGroup>();
            if (row == null) return 0f;

            float total = row.padding.left + row.padding.right;
            int counted = 0;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == _label.transform || !child.gameObject.activeSelf) continue;

                LayoutElement element = child.GetComponent<LayoutElement>();
                if (element != null && element.ignoreLayout) continue;

                RectTransform rect = child as RectTransform;
                if (rect == null) continue;

                total += LayoutUtility.GetPreferredWidth(rect);
                counted++;
            }

            // One gap per neighbour: the spacing between the label and the first of them, and
            // between each pair.
            if (counted > 0) total += row.spacing * counted;

            return total;
        }

        /// <summary>Re-evaluate now (e.g. after changing font size).</summary>
        public void Refresh() => _lastText = null;
    }
}
