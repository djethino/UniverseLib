using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UniverseLib.Config;
using UniverseLib.UI;

namespace UniverseLib.Input
{
    /// <summary>
    /// What a UI can take from the game while it is being shown.
    /// </summary>
    /// <remarks>
    /// UniverseLib already stops the game from taking the cursor back (<see cref="CursorUnlocker"/>)
    /// and from owning the EventSystem (<see cref="EventSystemHelper"/>). Neither stops the game
    /// from READING input itself: a game whose Update calls Input.GetAxis("Mouse X") without
    /// checking Cursor.lockState keeps turning the camera under an open menu, and one calling
    /// Input.GetKey keeps walking while someone types in a text field.
    ///
    /// This closes that gap by making the legacy Input API answer "nothing is pressed" while a UI
    /// asks for it. Off by default: a library does not change what every existing consumer does.
    ///
    /// ⚠ Several members of UnityEngine.Input are InternalCall and therefore have no IL body, so
    /// Harmony cannot patch them. Which ones varies by runtime (Mono vs IL2CPP), Unity version and
    /// stripping, so this class does NOT carry a table of what works — it tries, records the
    /// outcome, and publishes it through <see cref="Capabilities"/>. Callers show or grey out their
    /// own options from that, with <see cref="Capability.Reason"/> as the explanation.
    /// </remarks>
    public static class InputCapture
    {
        /// <summary>One thing a caller may want to take from the game, and whether it can be had here.</summary>
        public class Capability
        {
            /// <summary>True when the underlying methods were successfully patched on this game.</summary>
            public bool Available { get; internal set; }

            /// <summary>Why it is unavailable, in words a user interface can show. Null when available.</summary>
            public string Reason { get; internal set; }

            /// <summary>Methods actually patched — for diagnostics, and to explain partial coverage.</summary>
            public List<string> Patched { get; } = new List<string>();

            /// <summary>Methods that could not be patched, with the reason each failed.</summary>
            public List<string> Missed { get; } = new List<string>();
        }

        /// <summary>Keys: Input.GetKey / GetKeyDown / GetKeyUp.</summary>
        public static Capability Keyboard { get; } = new Capability();

        /// <summary>Mouse buttons: Input.GetMouseButton / GetMouseButtonDown / GetMouseButtonUp.</summary>
        public static Capability MouseButtons { get; } = new Capability();

        /// <summary>Mouse movement: Input.GetAxis / GetAxisRaw for the mouse axes — the FPS camera.</summary>
        public static Capability MouseAxes { get; } = new Capability();

        /// <summary>All three, for callers that want to enumerate rather than name them.</summary>
        public static IEnumerable<KeyValuePair<string, Capability>> Capabilities
        {
            get
            {
                yield return new KeyValuePair<string, Capability>(nameof(Keyboard), Keyboard);
                yield return new KeyValuePair<string, Capability>(nameof(MouseButtons), MouseButtons);
                yield return new KeyValuePair<string, Capability>(nameof(MouseAxes), MouseAxes);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // What is being captured right now. Set by the consumer, read by the prefixes.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether each capture is wanted at this instant — asked every time the game reads input,
        /// so a caller can vary it moment to moment rather than for a whole session.
        ///
        /// Left null (the default) nothing is ever captured, whatever the flags below say. This is
        /// what keeps the library's existing behaviour unchanged for consumers that never heard of
        /// this class.
        /// </summary>
        public static Func<CaptureKind, bool> ShouldCapture { get; set; }

        /// <summary>The three things <see cref="ShouldCapture"/> is asked about.</summary>
        public enum CaptureKind
        {
            /// <summary>Keys.</summary>
            Keyboard,
            /// <summary>Mouse buttons.</summary>
            MouseButtons,
            /// <summary>Mouse movement axes.</summary>
            MouseAxes,
        }

        /// <summary>
        /// Set while UniverseLib itself is the one reading input. Nothing is captured then.
        /// </summary>
        /// <remarks>
        /// ⚠ Load-bearing. The patches sit on UnityEngine.Input, which cannot tell who is asking —
        /// and UniverseLib asks constantly: <see cref="LegacyInput"/> for the consumer's hotkeys,
        /// and its own StandaloneInputModule for every click and every arrow key inside the menu.
        /// Without this, turning capture on would make the menu unclickable and its opening hotkey
        /// dead — a UI you cannot close because it captured the key that closes it.
        /// </remarks>
        internal static bool Bypass { get; set; }

        static bool Wants(CaptureKind kind)
        {
            if (Bypass)
                return false;

            var ask = ShouldCapture;
            if (ask == null)
                return false;

            try
            {
                return ask(kind);
            }
            catch (Exception ex)
            {
                // A consumer's callback throwing must not make the game's input throw. Reported,
                // not swallowed: a silent false here would look exactly like "capture is off".
                Universe.LogWarning($"[InputCapture] ShouldCapture({kind}) threw, treating as no capture: {ex}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Setup
        // ─────────────────────────────────────────────────────────────────────────────

        static bool initialized;

        /// <summary>
        /// Try to patch the legacy Input API. Safe to call when the game has no legacy Input at
        /// all: every capability then reports unavailable with that as its reason.
        /// </summary>
        internal static void Init()
        {
            if (initialized)
                return;
            initialized = true;

            Type input = null;
            try
            {
                input = ReflectionUtility.GetTypeByName("UnityEngine.Input");
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[InputCapture] Could not look up UnityEngine.Input: {ex.Message}");
            }

            if (input == null)
            {
                const string none = "This game does not use Unity's legacy Input.";
                Keyboard.Reason = MouseButtons.Reason = MouseAxes.Reason = none;
                return;
            }

            PatchKeyboard(input);
            PatchMouseButtons(input);
            PatchMouseAxes(input);
            PatchOwnInputModule();

            foreach (var pair in Capabilities)
            {
                var c = pair.Value;
                if (c.Available)
                    Universe.Log($"[InputCapture] {pair.Key}: can be captured ({c.Patched.Count} method(s))"
                        + (c.Missed.Count > 0 ? $", {c.Missed.Count} out of reach: {string.Join(", ", c.Missed.ToArray())}" : ""));
                else
                    Universe.Log($"[InputCapture] {pair.Key}: cannot be captured — {c.Reason}");
            }
        }

        /// <summary>
        /// Patch one method and record the outcome on <paramref name="cap"/>.
        /// Returns whether it took.
        /// </summary>
        static bool TryPatch(Capability cap, Type type, string method, Type[] args, string prefixName)
        {
            var prefix = AccessTools.Method(typeof(InputCapture), prefixName);
            bool ok = Universe.Patch(type, method, MethodType.Normal, args, postfix: prefix);

            string label = args != null && args.Length > 0
                ? $"{method}({args[0].Name})"
                : method;

            if (ok) cap.Patched.Add(label);
            else cap.Missed.Add(label);
            return ok;
        }

        static void PatchKeyboard(Type input)
        {
            TryPatch(Keyboard, input, "GetKey", new[] { typeof(KeyCode) }, nameof(Postfix_Bool));
            TryPatch(Keyboard, input, "GetKey", new[] { typeof(string) }, nameof(Postfix_Bool));
            TryPatch(Keyboard, input, "GetKeyDown", new[] { typeof(KeyCode) }, nameof(Postfix_Bool));
            TryPatch(Keyboard, input, "GetKeyDown", new[] { typeof(string) }, nameof(Postfix_Bool));
            TryPatch(Keyboard, input, "GetKeyUp", new[] { typeof(KeyCode) }, nameof(Postfix_Bool));
            TryPatch(Keyboard, input, "GetKeyUp", new[] { typeof(string) }, nameof(Postfix_Bool));

            Keyboard.Available = Keyboard.Patched.Count > 0;
            if (!Keyboard.Available)
                Keyboard.Reason = "This game's key checks cannot be intercepted (no patchable method).";
        }

        static void PatchMouseButtons(Type input)
        {
            TryPatch(MouseButtons, input, "GetMouseButton", new[] { typeof(int) }, nameof(Postfix_Bool));
            TryPatch(MouseButtons, input, "GetMouseButtonDown", new[] { typeof(int) }, nameof(Postfix_Bool));
            TryPatch(MouseButtons, input, "GetMouseButtonUp", new[] { typeof(int) }, nameof(Postfix_Bool));

            MouseButtons.Available = MouseButtons.Patched.Count > 0;
            if (!MouseButtons.Available)
                MouseButtons.Reason = "This game's mouse buttons are read through a method that cannot be intercepted.";
        }

        static void PatchMouseAxes(Type input)
        {
            TryPatch(MouseAxes, input, "GetAxis", new[] { typeof(string) }, nameof(Postfix_Axis));
            TryPatch(MouseAxes, input, "GetAxisRaw", new[] { typeof(string) }, nameof(Postfix_Axis));

            MouseAxes.Available = MouseAxes.Patched.Count > 0;
            if (!MouseAxes.Available)
                MouseAxes.Reason = "This game reads mouse movement through a method that cannot be intercepted.";
        }

        /// <summary>
        /// Wrap our own input module's frame in <see cref="Bypass"/>, so the menu keeps receiving
        /// the clicks and keys it captured from the game.
        /// </summary>
        /// <remarks>
        /// The module reads UnityEngine.Input from inside Unity's own code — there is no call of
        /// ours to wrap, hence patching Process. The instance check matters: the game very likely
        /// runs a StandaloneInputModule of its own, and bypassing during ITS frame would hand back
        /// exactly the input we were asked to take.
        /// </remarks>
        static void PatchOwnInputModule()
        {
            var module = ReflectionUtility.GetTypeByName("UnityEngine.EventSystems.StandaloneInputModule");
            if (module == null)
            {
                Universe.Log("[InputCapture] No StandaloneInputModule type — menu input protection not installed");
                return;
            }

            bool ok = Universe.Patch(module, "Process", MethodType.Normal, Type.EmptyTypes,
                prefix: AccessTools.Method(typeof(InputCapture), nameof(Prefix_Module_Process)),
                finalizer: AccessTools.Method(typeof(InputCapture), nameof(Finalizer_Module_Process)));

            // Not fatal on its own: with nothing capturable there is nothing to protect from. It is
            // only alarming when a capture IS available, so say so at the level that matches.
            if (!ok && (Keyboard.Available || MouseButtons.Available))
                Universe.LogWarning("[InputCapture] Could not protect the menu's own input module — "
                    + "capturing may make the menu unresponsive on this game.");
        }

        /// <summary>Bypass while OUR module processes; a finalizer clears it whatever happens.</summary>
        public static void Prefix_Module_Process(object __instance)
        {
            if (InputManager.inputHandler is LegacyInput legacy
                && legacy.inputModule != null
                && ReferenceEquals(legacy.inputModule, __instance))
            {
                Bypass = true;
            }
        }

        /// <summary>
        /// Finalizer, not postfix, on purpose: it runs even if the module throws, so an exception
        /// inside Unity's input handling cannot leave Bypass stuck on — which would silently
        /// disable every capture for the rest of the session.
        /// </summary>
        public static void Finalizer_Module_Process()
        {
            Bypass = false;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Prefixes — postfixes, in fact: the game's own call still runs, only its ANSWER is
        // replaced. Suppressing the call itself would skip Unity's internal bookkeeping for a
        // frame, and some input backends notice.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Applied to GetKey* and GetMouseButton*: report nothing pressed while captured.</summary>
        public static void Postfix_Bool(ref bool __result, MethodBase __originalMethod)
        {
            if (!__result)
                return;

            CaptureKind kind = __originalMethod.Name.StartsWith("GetMouse")
                ? CaptureKind.MouseButtons
                : CaptureKind.Keyboard;

            if (Wants(kind))
                __result = false;
        }

        /// <summary>
        /// Applied to GetAxis/GetAxisRaw: flatten the MOUSE axes only.
        /// </summary>
        /// <remarks>
        /// Never every axis: these two also carry "Horizontal", "Vertical" and whatever the game
        /// named its own, and zeroing those would freeze a gamepad and a keyboard's movement keys
        /// through a door nobody opened. The mouse axes are the ones an open menu has a claim on.
        /// </remarks>
        public static void Postfix_Axis(ref float __result, string __0)
        {
            if (__result == 0f || __0 == null)
                return;

            // Unity's own names are "Mouse X", "Mouse Y" and "Mouse ScrollWheel"; games rename them
            // rarely and always keeping the word, so match on it rather than on an exact list.
            if (__0.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            if (Wants(CaptureKind.MouseAxes))
                __result = 0f;
        }
    }
}
