using System;
using UnityEngine;

namespace UR3eVR
{
    /// <summary>
    /// VRControllerGripPoseHandler: Ensures Meta Quest Natural Controller-Driven Hand Poses
    /// are active for the left remote hand, and prevents interference with bare-hand robot teleoperation.
    /// - Right hand is strictly bare-hand tracking for UR3e robot teleoperation (no remote posing).
    /// - Left hand utilizes Meta Quest native Natural Controller-driven hand poses when holding controller.
    /// </summary>
    public class VRControllerGripPoseHandler : MonoBehaviour
    {
        public bool isRightHand = false;

        private void Awake()
        {
            DetectHandSide();
            if (isRightHand)
            {
                // Right hand is dedicated purely to UR3e robot control with bare hand tracking!
                enabled = false;
                return;
            }

            // Ensure Meta Quest native natural controller-driven hand poses are enabled
            EnableNativeControllerHandPoses();
        }

        private void Start()
        {
            DetectHandSide();
            if (isRightHand)
            {
                enabled = false;
                return;
            }

            EnableNativeControllerHandPoses();
        }

        private void DetectHandSide()
        {
            var skel = GetComponent<OVRSkeleton>();
            if (skel != null)
            {
                var st = skel.GetSkeletonType();
                if (st == OVRSkeleton.SkeletonType.XRHandRight || st == OVRSkeleton.SkeletonType.HandRight)
                {
                    isRightHand = true;
                    return;
                }
                else if (st == OVRSkeleton.SkeletonType.XRHandLeft || st == OVRSkeleton.SkeletonType.HandLeft)
                {
                    isRightHand = false;
                    return;
                }
            }

            isRightHand = gameObject.name.ToLower().Contains("right");
        }

        private void EnableNativeControllerHandPoses()
        {
            if (OVRManager.instance != null)
            {
                OVRManager.instance.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.Natural;
            }
        }
    }
}
