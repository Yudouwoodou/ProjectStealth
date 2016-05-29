﻿using UnityEngine;
using System.Collections;

public class MagGripUpgrade : MonoBehaviour 
{
    private SpriteRenderer spriteRenderer;
    public IInputManager InputManager;

	//this allows us to reference player stuff like their movement state
	Player playerScript; //AVOID CIRCULAR REFERENCING
	PlayerStats playerStats;
    CharacterStats charStats;

	private const float WALL_GRAB_DELAY = 0.15f;
	private float wallGrabDelayTimer = 0.15f;

    //Mag Grip variables
    public Collider2D grabCollider = null; //private
    public enum ClimbState { notClimb, wallClimb, ceilingClimb };
    public ClimbState currentClimbState = ClimbState.notClimb;
    private ClimbState transitioningToState = ClimbState.notClimb; // used when bzr curving to a new climb state


    private const float WALL_CLIMB_SPEED = 2.0f;
    private const float WALL_SLIDE_SPEED = 3.0f;

    // ledge logic
    public bool lookingOverLedge; // TODO: private
    public bool againstTheLedge; // TODO: private
    public bool ledgeClimb; // TODO: private

    //consts
    protected const float JUMP_ACCEL = 4.0f; //base accel for jump off the wall with no input


	// Use this for initialization
	void Start () 
	{
		playerScript = GetComponent<Player> ();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
		playerStats = GetComponent<PlayerStats>();
        charStats = GetComponent<CharacterStats>();
        InputManager = GetComponent<IInputManager>();
	}
	
	// Update is called once per frame
	void Update() 
	{
        //wall grab delay timer
        if (wallGrabDelayTimer < WALL_GRAB_DELAY)
            wallGrabDelayTimer = wallGrabDelayTimer + Time.deltaTime * TimeScale.timeScale;
        else
            wallGrabDelayTimer = WALL_GRAB_DELAY;
        
        if (charStats.CurrentMasterState == CharacterStats.MasterState.climbState) 
		{
            if (currentClimbState == ClimbState.wallClimb)
            {
                ClimbMovementInput();
            }
            else if (currentClimbState == ClimbState.ceilingClimb)
            {
            }
		}
	}

    void FixedUpdate()
    {
        if (ledgeClimb)
        {
            transform.position = BezierCurveMovement(charStats.BzrDistance, charStats.BzrStartPosition, charStats.BzrEndPosition, charStats.BzrCurvePosition);

            if (charStats.BzrDistance < 1.0f)
                charStats.BzrDistance = charStats.BzrDistance + Time.deltaTime * TimeScale.timeScale * 5;
            else 
            {
                ledgeClimb = false;
                currentClimbState = transitioningToState;
                InputManager.InputOverride = false;
                if (currentClimbState == ClimbState.notClimb)
                {
                    lookingOverLedge = false;
                    againstTheLedge = false;
                    charStats.CurrentMasterState = CharacterStats.MasterState.defaultState;
                }
            }
        }
        else if (currentClimbState == ClimbState.wallClimb)
        {
            ClimbHorizontalVelocity();
            ClimbVerticalVelocity();
            playerScript.Collisions();
            ClimbVerticalEdgeDetect();

            //move the character after all calculations have been done
            transform.Translate(charStats.Velocity);

            //if you climb down and touch the ground, stop climbing
            if (charStats.OnTheGround)
            {
                StopClimbing();
            }
        }
        else if (currentClimbState == ClimbState.ceilingClimb)
        {

        }
    }

    void ClimbHorizontalVelocity()
    {
        if (Mathf.Approximately(charStats.Velocity.x - 1000000, -1000000))
            charStats.Velocity.x = 0;
    }

    void ClimbVerticalVelocity()
    {
        if (InputManager.VerticalAxis > 0.0f)
            charStats.Velocity.y = WALL_CLIMB_SPEED * InputManager.VerticalAxis;
        else if (InputManager.VerticalAxis < 0.0f)
            charStats.Velocity.y = -WALL_SLIDE_SPEED;
        else
            charStats.Velocity.y = 0.0f;
    }

    /// <summary>
    /// This function detects if the player is touching the ledge when climbing vertically against a wall
    /// </summary>
    void ClimbVerticalEdgeDetect()
    {
        // raycast to find the climbing object
        RaycastHit2D hit = Physics2D.Raycast(charStats.CharCollider.bounds.center, new Vector2(charStats.FacingDirection, 0), Mathf.Infinity, CollisionMasks.WallGrabMask);
        if (hit.collider != null)
        {
            float colliderTop = hit.collider.bounds.max.y;
            float colliderBottom = hit.collider.bounds.min.y;
            float characterTop = charStats.CharCollider.bounds.max.y;
            float characterBottom = charStats.CharCollider.bounds.min.y;

            // stop at the edges of the surface
            float ledgeDistanceTop = colliderTop - characterTop;
            if (charStats.Velocity.y > 0.0f && ledgeDistanceTop <= Mathf.Abs(charStats.Velocity.y))
                charStats.Velocity.y = ledgeDistanceTop;

            float ledgeDistanceBottom = characterBottom - colliderBottom;
            if (charStats.Velocity.y < 0.0f && ledgeDistanceBottom <= Mathf.Abs(charStats.Velocity.y))
                charStats.Velocity.y = -ledgeDistanceBottom;

            // set if you're against the ledge
            if (ledgeDistanceTop == 0.0f || ledgeDistanceBottom == 0.0f)
                againstTheLedge = true;
            else
                againstTheLedge = false;
        }
        else
            Debug.LogError("Can't find collider when climbing against a wall. This shouldn't happen");
    }

    void ClimbMovementInput()
    {
        LedgeLook();

        if (grabCollider)
        {
            // Jump logic.
            if (!lookingOverLedge && InputManager.JumpInputInst) {
                StopClimbing();
                charStats.IsJumping = true;
                charStats.JumpInputTime = 0.0f;
                charStats.FacingDirection = -charStats.FacingDirection;
                if (charStats.FacingDirection == 1)
                    charStats.CharacterAccel = JUMP_ACCEL;
                else
                    charStats.CharacterAccel = -JUMP_ACCEL;

                playerScript.HorizontalJumpVelNoAccel((playerScript.GetJumpHoriSpeedMin() + playerScript.GetJumpHoriSpeedMax()) / 2.0f);
                playerScript.SetFacing();

                // gotta do a second call of fixed update to make sure we move off the wall
                //base.FixedUpdate (); //BECAUSE OF THE SEPERATION OF THIS INTO IT'S OWN MODULE, THIS IS PROBABLYNOT NECESSARY ANYMORE. MASTER STATE WILL PREVENT US FROM STICKING TO THE WALL
            } 
            //ledge climb logic
            else if (lookingOverLedge && InputManager.JumpInputInst) 
            {
                // if we're looking below
                if (grabCollider.bounds.min.y == charStats.CharCollider.bounds.min.y)
                {
                    StopClimbing();
                }
                // if we're looking above
                else if (grabCollider.bounds.max.y == charStats.CharCollider.bounds.max.y)
                {
                    SetupLedgeClimb(currentClimbState);
                }
                else
                    Debug.LogError("The grabCollider object is most likely null. This should never happen");
            }
        }
    }

    void LedgeLook()
    {
        if (currentClimbState == ClimbState.wallClimb)
        {
            if (againstTheLedge && (Mathf.Abs(InputManager.VerticalAxis) > 0.0f ||
                (InputManager.HorizontalAxis > 0.0f && charStats.CharCollider.bounds.center.x < grabCollider.bounds.center.x) ||
                (InputManager.HorizontalAxis < 0.0f && charStats.CharCollider.bounds.center.x > grabCollider.bounds.center.x)))
                lookingOverLedge = true;
            else
                lookingOverLedge = false;
        }
        else if (currentClimbState == ClimbState.ceilingClimb)
        {

        }
    }

    /// <summary>
    /// sets everything that needs to be done when player stops climbing
    /// </summary>
    void StopClimbing()
    {
        currentClimbState = ClimbState.notClimb;
        //no grab, no collider
        grabCollider = null;
        //reset the delay before we can wall grab again
        wallGrabDelayTimer = 0.0f;
        //set the master state to the default state. It'll transition into any other state from there. 
        charStats.CurrentMasterState = CharacterStats.MasterState.defaultState;
    }

    Vector2 BezierCurveMovement(float distance, Vector2 start, Vector2 end, Vector2 curvePoint)
    {
        Vector2 ab = Vector2.Lerp(start, curvePoint, distance);
        Vector2 bc = Vector2.Lerp(curvePoint, end, distance);
        return Vector2.Lerp(ab, bc, distance);
    }
    public void InitiateWallGrab(Collider2D collisionObject)
    {
        if (playerStats.AquiredMagGrip)
        {
            if (currentClimbState == ClimbState.notClimb)
            {
                //TODO: check to make sure the wall is climbable (walls should have a component with some public vars. if the object is climbable should be one of the properties

                if (!charStats.OnTheGround && currentClimbState == ClimbState.notClimb && wallGrabDelayTimer == WALL_GRAB_DELAY)
                {
                    // only grab the wall if we aren't popping out under it or over it
                    if (collisionObject.bounds.min.y <= charStats.CharCollider.bounds.min.y)
                    {
                        // if character is a bit too above the ledge, bump them down till they're directly under it
                        // if this block is commented out, then the character will not snap directly to the ledge if slightly above it and will slide down till they grab on
                        /*
                        if (collisionObject.bounds.max.y < charStats.CharCollider.bounds.max.y)
                        {
                            // check to see if the wall we're gonna be offsetting against is too short.
                            RaycastHit2D predictionCast;
                            float offsetDistance = charStats.CharCollider.bounds.max.y - collisionObject.bounds.max.y;
                            Vector2 predictionCastOrigin = new Vector2(charStats.CharCollider.bounds.center.x, charStats.CharCollider.bounds.min.y - offsetDistance);
                            if (collisionObject.bounds.center.x < charStats.CharCollider.bounds.center.x)
                                predictionCast = Physics2D.Raycast(predictionCastOrigin, Vector2.left, Mathf.Infinity, CollisionMasks.AllCollisionMask);
                            else
                                predictionCast = Physics2D.Raycast(predictionCastOrigin, Vector2.right, Mathf.Infinity, CollisionMasks.AllCollisionMask);

                            if (predictionCast.collider == collisionObject)
                                transform.Translate(0.0f, -(charStats.CharCollider.bounds.max.y - collisionObject.bounds.max.y), 0.0f);
                        }
                        */
                        // if we're good to grab, get everything in order
                        if (collisionObject.bounds.max.y >= charStats.CharCollider.bounds.max.y)
                        {
                            charStats.ResetJump();
                            currentClimbState = ClimbState.wallClimb;
                            charStats.CurrentMasterState = CharacterStats.MasterState.climbState;

                            // variable sets to prevent weird turning when grabbing onto a wall
                            // if the wall is to our left
                            if (collisionObject.bounds.center.x < charStats.CharCollider.bounds.center.x)
                            {
                                charStats.FacingDirection = -1;
                                spriteRenderer.flipX = true;
                            }
                            // if the wall is to our right
                            else
                            {
                                charStats.FacingDirection = 1;
                                spriteRenderer.flipX = false;
                            }
                            charStats.Velocity.x = 0.0f;
                            // assign the grabCollider now that the grab is actually happening
                            grabCollider = collisionObject.GetComponent<Collider2D>();
                        }
                    }
                }
            }
        }
    }

    void SetupLedgeClimb(ClimbState startingState, Collider2D climbObject = null)
    {
        grabCollider = climbObject;
        InputManager.JumpInputInst = false;
        InputManager.InputOverride = true;
        // translate body to on the ledge
        charStats.BzrDistance = 0.0f;
        charStats.BzrStartPosition = charStats.CharCollider.bounds.center;

        // variable sterilazation
        charStats.IsJumping = false;

        if (startingState == ClimbState.notClimb)
        {
            if (charStats.FacingDirection == 1)
            {
                charStats.BzrEndPosition = new Vector2(climbObject.bounds.max.x + charStats.CharCollider.bounds.extents.x + 0.01f, climbObject.bounds.max.y - charStats.CharCollider.bounds.extents.y);
                charStats.BzrCurvePosition = new Vector2(charStats.CharCollider.bounds.center.x + charStats.CharCollider.bounds.extents.x * 2, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.size.y);
            }
            else
            {
                charStats.BzrEndPosition = new Vector2(climbObject.bounds.min.x - charStats.CharCollider.bounds.extents.x - 0.01f, climbObject.bounds.max.y - charStats.CharCollider.bounds.extents.y);
                charStats.BzrCurvePosition = new Vector2(charStats.CharCollider.bounds.center.x - charStats.CharCollider.bounds.extents.x * 2, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.size.y);
            }
            transitioningToState = ClimbState.wallClimb;
            charStats.FacingDirection = -charStats.FacingDirection;
            playerScript.SetFacing();
        }
        else if (startingState == ClimbState.wallClimb)
        {
            if (charStats.FacingDirection == 1)
            {
                charStats.BzrEndPosition = charStats.CharCollider.bounds.center + charStats.CharCollider.bounds.size;
                charStats.BzrCurvePosition = new Vector2(charStats.CharCollider.bounds.center.x - charStats.CharCollider.bounds.extents.x, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.size.y * 2);
            }
            else
            {
                charStats.BzrEndPosition = new Vector2(charStats.CharCollider.bounds.center.x - charStats.CharCollider.bounds.size.x, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.size.y);
                charStats.BzrCurvePosition = new Vector2(charStats.CharCollider.bounds.center.x + charStats.CharCollider.bounds.extents.x, charStats.CharCollider.bounds.center.y + charStats.CharCollider.bounds.size.y * 2);
            }
            transitioningToState = ClimbState.notClimb;
        }
        ledgeClimb = true;
    }

    /// <summary>
    /// Function that initiates a wallclimb from a ledge
    /// </summary>
    public void WallClimbFromLedge()
    {
        // This is kinda inefficient as it is redundant code from the collision detection...
        Vector2 verticalBoxSize = new Vector2(charStats.CharCollider.bounds.size.x - 0.01f, 0.01f);
        Vector2 downHitOrigin = new Vector2(charStats.CharCollider.bounds.center.x, charStats.CharCollider.bounds.center.y - charStats.CharCollider.bounds.extents.y + 0.01f);
        RaycastHit2D downHit = Physics2D.BoxCast(downHitOrigin, verticalBoxSize, 0.0f, Vector2.down, Mathf.Infinity, CollisionMasks.AllCollisionMask);
        if (downHit.collider != null)
        {
            //check the ledge to see if it's tall enough to grab onto
            RaycastHit2D grabCheck;
            if (charStats.FacingDirection == 1)
            {
                Vector2 leftPoint = new Vector2(charStats.CharCollider.bounds.center.x + charStats.CharCollider.bounds.size.x, charStats.CharCollider.bounds.min.y - charStats.CharCollider.bounds.size.y);
                grabCheck = Physics2D.Raycast(leftPoint, Vector2.left);
            }
            else
            {
                Vector2 rightPoint = new Vector2(charStats.CharCollider.bounds.center.x - charStats.CharCollider.bounds.size.x, charStats.CharCollider.bounds.min.y - charStats.CharCollider.bounds.size.y);
                grabCheck = Physics2D.Raycast(rightPoint, Vector2.right);
            }

            if (grabCheck.collider == downHit.collider)
            {
                charStats.CurrentMasterState = CharacterStats.MasterState.climbState;
                SetupLedgeClimb(currentClimbState, downHit.collider);
            }
            else
            {
                charStats.IsJumping = false;
                //TODO: head shake animation to notify that the ledge is too short
                
            }
        }
    }
}