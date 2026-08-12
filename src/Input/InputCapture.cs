using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UniverseLib.Input
{
    /// <summary>
    /// What a UI can take from the game while it is being shown.
    /// </summary>
    /// <remarks>
    /// UniverseLib already stops the game from taking the cursor back (<see cref="CursorUnlocker"/>)
    /// and from owning the EventSystem (<see cref="EventSystemHelper"/>). Neither stops the game
    /// from READING input itself: one whose Update calls Input.GetAxis("Mouse X") without checking
    /// Cursor.lockState keeps turning the camera under an open menu, and one calling Input.GetKey
    /// keeps walking while somebody types in a text field.
    ///
    /// Off by default — a library does not change what every existing consumer does. Set
    /// <see cref="ShouldCapture"/> and nothing more; everything below decides by itself HOW, or
    /// says why it cannot.
    ///
    /// ── Intention vs means ──────────────────────────────────────────────────────────────
    /// A <see cref="CaptureKind"/> is what a consumer WANTS ("the keyboard is mine while my window
    /// is open"). A <see cref="Strategy"/> is one way of obtaining it, and which ones work is a
    /// property of the game, not of the request. Three configurations, all measured on real games:
    ///
    ///   game reads legacy,       we read legacy  → patches work
    ///   game reads Input System, we read legacy  → taking its devices works
    ///   game reads Input System, we read it too  → NEITHER works yet: cutting the source would
    ///                                              cut the menu's own input with it
    ///
    /// Hence: no table of what works, anywhere. Each strategy probes at startup and reports, and a
    /// caller asks <see cref="CanCapture"/> / <see cref="WhyNot"/> per intention — which is exactly
    /// what a settings screen needs to show an option or grey it out with a reason.
    /// </remarks>
    public static class InputCapture
    {
        /// <summary>The things a consumer can ask for.</summary>
        public enum CaptureKind
        {
            /// <summary>Keys.</summary>
            Keyboard,
            /// <summary>Mouse buttons.</summary>
            MouseButtons,
            /// <summary>Mouse movement axes — the first-person camera.</summary>
            MouseAxes,
        }

        static readonly CaptureKind[] AllKinds =
        {
            CaptureKind.Keyboard, CaptureKind.MouseButtons, CaptureKind.MouseAxes
        };

        // ─────────────────────────────────────────────────────────────────────────────
        // Strategies
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>One way of taking input from the game, and whether it works here.</summary>
        public abstract class Strategy
        {
            /// <summary>Short name, for logs and diagnostics.</summary>
            public abstract string Name { get; }

            /// <summary>True once <see cref="Probe"/> found this usable on this game.</summary>
            public bool Available { get; protected set; }

            /// <summary>Why it is not usable, in words a settings screen can show. Null when it is.</summary>
            public string Reason { get; protected set; }

            /// <summary>What it managed to hook, and what slipped through — for diagnostics.</summary>
            public List<string> Hooked { get; } = new List<string>();
            /// <inheritdoc cref="Hooked"/>
            public List<string> Missed { get; } = new List<string>();

            /// <summary>Times the game came through here while capture was on.</summary>
            public int Asked { get; internal set; }
            /// <summary>Times something was actually taken.</summary>
            public int Silenced { get; internal set; }

            /// <summary>Does this strategy serve that intention on this game?</summary>
            public abstract bool Serves(CaptureKind kind);

            /// <summary>Work out whether this can be used here. Called once, at startup.</summary>
            public abstract void Probe();

            /// <summary>Called every frame for strategies that act rather than intercept.</summary>
            public virtual void Tick() { }

            /// <summary>Give everything back. Must be safe to call when nothing was taken.</summary>
            public virtual void Release() { }

            /// <summary>
            /// One line: what this managed since the last reset. Distinguishes the three outcomes
            /// that look alike from outside the game — unusable, usable but never solicited
            /// (the game reads elsewhere), and working.
            /// </summary>
            public string Describe()
            {
                if (!Available) return $"unavailable ({Reason})";
                if (Asked == 0) return "ready, but the game never came through it";
                return $"{Silenced}/{Asked} taken";
            }

            internal void ResetActivity() { Asked = 0; Silenced = 0; }
        }

        static readonly List<Strategy> _strategies = new List<Strategy>();

        /// <summary>Every known strategy, probed or not.</summary>
        public static IEnumerable<Strategy> Strategies { get { return _strategies; } }

        // ─────────────────────────────────────────────────────────────────────────────
        // What a consumer asks for
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Asked every time the game reads input, so a consumer can vary its answer moment to
        /// moment rather than for a whole session. Null (the default) captures nothing at all.
        /// </summary>
        public static Func<CaptureKind, bool> ShouldCapture { get; set; }

        /// <summary>
        /// Set while UniverseLib itself is the one reading. Nothing is captured then.
        /// </summary>
        /// <remarks>
        /// ⚠ Load-bearing. The patches sit on UnityEngine.Input, which cannot tell who is asking —
        /// and UniverseLib asks constantly: <see cref="LegacyInput"/> for the consumer's hotkeys,
        /// its own StandaloneInputModule for every click and arrow key inside the menu. Without
        /// this, capturing would make the menu unclickable and its opening hotkey dead: a window
        /// you cannot close because it captured the key that closes it.
        /// </remarks>
        internal static bool Bypass { get; set; }

        /// <summary>Is that intention obtainable on this game, by any means?</summary>
        public static bool CanCapture(CaptureKind kind)
        {
            foreach (var s in _strategies)
            {
                if (s.Available && s.Serves(kind))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Why that intention cannot be served here — the reason of every strategy that would have
        /// served it. Null when it can be. This is the text a greyed-out option should carry.
        /// </summary>
        public static string WhyNot(CaptureKind kind)
        {
            if (CanCapture(kind))
                return null;

            var reasons = new List<string>();
            foreach (var s in _strategies)
            {
                if (s.Serves(kind) && !string.IsNullOrEmpty(s.Reason) && !reasons.Contains(s.Reason))
                    reasons.Add(s.Reason);
            }

            return reasons.Count > 0
                ? string.Join(" ", reasons.ToArray())
                : "This game gives no way to take that from it.";
        }

        static bool Wants(CaptureKind kind, bool count)
        {
            if (Bypass)
                return false;

            var ask = ShouldCapture;
            if (ask == null)
                return false;

            try
            {
                bool wanted = ask(kind);
                if (wanted && count)
                {
                    // Counted on the read, never on our own polling: counting Tick's once-a-frame
                    // question made "0/3222" look like three thousand reads by a game that had
                    // never touched the API. A tally that reads as a working capture where there
                    // is none is worse than no tally.
                    foreach (var s in _strategies)
                    {
                        if (s.Available && s.Serves(kind)) s.Asked++;
                    }
                }
                return wanted;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[InputCapture] ShouldCapture({kind}) threw, treating as no capture: {ex}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────────────

        static bool initialized;

        internal static void Init()
        {
            if (initialized)
                return;
            initialized = true;

            _strategies.Add(new LegacyPatchStrategy());
            _strategies.Add(new InputSystemDeviceStrategy());

            foreach (var s in _strategies)
            {
                try
                {
                    s.Probe();
                }
                catch (Exception ex)
                {
                    // A strategy that throws while probing is simply unavailable — but say so:
                    // silently dropping it would look exactly like a game that cannot be captured.
                    Universe.LogWarning($"[InputCapture] {s.Name} failed to probe: {ex}");
                }

                Universe.Log($"[InputCapture] {s.Name}: "
                    + (s.Available
                        ? $"usable ({string.Join(", ", s.Hooked.ToArray())})"
                            + (s.Missed.Count > 0 ? $"; out of reach: {string.Join(", ", s.Missed.ToArray())}" : "")
                        : $"unusable — {s.Reason}"));
            }

            foreach (var kind in AllKinds)
            {
                if (!CanCapture(kind))
                    Universe.Log($"[InputCapture] {kind} cannot be captured here — {WhyNot(kind)}");
            }
        }

        internal static void Tick()
        {
            foreach (var s in _strategies)
            {
                if (!s.Available) continue;
                try { s.Tick(); }
                catch (Exception ex) { Universe.LogWarning($"[InputCapture] {s.Name} tick failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Give everything back. A game left with its keyboard disabled is unplayable, and nothing
        /// else would ever put that right — call this on shutdown.
        /// </summary>
        public static void ReleaseAll()
        {
            foreach (var s in _strategies)
            {
                try { s.Release(); }
                catch (Exception ex) { Universe.LogWarning($"[InputCapture] {s.Name} release failed: {ex.Message}"); }
            }
        }

        /// <summary>Forget the counters, so the next report covers one episode only.</summary>
        public static void ResetActivity()
        {
            foreach (var s in _strategies)
                s.ResetActivity();
        }

        /// <summary>One line per strategy: what each managed since the last reset.</summary>
        public static string DescribeActivity()
        {
            var parts = new List<string>();
            foreach (var s in _strategies)
                parts.Add($"{s.Name}: {s.Describe()}");
            return string.Join(" | ", parts.ToArray());
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Strategy 1 — patch the legacy Input API
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Makes UnityEngine.Input answer "nothing pressed" while a UI asks for it.
        /// </summary>
        /// <remarks>
        /// ⚠ Several members of UnityEngine.Input are InternalCall and so have no IL body Harmony
        /// could patch — and WHICH ones varies by runtime, Unity version and stripping. Measured:
        /// GetAxis and GetMouseButton* are out of reach on Mono but patchable on IL2CPP, and a
        /// game that never calls GetKeyUp has it stripped away entirely. Hence probing rather than
        /// assuming, per method, with what was reached kept in <see cref="Strategy.Hooked"/>.
        /// </remarks>
        class LegacyPatchStrategy : Strategy
        {
            public override string Name { get { return "Legacy patches"; } }

            bool keys, buttons, axes;

            public override bool Serves(CaptureKind kind)
            {
                switch (kind)
                {
                    case CaptureKind.Keyboard: return keys;
                    case CaptureKind.MouseButtons: return buttons;
                    case CaptureKind.MouseAxes: return axes;
                    default: return false;
                }
            }

            public override void Probe()
            {
                Type input = ReflectionUtility.GetTypeByName("UnityEngine.Input");
                if (input == null)
                {
                    Reason = "This game does not use Unity's legacy Input.";
                    return;
                }

                keys = TryPatch(input, "GetKey", typeof(KeyCode), nameof(Postfix_Bool))
                     | TryPatch(input, "GetKey", typeof(string), nameof(Postfix_Bool))
                     | TryPatch(input, "GetKeyDown", typeof(KeyCode), nameof(Postfix_Bool))
                     | TryPatch(input, "GetKeyDown", typeof(string), nameof(Postfix_Bool))
                     | TryPatch(input, "GetKeyUp", typeof(KeyCode), nameof(Postfix_Bool))
                     | TryPatch(input, "GetKeyUp", typeof(string), nameof(Postfix_Bool));

                buttons = TryPatch(input, "GetMouseButton", typeof(int), nameof(Postfix_Bool))
                        | TryPatch(input, "GetMouseButtonDown", typeof(int), nameof(Postfix_Bool))
                        | TryPatch(input, "GetMouseButtonUp", typeof(int), nameof(Postfix_Bool));

                axes = TryPatch(input, "GetAxis", typeof(string), nameof(Postfix_Axis))
                     | TryPatch(input, "GetAxisRaw", typeof(string), nameof(Postfix_Axis));

                Available = keys || buttons || axes;
                if (!Available)
                    Reason = "None of this game's legacy input methods can be intercepted.";

                ProtectOwnInputModule();
            }

            bool TryPatch(Type type, string method, Type arg, string postfixName)
            {
                var postfix = AccessTools.Method(typeof(InputCapture), postfixName);
                bool ok = Universe.Patch(type, method, MethodType.Normal, new Type[] { arg }, postfix: postfix);

                string label = $"{method}({arg.Name})";
                if (ok) Hooked.Add(label); else Missed.Add(label);
                return ok;
            }

            /// <summary>
            /// Wrap our own input module's frame in <see cref="Bypass"/>, so the menu keeps the
            /// clicks and keys it took from the game.
            /// </summary>
            /// <remarks>
            /// The module reads UnityEngine.Input from inside Unity's own code — there is no call
            /// of ours to wrap, hence patching Process. The instance check matters: the game very
            /// likely runs a StandaloneInputModule of its own, and bypassing during ITS frame
            /// would hand back exactly the input we were asked to take.
            /// </remarks>
            void ProtectOwnInputModule()
            {
                var module = ReflectionUtility.GetTypeByName("UnityEngine.EventSystems.StandaloneInputModule");
                if (module == null)
                    return;

                bool ok = Universe.Patch(module, "Process", MethodType.Normal, Type.EmptyTypes,
                    prefix: AccessTools.Method(typeof(InputCapture), nameof(Prefix_Module_Process)),
                    finalizer: AccessTools.Method(typeof(InputCapture), nameof(Finalizer_Module_Process)));

                if (!ok && Available)
                    Universe.LogWarning("[InputCapture] Could not protect the menu's own input module — "
                        + "capturing may make the menu unresponsive on this game.");
            }
        }

        /// <summary>The legacy strategy, for the postfixes to report into.</summary>
        static Strategy Legacy
        {
            get { return _strategies.Count > 0 ? _strategies[0] : null; }
        }

        /// <summary>Applied to GetKey* and GetMouseButton*: report nothing pressed while captured.</summary>
        public static void Postfix_Bool(ref bool __result, MethodBase __originalMethod)
        {
            if (!__result)
                return;

            CaptureKind kind = __originalMethod.Name.StartsWith("GetMouse")
                ? CaptureKind.MouseButtons
                : CaptureKind.Keyboard;

            if (Wants(kind, true))
            {
                __result = false;
                var s = Legacy;
                if (s != null) s.Silenced++;
            }
        }

        /// <summary>
        /// Applied to GetAxis/GetAxisRaw: flatten the MOUSE axes only.
        /// </summary>
        /// <remarks>
        /// Never every axis: these two also carry "Horizontal", "Vertical" and whatever the game
        /// named its own, and zeroing those would freeze a gamepad and the movement keys through a
        /// door nobody opened. The mouse axes are the ones an open menu has a claim on.
        /// </remarks>
        public static void Postfix_Axis(ref float __result, string __0)
        {
            if (__result == 0f || __0 == null)
                return;

            // Unity's own are "Mouse X", "Mouse Y", "Mouse ScrollWheel"; games rename them rarely
            // and keep the word, so match on it rather than on an exact list.
            if (__0.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            if (Wants(CaptureKind.MouseAxes, true))
            {
                __result = 0f;
                var s = Legacy;
                if (s != null) s.Silenced++;
            }
        }

        /// <summary>Bypass while OUR module processes; a finalizer clears it whatever happens.</summary>
        public static void Prefix_Module_Process(object __instance)
        {
            var legacy = InputManager.inputHandler as LegacyInput;
            if (legacy != null && legacy.inputModule != null && ReferenceEquals(legacy.inputModule, __instance))
                Bypass = true;
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
        // Strategy 2 — take the Input System's devices
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Disables the Input System's keyboard and mouse while a UI asks for them.
        /// </summary>
        /// <remarks>
        /// The patches above cannot reach a game on the Input System: it never calls
        /// UnityEngine.Input, so they watch a road nobody drives on. Measured on one reading
        /// everything that way — every legacy hook reported "the game never came through it" while
        /// its camera kept turning under an open menu.
        ///
        /// ⚠ Coarser than the legacy side, irreducibly: a mouse is one device, so its buttons and
        /// its movement come and go together.
        /// </remarks>
        class InputSystemDeviceStrategy : Strategy
        {
            public override string Name { get { return "Input System devices"; } }

            MethodInfo m_disable, m_enable;
            PropertyInfo p_keyboard, p_mouse;
            bool keyboardTaken, mouseTaken;

            public override bool Serves(CaptureKind kind)
            {
                switch (kind)
                {
                    case CaptureKind.Keyboard: return p_keyboard != null;
                    case CaptureKind.MouseButtons:
                    case CaptureKind.MouseAxes: return p_mouse != null;
                    default: return false;
                }
            }

            public override void Probe()
            {
                Type system = ReflectionUtility.GetTypeByName("UnityEngine.InputSystem.InputSystem");
                if (system == null)
                {
                    Reason = "This game does not use Unity's Input System package.";
                    return;
                }

                // ⚠ Refused when UniverseLib reads through the Input System as well: disabling
                // those devices would take the menu's own keyboard and mouse along with the game's,
                // leaving a window that cannot be clicked or closed. Better to do nothing, and say
                // why, than to make the mod unusable on those games.
                if (InputManager.CurrentType == InputType.InputSystem)
                {
                    Reason = "The menu itself reads through the Input System here; "
                        + "taking those devices would make it unusable.";
                    return;
                }

                p_keyboard = PropertyOf("UnityEngine.InputSystem.Keyboard");
                p_mouse = PropertyOf("UnityEngine.InputSystem.Mouse");

                Type device = ReflectionUtility.GetTypeByName("UnityEngine.InputSystem.InputDevice");
                m_disable = FindDeviceMethod(system, "DisableDevice", device);
                m_enable = FindDeviceMethod(system, "EnableDevice", device);

                if (m_disable == null || m_enable == null || (p_keyboard == null && p_mouse == null))
                {
                    // Name what is missing. "Does not expose device enabling" once sent us looking
                    // at the game when the fault was a signature we had asked for too precisely.
                    var missing = new List<string>();
                    if (m_disable == null) missing.Add("InputSystem.DisableDevice");
                    if (m_enable == null) missing.Add("InputSystem.EnableDevice");
                    if (p_keyboard == null && p_mouse == null) missing.Add("Keyboard.current / Mouse.current");
                    Reason = "This game's Input System is missing " + string.Join(", ", missing.ToArray()) + ".";
                    return;
                }

                Available = true;
                if (p_keyboard != null) Hooked.Add("Keyboard"); else Missed.Add("Keyboard");
                if (p_mouse != null) Hooked.Add("Mouse"); else Missed.Add("Mouse");
            }

            static PropertyInfo PropertyOf(string typeName)
            {
                Type t = ReflectionUtility.GetTypeByName(typeName);
                return t == null ? null : t.GetProperty("current");
            }

            /// <summary>
            /// Find the device methods by name and first parameter, never by exact signature.
            /// </summary>
            /// <remarks>
            /// ⚠ Asking for the exact signature (InputDevice) finds nothing on a current Input
            /// System: these gained an optional second parameter along the way, and reflection does
            /// not apply defaults when matching. That miss made a game with a perfectly working
            /// Input System report itself as not exposing device enabling at all.
            /// </remarks>
            static MethodInfo FindDeviceMethod(Type system, string name, Type device)
            {
                foreach (var m in system.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name) continue;

                    var ps = m.GetParameters();
                    if (ps.Length == 0) continue;

                    // When the type resolved, insist on it; otherwise take the name's word for it
                    // rather than refusing to work at all.
                    if (device != null && !ps[0].ParameterType.IsAssignableFrom(device)) continue;

                    return m;
                }
                return null;
            }

            /// <summary>
            /// Bring the devices in line with what is wanted. Called every frame.
            /// </summary>
            /// <remarks>
            /// Driven by comparing state rather than by reacting to an event, on purpose: a device
            /// the game re-enables itself, or one plugged in mid-session, is brought back in line
            /// on the next frame instead of staying wrong until something happens to fire again.
            /// </remarks>
            public override void Tick()
            {
                // Asked without counting: this is our own polling, not the game reading anything.
                bool wantKeyboard = Wants(CaptureKind.Keyboard, false);
                // One device carries both, and both are asked explicitly — a || short-circuit
                // would leave MouseAxes permanently unasked whenever buttons were wanted too.
                bool wantButtons = Wants(CaptureKind.MouseButtons, false);
                bool wantAxes = Wants(CaptureKind.MouseAxes, false);
                bool wantMouse = wantButtons || wantAxes;

                if (wantKeyboard || wantMouse)
                    Asked++;

                if (wantKeyboard != keyboardTaken && Set(p_keyboard, wantKeyboard))
                {
                    keyboardTaken = wantKeyboard;
                    if (wantKeyboard) Silenced++;
                }

                if (wantMouse != mouseTaken && Set(p_mouse, wantMouse))
                {
                    mouseTaken = wantMouse;
                    if (wantMouse) Silenced++;
                }
            }

            public override void Release()
            {
                if (keyboardTaken && Set(p_keyboard, false)) keyboardTaken = false;
                if (mouseTaken && Set(p_mouse, false)) mouseTaken = false;
            }

            bool Set(PropertyInfo current, bool take)
            {
                object dev = null;
                try { if (current != null) dev = current.GetValue(null, null); }
                catch { }
                if (dev == null)
                    return false;

                try
                {
                    var method = take ? m_disable : m_enable;
                    method.Invoke(null, ArgsFor(method, dev));
                    return true;
                }
                catch (Exception ex)
                {
                    Universe.LogWarning($"[InputCapture] Could not {(take ? "take" : "return")} an Input System device: {ex.Message}");
                    return false;
                }
            }

            /// <summary>The device, then whatever defaults follow it.</summary>
            static object[] ArgsFor(MethodInfo method, object device)
            {
                var ps = method.GetParameters();
                var args = new object[ps.Length];
                args[0] = device;
                for (int i = 1; i < ps.Length; i++)
                {
                    // IsOptional/DefaultValue rather than HasDefaultValue: the Mono build of this
                    // library targets net35, where the latter does not exist.
                    object given = ps[i].IsOptional ? ps[i].DefaultValue : null;
                    if (given == null || given == DBNull.Value)
                        given = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                    args[i] = given;
                }
                return args;
            }
        }
    }
}
