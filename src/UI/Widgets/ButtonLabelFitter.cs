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

        // 🔴 **What the width actually depends on — not just the text.** Re-measuring only when the
        // string changes assumes the same string is always the same number of pixels, and it is not:
        // a substituted font, a font size, or a control laid beside the label all move it while the
        // text stands still. A button then keeps a width computed for something it no longer holds,
        // and the label runs out of the coloured rectangle with nothing to say so.
        //
        // ⚠ Reconciled from the state rather than driven by transitions. Watching for the events
        // that *should* change a width means the one nobody thought of is invisible for good; asking
        // "is the width still right" costs a comparison per frame and cannot miss.
        private Font _lastFont;
        private int _lastFontSize;
        private int _lastNeighbours;
        private float _written = -1f;

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

        /// <summary>
        /// 🔴 **A safety net, not the mechanism.** An injected MonoBehaviour does not reliably get
        /// Unity's callbacks on every IL2CPP runtime, and a width that only ever arrives through one
        /// is a width that silently never arrives — which is what shipped: on a game where this
        /// never ran, every button kept the size its layout group worked out on its own, and three
        /// labels out of four ran outside their rectangle. The measurement is now DRIVEN
        /// (<see cref="Apply"/>), called at the moment something changes; this keeps polling as a
        /// second chance for whatever a caller forgets to announce.
        /// </summary>
        private void LateUpdate() => Apply();

        /// <summary>
        /// Measure now. Cheap and idempotent: it returns immediately unless something the width
        /// depends on has actually moved, so calling it on every refresh costs nothing.
        /// </summary>
        public void Apply()
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

            // 🔴 **A button that lays its content out in a row is measured by its consumer — stay
            // out of it.** Two things writing one width is how they end up disagreeing, and here
            // they did: the consumer knows what it laid beside the label because it put it there,
            // while this class has to walk the children to find out — a walk that returns "no
            // neighbours at all" on IL2CPP, where a Transform does not cast to RectTransform. Every
            // such button came out short by exactly what the walk missed, with nothing to say so.
            //
            // ⚠ The case this class is still for is the ORDINARY button, whose label is anchored to
            // fill it: no group, nothing summing anything, and a button that would keep whatever
            // minimum its caller asked for however long a translated label turned out to be.
            if (GetComponent<HorizontalLayoutGroup>() != null) return;

            if (_callerMinWidth < 0f) _callerMinWidth = _layout.minWidth;

            // Everything the measurement rests on. Counting the neighbours (rather than measuring
            // them) keeps this cheap: their widths are fixed by their own LayoutElements, so their
            // NUMBER is what changes when a caller adorns or strips a button.
            int neighbours = NeighbourCount();
            bool unchanged = _label.text == _lastText
                             && _label.font == _lastFont
                             && _label.fontSize == _lastFontSize
                             && neighbours == _lastNeighbours
                             // Somebody else writing this LayoutElement — a caller re-running its
                             // own SetLayoutElement on a refresh — puts back a width that was right
                             // before the label grew. Noticing costs one comparison.
                             && Mathf.Approximately(_layout.minWidth, _written);
            if (unchanged) return;

            _lastText = _label.text;
            _lastFont = _label.font;
            _lastFontSize = _label.fontSize;
            _lastNeighbours = neighbours;

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

            // What we just asked for, so the next pass can tell our own width from somebody else's.
            _written = _layout.minWidth;
        }

        /// <summary>
        /// How many things share the row with the label — the cheap half of <see cref="NeighboursWidth"/>,
        /// used to notice that the row's composition changed.
        /// </summary>
        private int NeighbourCount()
        {
            if (GetComponent<HorizontalLayoutGroup>() == null) return 0;

            int counted = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == _label.transform || !child.gameObject.activeSelf) continue;

                LayoutElement element = child.GetComponent<LayoutElement>();
                if (element != null && element.ignoreLayout) continue;

                counted++;
            }

            return counted;
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

                // 🔴 **GetComponent, never `as`.** `GetChild` hands back a wrapper typed `Transform`,
                // and on IL2CPP a managed cast to `RectTransform` fails on it even though the object
                // IS one — silently, returning null. It happened to succeed while the children were
                // being built (their wrappers were still the ones just created) and failed on every
                // later call, so this loop counted four neighbours at construction and none
                // afterwards: 79 pixels of marks became 16 of padding, and the button was measured
                // 63 pixels short with its label running out of the right-hand edge.
                RectTransform rect = child.GetComponent<RectTransform>();
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
