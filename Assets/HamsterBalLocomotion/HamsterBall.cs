using UnityEngine;
using System.Collections;

// Updates the position of the Camera Rig by grabbing the sides of a sphere collider's interior, like a hamster ball.
//
// Script Usage:
// Place the `HamsterBall` script on a scene GameObject containing a SphereCollider. Assign a CameraRig and Controllers. 
// Tweak to taste in inspector.
//
// To prevent collisions with scene objects:
// 1. Create 2 layers, "Ground" and "Ball" for example
// 2. Ensure all navigable surfaces/objects are on the "Ground" layer
// 3. Set the HamsterBall object to the "Ball" layer
// 4. In EDIT>SETTINGS>PROJECT SETTINGS>PHYSICS ensure that "Ball" has nothing checked except for "Ground"
// 
// An optional Sphere Renderer should by on or on a child of the game object. It's important that there is no collider on the child.
//
// *Should the ball reposition to the HMD when we're not rolling?

public class HamsterBall : MonoBehaviour
{
    [Tooltip("CameraRig. The PARENT of which will be moved when rolling the ball.")]
    [SerializeField]
    private Transform m_CameraRig;
    [Header("Settings")]
    [Tooltip("The event used to initiate ball push movement.")]
    [SerializeField]
    private ControllerButton m_ControllerButton = ControllerButton.Trigger;
    [Tooltip("Stregth applied to the ball when moving.")]
    [SerializeField]
    private float m_Strength = 20f;
    [Tooltip("The strength of stopping force when a push is completed. Allows for a gradual, yet controllable stop.")]
    [SerializeField]
    private float m_BrakeStrength = 2f;
    [Header("Optional for fading ball while idle")]
    [Tooltip("The sphere renderer used to fade in/out. Particle shaders recommended")]
    [SerializeField]
    private Renderer m_SphereRenderer;
    [Tooltip("The amount of time during inactivity to wait before fading the ball.")]
    [SerializeField]
    private float m_IdleTime = 2f;
    [Tooltip("The amount of time it takes to fade the ball.")]
    [SerializeField]
    private float m_FadeTime = 1f;

    private SteamVR_TrackedController m_LeftController;
    private SteamVR_TrackedController m_RightController;


    private Rigidbody m_RigidBody;
    private bool m_bIsRolling;
    private Rigidbody m_ConnectedBody;
    private SpringJoint m_Joint;
    private int m_nGrabbedCount;
    private float m_fIdleTimer;
    private bool m_bIsBallHidden;
    private Transform m_AnchorTransform;
    private Transform m_BodyAnchorTransform;

    private enum ControllerButton { Trigger, PadClick, PadTouch, Grip};

    private void Start()
    {
        // Obviously, SteamVR plugin is very important.
        if (SteamVR.instance == null)
            Debug.LogError("SteamVR plugin required. Nothing's gonna work!");

        m_RigidBody = GetComponent<Rigidbody>();

        // Make sure the CameraRig is parented, otherwise we can't move it.
        if (m_CameraRig.parent == null)
            m_CameraRig.parent = new GameObject("CameraRig_MoveableParent").transform;

        SubscribeToControllerEvents();

        // Create temporary transforms for measurement of joint anchor distance, 
        // which scales spring value, preventing "bounciness."
        m_AnchorTransform = new GameObject("Anchor").transform;
        m_AnchorTransform.parent = transform;
        m_BodyAnchorTransform = new GameObject("BodyAnchor").transform;
        m_BodyAnchorTransform.parent = transform;
    }

    // Do what it takes to create the joint and begin rolling...
    private void DoPushBall(object sender, ClickedEventArgs e)
    {
        // Increment grab count. This will avoid erroneously destroying the joint when the 
        // inactive controller receives an OFF event.
        m_nGrabbedCount++;
        // Get Controller Transform
        Transform controllerTransform = (e.controllerIndex == m_LeftController.controllerIndex) ? m_LeftController.transform : m_RightController.transform;
        // Get the near point on the ball where we're pointing
        Vector3 hitPosition = GetBallPointerPosition(controllerTransform);
        // State that we're rolling (so we don't try to brake)
        m_bIsRolling = true;
        // Reset the Idle Timer (used to fade the hamster ball renderer)
        ResetTimer();
        // Create the joint
        CreateJoint(controllerTransform.gameObject, hitPosition);
    }

    private void DoReleaseBall(object sender, ClickedEventArgs e)
    {
        // Decrement the grab count
        m_nGrabbedCount--;
        // If niether controller has the ball, destroy the joint and state that we're 
        // not rolling (which will cause braking).
        if (m_nGrabbedCount < 1)
        {
            ReleaseJoint();
            m_bIsRolling = false;
        }
    }

    private void Update()
    {
        if (!m_bIsRolling)
        {
            // We're not rolling, so slow down and decerement IdleTimer
            m_RigidBody.AddForce(-m_BrakeStrength * m_RigidBody.velocity);
            m_fIdleTimer = m_fIdleTimer - Time.deltaTime;
        }

        if (m_fIdleTimer < 0f && !m_bIsBallHidden)
        {
            // Idle time is up, hide the ball.
            m_bIsBallHidden = true;
            StartCoroutine(FadeBall(true));
        }

        if (m_Joint != null)
        {
            // We have a joint. so..
            // Parent our temp transform to the controller
            m_BodyAnchorTransform.parent = m_Joint.connectedBody.transform;
            // Get the position the controller's pointing to
            Vector3 hitPosition = GetBallPointerPosition(m_Joint.connectedBody.transform);
            // set the Joint's connected anchor to that position (in controller's local space)
            m_Joint.connectedAnchor = m_Joint.connectedBody.transform.InverseTransformPoint(hitPosition);
            // Position the temp transform
            m_BodyAnchorTransform.localPosition = m_Joint.connectedAnchor;
            // Scale spring strength based on the distance between the temp transforms. 
            // This prevents boingy boingy stuff from happening when the ball reaches it's goal position
            m_Joint.spring = Mathf.Lerp(m_Strength, 0, Mathf.InverseLerp(5, 0, Vector3.Distance(m_AnchorTransform.position, m_BodyAnchorTransform.position)));
        }
    }

    private void LateUpdate()
    {
        // Set the camera rig position to the bottom of the ball.
        m_CameraRig.parent.position = transform.position + Vector3.down * (GetComponent<SphereCollider>().bounds.extents.magnitude / 2);
    }

    private Vector3 GetBallPointerPosition(Transform origin)
    {
        // We want to find the point on the outside ball. So we reach out of the ball with our test point.
        float reach = GetComponent<SphereCollider>().radius * 2;
        // Get and return the closest point on the ball.
        Vector3 resultingPoint = GetComponent<SphereCollider>().ClosestPoint(origin.position + origin.forward * reach);
        return resultingPoint;
    }

    private void CreateJoint(GameObject controller, Vector3 anchor)
    {
        // Make sure the controller has a rigidbody, for the purpose of anchoring the joint.
        Rigidbody controllerBody;
        if (!controller.GetComponent<Rigidbody>())
        {
            controller.gameObject.AddComponent<Rigidbody>();
        }
        controllerBody = controller.gameObject.GetComponent<Rigidbody>();
        controllerBody.useGravity = false;
        controllerBody.isKinematic = true;

        // Make a joint if we don't have one already
        if (m_Joint == null)
            m_Joint = gameObject.AddComponent<SpringJoint>();
        // Joint set up
        m_Joint.spring = m_Strength;
        m_Joint.autoConfigureConnectedAnchor = false;
        // Connect the controller and set the anchor position in controller's local space.
        m_Joint.connectedBody = controllerBody;
        m_Joint.connectedAnchor = controllerBody.transform.InverseTransformPoint(anchor);
        // Set the the local anchor in the ball's local space.
        m_Joint.anchor = m_RigidBody.transform.InverseTransformPoint(anchor);
        m_AnchorTransform.position = anchor;
    }

    private void ReleaseJoint()
    {
        // Kill the joint. Sadly, there's no way I could find to simply disable the joint.
        // TODO: experiment with setting a super high MinDistance rather than destroying HamsterBall Joint on release.
        if (m_Joint != null)
        {
            Destroy(m_Joint);
            m_Joint = null;
        }
    }

    private void ResetTimer()
    {
        // Reset the idle timer and show the ball if necessary.
        m_fIdleTimer = m_IdleTime;
        if (m_bIsBallHidden)
        {
            m_bIsBallHidden = false;
            StartCoroutine(FadeBall(false));
        }
    }

    private T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        if (!gameObject.GetComponent<T>())
        {
            gameObject.AddComponent<T>();
        }
        return gameObject.GetComponent<T>();
    }

    private void SubscribeToControllerEvents()
    {
        // Make sure we have TrackedController components on the controllers
        m_LeftController = EnsureComponent<SteamVR_TrackedController>(m_CameraRig.GetComponent<SteamVR_ControllerManager>().left);
        m_RightController = EnsureComponent<SteamVR_TrackedController>(m_CameraRig.GetComponent<SteamVR_ControllerManager>().right);

        // Subscribe to the controller button events selected in the inspector.
        if (m_ControllerButton == ControllerButton.Grip)
        {
            m_LeftController.Gripped += DoPushBall;
            m_LeftController.Ungripped += DoReleaseBall;
            m_RightController.Gripped += DoPushBall;
            m_RightController.Ungripped += DoReleaseBall;
        }
        else if (m_ControllerButton == ControllerButton.PadClick)
        {
            m_LeftController.PadClicked += DoPushBall;
            m_LeftController.PadUnclicked += DoReleaseBall;
            m_RightController.PadClicked += DoPushBall;
            m_RightController.PadUnclicked += DoReleaseBall;
        }
        else if (m_ControllerButton == ControllerButton.PadTouch)
        {
            m_LeftController.PadTouched += DoPushBall;
            m_LeftController.PadUntouched += DoReleaseBall;
            m_RightController.PadTouched += DoPushBall;
            m_RightController.PadUntouched += DoReleaseBall;
        }
        else
        {
            m_LeftController.TriggerClicked += DoPushBall;
            m_LeftController.TriggerUnclicked += DoReleaseBall;
            m_RightController.TriggerClicked += DoPushBall;
            m_RightController.TriggerUnclicked += DoReleaseBall;
        }

    }
    // Fade the ball in or out. 
    private IEnumerator FadeBall(bool fadeOut)
    {
        if (m_SphereRenderer != null)
        {
            // Get the ball color.
            Color color = m_SphereRenderer.material.GetColor("_TintColor");
            float fadeValue;
            if (fadeOut)
            {
                // We're fading out, so loop through until the alpha value is 0.
                fadeValue = m_FadeTime;
                while (fadeValue > 0)
                {
                    fadeValue -= Time.deltaTime;
                    color.a = Mathf.Clamp01(fadeValue / m_FadeTime);
                    m_SphereRenderer.material.SetColor("_TintColor", color);
                    yield return new WaitForEndOfFrame();
                }
            }
            else
            {
                // We're fading out, so loop through until the alpha value is 1.
                fadeValue = 0;
                while (fadeValue < 1)
                {
                    fadeValue += Time.deltaTime;
                    color.a = 0.0f + Mathf.Clamp01(fadeValue / m_FadeTime);
                    m_SphereRenderer.material.SetColor("_TintColor", color);
                    yield return new WaitForEndOfFrame();
                }
            }
        }
    }
}

