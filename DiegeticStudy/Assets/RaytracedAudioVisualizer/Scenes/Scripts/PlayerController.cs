using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")] 
    [SerializeField] private MouseLook mouseLook;

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float jumpHeight = 1.2f; // Added jump height

    [Header("Footsteps")] 
    [SerializeField] private AudioSource normalAudio;

    // [Header("Gunshots")] [SerializeField] private AudioSource gunAudio;

    [Tooltip("How many meters between each footstep")] 
    [SerializeField] private float stepDistance = 1.8f;

    [SerializeField] private bool isNpc;


    private CharacterController _controller;
    private float _distanceSinceLastStep;

    private Vector3 _lastPosition;
    private float _npcDir = 2.0f;
    private float _randomStartTime;
    private float _timeSinceSound;
    private float _timeSinceSwitch;
    private Vector3 _velocity;
    private bool _wasMoving;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _randomStartTime = Random.Range(0.0f, 0.5f);
    }

    private void Start()
    {
        _lastPosition = transform.position;
        _distanceSinceLastStep = 0f;
        _timeSinceSound += _randomStartTime;
    }

    private void Update()
    {
        if (StudyGameManager.Instance != null && StudyGameManager.Instance.IsPaused) return;

        if (!isNpc)
        {
            HandleMovement();
            HandleFootsteps();
            // if (Input.GetMouseButton(0))
            // if (!gunAudio.isPlaying)
            // {
            //     // gunAudio.Play();
            // }
        }
        // else
        // {
        //     if (_timeSinceSound > 0.5f)
        //     {
        //         normalAudio?.Play();
        //         _timeSinceSound = 0;
        //     }
        //
        //     // if (Input.GetMouseButton(0))
        //     //     if (!gunAudio.isPlaying)
        //     //         gunAudio.Play();
        //
        //     // if (_timeSinceSwitch > 5.0f)
        //     // {
        //     //     _npcDir *= -1.0f;
        //     //     _timeSinceSwitch = 0.0f;
        //     // }
        //     //
        //     // var move = transform.forward * _npcDir;
        //     // _controller.Move((move + _velocity) * Time.deltaTime);
        //    // _timeSinceSwitch += Time.deltaTime;
        //     _timeSinceSound += Time.deltaTime;
        // }
    }

    private void HandleMovement()
    {
        // 1. Get raw input
        var x = Input.GetAxis("Horizontal");
        var z = Input.GetAxis("Vertical");

        // 2. Prevent faster diagonal movement by clamping the input magnitude
        Vector3 inputDirection = new Vector3(x, 0f, z);
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f);

        // 3. Calculate movement relative to player's rotation
        var move = transform.right * inputDirection.x + transform.forward * inputDirection.z;
        move *= moveSpeed;

        // 4. Handle Grounding and Jumping
        if (_controller.isGrounded)
        {
            // Keep the player snapped to the ground
            if (_velocity.y < 0f)
            {
                _velocity.y = -2f;
            }

            // Jump when the jump button (Spacebar) is pressed
            if (Input.GetButtonDown("Jump"))
            {
                // Physics equation to calculate the exact velocity needed to reach jumpHeight
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        // 5. Apply gravity over time
        _velocity.y += gravity * Time.deltaTime;

        // 6. Move the controller
        _controller.Move((move + _velocity) * Time.deltaTime);
    }

    private void HandleFootsteps()
    {
        if (!_controller.isGrounded)
        {
            _wasMoving = false;
            _lastPosition = transform.position;
            return;
        }

        var x = Input.GetAxis("Horizontal");
        var z = Input.GetAxis("Vertical");
        var isMoving = Mathf.Abs(x) > 0.01f || Mathf.Abs(z) > 0.01f;

        if (isMoving && !_wasMoving)
            //normalAudio?.Play();
            _distanceSinceLastStep = 0f;

        if (isMoving)
        {
            var delta = Vector3.Distance(transform.position, _lastPosition);
            _distanceSinceLastStep += delta;

            if (_distanceSinceLastStep >= stepDistance) 
            {
                _distanceSinceLastStep -= stepDistance;
                //normalAudio?.Play();
            }
        }

        _wasMoving = isMoving;
        _lastPosition = transform.position;
    }
}