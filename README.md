# ViveHamsterBallLocomotion
Roll the play area around the scene, like you're in a hamster ball. Surprisingly natural feeling.
![sample](VRHamsterBall.gif)    
Updates the position of the Camera Rig by grabbing the sides of a sphere collider's interior, like a hamster ball.

### Script Usage:
Place the `HamsterBall` script on a scene GameObject containing a SphereCollider. Assign a CameraRig and Controllers. 
Tweak to taste in inspector.

To prevent collisions with scene objects:
1. Create 2 layers, `Ground` and `Ball` for example
2. Ensure all navigable surfaces/objects are on the `Ground` layer
3. Set the HamsterBall object to the `Ball` layer
4. In EDIT>SETTINGS>PROJECT SETTINGS>PHYSICS ensure that `Ball` has nothing checked except for "Ground"
 
An optional Sphere Renderer should by on or on a child of the game object. It's important that there is no collider on the child.

* TBD: Should the ball reposition to the HMD when we're not rolling?
