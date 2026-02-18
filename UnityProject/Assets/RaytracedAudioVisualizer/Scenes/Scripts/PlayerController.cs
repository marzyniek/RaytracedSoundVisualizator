using System;
using UnityEngine;
using Random = System.Random;


[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private MouseLook mouseLook;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Footsteps")]
    [SerializeField] private AudioSource normalAudio;
    [Header("Gunshots")]
    [SerializeField] private AudioSource gunAudio;

    [Tooltip("How many meters between each footstep")]
    [SerializeField] private float stepDistance = 1.8f;

    [SerializeField] private bool isNpc = false;


    private CharacterController _controller;
    private Vector3 _velocity;

    private Vector3 _lastPosition;
    private float   _distanceSinceLastStep;
    private bool _wasMoving;
    private float _npcDir = 2.0f;
    private float _timeSinceSwitch = 0;
    private float _timeSinceSound = 0;
    private float _randomStartTime = 0;
    
    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _randomStartTime = UnityEngine.Random.Range(0.0f, 0.5f);
    }

    private void Start()
    {
        _lastPosition = transform.position;
        _distanceSinceLastStep = 0f;
        _timeSinceSound += _randomStartTime;
    }

    private void Update()
    {
        if (!isNpc)
        {
            HandleMovement();
            HandleFootsteps();
            if (Input.GetMouseButton(0))
            {
                if (!gunAudio.isPlaying)
                {
                    gunAudio.Play();
                }
            }
        }
        else
        {
            if (_timeSinceSound > 0.5f)
            {
                normalAudio?.Play();
                _timeSinceSound = 0;
            }
            if (Input.GetMouseButton(0))
            {
                if (!gunAudio.isPlaying)
                {
                    gunAudio.Play();
                }
            }
            
            if (_timeSinceSwitch > 5.0f)
            {
                _npcDir *= -1.0f;
                _timeSinceSwitch = 0.0f;    
            }
            Vector3 move = transform.forward * _npcDir;
            _controller.Move((move + _velocity) * Time.deltaTime);
            _timeSinceSwitch += Time.deltaTime;
            _timeSinceSound += Time.deltaTime;


        }
        
    }
    
    private void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;
        move *= moveSpeed;

        if (_controller.isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;

        _velocity.y += gravity * Time.deltaTime;

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

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        bool isMoving = Mathf.Abs(x) > 0.01f || Mathf.Abs(z) > 0.01f;

        if (isMoving && !_wasMoving)
        {
           
           normalAudio?.Play();
           _distanceSinceLastStep = 0f;
        }

        if (isMoving)
        {
            float delta = Vector3.Distance(transform.position, _lastPosition);
            _distanceSinceLastStep += delta;

            if (_distanceSinceLastStep >= stepDistance)
            {
                _distanceSinceLastStep -= stepDistance;
                normalAudio?.Play();
            }
        }

        _wasMoving = isMoving;
        _lastPosition = transform.position;
    }
}