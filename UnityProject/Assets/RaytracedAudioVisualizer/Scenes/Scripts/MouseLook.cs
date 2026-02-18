using UnityEngine;

/// <summary>
/// Controls first-person camera rotation: free yaw on the player body and clamped pitch on the camera pivot.
/// </summary>
public class MouseLook : MonoBehaviour
{
    // Sensitivity for mouse movement (degrees per second)
    [Header("Speeds")]
    [SerializeField] private float mouseSensitivity = 100f;

    // Minimum pitch angle (looking down) in degrees
    [Header("Pitch Limits (°)")]
    [SerializeField] private float minPitch = -40f;
    // Maximum pitch angle (looking up) in degrees
    [SerializeField] private float maxPitch =  40f;

    // Reference to the player's root transform for yaw rotation
    [Tooltip("Assign your Player (root) here")]
    [SerializeField] private Transform playerBody;

    // Current pitch rotation around the X axis
    private float _pitch;

    /// <summary>
    /// Locks the cursor to the center of the screen on start.
    /// </summary>
    private void Start() => Cursor.lockState = CursorLockMode.Locked;

    /// <summary>
    /// Reads mouse input each frame, applies yaw to the player body and applies clamped pitch to this camera pivot.
    /// </summary>
    private void Update()
    {
        // Read mouse movement deltas
        float mX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Rotate the player body around the Y axis (yaw)
        playerBody.Rotate(Vector3.up * mX);

        // Accumulate pitch and clamp within specified limits
        _pitch -= mY;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // Apply clamped pitch to the camera pivot
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}