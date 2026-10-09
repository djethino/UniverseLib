using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UniverseLib;
using UniverseLib.Config;
using UniverseLib.Input;
using UniverseLib.Runtime;
using UniverseLib.UI;
using UniverseLib.Utility;

namespace UniverseLib.Input
{
    /// <summary>
    /// Handles taking control of the mouse/cursor and EventSystem (depending on Config settings) when a UniversalUI is being used.
    /// </summary>
    public class CursorUnlocker
    {
        /// <summary>
        /// True if a UI is being displayed and <see cref="ConfigManager.Force_Unlock_Mouse"/> is true.
        /// </summary>
        public static bool ShouldUnlock => ConfigManager.Force_Unlock_Mouse && UniversalUI.AnyUIShowing;

        private static bool currentlySettingCursor;
        private static CursorLockMode lastLockMode;
        private static bool lastVisibleState;

        // Whether the cursor and the EventSystem are ours right now — set when ShouldUnlock takes
        // them, cleared when they are given back. Restoring is owed only while this is true: the
        // restore branch used to run every frame a UI was registered as showing (a consumer's
        // non-interactive overlay counts) or no game EventSystem was known yet (so from launch),
        // writing the remembered cursor state over the game's own each frame. A game that sets
        // its cursor by a path the setter prefixes do not see kept being thrown out of mouse-look.
        private static bool holding;

        // Whether the game showed the system cursor at the moment it was taken — read from the
        // engine then, never from the prefixes, which a game setting it from native code bypasses.
        private static bool visibleWhenTaken;

        /// <summary>
        /// The cursor is held while the game had the system cursor hidden — a game drawing its own,
        /// or none. Forcing the system cursor visible loses to such a game every frame, so a consumer
        /// that needs a pointer on screen draws one itself while this is true.
        /// </summary>
        public static bool HoldingAHiddenCursor => holding && !visibleWhenTaken;

        // A prefix failure is said once per distinct message: these run at every cursor write.
        private static readonly HashSet<string> prefixFaultsSaid = new();

        private static WaitForEndOfFrame waitForEndOfFrame = new();

        [Obsolete("Moved to EventSystemHelper")]
        public static EventSystem CurrentEventSystem => EventSystemHelper.CurrentEventSystem;

        internal static void Init()
        {
            lastLockMode = Cursor.lockState;
            lastVisibleState = Cursor.visible;

            InitPatches();
            UpdateCursorControl();

            try
            {
                RuntimeHelper.Instance.Internal_StartCoroutine(UnlockCoroutine());
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"Exception setting up Aggressive Mouse Unlock: {ex}");
            }
        }

        /// <summary>
        /// Uses WaitForEndOfFrame in a Coroutine to aggressively set the Cursor state every frame.
        /// </summary>
        private static IEnumerator UnlockCoroutine()
        {
            while (!UniversalBehaviour.Quitting)
            {
                yield return waitForEndOfFrame ??= new WaitForEndOfFrame();
                if (UniversalUI.AnyUIShowing || !EventSystemHelper.lastEventSystem)
                    UpdateCursorControl();
            }
        }

        /// <summary>
        /// Checks current ShouldUnlock state and sets the Cursor and EventSystem as required.
        /// </summary>
        internal static void UpdateCursorControl()
        {
            try
            {
                currentlySettingCursor = true;

                if (ShouldUnlock)
                {
                    if (!holding)
                    {
                        // What the game shows at the moment it is taken, read from the engine
                        // rather than from what the prefixes happened to see: this is what goes
                        // back when it is released (the prefixes keep it current meanwhile).
                        lastLockMode = Cursor.lockState;
                        lastVisibleState = Cursor.visible;
                        visibleWhenTaken = lastVisibleState;
                        holding = true;
                    }

                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    if (!ConfigManager.Disable_EventSystem_Override)
                        EventSystemHelper.EnableEventSystem();
                }
                else if (holding)
                {
                    // Given back once, on the frame the unlock ends — never rewritten after that.
                    holding = false;

                    Cursor.lockState = lastLockMode;
                    Cursor.visible = lastVisibleState;

                    if (!ConfigManager.Disable_EventSystem_Override)
                        EventSystemHelper.ReleaseEventSystem();
                }
            }
            catch (Exception e)
            {
                Universe.Log($"Exception setting Cursor state: {e}");
            }
            finally
            {
                // A throw above used to leave this set, and the prefixes stopped tracking the game.
                currentlySettingCursor = false;
            }
        }

        // Patches

        internal static void InitPatches()
        {
            Universe.Patch(typeof(Cursor),
                "lockState",
                MethodType.Setter,
                prefix: AccessTools.Method(typeof(CursorUnlocker), nameof(Prefix_set_lockState)));

            Universe.Patch(typeof(Cursor),
                "visible",
                MethodType.Setter,
                prefix: AccessTools.Method(typeof(CursorUnlocker), nameof(Prefix_set_visible)));
        }

        // Force mouse to stay unlocked and visible while UnlockMouse and ShowMenu are true.
        // Also keep track of when anything else tries to set Cursor state, this will be the
        // value that we set back to when we close the menu or disable force-unlock.

        internal static void Prefix_set_lockState(ref CursorLockMode value)
        {
            try
            {
                if (!currentlySettingCursor)
                {
                    lastLockMode = value;

                    if (ShouldUnlock)
                        value = CursorLockMode.None;
                }
            }
            catch (Exception e)
            {
                SayPrefixFault("lockState", e);
            }
        }

        internal static void Prefix_set_visible(ref bool value)
        {
            try
            {
                if (!currentlySettingCursor)
                {
                    lastVisibleState = value;

                    if (ShouldUnlock)
                        value = true;
                }
            }
            catch (Exception e)
            {
                SayPrefixFault("visible", e);
            }
        }

        private static void SayPrefixFault(string setter, Exception e)
        {
            string message = $"{setter}: {e.GetType().Name}: {e.Message}";
            if (prefixFaultsSaid.Add(message))
                Universe.LogWarning($"Exception tracking the game's Cursor.{message} — the cursor it set may not be the one given back");
        }
    }
}