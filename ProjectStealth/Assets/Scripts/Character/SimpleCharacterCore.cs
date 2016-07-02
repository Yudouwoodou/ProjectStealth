﻿using UnityEngine;
using System.Collections;

public class SimpleCharacterCore : MonoBehaviour
{
    protected CharacterStats charStats;

    protected int previousFacingDirection = 1;
    //public int FacingDirection = 1;
    private SpriteRenderer spriteRenderer;
    public Animator Anim;
    public IInputManager InputManager;
    //public Vector2 Velocity;
    //public BoxCollider2D characterCollider;

    //gravity vars
    //public bool OnTheGround = false;
    private const float MAX_VERTICAL_SPEED = 10.0f;
    private const float GRAVITATIONAL_FORCE = -30.0f;

    //jump vars
    private const float JUMP_VERTICAL_SPEED = 6.0f;
    protected const float JUMP_HORIZONTAL_SPEED_MIN = 2.5f;
    protected const float JUMP_HORIZONTAL_SPEED_MAX = 4.0f;
    //protected const float JUMP_ACCEL = 4.0f;

    private const float JUMP_CONTROL_TIME = 0.20f; //maximum duration of a jump if you hold it
    private const float JUMP_DURATION_MIN = 0.10f; //minimum duration of a jump if you tap it
    private const float JUMP_GRACE_PERIOD_TIME = 0.10f; //how long a player has to jump if they slip off a platform
    private bool jumpGracePeriod; //variable for jump tolerance if a player walks off a platform but wants to jump
    private float jumpGracePeriodTime;
    //public bool IsJumping; //TODO: protected
	//protected float jumpInputTime;
	//public bool jumpTurned; //TODO: protected

    //walk and run vars
    private const float MAX_HORIZONTAL_SPEED = 10.0f;
    protected float WALK_SPEED = 1.0f; //used for cutscenes with Alice, guards will walk when not alerted
    protected float SNEAK_SPEED = 2.0f; //Alice's default speed, enemies that were walking will use this speed when on guard
    protected float RUN_SPEED = 4.5f;
    protected float ACCELERATION = 6.0f; // acceleration used for velocity calcs when running
    protected float DRAG = 15.0f; // how quickly a character decelerates when running
    public enum moveState { isWalking, isSneaking, isRunning }; //TODO: protected
    public moveState currentMoveState = moveState.isWalking; //TODO: protected
    protected moveState prevMoveState;
    //protected float characterAccel = 0.0f;
    private bool startRun; // this bool prevents wonky shit from happening if you turn around during a run

    // ledge logic
    public bool lookingOverLedge; // TODO: private
    public bool againstTheLedge; // TODO: private

	// bezier curve vars for getting up ledges and jumping over cover
	//protected Vector2 bzrStartPosition;
	//protected Vector2 bzrEndPosition;
	//protected Vector2 bzrCurvePosition;
	//protected float bzrDistance;

    // Use this for initialization
    public virtual void Start()
    {
		charStats = GetComponent<CharacterStats>();
        //characterCollider = GetComponent<BoxCollider2D>();
        Anim = GetComponent<Animator>();
        InputManager = GetComponent<IInputManager>();

        // character sprite is now a child object. If a chracter sprite has multiple child sprite tho, this might break
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        //Velocity = new Vector2(0.0f, 0.0f);
        jumpGracePeriod = false;
        //char_stats.IsJumping = false;
        //jumpInputTime = 0.0f;
    }

    public virtual void Update()
    {
        MovementInput();

        CalculateDirection();
        // set Sprite flip
        if (previousFacingDirection != charStats.FacingDirection)
            SetFacing();

        if (jumpGracePeriod == true)
        {
            jumpGracePeriodTime = jumpGracePeriodTime + Time.deltaTime * TimeScale.timeScale;
            if (jumpGracePeriodTime >= JUMP_GRACE_PERIOD_TIME)
                jumpGracePeriod = false;
        }
    }

    public virtual void LateUpdate()
    {
        // prev state assignments
        prevMoveState = currentMoveState;
        previousFacingDirection = charStats.FacingDirection;
    }

    public virtual void FixedUpdate()
    {
        // Give all characters gravity
        HorizontalVelocity();
        VerticalVelocity();
        Collisions();

        //move the character after all calculations have been done
        if (charStats.CurrentMasterState == CharacterStats.MasterState.defaultState)
        {
            transform.Translate(charStats.Velocity);
        }
    }

    private void HorizontalVelocity()
    {
        if(charStats.OnTheGround)
        {
            //movement stuff
            if (currentMoveState == moveState.isWalking)
                charStats.Velocity.x = WALK_SPEED * InputManager.HorizontalAxis;
            else if (currentMoveState == moveState.isSneaking)
            {
                // if current speed is greater than sneak speed, then decel to sneak speed.
                if(Mathf.Abs(charStats.Velocity.x) > SNEAK_SPEED)
                {
                    if (charStats.Velocity.x > 0.0f)
                        charStats.Velocity.x = charStats.Velocity.x - DRAG * Time.deltaTime * TimeScale.timeScale;
                    else if (charStats.Velocity.x < 0.0f)
                        charStats.Velocity.x = charStats.Velocity.x + DRAG * Time.deltaTime * TimeScale.timeScale;
                }
                else
                {
                    charStats.Velocity.x = SNEAK_SPEED * InputManager.HorizontalAxis;
                }
            }
            else if (currentMoveState == moveState.isRunning)
            {   
                //smooth damp to 0 if there's no directional input for running or if you're trying to run the opposite direction
                if (charStats.CharacterAccel == 0.0f || (charStats.CharacterAccel < 0.0f && charStats.Velocity.x > 0.0f) || (charStats.CharacterAccel > 0.0f && charStats.Velocity.x < 0.0f))
                {
                    if (Mathf.Abs(charStats.Velocity.x) > SNEAK_SPEED)
                    {
                        //print("SKID BOIS");
                        if (charStats.Velocity.x > 0.0f)
                            charStats.Velocity.x = charStats.Velocity.x - DRAG * Time.deltaTime * TimeScale.timeScale;
                        else if(charStats.Velocity.x < 0.0f)
                            charStats.Velocity.x = charStats.Velocity.x + DRAG * Time.deltaTime * TimeScale.timeScale;
                    }
                    else
                    {
                        charStats.Velocity.x = 0.0f;
                    }
                }
                else
                    charStats.Velocity.x = charStats.Velocity.x + charStats.CharacterAccel * Time.deltaTime * TimeScale.timeScale;

                charStats.Velocity.x = Mathf.Clamp(charStats.Velocity.x, -RUN_SPEED, RUN_SPEED);
            }
        }
        else
        {
            if (charStats.JumpTurned)
                HorizontalJumpVelNoAccel(SNEAK_SPEED);
            else
            {
                if (currentMoveState == moveState.isWalking || currentMoveState == moveState.isSneaking)
                {
                    HorizontalJumpVelAccel();
                }
                else if (currentMoveState == moveState.isRunning)
                {
                    HorizontalJumpVelAccel();
                }
            }
        }

        charStats.Velocity.x = Mathf.Clamp(charStats.Velocity.x, -MAX_HORIZONTAL_SPEED, MAX_HORIZONTAL_SPEED);

        if (Mathf.Approximately(charStats.Velocity.x - 1000000, -1000000))
        {
            charStats.Velocity.x = 0;
        }
    }

    public void HorizontalJumpVelNoAccel(float speed)
    {
        if (charStats.CharacterAccel > 0.0f)
            charStats.Velocity.x = speed;
        else if (charStats.CharacterAccel < 0.0f)
            charStats.Velocity.x = -speed;
    }

    protected void HorizontalJumpVelAccel()
    {
        if (charStats.CharacterAccel < 0.0f && charStats.Velocity.x >= 0.0f)
        {
            charStats.Velocity.x = -SNEAK_SPEED;
        }
        else if (charStats.CharacterAccel > 0.0f && charStats.Velocity.x <= 0.0f)
        {
            charStats.Velocity.x = SNEAK_SPEED;
        }
        else
        {
            charStats.Velocity.x = charStats.Velocity.x + charStats.CharacterAccel * Time.deltaTime * TimeScale.timeScale;
            charStats.Velocity.x = Mathf.Clamp(charStats.Velocity.x, -JUMP_HORIZONTAL_SPEED_MAX, JUMP_HORIZONTAL_SPEED_MAX);
        }
    }

    private void VerticalVelocity()
    {
        charStats.Velocity.y = charStats.Velocity.y + GRAVITATIONAL_FORCE * Time.deltaTime * TimeScale.timeScale;

        //override the vertical velocity if we're in hte middle of jumping
        if (charStats.IsJumping)
        {
            charStats.JumpInputTime = charStats.JumpInputTime + Time.deltaTime * Time.timeScale;
            if ((InputManager.JumpInput && charStats.JumpInputTime <= JUMP_CONTROL_TIME) || charStats.JumpInputTime <= JUMP_DURATION_MIN)
            {
                charStats.Velocity.y = JUMP_VERTICAL_SPEED;
            }
            else
            {
                charStats.IsJumping = false;
            }
        }

        // if you turned while jumping, turn off the jump var
        if (charStats.JumpTurned && charStats.Velocity.y > 0.0f)
        {
            charStats.IsJumping = false;
        }

        charStats.Velocity.y = Mathf.Clamp(charStats.Velocity.y, -MAX_VERTICAL_SPEED, MAX_VERTICAL_SPEED);

        if (charStats.OnTheGround && Mathf.Approximately(charStats.Velocity.y - 1000000, -1000000))
        {
            charStats.Velocity.y = 0;
        }
    }

	public virtual void Collisions()
    {
        // Horizontal Collision Block
        // box used to collide against horizontal objects. Extend the hitbox vertically while in the air to avoid corner clipping
        Vector2 horizontalBoxSize;
		if (charStats.OnTheGround)
            horizontalBoxSize = new Vector2 (0.1f, charStats.CharCollider.bounds.size.y - 0.1f);
		else
            horizontalBoxSize = new Vector2 (0.1f, charStats.CharCollider.bounds.size.y + 25.0f);//15.0f);

        // raycast to collide right
        Vector2 rightHitOrigin = new Vector2(charStats.CharCollider.bounds.center.x + charStats.CharCollider.bounds.extents.x - 0.1f, charStats.CharCollider.bounds.center.y);
        RaycastHit2D rightHit = Physics2D.BoxCast(rightHitOrigin, horizontalBoxSize, 0.0f, Vector2.right, 25.0f, CollisionMasks.AllCollisionMask);
        if (rightHit.collider != null)
        {
            float rightHitDist = rightHit.distance - 0.05f;
            if (charStats.Velocity.x > 0.0f && rightHitDist <= Mathf.Abs(charStats.Velocity.x))
                charStats.Velocity.x = rightHitDist;

            // are we touching the right wall?
            if (Mathf.Approximately(rightHitDist - 1000000, -1000000))
                TouchedWall(rightHit.collider.gameObject);
        }

        // raycast to collide left
        Vector2 leftHitOrigin = new Vector2(charStats.CharCollider.bounds.center.x - charStats.CharCollider.bounds.extents.x + 0.1f, charStats.CharCollider.bounds.center.y);
        RaycastHit2D leftHit = Physics2D.BoxCast(leftHitOrigin, horizontalBoxSize, 0.0f, Vector2.left, 25.0f, CollisionMasks.AllCollisionMask);
        if (leftHit.collider != null)
        {
            float leftHitDist = leftHit.distance - 0.05f;
            if (charStats.Velocity.x < 0.0f && leftHitDist <= Mathf.Abs(charStats.Velocity.x))
                charStats.Velocity.x = -leftHitDist;

            // are we touching the left wall?
            if (Mathf.Approximately(leftHitDist - 1000000, -1000000))
                TouchedWall(leftHit.collider.gameObject);
        }

		// Vertical Collision Block
        Vector2 verticalBoxSize = new Vector2(charStats.CharCollider.bounds.size.x - 0.1f, 0.1f);

        // raycast to hit the ceiling
        Vector2 upHitOrigin = new Vector2(charStats.CharCollider.bounds.center.x, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.extents.y - 0.1f);
        RaycastHit2D upHit = Physics2D.BoxCast(upHitOrigin, verticalBoxSize, 0.0f, Vector2.up, 25.0f, CollisionMasks.AllCollisionMask);
        if (upHit.collider != null)
        {
            float hitDist = upHit.distance - 0.05f;
            if (charStats.Velocity.y > 0.0f && hitDist <= Mathf.Abs(charStats.Velocity.y))
                charStats.Velocity.y = hitDist;

            // are we touching the ceiling?
            if (Mathf.Approximately(hitDist - 1000000, -1000000))
                TouchedCeiling(upHit.collider.gameObject);
        }

        // raycast to find the floor
        Vector2 downHitOrigin = new Vector2(charStats.CharCollider.bounds.center.x, charStats.CharCollider.bounds.center.y - charStats.CharCollider.bounds.extents.y + 0.1f);
        RaycastHit2D downHit = Physics2D.BoxCast(downHitOrigin, verticalBoxSize, 0.0f, Vector2.down, 25.0f, CollisionMasks.AllCollisionMask);
        if (downHit.collider != null)
        {
            float downHitColliderLeft = downHit.collider.bounds.min.x;
            float downHitColliderRight = downHit.collider.bounds.max.x;
            float characterLeft = charStats.CharCollider.bounds.min.x;
            float characterRight = charStats.CharCollider.bounds.max.x;
            bool touchGround = true; // this is to prevent the game from thinking you touched the ground when you're gonna slip off the side when falling

            float hitDist = downHit.distance - 0.05f;
            if (charStats.Velocity.y < 0.0f && hitDist <= Mathf.Abs(charStats.Velocity.y))
            {
                // if the character is about to clip into the enviornment with the back of their hit box, move them so that they won't clip
                if (charStats.Velocity.x > 0.0f && downHitColliderRight < charStats.CharCollider.bounds.center.x)
                {
                    transform.Translate(downHitColliderRight - characterLeft, 0.0f, 0.0f);
                    touchGround = false;
                }
                else if (charStats.Velocity.x < 0.0f && downHitColliderLeft > charStats.CharCollider.bounds.center.x)
                {
                    transform.Translate(-(characterRight - downHitColliderLeft), 0.0f, 0.0f);
                    touchGround = false;
                }
                else // otherwise, touch the ground
                {
                    charStats.Velocity.y = -hitDist;
                }
            }
            // Approximate! since floats are dumb
            if (Mathf.Approximately(hitDist - 1000000, -1000000))
            {
                if (touchGround)
                {
                    charStats.OnTheGround = true;
                    charStats.JumpTurned = false;
                }
            }
            else
            {
                // this block is for jump tolerance
                if (charStats.OnTheGround && charStats.IsJumping == false)
                {
                    jumpGracePeriod = true;
                    jumpGracePeriodTime = 0.0f;
                }
                charStats.OnTheGround = false;
                againstTheLedge = false;
            }

            // stop at the edge of a platform
            if (charStats.OnTheGround && currentMoveState != moveState.isRunning)
            {
                float rightLedgeDist = downHitColliderRight - characterRight;
                if (charStats.Velocity.x > 0.0f && rightLedgeDist <= Mathf.Abs(charStats.Velocity.x))
                {
                    if (characterRight < downHitColliderRight)
                        charStats.Velocity.x = rightLedgeDist;
                    else
                        charStats.Velocity.x = 0.0f;
                }

                float leftLedgeDist = characterLeft - downHitColliderLeft;
                if (charStats.Velocity.x < 0.0f && leftLedgeDist <= Mathf.Abs(charStats.Velocity.x))
                {
                    if (characterLeft > downHitColliderLeft)
                        charStats.Velocity.x = -leftLedgeDist;
                    else
                        charStats.Velocity.x = 0.0f;
                }

                // set if character is against the ledge
                if ((rightLedgeDist < 1.0f && charStats.FacingDirection == 1) || (leftLedgeDist < 1.0f && charStats.FacingDirection == -1))
                    againstTheLedge = true;
                else
                    againstTheLedge = false;
            }
        }
        // if there is no floor, just fall
        else
        {
            charStats.OnTheGround = false;
        }
    }

    void CalculateDirection()
    {
        // character direction logic
        if (charStats.FacingDirection == -1 && InputManager.HorizontalAxis > 0)
        {
            charStats.FacingDirection = 1;
            //Anim.SetBool("TurnAround", true);

            if (!charStats.OnTheGround)
            {
                charStats.JumpTurned = true;
            }
        }
        else if (charStats.FacingDirection == 1 && InputManager.HorizontalAxis < 0)
        {
            charStats.FacingDirection = -1;
            //Anim.SetBool("TurnAround", true);

            if (!charStats.OnTheGround)
            {
                charStats.JumpTurned = true;
            }
        }
    }

    public void SetFacing()
    {
        if (charStats.FacingDirection == -1)
            spriteRenderer.flipX = true;
        else
            spriteRenderer.flipX = false;
    }

    protected void MovementInput()
    {
		LookingOverLedge();

        if(InputManager.RunInput)
            currentMoveState = moveState.isRunning;
        else
            currentMoveState = moveState.isSneaking;

        if(currentMoveState == moveState.isRunning)
        {
            // if the character comes to a full stop, let them start the run again
            // This also works when turning around
            if (charStats.Velocity.x == 0.0f)
                startRun = true;

            // running automatically starts at the sneaking speed and accelerates from there
            if (startRun == true && InputManager.HorizontalAxis > 0 && Mathf.Abs(charStats.Velocity.x) < SNEAK_SPEED)
            {
                charStats.Velocity.x = SNEAK_SPEED;
                startRun = false;
            }
            else if (startRun == true && InputManager.HorizontalAxis < 0 && Mathf.Abs(charStats.Velocity.x) < SNEAK_SPEED)
            {
                charStats.Velocity.x = -SNEAK_SPEED;
                startRun = false;
            }
        }
        
        if (InputManager.HorizontalAxis > 0)
            charStats.CharacterAccel = ACCELERATION;
        else if (InputManager.HorizontalAxis < 0)
            charStats.CharacterAccel = -ACCELERATION;
        else
            charStats.CharacterAccel = 0.0f;

        // Jump logic. Keep the Y velocity constant while holding jump for the duration of JUMP_CONTROL_TIME
        if ((jumpGracePeriod || charStats.OnTheGround) && InputManager.JumpInputInst)
        {
            charStats.IsJumping = true;
            jumpGracePeriod = false;
            charStats.JumpInputTime = 0.0f;
        }
    }

    public virtual void LookingOverLedge()
    {
        // check if you're looking over the ledge
        if (againstTheLedge && InputManager.VerticalAxis < 0.0f)
            lookingOverLedge = true;
        else
            lookingOverLedge = false;
    }

    public virtual void TouchedWall(GameObject collisionObject)
    {
        //base class does nothing with this function. gets overridden at the subclass level to handle such occasions
    }

    public virtual void TouchedCeiling(GameObject collisionObject)
    {
        //base class does nothing with this function. gets overridden at the subclass level to handle such occasions
    }

    /*
    protected Vector2 BezierCurveMovement(float distance, Vector2 start, Vector2 end, Vector2 curvePoint)
	{
		Vector2 ab = Vector2.Lerp(start, curvePoint, distance);
		Vector2 bc = Vector2.Lerp(curvePoint, end, distance);
		return Vector2.Lerp(ab, bc, distance);
	}
    */

    public float GetJumpHoriSpeedMin()
    {
        return JUMP_HORIZONTAL_SPEED_MIN;
    }

    public float GetJumpHoriSpeedMax()
    {
        return JUMP_HORIZONTAL_SPEED_MAX;
    }
}
