using UnityEngine;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
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

            var viewportTransform = transform.Find("Viewport");
            _viewport = viewportTransform != null ? viewportTransform.GetComponent<RectTransform>() : null;
            _content = _scrollRect.content;
            var scrollbarTransform = transform.Find("AutoSliderScrollbar");
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

            _lastContentHeight = contentHeight;
            _lastViewportHeight = viewportHeight;

            // Determine if scrollbar is needed (with buffer to avoid flickering)
            bool needsScrollbar = contentHeight > viewportHeight + FlickerBuffer;

            if (needsScrollbar != _scrollbarVisible)
            {
                _scrollbarVisible = needsScrollbar;
                UpdateScrollbarVisibility();
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
                _viewport.offsetMax = new Vector2(_scrollbarVisible ? -ScrollbarWidth : 0f, 0f);
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
