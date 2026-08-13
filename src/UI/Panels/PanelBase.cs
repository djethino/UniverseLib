using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UniverseLib.Input;
using UniverseLib.UI.Models;

namespace UniverseLib.UI.Panels
{
    public abstract class PanelBase : UIBehaviourModel
    {
        public UIBase Owner { get; }

        public abstract string Name { get; }

        public abstract int MinWidth { get; }
        public abstract int MinHeight { get; }

        /// <summary>
        /// Maximum width for resize. Override to constrain panel width.
        /// Default is int.MaxValue (no constraint).
        /// </summary>
        public virtual int MaxWidth => int.MaxValue;

        /// <summary>
        /// Maximum height for resize. Override to constrain panel height.
        /// Default is int.MaxValue (no constraint).
        /// </summary>
        public virtual int MaxHeight => int.MaxValue;

        public abstract Vector2 DefaultAnchorMin { get; }
        public abstract Vector2 DefaultAnchorMax { get; }
        public virtual Vector2 DefaultPosition { get; }

        public virtual bool CanDragAndResize => true;
        public PanelDragger Dragger { get; internal set; }

        public override GameObject UIRoot => uiRoot;
        protected GameObject uiRoot;
        public RectTransform Rect { get; private set; }
        public GameObject ContentRoot { get; protected set; }

        public GameObject TitleBar { get; private set; }

        public PanelBase(UIBase owner)
        {
            Owner = owner;

            ConstructUI();

            // Add to owner
            Owner.Panels.AddPanel(this);
        }

        public override void Destroy()
        {
            Owner.Panels.RemovePanel(this);
            base.Destroy();
        }

        public virtual void OnFinishResize()
        {
        }

        public virtual void OnFinishDrag()
        {
        }

        // Frame on which this panel asked to close from inside a click. -1 = not pending.
        int closeRequestedOnFrame = -1;

        public override void SetActive(bool active)
        {
            // ⚠ Closing from inside an OnClick pulls the ground from under the click being
            // resolved: the pressed object goes inactive mid-dispatch, and the module then looks
            // for somewhere else to deliver it — which is how clicking a panel's close button
            // ended up pressing whatever the game had behind it.
            //
            // Measured before this existed: during the capture every one of the game's raycasters
            // was correctly blocked, and the click still landed the moment input was handed back.
            // It was never travelling through; it was waiting. So delaying the handover changed
            // nothing (2 frames and 10 were both tried) — what had to move was the disappearing.
            //
            // One frame is enough: the module finishes this dispatch on a button that still
            // exists, and the panel goes away before anyone could see it linger.
            if (!active && InputCapture.InModuleProcess)
            {
                closeRequestedOnFrame = Time.frameCount;
                Universe.Log($"[PanelBase] '{Name}' close deferred for {CloseDelayFrames} frames");
                return;
            }

            closeRequestedOnFrame = -1;

            if (this.Enabled != active)
                base.SetActive(active);

            if (!active)
                this.Dragger.WasDragging = false;
            else
            {
                this.UIRoot.transform.SetAsLastSibling();
                this.Owner.Panels.InvokeOnPanelsReordered();
            }
        }
        
        /// <summary>
        /// Carry out a close that was asked for from inside a click, once that click is over.
        /// </summary>
        internal void TickPendingClose()
        {
            if (closeRequestedOnFrame < 0 || Time.frameCount < closeRequestedOnFrame + CloseDelayFrames)
                return;

            Universe.Log($"[PanelBase] '{Name}' deferred close happening now");
            closeRequestedOnFrame = -1;
            SetActive(false);
        }

        /// <summary>
        /// Frames a click-initiated close waits before it actually happens.
        /// </summary>
        /// <remarks>
        /// One is the sane value — just long enough for the dispatch to finish on a button that
        /// still exists. Exposed so it can be pushed to something absurd while investigating: a
        /// visible pause is the only way to tell "the delay does not help" from "the delay never
        /// happened", and those two look identical from in front of the game.
        /// </remarks>
        public static int CloseDelayFrames = 1;

        protected virtual void OnClosePanelClicked()
        {
            this.SetActive(false);
        }

        // Setting size and position

        public virtual void SetDefaultSizeAndPosition()
        {
            Rect.localPosition = DefaultPosition;
            Rect.pivot = new Vector2(0f, 1f);

            Rect.anchorMin = DefaultAnchorMin;
            Rect.anchorMax = DefaultAnchorMax;

            LayoutRebuilder.ForceRebuildLayoutImmediate(this.Rect);

            EnsureValidPosition();
            EnsureValidSize();

            Dragger.OnEndResize();
        }

        public virtual void EnsureValidSize()
        {
            if (Rect.rect.width < MinWidth)
                Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, MinWidth);

            if (Rect.rect.height < MinHeight)
                Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, MinHeight);

            Dragger.OnEndResize();
        }

        public virtual void EnsureValidPosition()
        {
            // Prevent panel going oustide screen bounds

            Vector3 pos = this.Rect.localPosition;
            Vector2 dimensions = Owner.Panels.ScreenDimensions;

            float halfW = dimensions.x * 0.5f;
            float halfH = dimensions.y * 0.5f;

            pos.x = Math.Max(-halfW - this.Rect.rect.width + 50, Math.Min(pos.x, halfW - 50));
            pos.y = Math.Max(-halfH + 50, Math.Min(pos.y, halfH));

            this.Rect.localPosition = pos;
        }

        // UI Construction

        protected abstract void ConstructPanelContent();

        protected virtual PanelDragger CreatePanelDragger() => new(this);

        public virtual void ConstructUI()
        {
            // create core canvas
            uiRoot = UIFactory.CreatePanel(Name, Owner.Panels.PanelHolder, out GameObject contentRoot);
            ContentRoot = contentRoot;
            Rect = this.uiRoot.GetComponent<RectTransform>();

            UIFactory.SetLayoutGroup<VerticalLayoutGroup>(this.ContentRoot, false, false, true, true, 0, 0, 0, 0, 0, TextAnchor.UpperLeft);
            UIFactory.SetLayoutElement(ContentRoot, 0, 0, flexibleWidth: 9999, flexibleHeight: 9999);

            // Title bar - cleaner, slimmer design
            TitleBar = UIFactory.CreateHorizontalGroup(ContentRoot, "TitleBar", false, true, true, true, 0,
                new Vector4(8, 4, 8, 4), UIFactory.Colors.TitleBarBackground);
            UIFactory.SetLayoutElement(TitleBar, minHeight: 28, flexibleHeight: 0);
            // Round at the top only: the bar crowns the panel, so it follows its corners there and
            // stays square where it meets the content underneath.
            UIFactory.SetShape(TitleBar.GetComponent<Image>(), UIFactory.Shapes.CardTop);

            // Title text
            Text titleTxt = UIFactory.CreateLabel(TitleBar, "TitleText", Name, TextAnchor.MiddleLeft, fontSize: 14);
            titleTxt.fontStyle = FontStyle.Normal;
            UIFactory.SetLayoutElement(titleTxt.gameObject, 50, 28, 9999, 0);

            // Close button - modern X style
            GameObject closeHolder = UIFactory.CreateUIObject("CloseHolder", TitleBar);
            UIFactory.SetLayoutElement(closeHolder, minHeight: 28, flexibleHeight: 0, minWidth: 28, flexibleWidth: 0);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(closeHolder, false, false, true, true, 0, childAlignment: TextAnchor.MiddleCenter);

            ButtonRef closeBtn = UIFactory.CreateButton(closeHolder, "CloseButton", "✕");
            UIFactory.SetLayoutElement(closeBtn.Component.gameObject, minHeight: 22, minWidth: 22, flexibleWidth: 0);
            RuntimeHelper.SetColorBlock(closeBtn.Component,
                UIFactory.Colors.ButtonNormal,
                new Color(0.85f, 0.35f, 0.35f), // Red on hover
                new Color(0.70f, 0.25f, 0.25f)); // Darker red on press

            // Make close button text slightly smaller
            Text closeBtnText = closeBtn.Component.GetComponentInChildren<Text>();
            if (closeBtnText != null)
            {
                closeBtnText.fontSize = 12;
            }

            closeBtn.OnClick += () =>
            {
                OnClosePanelClicked();
            };

            if (!CanDragAndResize)
                TitleBar.SetActive(false);

            // Panel dragger
            Dragger = CreatePanelDragger();
            Dragger.OnFinishResize += OnFinishResize;
            Dragger.OnFinishDrag += OnFinishDrag;

            // content (abstract)
            ConstructPanelContent();
            SetDefaultSizeAndPosition();

            RuntimeHelper.StartCoroutine(LateSetupCoroutine());
        }

        private IEnumerator LateSetupCoroutine()
        {
            yield return null;

            LateConstructUI();
        }

        protected virtual void LateConstructUI()
        {
            SetDefaultSizeAndPosition();
        }

#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
        [Obsolete("Not used. Use ConstructUI() instead.")]
        public override void ConstructUI(GameObject parent) => ConstructUI();
#pragma warning restore CS0809

    }
}
