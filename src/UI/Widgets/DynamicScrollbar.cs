using UnityEngine;
using UnityEngine.UI;
using UniverseLib.Utility;
// ⚠ Il2CppInterop is ONE of the two IL2CPP chains; `#if CPP` covers both. This file is where that
// mistake was first made, on 2026-01-01 — see FocusRing for the full account.
#if INTEROP
using Il2CppInterop.Runtime.Injection;
#endif
#if UNHOLLOWER
using UnhollowerRuntimeLib;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Monitors scroll view content and dynamically hides/shows scrollbar.
    /// When content fits in viewport, hides scrollbar and expands viewport to full width.
    /// When content overflows, shows scrollbar and shrinks viewport.
    ///
    /// Add this component to a ScrollView created with UIFactory.CreateScrollView,
    /// or use UIFactory.ConfigureAutoHideScrollbar().
    /// </summary>
    public class DynamicScrollbar : MonoBehaviour
    {
        /// <summary>
        /// Width of the scrollbar area in pixels. Default matches UniverseLib's standard.
        /// </summary>
        public float ScrollbarWidth { get; set; } = 28f;

        /// <summary>
        /// Buffer zone in pixels to prevent flickering at content/viewport boundary.
        /// </summary>
        public float FlickerBuffer { get; set; } = 5f;

        /// <summary>
        /// Whether the scrollbar is currently visible.
        /// </summary>
        public bool IsScrollbarVisible => _scrollbarVisible;

        private ScrollRect _scrollRect;
        private RectTransform _viewport;
        private RectTransform _content;
        private GameObject _scrollbarObj;
        private ContentSizeFitter _fitter;
        private HorizontalOrVerticalLayoutGroup _group;

        private bool _scrollbarVisible = true;
        private float _lastContentHeight;
        private float _lastViewportHeight;
        private bool _initialized;

#if CPP
        public DynamicScrollbar(System.IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<DynamicScrollbar>();
                Registered = true;
            }
            catch (System.Exception ex)
            {
                Universe.LogWarning($"[DynamicScrollbar] Failed to register IL2CPP type: {ex.Message}");
            }
        }
#endif

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            _scrollRect = GetComponent<ScrollRect>();
            if (_scrollRect == null)
            {
                Universe.LogWarning("[DynamicScrollbar] No ScrollRect component found");
                return;
            }

            var viewportTransform = UnityHelpers.FindChild(transform, "Viewport");
            _viewport = viewportTransform != null ? viewportTransform.GetComponent<RectTransform>() : null;
            _content = _scrollRect.content;
            // How the content is sized: by its layout (a fitter over a group), or by code.
            _fitter = _content != null ? _content.GetComponent<ContentSizeFitter>() : null;
            _group = _content != null ? _content.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            var scrollbarTransform = UnityHelpers.FindChild(transform, "AutoSliderScrollbar");
            _scrollbarObj = scrollbarTransform != null ? scrollbarTransform.gameObject : null;

            if (_viewport == null)
            {
                Universe.LogWarning("[DynamicScrollbar] Viewport not found");
                return;
            }

            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized || _scrollRect == null || _viewport == null || _content == null)
                return;

            float contentHeight = _content.rect.height;
            float viewportHeight = _viewport.rect.height;

            // Only update if heights changed (optimization)
            if (Mathf.Approximately(contentHeight, _lastContentHeight) &&
                Mathf.Approximately(viewportHeight, _lastViewportHeight))
            {
                return;
            }

            // 🔴 **Decided on the layout as it stands now, not on the last frame's** (2026-10-22,
            // measured: every tab switch of a window took its 28 px for ONE frame, 1070 -> 1042 ->
            // 1070, its content ending exactly as high as the window). The rect read here is the
            // layout computed at the end of the previous frame; right after a tab is shown, it is
            // the content before its lists were given their share of the height — taller than the
            // window, for a frame. Rebuilt first, the content has the height it will be drawn at.
            // Only when a height moved, which is when the answer can change: never every frame.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            contentHeight = _content.rect.height;
            viewportHeight = _viewport.rect.height;

            // A window with no height yet has not been laid out: nothing to decide, and the next
            // frame — when it has its size — asks again (nothing is remembered). Measured: a list
            // on the Tools tab read viewport 0 on the frame it appeared, took its scrollbar, and
            // gave it back at the next with its 80 px.
            if (viewportHeight <= 0f) return;

            _lastContentHeight = contentHeight;
            _lastViewportHeight = viewportHeight;

            // 🔴 **What the children need, not the height the content was given** (2026-10-22,
            // measured). A content kept at least as tall as its window (FillViewportHeight) is
            // made max(window, children) from a copy of the window's height taken in ITS
            // LateUpdate — one frame stale, in an order nothing guarantees against this one. So
            // a window that shrank (the font sub-tabs appearing: 1091 kept for 1056) or the
            // measure of every tab at once (3462) read as an overflow for one frame, and the
            // content gave its 28 px to a scrollbar it did not need. Sized by its layout, the
            // content overflows exactly when its group's children ask for more than the window.
            float need = contentHeight;
            if (_fitter != null && _fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize && _group != null)
                need = _group.preferredHeight;

            // Determine if scrollbar is needed (with buffer to avoid flickering)
            bool needsScrollbar = need > viewportHeight + FlickerBuffer;

            if (needsScrollbar != _scrollbarVisible)
            {
                _scrollbarVisible = needsScrollbar;
                UpdateScrollbarVisibility();
                // One line per change, which is the event looked for (a window's content seen
                // narrowing by the scrollbar's width for one frame at a tab switch, 2026-10-22):
                // what was measured, and how high each block of the content stood.
                var blocks = new System.Text.StringBuilder();
                for (int i = 0; i < _content.childCount; i++)
                {
                    var child = _content.GetChild(i).GetComponent<RectTransform>();
                    if (child == null || !child.gameObject.activeSelf) continue;
                    blocks.Append(child.name).Append(' ').Append(child.rect.height.ToString("0")).Append("; ");
                }
                Universe.Log($"[DynamicScrollbar] {name} {(needsScrollbar ? "shown" : "hidden")} at frame {Time.frameCount}: "
                             + $"content {contentHeight:0}, children need {need:0}, viewport {viewportHeight:0} | {blocks}");
            }
        }

        private void UpdateScrollbarVisibility()
        {
            if (_scrollbarObj != null)
            {
                _scrollbarObj.SetActive(_scrollbarVisible);
            }

            if (_viewport != null)
            {
                // Adjust viewport width based on scrollbar visibility
                // UniverseLib sets offsetMax.x = -28 by default for scrollbar space
                // ⚠ The width only: the height is a consumer's to move — a scroll's end giving a
                // few pixels shrinks the viewport from the top, and resetting y here would snap it
                // back mid-bounce and leave the consumer's own record of it wrong.
                _viewport.offsetMax = new Vector2(_scrollbarVisible ? -ScrollbarWidth : 0f, _viewport.offsetMax.y);
            }
        }

        private void OnEnable()
        {
            // Force refresh on enable
            _lastContentHeight = -1;
            _lastViewportHeight = -1;
        }

        /// <summary>
        /// Force a refresh of the scrollbar visibility state.
        /// Call this after programmatically changing content.
        /// </summary>
        public void Refresh()
        {
            _lastContentHeight = -1;
            _lastViewportHeight = -1;
        }

        /// <summary>
        /// Force the scrollbar to be shown regardless of content size.
        /// </summary>
        public void ForceShow()
        {
            _scrollbarVisible = true;
            UpdateScrollbarVisibility();
        }

        /// <summary>
        /// Force the scrollbar to be hidden regardless of content size.
        /// </summary>
        public void ForceHide()
        {
            _scrollbarVisible = false;
            UpdateScrollbarVisibility();
        }
    }
}
