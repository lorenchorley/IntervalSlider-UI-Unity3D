using System;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UnityEngine.UI {
    [AddComponentMenu("UI/Interval Slider")]
    [RequireComponent(typeof(RectTransform))]
    public class IntervalSlider : Selectable, IDragHandler, IInitializePotentialDragHandler, ICanvasElement {
        public enum Direction {
            LeftToRight,
            RightToLeft,
            BottomToTop,
            TopToBottom,
        }

        public enum DraggableComponent {
            MinHandle,
            MaxHandle,
            Fill
        }

        private DraggableComponent componentBeingDragged;

        private float LowerValueOffsetFromMouseOnIntervalDragStart;
        private float UpperValueOffsetFromMouseOnIntervalDragStart;

        [Serializable]
        public class SliderEvent : UnityEvent<float, float> { }

        [SerializeField]
        private RectTransform m_FillRect;
        public RectTransform fillRect { get { return m_FillRect; } set { if (SetPropertyUtility.SetClass(ref m_FillRect, value)) { UpdateCachedReferences(); UpdateVisuals(); } } }

        [SerializeField]
        private RectTransform m_LowerHandleRect;
        public RectTransform lowerHandleRect { get { return m_LowerHandleRect; } set { if (SetPropertyUtility.SetClass(ref m_LowerHandleRect, value)) { UpdateCachedReferences(); UpdateVisuals(); } } }

        [SerializeField]
        private RectTransform m_UpperHandleRect;
        public RectTransform upperHandleRect { get { return m_UpperHandleRect; } set { if (SetPropertyUtility.SetClass(ref m_UpperHandleRect, value)) { UpdateCachedReferences(); UpdateVisuals(); } } }

        [Space]

        [SerializeField]
        private Direction m_Direction = Direction.LeftToRight;
        public Direction direction { get { return m_Direction; } set { if (SetPropertyUtility.SetStruct(ref m_Direction, value)) UpdateVisuals(); } }

        [SerializeField]
        private float m_MinValue = 0;
        public float minValue { get { return m_MinValue; } set { if (SetPropertyUtility.SetStruct(ref m_MinValue, value)) { SetLower(m_LowerValue, false); SetUpper(m_UpperValue); UpdateVisuals(); } } }

        [SerializeField]
        private float m_MaxValue = 1;
        public float maxValue { get { return m_MaxValue; } set { if (SetPropertyUtility.SetStruct(ref m_MaxValue, value)) { SetLower(m_LowerValue, false); SetUpper(m_UpperValue); UpdateVisuals(); } } }

        [SerializeField]
        private bool m_WholeNumbers = false;
        public bool wholeNumbers { get { return m_WholeNumbers; } set { if (SetPropertyUtility.SetStruct(ref m_WholeNumbers, value)) { SetLower(m_LowerValue, false); SetUpper(m_UpperValue); UpdateVisuals(); } } }

        [SerializeField]
        protected float m_LowerValue;
        public virtual float lowerValue
        {
            get
            {
                if (wholeNumbers)
                    return Mathf.Round(m_LowerValue);
                return m_LowerValue;
            }
            set
            {
                SetLower(value);
            }
        }

        [SerializeField]
        protected float m_UpperValue;
        public virtual float upperValue
        {
            get
            {
                if (wholeNumbers)
                    return Mathf.Round(m_UpperValue);
                return m_UpperValue;
            }
            set
            {
                SetUpper(value);
            }
        }

        public float normalizedLowerValue
        {
            get
            {
                if (Mathf.Approximately(minValue, maxValue))
                    return 0;
                return Mathf.InverseLerp(minValue, maxValue, lowerValue);
            }
            set
            {
                this.lowerValue = Mathf.Lerp(minValue, maxValue, value);
            }
        }

        public float normalizedUpperValue
        {
            get
            {
                if (Mathf.Approximately(minValue, maxValue))
                    return 0;
                return Mathf.InverseLerp(minValue, maxValue, upperValue);
            }
            set
            {
                this.upperValue = Mathf.Lerp(minValue, maxValue, value);
            }
        }

        [Space]

        // Allow for delegate-based subscriptions for faster events than 'eventReceiver', and allowing for multiple receivers.
        [SerializeField]
        private SliderEvent m_OnValueChanged = new SliderEvent();
        public SliderEvent onValueChanged { get { return m_OnValueChanged; } set { m_OnValueChanged = value; } }

        // Private fields

        private Image m_FillImage;
        private Transform m_FillTransform;
        private RectTransform m_FillContainerRect;
        private Transform m_HandleTransform;
        private RectTransform m_HandleContainerRect;

        // The offset from handle position to mouse down position
        private Vector2 m_Offset = Vector2.zero;

        private DrivenRectTransformTracker m_LowerTracker;
        private DrivenRectTransformTracker m_UpperTracker;
        private DrivenRectTransformTracker m_FillTracker;

        // Size of each step.
        float stepSize { get { return wholeNumbers ? 1 : (maxValue - minValue) * 0.1f; } }

        protected IntervalSlider() { }

#if UNITY_EDITOR
        protected override void OnValidate() {
            base.OnValidate();

            if (wholeNumbers) {
                m_MinValue = Mathf.Round(m_MinValue);
                m_MaxValue = Mathf.Round(m_MaxValue);
            }

            //Onvalidate is called before OnEnabled. We need to make sure not to touch any other objects before OnEnable is run.
            if (IsActive()) {
                UpdateCachedReferences();
                SetLower(m_LowerValue, false);
                SetUpper(m_UpperValue, false);
                // Update rects since other things might affect them even if value didn't change.
                UpdateVisuals();
            }

            var prefabType = UnityEditor.PrefabUtility.GetPrefabType(this);
            if (prefabType != UnityEditor.PrefabType.Prefab && !Application.isPlaying)
                CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
        }

#endif // if UNITY_EDITOR

        public virtual void Rebuild(CanvasUpdate executing) {
#if UNITY_EDITOR
            if (executing == CanvasUpdate.Prelayout)
                onValueChanged.Invoke(lowerValue, upperValue);
#endif
        }

        public virtual void LayoutComplete() { }

        public virtual void GraphicUpdateComplete() { }

        protected override void OnEnable() {
            base.OnEnable();
            UpdateCachedReferences();
            SetLower(m_LowerValue, false);
            SetUpper(m_UpperValue, false);
            // Update rects since they need to be initialized correctly.
            UpdateVisuals();
        }

        protected override void OnDisable() {
            m_LowerTracker.Clear();
            m_UpperTracker.Clear();
            m_FillTracker.Clear();
            base.OnDisable();
        }

        private void GetNormalisedValuesFromFilledImage(out float lowerNormalised, out float upperNormalised) {
            if (m_FillImage == null || m_FillContainerRect == null)
                throw new Exception("Attempting to update filled image when object reference is not set");

            lowerNormalised = m_FillImage.rectTransform.localPosition[(int) axis] * m_FillContainerRect.rect.width / (m_MaxValue - m_MinValue);
            upperNormalised = m_FillImage.fillAmount + lowerNormalised;
        }

        private void SetNormalisedValuesToFilledImage(float lowerNormalised, float upperNormalised) {
            if (m_FillImage == null || m_FillContainerRect == null)
                throw new Exception("Attempting to update filled image when object reference is not set");

            Vector3 newPos = m_FillImage.rectTransform.localPosition;
            newPos[(int) axis] = lowerNormalised * (m_MaxValue - m_MinValue) / m_FillContainerRect.rect.width;
            m_FillImage.rectTransform.localPosition = newPos;
            m_FillImage.fillAmount = upperNormalised - lowerNormalised;
        }

        protected override void OnDidApplyAnimationProperties() {
            // Has value changed? Various elements of the slider have the old normalisedValue assigned, we can use this to perform a comparison.
            // We also need to ensure the value stays within min/max.
            m_LowerValue = ClampValue(m_LowerValue);
            m_UpperValue = ClampValue(m_UpperValue);
            float oldNormalizedLowerValue = normalizedLowerValue;
            float oldNormalizedUpperValue = normalizedUpperValue;
            if (m_FillContainerRect != null) {
                if (m_FillImage != null && m_FillImage.type == Image.Type.Filled) { 
                    GetNormalisedValuesFromFilledImage(out oldNormalizedLowerValue, out oldNormalizedUpperValue);
                } else { 
                    oldNormalizedLowerValue = (reverseValue ? 1 - m_FillRect.anchorMax[(int) axis] : m_FillRect.anchorMin[(int) axis]); // TODO
                    oldNormalizedUpperValue = (reverseValue ? 1 - m_FillRect.anchorMin[(int) axis] : m_FillRect.anchorMax[(int) axis]);
                }
            } else if (m_HandleContainerRect != null) { 
                oldNormalizedLowerValue = (reverseValue ? 1 - m_LowerHandleRect.anchorMax[(int) axis] : m_LowerHandleRect.anchorMin[(int) axis]); // TODO
                oldNormalizedUpperValue = (reverseValue ? 1 - m_UpperHandleRect.anchorMin[(int) axis] : m_UpperHandleRect.anchorMax[(int) axis]); // TODO
            }

            UpdateVisuals();

            if (oldNormalizedLowerValue != normalizedLowerValue || oldNormalizedUpperValue != normalizedUpperValue)
                onValueChanged.Invoke(m_LowerValue, m_UpperValue);
        }

        void UpdateCachedReferences() {
            if (m_FillRect) {
                m_FillTransform = m_FillRect.transform;
                m_FillImage = m_FillRect.GetComponent<Image>();
                if (m_FillTransform.parent != null)
                    m_FillContainerRect = m_FillTransform.parent.GetComponent<RectTransform>();
            } else {
                m_FillContainerRect = null;
                m_FillImage = null;
            }

            if (m_LowerHandleRect) {
                m_HandleTransform = m_LowerHandleRect.transform;
                if (m_HandleTransform.parent != null)
                    m_HandleContainerRect = m_HandleTransform.parent.GetComponent<RectTransform>();
            } else {
                m_HandleContainerRect = null;
            }
        }

        float ClampValue(float input) {
            float newValue = Mathf.Clamp(input, minValue, maxValue);
            if (wholeNumbers)
                newValue = Mathf.Round(newValue);
            return newValue;
        }

        // Set the valueUpdate the visible Image.
        void SetLower(float input) {
            SetLower(input, true);
        }

        protected virtual void SetLower(float input, bool sendCallback) {
            // Clamp the input
            float newValue = ClampValue(input);

            // If the stepped value doesn't match the last one, it's time to update
            if (m_LowerValue == newValue)
                return;

            if (m_LowerValue > m_UpperValue)
                m_UpperValue = m_LowerValue;

            m_LowerValue = newValue;
            UpdateVisuals();
            if (sendCallback)
                m_OnValueChanged.Invoke(m_LowerValue, m_UpperValue);
        }

        // Set the valueUpdate the visible Image.
        void SetUpper(float input) {
            SetUpper(input, true);
        }

        protected virtual void SetUpper(float input, bool sendCallback) {
            // Clamp the input
            float newValue = ClampValue(input);

            // If the stepped value doesn't match the last one, it's time to update
            if (m_UpperValue == newValue)
                return;

            if (m_UpperValue < m_LowerValue)
                m_LowerValue = m_UpperValue;

            m_UpperValue = newValue;
            UpdateVisuals();
            if (sendCallback)
                m_OnValueChanged.Invoke(m_LowerValue, m_UpperValue);
        }

        protected override void OnRectTransformDimensionsChange() {
            base.OnRectTransformDimensionsChange();

            //This can be invoked before OnEnabled is called. So we shouldn't be accessing other objects, before OnEnable is called.
            if (!IsActive())
                return;

            UpdateVisuals();
        }

        enum Axis {
            Horizontal = 0,
            Vertical = 1
        }

        Axis axis { get { return (m_Direction == Direction.LeftToRight || m_Direction == Direction.RightToLeft) ? Axis.Horizontal : Axis.Vertical; } }
        bool reverseValue { get { return m_Direction == Direction.RightToLeft || m_Direction == Direction.TopToBottom; } }

        // Force-update the slider. Useful if you've changed the properties and want it to update visually.
        private void UpdateVisuals() {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UpdateCachedReferences();
#endif

            m_LowerTracker.Clear();
            m_UpperTracker.Clear();
            m_FillTracker.Clear();

            if (m_FillContainerRect != null) {
                m_FillTracker.Add(this, m_FillRect, DrivenTransformProperties.Anchors);
                Vector2 anchorMin = Vector2.zero;
                Vector2 anchorMax = Vector2.one;

                if (m_FillImage != null && m_FillImage.type == Image.Type.Filled) {
                    SetNormalisedValuesToFilledImage(normalizedLowerValue, normalizedUpperValue);
                } else {
                    if (reverseValue) { 
                        anchorMin[(int) axis] = 1 - normalizedUpperValue;
                        anchorMax[(int) axis] = 1 - normalizedLowerValue;
                    } else {
                        anchorMin[(int) axis] = normalizedLowerValue;
                        anchorMax[(int) axis] = normalizedUpperValue;
                    }
                }

                m_FillRect.anchorMin = anchorMin;
                m_FillRect.anchorMax = anchorMax;
            }

            if (m_HandleContainerRect != null) {
                m_LowerTracker.Add(this, m_LowerHandleRect, DrivenTransformProperties.Anchors);
                m_UpperTracker.Add(this, m_UpperHandleRect, DrivenTransformProperties.Anchors);

                Vector2 anchorMin = Vector2.zero;
                Vector2 anchorMax = Vector2.one;
                anchorMin[(int) axis] = anchorMax[(int) axis] = (reverseValue ? (1 - normalizedLowerValue) : normalizedLowerValue); 
                m_LowerHandleRect.anchorMin = anchorMin;
                m_LowerHandleRect.anchorMax = anchorMax;

                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                anchorMin[(int) axis] = anchorMax[(int) axis] = (reverseValue ? (1 - normalizedUpperValue) : normalizedUpperValue); 
                m_UpperHandleRect.anchorMin = anchorMin;
                m_UpperHandleRect.anchorMax = anchorMax;

            }
        }

        // Update the slider's position based on the mouse.
        void UpdateDrag(PointerEventData eventData, Camera cam) {
            if (m_HandleContainerRect ?? m_FillContainerRect != null) { 
                if (m_HandleContainerRect.rect.size[(int) axis] > 0) {

                    Vector2 localCursor;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_HandleContainerRect, eventData.position, cam, out localCursor))
                        return;
                    localCursor -= m_HandleContainerRect.rect.position;

                    switch (componentBeingDragged) {
                    case DraggableComponent.MinHandle:
                        normalizedLowerValue = Mathf.Clamp01((localCursor - m_Offset)[(int) axis] / m_HandleContainerRect.rect.size[(int) axis]);
                        break;
                    case DraggableComponent.MaxHandle:
                        normalizedUpperValue = Mathf.Clamp01((localCursor - m_Offset)[(int) axis] / m_HandleContainerRect.rect.size[(int) axis]);
                        break;
                    case DraggableComponent.Fill:
                        float val = Mathf.Clamp01((localCursor)[(int) axis] / m_HandleContainerRect.rect.size[(int) axis]) * (m_MaxValue - m_MinValue) + m_MinValue;
                        normalizedLowerValue = val - LowerValueOffsetFromMouseOnIntervalDragStart;
                        normalizedUpperValue = val + UpperValueOffsetFromMouseOnIntervalDragStart;
                        break;
                    }
                    
                }
            }
        }

        private bool MayDrag(PointerEventData eventData) {
            return IsActive() && IsInteractable() && eventData.button == PointerEventData.InputButton.Left;
        }

        public override void OnPointerDown(PointerEventData eventData) {
            if (!MayDrag(eventData))
                return;

            base.OnPointerDown(eventData);

            m_Offset = Vector2.zero;
            RectTransform DraggingObject = null;
            if (m_HandleContainerRect != null) { 
                if (RectTransformUtility.RectangleContainsScreenPoint(m_LowerHandleRect, eventData.position, eventData.enterEventCamera)) {
                    DraggingObject = m_LowerHandleRect;
                    componentBeingDragged = DraggableComponent.MinHandle;
                } else if (RectTransformUtility.RectangleContainsScreenPoint(m_UpperHandleRect, eventData.position, eventData.enterEventCamera)) {
                    DraggingObject = m_UpperHandleRect;
                    componentBeingDragged = DraggableComponent.MaxHandle;
                } else if (RectTransformUtility.RectangleContainsScreenPoint(m_FillRect, eventData.position, eventData.enterEventCamera)) {
                    DraggingObject = m_FillRect;
                    componentBeingDragged = DraggableComponent.Fill;

                    Vector2 localCursor;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_HandleContainerRect, eventData.position, eventData.pressEventCamera, out localCursor))
                        return;
                    localCursor -= m_HandleContainerRect.rect.position;

                    float mouseValue = Mathf.Clamp01((localCursor).x / m_HandleContainerRect.rect.size.x) * (m_MaxValue - m_MinValue) + m_MinValue;

                    LowerValueOffsetFromMouseOnIntervalDragStart = mouseValue - m_LowerValue;
                    UpperValueOffsetFromMouseOnIntervalDragStart = m_UpperValue - mouseValue;

                } else {
                    if (Input.mousePosition.x < m_LowerHandleRect.position.x) {
                        DraggingObject = m_LowerHandleRect;
                        componentBeingDragged = DraggableComponent.MinHandle;
                    } else {
                        DraggingObject = m_UpperHandleRect;
                        componentBeingDragged = DraggableComponent.MaxHandle;
                    }
                }

                Vector2 localMousePos;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(DraggingObject, eventData.position, eventData.pressEventCamera, out localMousePos))
                    m_Offset = localMousePos;

            } else {
                // Outside the slider handle - jump to this point instead
                UpdateDrag(eventData, eventData.pressEventCamera);
            }

        }

        public virtual void OnDrag(PointerEventData eventData) {
            if (!MayDrag(eventData))
                return;
            UpdateDrag(eventData, eventData.pressEventCamera);
        }

        public override void OnMove(AxisEventData eventData) {
            if (!IsActive() || !IsInteractable()) {
                base.OnMove(eventData);
                return;
            }

            switch (eventData.moveDir) {
            case MoveDirection.Left:
                if (axis == Axis.Horizontal && FindSelectableOnLeft() == null) { 
                    SetLower(reverseValue ? lowerValue + stepSize : lowerValue - stepSize);
                    SetUpper(reverseValue ? upperValue + stepSize : upperValue - stepSize);
                } else
                    base.OnMove(eventData);
                break;
            case MoveDirection.Right:
                if (axis == Axis.Horizontal && FindSelectableOnRight() == null) {
                    SetLower(reverseValue ? lowerValue - stepSize : lowerValue + stepSize);
                    SetUpper(reverseValue ? upperValue - stepSize : upperValue + stepSize);
                } else
                    base.OnMove(eventData);
                break;
            case MoveDirection.Up:
                if (axis == Axis.Vertical && FindSelectableOnUp() == null) {
                    SetLower(reverseValue ? lowerValue - stepSize : lowerValue + stepSize);
                    SetUpper(reverseValue ? upperValue - stepSize : upperValue + stepSize);
                } else
                    base.OnMove(eventData);
                break;
            case MoveDirection.Down:
                if (axis == Axis.Vertical && FindSelectableOnDown() == null) {
                    SetLower(reverseValue ? lowerValue + stepSize : lowerValue - stepSize);
                    SetUpper(reverseValue ? upperValue + stepSize : upperValue - stepSize);
                } else
                    base.OnMove(eventData);
                break;
            }
        }

        public override Selectable FindSelectableOnLeft() {
            if (navigation.mode == Navigation.Mode.Automatic && axis == Axis.Horizontal)
                return null;
            return base.FindSelectableOnLeft();
        }

        public override Selectable FindSelectableOnRight() {
            if (navigation.mode == Navigation.Mode.Automatic && axis == Axis.Horizontal)
                return null;
            return base.FindSelectableOnRight();
        }

        public override Selectable FindSelectableOnUp() {
            if (navigation.mode == Navigation.Mode.Automatic && axis == Axis.Vertical)
                return null;
            return base.FindSelectableOnUp();
        }

        public override Selectable FindSelectableOnDown() {
            if (navigation.mode == Navigation.Mode.Automatic && axis == Axis.Vertical)
                return null;
            return base.FindSelectableOnDown();
        }

        public virtual void OnInitializePotentialDrag(PointerEventData eventData) {
            eventData.useDragThreshold = false;
        }

        public void SetDirection(Direction direction, bool includeRectLayouts) {
            Axis oldAxis = axis;
            bool oldReverse = reverseValue;
            this.direction = direction;

            if (!includeRectLayouts)
                return;

            if (axis != oldAxis)
                RectTransformUtility.FlipLayoutAxes(transform as RectTransform, true, true);

            if (reverseValue != oldReverse)
                RectTransformUtility.FlipLayoutOnAxis(transform as RectTransform, (int) axis, true, true);
        }
    }
}