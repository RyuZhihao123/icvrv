using UnityEngine;
using System.Collections;

public class EventController : MonoBehaviour
{
    public Camera camera;
    public GameObject controllerpoint;
	// Use this for initialization
	void Start ()
	{

	    transform.gameObject.AddComponent<pvr_GazeInputController>();
	    var controller = transform.GetComponent<pvr_GazeInputController>();
	    if (controller)
	    {
	        controller.camera = camera;
	        controller.controllerPointer = controllerpoint;
	    }

	}
	
	// Update is called once per frame
	void Update () {
	
	}
}
