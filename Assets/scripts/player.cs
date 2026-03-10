using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.UI;

public class player : MonoBehaviour
{
    // ------------------
    // Inspector Tunables
    // ------------------
    [Header("Walking / Running")]
    [Tooltip("Normal walking top speed")]
    public float walkSpeed = 5f;
    [Tooltip("Top speed while holding the run button")]
    public float runSpeed = 10f;
    [Tooltip("How quickly Mario reaches top speed on the ground")]
    public float groundAccel = 20f;
    [Tooltip("How quickly Mario decelerates on the ground")]
    public float groundDecel = 25f;

    [Header("Crouching")]
    [Tooltip("Speed cap while crouch-walking")]
    public float crouchWalkSpeed = 2.5f;

    [Header("Sliding")]
    [Tooltip("SInitial speed boost applied when a running slide starts")]
    public float slideInitialBoostMulti = 1.2f; 
    [Tooltip("Friction that bleeds off slide speed")]
    public float slideFriction = 18f;
    [Tooltip("Minimum speed before the slide ends automatically")]
    public float slideEndSpeed = 1.5f;

    [Header("Jumping")]
    public float jumpForce = 14f;
    [Tooltip("Extra force for jump #2 in a triple-jump sequence")]
    public float jump2Force = 15.5f;
    [Tooltip("Extra force for jump #3 (the big flip)")]
    public float jump3Force = 18f;
    [Tooltip("Second allowed between jumps to count as a combo")]
    public float tripleJumpTimeWindow = 0.45f;
    [Tooltip("Hold jump to raise higher (released early = lower arc)")]
    public float jumpHoldMulti = 0.55f;
    [Tooltip("Extra downward force when jump is released early")]
    public float jumpCutGravityScale = 3f;
    [Tooltip("Gravity scale during normal fall")]
    public float fallGravityScale = 2.8f;

    [Header("Ground Slam")]
    [Tooltip("Downward speed applied instantly on slam input")]
    public float groundSlamSpeed = 22f;

    [Header("AirSpin")]
    [Tooltip("Seconds velocity is frozen on spin")]
    public float spinFreezeDuration = 0.15f;
    [Tooltip("Seconds the spin lasts")]
    public float spinDuration = 0.35f;
    [Tooltip("Seconds before the air spin can be used again")]
    public float spinCooldown = 0.6f;

    [Header("Wall Sliding & Wall Jump")]
    [Tooltip("How fast Mario slides down a wall")]
    public float wallSlideSpeedScale = 2f;
    [Tooltip("Horizontal kick away from wall")]
    public float wallJumpHForce = 8f;
    [Tooltip("Vertical force for wall jump")]
    public float wallJumpVForce = 13f;
    [Tooltip("Seconds input is locked after a wall jump")]
    public float wallJumpLockTime = 0.2f;

    [Header("Ground Detection")]
    public LayerMask groundLayer;
    [Tooltip("Slight overlap distance for the ground raycast")]
    public float groundCheckDist = 0.08f;

    [Header("Wall Detection")]
    public float wallCheckDist = 0.06f;

    [Header("Collider Heights")]
    [Tooltip("Normal standing collider height")]
    public float standHeight = 1.8f;
    [Tooltip("Crouching collider height")]
    public float crouchHeight = 1.0f;

    // -------------
    // Private State
    // -------------
    Rigidbody2D _rb;
    CapsuleCollider2D _col;

    // action references
    InputAction moveAction;
    InputAction runAction;
    InputAction jumpAction;
    InputAction crouchAction;
    InputAction airSpinAction;

    // ground / wall
    bool _isGrounded;
    bool _isTouchingWallLeft;
    bool _isTouchingWallRight;
    bool _isWallSliding;

    // movement
    float _inputX;
    bool _isRunning;
    bool _isCrouching;
    bool _isSliding;
    float _slideSpeed;
    int _facingDir = 1;

    // jump / air
    int _jumpCount;
    float _lastLandTime = -10f;
    float _lastJumpTime = -10f;
    bool _jumpHeld;
    bool _jumpBuffered;
    float _jumpBufferTimer;
    const float JumpBufferTime = 0.12f;

    // ground slam
    bool _isGroundSlamming;
    bool _groundSlamPressedThisFrame;

    // air spin
    bool _airSpinPressedThisFrame;
    bool _isAirSpinning;
    bool _isSpinFreezing;
    float _airSpinTimer;
    float _spinFreezeTimer;
    float _airSpinCooldownTimer;

    // wall jump
    float _wallJumpLockTimer = 0f;

    // ----------
    // Life Cycle
    // ----------
    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<CapsuleCollider2D>();
        _rb.freezeRotation = true;

        moveAction = InputSystem.actions.FindAction("Move");
        runAction = InputSystem.actions.FindAction("Run");
        jumpAction = InputSystem.actions.FindAction("Jump");
        crouchAction = InputSystem.actions.FindAction("Crouch");
        airSpinAction = InputSystem.actions.FindAction("AirSpin");
    }

    void Update()
    {
        GatherInput();
        UpdateJumpBuffer();
    }

    void FixedUpdate()
    {
        // Check walls
        CheckGround();
        CheckWalls();

        // Timers (lockouts)
        if (_wallJumpLockTimer > 0f) _wallJumpLockTimer -= Time.fixedDeltaTime;
        if (_airSpinCooldownTimer > 0f) _airSpinCooldownTimer -= Time.fixedDeltaTime;

        HandleCrouch();
        HandleSlide();
        HandleHorizontalMovement();
        HandleWallSlide();
        HandleJump();
        HandleGroundSlam();
        HandleAirSpin();
        ApplyGravityTweaks();
        ClampFallSpeed();
    }

    // -----
    // Input
    // -----
    void GatherInput()
    {
        _inputX = moveAction.ReadValue<Vector2>().x;
        _isRunning = runAction.IsPressed();

        // Jump pressed -> buffer it
        if (jumpAction.WasPressedThisFrame())
        {
            _jumpBuffered = true;
            _jumpBufferTimer = JumpBufferTime;
            _jumpHeld = true;
        }

        if (jumpAction.WasReleasedThisFrame())
            _jumpHeld = false;

        // Crouch (held state for crouch-walk / slide)
        _isCrouching = crouchAction.IsPressed();

        if (crouchAction.WasPressedThisFrame())
        {
            _groundSlamPressedThisFrame = true;    
        }

        if (airSpinAction.WasPressedThisFrame())
            _airSpinPressedThisFrame = true;
    }

    void UpdateJumpBuffer()
    {
        if (_jumpBufferTimer > 0f)
        {
            _jumpBufferTimer -= Time.deltaTime;
            if (_jumpBufferTimer <= 0f) _jumpBuffered = false;
        }
    }

    // -----------------------
    // Ground / Wall Detection
    // -----------------------
    void CheckGround()
    {
        // Cast two rays from the bottom corners of the collider
        Vector2 origin = (Vector2)transform.position;
        float halfW = _col.size.x * 0.4f;

        bool left  = Physics2D.Raycast(origin + Vector2.left  * halfW, Vector2.down, _col.size.y * 0.5f + groundCheckDist, groundLayer);
        bool right = Physics2D.Raycast(origin + Vector2.right * halfW, Vector2.down, _col.size.y * 0.5f + groundCheckDist, groundLayer);

        bool wasGrounded = _isGrounded;
        _isGrounded = left || right;

        if (_isGrounded && !wasGrounded)
        {
            OnLand();
        }
    }

    void CheckWalls()
    {
        Vector2 origin = (Vector2)transform.position;
        float halfH = _col.size.y * 0.3f; // mid-height check

        _isTouchingWallLeft  = Physics2D.Raycast(origin - Vector2.up * halfH, Vector2.left,  _col.size.x * 0.5f + wallCheckDist, groundLayer)
                            || Physics2D.Raycast(origin + Vector2.up * halfH, Vector2.left,  _col.size.x * 0.5f + wallCheckDist, groundLayer);

        _isTouchingWallRight = Physics2D.Raycast(origin - Vector2.up * halfH, Vector2.right, _col.size.x * 0.5f + wallCheckDist, groundLayer)
                            || Physics2D.Raycast(origin + Vector2.up * halfH, Vector2.right, _col.size.x * 0.5f + wallCheckDist, groundLayer);
    }

    void OnLand()
    {
        _lastLandTime = Time.time;
        _isGroundSlamming = false;
        _isAirSpinning = false;
        _isWallSliding = false;

        // Reset triple-jump counter if landed too long after last jump
        if (Time.time - _lastLandTime > tripleJumpTimeWindow)
            _jumpCount = 0;
    }

    // ------
    // Crouch
    // ------
    void HandleCrouch()
    {
        if (!_isGrounded || _isSliding) return;

        if (_isCrouching)
            SetColliderHeight(crouchHeight);
        else
            SetColliderHeight(standHeight);
    }

    void SetColliderHeight(float h)
    {
        // Keep feet pinned to ground
        _col.size = new Vector2(_col.size.x, h);
        _col.offset = new Vector2(_col.offset.x, h * 0.5f);
    }

    // -------
    // Sliding
    // -------
    void HandleSlide()
    {
        if (!_isGrounded)
        {
            _isSliding = false;
            return;
        }

        // Trigger slide: crouched while running abhove walk speed
        if (!_isSliding && _isCrouching && Mathf.Abs(_rb.linearVelocityX) > walkSpeed + 0.5f)
        {
            _isSliding = true;
            _slideSpeed = Mathf.Abs(_rb.linearVelocityX) * slideInitialBoostMulti;
            SetColliderHeight(crouchHeight);
        }

        if (_isSliding)
        {
            // Bleed off speed
            _slideSpeed = Mathf.MoveTowards(_slideSpeed, 0f, slideFriction * Time.fixedDeltaTime);

            _rb.linearVelocity = new Vector2(_slideSpeed * _facingDir, _rb.linearVelocityY);

            // End slide
            if (_slideSpeed <= slideEndSpeed || !_isCrouching)
            {
                _isSliding = false;
                if (!_isCrouching) SetColliderHeight(standHeight);
            }
        }
    }

    // -------------------
    // Horizontal Movement
    // -------------------
    void HandleHorizontalMovement()
    {
        // Dont override slide or post-wall-jump lock
        if (_isSliding) return;
        if (_wallJumpLockTimer > 0f) return;
        if (_isGroundSlamming) return;
        if (_isWallSliding) return;

        // Crouch-walk: restrict speed
        float topSpeed = _isRunning ? runSpeed : walkSpeed;
        if (_isCrouching && _isGrounded)
            topSpeed = crouchWalkSpeed;

        // Facing direction
        if (_inputX != 0) _facingDir = (int)Mathf.Sign(_inputX);

        float targetVX = _inputX * topSpeed;
        float accel = _isGrounded
                    ? (_inputX != 0 ? groundAccel : groundDecel)
                    : (_inputX != 0 ? groundAccel * 0.6f : groundDecel * 0.3f);  // less air control

        float newVX = Mathf.MoveTowards(_rb.linearVelocity.x, targetVX, accel * Time.fixedDeltaTime);
        _rb.linearVelocity = new Vector2(newVX, _rb.linearVelocity.y);

        // Flip sprite
        if (_inputX != 0)
            transform.localScale = new Vector3(_facingDir, 1f, 1f);
    }

    // ----------
    // Wall Slide
    // ----------
    void HandleWallSlide()
    {
        if (_isGrounded)
        {
            _isWallSliding = false;
            return;
        }
        
        bool pushingIntoWall = (_isTouchingWallLeft && _inputX < 0)
                            || (_isTouchingWallRight && _inputX > 0);

        if (pushingIntoWall && _rb.linearVelocityY < 0)
        {
            // Enter or stay in wall slide
            _isWallSliding = true;
        }
        else if (_isWallSliding)
        {
            // Only exit if the player has clearly pushed away or hte wall is gone
            bool wallGone = !_isTouchingWallLeft && !_isTouchingWallRight;
            bool pushedAway = (_isTouchingWallLeft && _inputX >= 0) 
                            || (_isTouchingWallRight && _inputX <= 0);

            if (wallGone || pushedAway) 
                _isWallSliding = false;
        }

        if (_isWallSliding)
        {
            // Clamp downward speed - set velocity directly so gravity can't accumulate beyond slide speed 
            _rb.gravityScale = 0f;
            _rb.linearVelocity = new Vector2(0f, Mathf.Max(_rb.linearVelocityY, -wallSlideSpeedScale));
        }
    }

    // -------
    // Jumping
    // -------
    void HandleJump()
    {
        // Wall Jump
        if (_jumpBuffered && _isWallSliding)
        {
            _jumpBuffered = false;
            int kickDir = _isTouchingWallLeft ? 1 : -1;
            _rb.linearVelocity = new Vector2(kickDir * wallJumpHForce, wallJumpVForce);
            _wallJumpLockTimer = wallJumpLockTime;
            _isWallSliding = false;
            _jumpCount = 1;
            _lastJumpTime = Time.time;
            _jumpBuffered = false;
            return;
        }

        // Ground Jump
        if (_jumpBuffered && _isGrounded && !_isSliding)
        {
            _jumpBuffered = false;

            // Reset combo if too slow or crouching
            if (_isCrouching || (Time.time - _lastLandTime > tripleJumpTimeWindow && _jumpCount > 0))
                _jumpCount = 0;

            _jumpCount++;
            _jumpCount = Mathf.Clamp(_jumpCount, 1, 3);

            float force = _jumpCount == 1 ? jumpForce
                        : _jumpCount == 2 ? jump2Force
                                          : jump3Force;

            _rb.linearVelocity = new Vector2(_rb.linearVelocityX, force);
            _lastJumpTime = Time.time;

            // After tiple jump reset combo
            if (_jumpCount == 3) _jumpCount = 0;

            SetColliderHeight(standHeight);
            _isCrouching = false;
            return;
        }

        // Variable jump height - cut velocity when button released early
        if (!_jumpHeld && _rb.linearVelocityY > 0 && !_isGrounded)
        {
            _rb.linearVelocity += Vector2.down * jumpCutGravityScale * Time.fixedDeltaTime;
        }
    }

    // -----------
    // Ground Slam
    // -----------
    void HandleGroundSlam()
    {
        // Trigger: Crouch pressed while airborne
        if (_groundSlamPressedThisFrame && !_isGrounded && !_isGroundSlamming)
        {
            _isGroundSlamming = true;
            _isWallSliding = false;
            _rb.linearVelocity = new Vector2(0f, -groundSlamSpeed); // kill horizontal, slam down
        }

        // Always clear the latch
        _groundSlamPressedThisFrame = false;
    }

    // --------
    // Air Spin
    // --------
    void HandleAirSpin()
    {
        // Trigger: AirSpin pressed while falling, cooldown expired, not slamming
        if (
            !_isGrounded && 
            !_isGroundSlamming && 
            _rb.linearVelocity.y < 0f && 
            !_isAirSpinning && 
            _airSpinCooldownTimer <= 0f && 
            _airSpinPressedThisFrame)
        {
            _isAirSpinning = true;
            _isSpinFreezing = true;
            _spinFreezeTimer = spinFreezeDuration;
            _airSpinTimer = spinDuration;
            _airSpinCooldownTimer = spinCooldown;

            // Small upward nudge
            _rb.linearVelocity = new Vector2(_rb.linearVelocityX, 0f);
        }

        _airSpinPressedThisFrame = false;

        // Tick freeze window
        if (_isSpinFreezing)
        {
            _spinFreezeTimer -= Time.fixedDeltaTime;
            if (_spinFreezeTimer <= 0f)
                _isSpinFreezing = false;
        }

        // Tick overall duration
        if (_isAirSpinning)
        {
            _airSpinTimer -= Time.fixedDeltaTime;
            if (_airSpinTimer <= 0f) 
                _isAirSpinning = false;
        }
    }

    // --------------
    // Gravity Tweaks
    // --------------
    void ApplyGravityTweaks()
    {
        if (_isGrounded)
        {
            _rb.gravityScale = 1f;
            return;
        }

        if (_isWallSliding)
        {
            _rb.gravityScale = 0.3f; 
            return;
        }

        if (_isSpinFreezing)
        {
            _rb.gravityScale = 0f;
            return;
        }

        if (_isAirSpinning)
        {
            _rb.gravityScale = 0.6f;
            return;
        }

        // Heavier fall
        _rb.gravityScale = (_rb.linearVelocityY < 0f) ? fallGravityScale : 1.8f;
    }

    void ClampFallSpeed()
    {
        if (_isGroundSlamming) return;
        float maxFall = _isWallSliding ? wallSlideSpeedScale : 30f;
        if (_rb.linearVelocityY < -maxFall) 
            _rb.linearVelocity = new Vector2(_rb.linearVelocityX, -maxFall);
    }

    // ----------------------
    // Public State Accessors
    // ----------------------
    public bool  IsGrounded       => _isGrounded;
    public bool  IsRunning        => _isRunning && Mathf.Abs(_rb.linearVelocity.x) > walkSpeed * 0.8f;
    public bool  IsCrouching      => _isCrouching && _isGrounded && !_isSliding;
    public bool  IsSliding        => _isSliding;
    public bool  IsJumping        => !_isGrounded && _rb.linearVelocity.y > 0.1f;
    public bool  IsFalling        => !_isGrounded && _rb.linearVelocity.y < -0.1f;
    public bool  IsWallSliding    => _isWallSliding;
    public bool  IsGroundSlamming => _isGroundSlamming;
    public bool  IsAirSpinning    => _isAirSpinning;
    public bool  IsSpinFreezing   => _isSpinFreezing;
    public bool  CanAirSpin       => !_isGrounded && _airSpinCooldownTimer <= 0f && !_isAirSpinning && _rb.linearVelocity.y < 0f;
    public float AirSpinCooldownNormalized => Mathf.Clamp01(_airSpinCooldownTimer / spinCooldown);
    public int   JumpComboCount   => _jumpCount;
    public int   FacingDirection  => _facingDir;

    // -----------------
    // Animation Helpers
    // -----------------
}