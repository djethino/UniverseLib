using System;
using UnityEngine;
using UnityEngine.UI;
#if CPP
using Il2CppInterop.Runtime.Injection;
#endif

namespace UniverseLib.UI.Widgets
{
    /// <summary>
    /// Keeps a uGUI <see cref="Text"/> on a single line and trims it with an ellipsis when it does
    /// not fit its RectTransform — uGUI has no native ellipsis, its only options are wrapping (which
    /// pushes the tail out of a one-line control) or a hard clip with no visual cue.
    ///
    /// Set the value through <see cref="FullText"/>, not through <c>Text.text</c>: the component owns
    /// the displayed string and recomputes it whenever the value or the available width changes, so
    /// the label stays correct when the window is resized.
    ///
    /// Add via <see cref="UIFactory.ConfigureEllipsis"/>.
    /// </summary>
    public class EllipsisLabel : MonoBehaviour
    {
        /// <summary>Character appended to a trimmed string.</summary>
        public const string Ellipsis = "…";

        private Text _text;
        private string _fullText = "";
        private string _appliedText;
        private float _appliedWidth = -1f;
        private bool _initialized;

        /// <summary>
        /// The complete, untrimmed string. Assign this instead of <c>Text.text</c>.
        /// </summary>
        public string FullText
        {
            get => _fullText;
            set
            {
                _fullText = value ?? "";
                _appliedWidth = -1f; // force a recompute on the next frame
            }
        }

#if CPP
        public EllipsisLabel(IntPtr ptr) : base(ptr) { }

        internal static bool Registered;

        internal static void RegisterType()
        {
            if (Registered) return;
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<EllipsisLabel>();
                Registered = true;
            }
            catch (Exception ex)
            {
                Universe.LogWarning($"[EllipsisLabel] Failed to register IL2CPP type: {ex.Message}");
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

            _text = GetComponent<Text>();
            if (_text == null)
            {
                Universe.LogWarning("[EllipsisLabel] No Text component found");
                return;
            }

            // We do the cutting ourselves, so let the generator lay the string out on one
            // unbounded line — wrapping or clipping here would fight the measurement below.
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;

            if (string.IsNullOrEmpty(_fullText) && !string.IsNullOrEmpty(_text.text))
                _fullText = _text.text;

            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                Initialize();
                if (!_initialized) return;
            }

            float width = _text.rectTransform.rect.width;
            if (width <= 0f) return; // not laid out yet

            if (_appliedText == _fullText && Mathf.Approximately(width, _appliedWidth))
                return;

            _appliedText = _fullText;
            _appliedWidth = width;
            Apply(width);
        }

        /// <summary>Recompute now (e.g. after changing the font or font size).</summary>
        public void Refresh()
        {
            _appliedWidth = -1f;
        }

        /// <summary>
        /// Width the untrimmed value would need. Lets a container size itself to its content
        /// instead of trimming (measurement only — the displayed text is left untouched).
        /// </summary>
        public float PreferredFullWidth
        {
            get
            {
                if (!_initialized) Initialize();
                return _initialized && !string.IsNullOrEmpty(_fullText) ? Measure(_fullText) : 0f;
            }
        }

        private void Apply(float maxWidth)
        {
            if (string.IsNullOrEmpty(_fullText))
            {
                _text.text = "";
                return;
            }

            if (Measure(_fullText) <= maxWidth)
            {
                _text.text = _fullText;
                return;
            }

            // Longest prefix that still fits once the ellipsis is appended.
            int low = 0, high = _fullText.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Measure(_fullText.Substring(0, mid) + Ellipsis) <= maxWidth)
                    low = mid;
                else
                    high = mid - 1;
            }

            _text.text = low > 0 ? _fullText.Substring(0, low) + Ellipsis : Ellipsis;
        }

        /// <summary>
        /// Width the string would occupy, measured without touching the displayed text (same
        /// generator path as <c>Text.preferredWidth</c>).
        /// </summary>
        private float Measure(string value)
        {
            TextGenerationSettings settings = _text.GetGenerationSettings(Vector2.zero);
            settings.generateOutOfBounds = false;

            float pixelsPerUnit = _text.pixelsPerUnit;
            if (pixelsPerUnit <= 0f) pixelsPerUnit = 1f;

            return _text.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / pixelsPerUnit;
        }
    }
}
