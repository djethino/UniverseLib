using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UniverseLib.UI.Models
{
    /// <summary>
    /// A simple wrapper class for working with InputFields, with some helpers and performance improvements.
    /// </summary>
    public class InputFieldRef : UIModel
    {
#if MONO
        // ILRepack-friendly access to InputField's internal cachedInputTextGenerator property.
        static readonly MethodInfo InputField_get_cachedInputTextGenerator
            = AccessTools.PropertyGetter(typeof(InputField), "cachedInputTextGenerator");
#endif

        // Static

        internal static readonly HashSet<InputFieldRef> inputsPendingUpdate = new();

        internal static void UpdateInstances()
        {
            while (inputsPendingUpdate.Any())
            {
                InputFieldRef inputField = inputsPendingUpdate.First();
                LayoutRebuilder.MarkLayoutForRebuild(inputField.Transform);
                inputField.OnValueChanged?.Invoke(inputField.Component.text);

                inputsPendingUpdate.Remove(inputField);
            }
        }

        // Instance

        /// <summary>
        /// Invoked at most once per frame, if the input was changed in the previous frame.
        /// </summary>
        public event Action<string> OnValueChanged;

        /// <summary>
        /// The actual InputField component which this object is a reference to.
        /// </summary>
        public InputField Component { get; }

        /// <summary>
        /// The placeholder Text component.
        /// </summary>
        public Text PlaceholderText { get; }

        /// <summary>
        /// The GameObject which the InputField is attached to.
        /// </summary>
        public override GameObject UIRoot => Component.gameObject;

        /// <summary>
        /// The GameObject which the InputField is attached to.
        /// </summary>
        public GameObject GameObject => Component.gameObject;

        /// <summary>
        /// The RectTransform for this InputField.
        /// </summary>
        public RectTransform Transform { get; }

        /// <summary>
        /// The Text set to the InputField.
        /// </summary>
        public string Text
        {
            get => Component.text;
            set => Component.text = value;
        }

        /// <summary>
        /// A reference to the InputField's cachedInputTextGenerator.
        /// </summary>
#if MONO
        public TextGenerator TextGenerator => (TextGenerator)InputField_get_cachedInputTextGenerator?.Invoke(Component, null);
#else
        public TextGenerator TextGenerator => Component.cachedInputTextGenerator;
#endif
        
        /// <summary>
        /// Returns true if the InputField's vertex count has reached the <see cref="UniversalUI.MAX_TEXT_VERTS"/> limit.
        /// </summary>
        public bool ReachedMaxVerts => TextGenerator.vertexCount >= UniversalUI.MAX_TEXT_VERTS;

        public InputFieldRef(InputField component)
        {
            this.Component = component;
            Transform = component.GetComponent<RectTransform>();
            PlaceholderText = component.placeholder.TryCast<Text>();
            component.onValueChanged.AddListener(OnInputChanged);
        }

        private void OnInputChanged(string value)
        {
            if (!inputsPendingUpdate.Contains(this))
                inputsPendingUpdate.Add(this);
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        public override void ConstructUI(GameObject parent) => throw new NotImplementedException();
    }
}
