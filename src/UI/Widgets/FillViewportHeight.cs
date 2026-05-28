using UnityEngine;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Keeps a ScrollRect's content container at least as tall as its viewport, while
    /// still letting it grow taller (so the global scroll keeps working) when its
    /// children's preferred height exceeds the viewport.
    ///
    /// Attach this to the <c>content</c> GameObject of a ScrollRect that uses a
    /// ContentSizeFitter (PreferredSize). It writes the viewport's height into a
    /// LayoutElement.preferredHeight on the content, so LayoutUtility returns
    /// <c>max(viewport_height, children_total_height)</c>:
    ///   - small content -> content fills the viewport, flexibleHeight children expand
    ///   - large content -> content grows past the viewport, scrollbar kicks in
    ///
    /// Use <see cref="UIFactory.AttachFillViewportHeight"/> to add safely (handles IL2CPP).
    /// </summary>
    public class FillViewportHeight : MonoBehaviour
    {
        private RectTransform _self;
        private RectTransform _viewport;
        private LayoutElement _layout;
        private float _lastViewportHeight = -1f;
        private bool _initialized;

#if CPP
        public FillViewportHeight(System.IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<FillViewportHeight>();
                Registered = true;
            }
            catch (System.Exception ex)
            {
                Universe.LogWarning($"[FillViewportHeight] Failed to register IL2CPP type: {ex.Message}");
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

            _self = GetComponent<RectTransform>();
            if (_self == null)
            {
                Universe.LogWarning("[FillViewportHeight] No RectTransform on host GameObject");
                return;
            }

            _layout = GetComponent<LayoutElement>();
            if (_layout == null)
                _layout = gameObject.AddComponent<LayoutElement>();

            // Parent of a ScrollRect content is the viewport (with the Mask).
            if (_self.parent != null)
                _viewport = _self.parent.GetComponent<RectTransform>();

            if (_viewport == null)
            {
                Universe.LogWarning("[FillViewportHeight] No parent RectTransform found (expected the ScrollRect viewport)");
                return;
            }

            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            float h = _viewport.rect.height;
            if (Mathf.Abs(h - _lastViewportHeight) < 0.5f) return;
            _lastViewportHeight = h;

            // Setting preferredHeight is enough: ContentSizeFitter (PreferredSize) on this
            // GameObject will pick max(LayoutElement.preferredHeight, VLG.preferredHeight).
            _layout.preferredHeight = h;
            LayoutRebuilder.MarkLayoutForRebuild(_self);
        }

        private void OnEnable()
        {
            _lastViewportHeight = -1f;
        }
    }
}
