// Gaze Input Module by Peter Koch <peterept@gmail.com>
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// To use:
// 1. Drag onto your EventSystem game object.
// 2. Disable any other Input Modules (eg: StandaloneInputModule & TouchInputModule) as they will fight over selections.
// 3. Make sure your Canvas is in world space and has a GraphicRaycaster (should by default).
// 4. If you have multiple cameras then make sure to drag your VR (center eye) camera into the canvas.
public class pvr_GazeInputController : PointerInputModule
{
    public Camera camera;
    public static float gazeFraction { get; private set; }
    private PointerEventData controllerpointerEventData;
    public RaycastResult controllerCurrentRaycast;
    public GameObject controllerPointer;


    public override void Process()
    {
        HandleLook();
        HandleSelection();

    }
    public override bool ShouldActivateModule()
    {
        if (!base.ShouldActivateModule())
        {
            return false;
        }
        return Pvr_UnitySDKManager.SDK.VRModeEnabled;
    }

    public override void DeactivateModule()
    {
        base.DeactivateModule();
        if (controllerpointerEventData != null)
        {
            HandlePendingClick(controllerpointerEventData);
            HandlePointerExitAndEnter(controllerpointerEventData, null);
            controllerpointerEventData = null;
        }
        eventSystem.SetSelectedGameObject(null, GetBaseEventData());

    }

    private void HandlePendingClick(PointerEventData pointerEnter)
    {
        if (pointerEnter == null)
        {
            return;
        }
        if (!pointerEnter.eligibleForClick)
        {
            return;
        }
        if (!Pvr_UnitySDKManager.SDK.picovrTriggered
            && Time.unscaledTime - pointerEnter.clickTime < 0.1)
        {
            return;
        }

        // Send pointer up and click events.
        ExecuteEvents.Execute(pointerEnter.pointerPress, pointerEnter, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(pointerEnter.pointerPress, pointerEnter, ExecuteEvents.pointerClickHandler);

        // Clear the click state.
        pointerEnter.pointerPress = null;
        pointerEnter.rawPointerPress = null;
        pointerEnter.eligibleForClick = false;
        pointerEnter.clickCount = 0;
    }

    private void UpdateCurrentObject(PointerEventData pointerData)
    {
        // Send enter events and update the highlight.
        var go = pointerData.pointerCurrentRaycast.gameObject;
        HandlePointerExitAndEnter(pointerData, go);
        // Update the current selection, or clear if it is no longer the current object.
        var selected = ExecuteEvents.GetEventHandler<ISelectHandler>(go);
        if (selected == eventSystem.currentSelectedGameObject)
        {
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, GetBaseEventData(),
                                  ExecuteEvents.updateSelectedHandler);
        }
        else
        {
            eventSystem.SetSelectedGameObject(null, pointerData);
        }
    }

    void HandleLook()
    {
        if (controllerpointerEventData == null)
        {
            controllerpointerEventData = new PointerEventData(eventSystem);
        }
        // fake a pointer always being at the center of the screen
        controllerpointerEventData.Reset();
        Vector3 pos = camera.WorldToScreenPoint(controllerPointer.transform.position);
        if (pos.x > 0 && pos.x < Screen.width && pos.y > 0 && pos.y < Screen.height)
        {
            controllerpointerEventData.position = new Vector2(pos.x, pos.y);
            controllerpointerEventData.delta = Vector2.zero;
            List<RaycastResult> controllerraycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(controllerpointerEventData, controllerraycastResults);
            controllerCurrentRaycast = controllerpointerEventData.pointerCurrentRaycast = FindFirstRaycast(controllerraycastResults);
            ProcessMove(controllerpointerEventData);
        }
    }

    void HandleSelection()
    {
        gazeFraction = 0;
        if (controllerpointerEventData.pointerEnter != null)
        {

            GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(controllerpointerEventData.pointerEnter);

            if (handler != null)
            {
                UpdateCurrentObject(controllerpointerEventData);
                HandlePendingClick(controllerpointerEventData);
                if (!Pvr_UnitySDKManager.SDK.picovrTriggered)
                {
                    return;
                }

                controllerpointerEventData.pointerPress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(handler);

                controllerpointerEventData.pressPosition = controllerpointerEventData.position;
                controllerpointerEventData.pointerPressRaycast = controllerpointerEventData.pointerCurrentRaycast;
                controllerpointerEventData.pointerPress =
                    ExecuteEvents.ExecuteHierarchy(handler, controllerpointerEventData, ExecuteEvents.pointerDownHandler)
                    ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(handler);

                controllerpointerEventData.rawPointerPress = handler;
                controllerpointerEventData.eligibleForClick = true;
                controllerpointerEventData.clickCount = 1;
                controllerpointerEventData.clickTime = Time.unscaledTime;
            }
        }

    }


}