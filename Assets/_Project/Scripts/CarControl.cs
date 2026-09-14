/*
 * UnityCar: CarControl.cs
 * Version: Unity 6+ (New Input)
 * Edits By: DeathwatchGaming
 * License: MIT
 */

// This script is responsible for controlling the car's movement, steering, and braking using the new Unity Input System. It handles the physics of the car through WheelColliders and updates the visual representation of the wheels accordingly.

// Using directives
using System;
using UnityEngine;
using System.Collections.Generic;

namespace UnityCar.Scripts
{
	public class CarControl : MonoBehaviour
	{
		// Serializable struct to hold wheel information
		[Serializable]
		public struct Wheel
		{
			[Tooltip("The WheelCollider component that handles the physics of the wheel.")]
			// The WheelCollider component that handles the physics of the wheel.
			public WheelCollider wheelCollider;
			[Tooltip("The transform of the wheel mesh, used for visual representation.")]
			// The transform of the wheel mesh, used for visual representation.
			public Transform wheelMesh;
			[Tooltip("Indicates if the wheel is steerable.")]
			// Indicates if the wheel is steerable.
			public bool steerable;
			[Tooltip("Indicates if the wheel is motorized.")]
			// Indicates if the wheel is motorized.
			public bool motorized;
		}

		// List of wheels for the car, initialized with 4 wheels
		[SerializeField] private List<Wheel> wheels = new List<Wheel>(new Wheel[4]);

		[Header("Car Properties")]
		[Tooltip("The rigidbody mass amount.")]
		// The mass of the car's rigidbody, which affects its physics behavior.
		[SerializeField] private float rigidBodyMass = 1500f;
		[Tooltip("The center of gravity offset amount.")]
		// The offset for the center of gravity, which affects the car's stability and handling.
		[SerializeField] private float centerOfGravityOffset = -1f;
		[Tooltip("The motor torque amount.")]
		// The torque applied to the wheels for acceleration, which affects the car's speed and performance.
		[SerializeField] private float motorTorque = 2000f;
		[Tooltip("The brake torque amount.")]
		// The torque applied to the wheels for braking, which affects the car's stopping power.
		[SerializeField] private float brakeTorque = 2000f;
		[Tooltip("The maximum speed amount.")]
		// The maximum speed the car can reach, which affects its top-end performance.
		[SerializeField] private float maxSpeed = 20f;
		[Tooltip("The steering range amount.")]
		// The range of steering angles for the wheels, which affects the car's turning radius and handling.
		[SerializeField] private float steeringRange = 30f;
		[Tooltip("The steering range amount at maximum speed.")]
		// The range of steering angles for the wheels at maximum speed, which affects the car's handling at high speeds.
		[SerializeField] private float steeringRangeAtMaxSpeed = 10f;

		// Private variables for internal use
	    
		private Rigidbody rigidBody; // Reference to the car's Rigidbody component for physics calculations
		private Vector3 centerOfMass; // The center of mass of the car, used to improve stability and prevent rolling
		private Vector3 wheelPosition; // The position of the wheel mesh, used for visual representation
		private Quaternion wheelRotation; // The rotation of the wheel mesh, used for visual representation

		private CarInputActions carControls; // Reference to the new input system
		private Vector2 inputVector; // The input vector from the new input system, representing player input for movement and steering	 
		
		private float motorInput; // The input value for motor torque, representing player input for acceleration and braking
		private float steerInput; // The input value for steering, representing player input for turning the car
		private float forwardSpeed; // The current speed of the car along its forward axis, used to adjust motor torque and steering range based on speed
		private float speedFactor; // A normalized value representing the car's speed relative to its maximum speed, used to adjust motor torque and steering range based on speed
		private float currentMotorTorque; // The current motor torque applied to the wheels, adjusted based on the car's speed and player input
		private float currentSteerRange; // The current steering range applied to the wheels, adjusted based on the car's speed and player input
		private bool isAccelerating; // A flag indicating whether the player is accelerating or trying to reverse, used to determine how to apply motor torque and braking
		private bool isBrakingKey; // A flag indicating whether the player is pressing the brake key, used to determine how to apply braking torque to the wheels

		// Awake is called when the script instance is being loaded
		private void Awake()
		{
			carControls = new CarInputActions(); // Initialize Input Actions				
		}

		// Start is called before the first frame update
		private void Start()
		{
			// Get the rigidbody
			rigidBody = GetComponent<Rigidbody>();

			// Set the rigidbody mass
			rigidBody.mass = rigidBodyMass;

			// Adjust center of mass to improve stability and prevent rolling
			centerOfMass = rigidBody.centerOfMass; // Get the current center of mass
			centerOfMass.y += centerOfGravityOffset; // Adjust the center of mass downward to improve stability
			rigidBody.centerOfMass = centerOfMass; // Set the new center of mass for the rigidbody
		}

		// Enable the input actions when the script is enabled
		private void OnEnable()
		{
			carControls.Enable();
		}

		// Disable the input actions when the script is disabled
		private void OnDisable()
		{
			carControls.Disable();
		}	

		// Update is called every frame
		private void Update()
		{
			// Check braking button value
			isBrakingKey = carControls.Car.Brake.IsPressed();

			// Update the wheel visuals to match the physics simulation
			UpdateWheels();
		}

		// FixedUpdate is called at a fixed time interval
		private void FixedUpdate()
		{
			// Read the Vector2 input from the new Input System
			inputVector = carControls.Car.Movement.ReadValue<Vector2>();

			// Get player input for acceleration and steering
			motorInput = inputVector.y; // Forward / backward input
			steerInput = inputVector.x; // Steering input

			// Calculate current speed along the car's forward axis
			forwardSpeed = Vector3.Dot(transform.forward, rigidBody.linearVelocity);
			speedFactor = Mathf.InverseLerp(0, maxSpeed, Mathf.Abs(forwardSpeed)); // Normalized speed factor

			// Reduce motor torque and steering at high speeds for better handling
			currentMotorTorque = Mathf.Lerp(motorTorque, 0, speedFactor);
			currentSteerRange = Mathf.Lerp(steeringRange, steeringRangeAtMaxSpeed, speedFactor);

			// Determine if the player is accelerating or trying to reverse
			isAccelerating = Mathf.Sign(motorInput) == Mathf.Sign(forwardSpeed);

			// Apply motor torque, steering and braking to the wheels
			foreach (var wheel in wheels)
			{
				// Apply steering to wheels that support steering
				if (wheel.steerable)
				{
					wheel.wheelCollider.steerAngle = steerInput * currentSteerRange;
				}

				// Apply motor torque and braking to wheels that support motorization
				if (isAccelerating)
				{
					// Apply torque to motorized wheels
					if (wheel.motorized)
					{
						wheel.wheelCollider.motorTorque = motorInput * currentMotorTorque;
					}

					// Apply braking if brake key is pressed
					if (isBrakingKey)
					{
						// Apply brakes
						wheel.wheelCollider.motorTorque = 0f;
						wheel.wheelCollider.brakeTorque = Mathf.Abs(motorInput) * brakeTorque;
					}

					else
					{
						// Release brakes when accelerating
						wheel.wheelCollider.brakeTorque = 0f;
					}										
 				}

 				else 
 				{
					// Apply brakes when reversing direction
					wheel.wheelCollider.motorTorque = 0f;
					wheel.wheelCollider.brakeTorque = Mathf.Abs(motorInput) * brakeTorque;
 				}

				// Apply braking when brake key is pressed and no movement input (neutral)
				if (isBrakingKey && Mathf.Approximately(motorInput, 0f))
				{
					wheel.wheelCollider.motorTorque = 0f;
					wheel.wheelCollider.brakeTorque = brakeTorque;
				}				
 			}
		}

		// Update the wheel visuals
		private void UpdateWheels()
		{
			// Update the position and rotation of each wheel mesh based on the corresponding WheelCollider
			foreach (var wheel in wheels)
			{
				// Get the Wheel collider's world pose values and
				// use them to set the wheel model's position and rotation
				wheel.wheelCollider.GetWorldPose(out wheelPosition, out wheelRotation);
				wheel.wheelMesh.transform.position = wheelPosition;
				wheel.wheelMesh.transform.rotation = wheelRotation;
			}
		}
	}
}
