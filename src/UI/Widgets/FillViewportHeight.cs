using UnityEngine;
using UnityEngine.UI;
// ⚠ Il2CppInterop is ONE of the two IL2CPP chains; `#if CPP` covers both — see FocusRing.
#if INTEROP
using Il2CppInterop.Runtime.Injection;
#endif
#if UNHOLLOWER
using UnhollowerRuntimeLib;
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
        private HorizontalOrVerticalLayoutGroup _group;
        private float _lastApplied = -1f;
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

            // The group that knows what the children need. Read directly rather than through
            // LayoutUtility — see LateUpdate for why that would answer our own question back.
            _group = GetComponent<HorizontalOrVerticalLayoutGroup>();

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

            // What the children actually need. Asked of the layout group itself and NOT of
            // LayoutUtility, which would hand back the value we wrote here last frame: a
            // LayoutElement outranks a layout group (priority 1 against 0), so LayoutUtility
            // returns the element's value instead of the larger of the two.
            //
            // That priority is also why writing the viewport height alone was wrong. It did not
            // raise a floor, it REPLACED what the children asked for — so content taller than
            // the viewport was told it was exactly viewport-sized, and whatever sat at the end
            // of it went out of reach with nothing to scroll to.
            float children = 0f;
            if (_group != null)
            {
                _group.CalculateLayoutInputVertical();
                children = _group.preferredHeight;
            }

            float wanted = Mathf.Max(_viewport.rect.height, children);

            // Compared against what we last APPLIED, not against the viewport: the content grows
            // when a list fills up, not only when the window is resized, and keying on the
            // viewport alone left that growth unnoticed for as long as the panel kept its size.
            if (Mathf.Abs(wanted - _lastApplied) < 0.5f) return;
            _lastApplied = wanted;

            _layout.preferredHeight = wanted;
            LayoutRebuilder.MarkLayoutForRebuild(_self);
        }

        private void OnEnable()
        {
            _lastApplied = -1f;
        }
    }
}
