using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Lights a field's border while it holds the caret, and puts it back when it does not.
    ///
    /// uGUI gives a Selectable a colour transition on its own graphic, which tints the FILL — the
    /// one thing a text field must not do, since the text sits on it. Every other surface in this
    /// product says "you are here" with its edge: the website draws a purple ring, the Manager
    /// draws the same one. A field that answered a click by going a shade paler was the only
    /// control in three products that did not.
    ///
    /// So the border object is the one that changes, and nothing else moves: no layout, no size,
    /// no fill. A ring is one pixel of colour and costs nothing to repaint.
    ///
    /// 🔴 **Polled, not handled — and that is IL2CPP's doing, not a preference.** The obvious
    /// version implements ISelectHandler/IDeselectHandler. It compiles on Mono and fails on both
    /// IL2CPP variants with CS1721: Il2CppInterop exposes those two as CLASSES, so a MonoBehaviour
    /// cannot also be one. Measured, not guessed — the Mono build passed and the two others did
    /// not. Comparing against the event system's current selection costs one reference test per
    /// field per frame and works identically on every runtime, which is exactly the arrangement
    /// <see cref="ButtonLabelFitter"/> already uses a file away.
    ///
    /// ⚠ IL2CPP needs the type registered before it can be added to an object, and the pointer
    /// constructor besides. Same as ButtonLabelFitter, including the failure being a warning
    /// rather than a throw: a field that does not light up still takes text, where an exception
    /// during a panel's construction takes the whole interface with it.
    /// </summary>
    public class FocusRing : MonoBehaviour
    {
        /// <summary>The border to recolour. Set by the factory that built the field.</summary>
        public Image Border { get; set; }

        /// <summary>What the border wears while the field holds the caret.</summary>
        public Color FocusColour { get; set; } = UIFactory.Colors.FocusRing;

        /// <summary>What it goes back to. Read from the border itself, so a caller that
        /// recoloured it keeps its choice.</summary>
        public Color RestColour { get; set; } = UIFactory.Colors.InputBorder;

        private bool _lit;

#if CPP
        public FocusRing(IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<FocusRing>();
                Registered = true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[FocusRing] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        private void LateUpdate()
        {
            if (!Border) return;

            EventSystem events = EventSystem.current;
            // ⚠ The whole subtree, not this object alone: a text field puts the caret on its own
            // object, but a dropdown hands it to the item list it opens. Asking about the object
            // that owns the border would have left a dropdown unlit for as long as it was open.
            GameObject selected = events ? events.currentSelectedGameObject : null;
            bool focused = selected && (selected == gameObject || selected.transform.IsChildOf(transform));

            if (focused == _lit) return;

            _lit = focused;
            Border.color = focused ? FocusColour : RestColour;
        }

        /// <summary>
        /// Put the ring out when the field goes away with the caret still in it.
        ///
        /// ⚠ A panel closed while a field held focus stops being updated, so the border stayed lit
        /// and came back lit the next time the panel opened — a field claiming a caret it does not
        /// have.
        /// </summary>
        private void OnDisable()
        {
            _lit = false;
            if (Border) Border.color = RestColour;
        }
    }
}
