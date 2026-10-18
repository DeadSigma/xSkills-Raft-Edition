using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace xSkills.GUI
{
    // Обрабатывается только Canvas навыков, без перехвата штатного ввода игры
    internal sealed class SkillPointerInput
    {
        private readonly EventSystem events;
        private readonly GraphicRaycaster raycaster;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private GameObject hovered;
        private GameObject leftPressed;
        private GameObject rightPressed;
        private GameObject dragTarget;
        private Vector2 dragOrigin;
        private Vector2 lastPosition;
        private bool dragging;

        public SkillPointerInput(EventSystem system, GraphicRaycaster uiRaycaster)
        {
            events = system;
            raycaster = uiRaycaster;
        }

        public void Reset()
        {
            if (hovered != null && events != null)
            {
                PointerEventData pointer = new PointerEventData(events);
                pointer.position = Input.mousePosition;
                ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerExitHandler);
            }
            hovered = null;
            leftPressed = null;
            rightPressed = null;
            dragTarget = null;
            dragging = false;
            lastPosition = Input.mousePosition;
        }

        public void Tick()
        {
            if (events == null || raycaster == null) return;
            Vector2 position = Input.mousePosition;
            PointerEventData pointer = new PointerEventData(events);
            pointer.position = position;
            pointer.delta = position - lastPosition;
            lastPosition = position;
            hits.Clear();
            raycaster.Raycast(pointer, hits);
            GameObject hit = null;
            if (hits.Count != 0)
            {
                hit = hits[0].gameObject;
                pointer.pointerCurrentRaycast = hits[0];
            }

            GameObject enter = hit == null ? null : ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hit);
            if (enter != hovered)
            {
                if (hovered != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerExitHandler);
                hovered = enter;
                if (hovered != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerEnterHandler);
            }

            Press(pointer, hit, PointerEventData.InputButton.Left, 0);
            Press(pointer, hit, PointerEventData.InputButton.Right, 1);

            if (Input.GetMouseButton(0) && dragTarget != null)
            {
                // Перетаскивание не зависит от наличия обработчика клика
                pointer.button = PointerEventData.InputButton.Left;
                pointer.pointerDrag = dragTarget;
                pointer.pressPosition = dragOrigin;
                if (!dragging && (position - dragOrigin).sqrMagnitude >= 64f)
                {
                    dragging = true;
                    ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.beginDragHandler);
                }
                if (dragging)
                    ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.dragHandler);
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > .001f && hit != null)
            {
                ScrollRect scroll = hit.GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.isActiveAndEnabled && scroll.vertical &&
                    scroll.content != null && scroll.viewport != null)
                {
                    float remaining = scroll.content.rect.height - scroll.viewport.rect.height;
                    if (remaining > 1f)
                    {
                        float step = wheel * scroll.scrollSensitivity / remaining;
                        scroll.verticalNormalizedPosition = Mathf.Clamp01(
                            scroll.verticalNormalizedPosition + step);
                    }
                }
                else
                {
                    pointer.scrollDelta = new Vector2(0f, wheel);
                    ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.scrollHandler);
                }
            }

            if (events.currentSelectedGameObject != null)
            {
                var selected = new BaseEventData(events);
                ExecuteEvents.Execute(events.currentSelectedGameObject, selected, ExecuteEvents.updateSelectedHandler);
            }
        }

        private void Press(PointerEventData pointer, GameObject hit,
            PointerEventData.InputButton button, int mouseButton)
        {
            GameObject click = hit == null ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            pointer.button = button;
            if (Input.GetMouseButtonDown(mouseButton))
            {
                if (mouseButton == 0)
                {
                    leftPressed = click;
                    dragOrigin = pointer.position;
                    dragTarget = hit == null ? null : ExecuteEvents.GetEventHandler<IDragHandler>(hit);
                    dragging = false;
                }
                else rightPressed = click;

                pointer.pressPosition = pointer.position;
                pointer.eligibleForClick = true;
                if (hit != null) ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            }
            if (!Input.GetMouseButtonUp(mouseButton)) return;
            GameObject pressed = mouseButton == 0 ? leftPressed : rightPressed;
            if (hit != null) ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
            if (mouseButton == 0 && dragging && dragTarget != null)
            {
                pointer.pointerDrag = dragTarget;
                pointer.pressPosition = dragOrigin;
                ExecuteEvents.Execute(dragTarget, pointer, ExecuteEvents.endDragHandler);
            }
            else if (pressed != null && pressed == click)
            {
                pointer.eligibleForClick = true;
                ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerClickHandler);
            }
            if (mouseButton == 0)
            {
                leftPressed = null;
                dragTarget = null;
                dragging = false;
            }
            else rightPressed = null;
        }
    }
}
