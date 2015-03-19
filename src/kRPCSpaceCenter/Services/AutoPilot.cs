using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KRPC.Service.Attributes;
using KRPC.Utils;
using KRPCSpaceCenter.ExtensionMethods;
using Tuple3 = KRPC.Utils.Tuple<double,double,double>;

namespace KRPCSpaceCenter.Services
{
    [KRPCEnum (Service = "SpaceCenter")]
    public enum SASMode
    {
        StabilityAssist,
        Maneuver,
        Prograde,
        Retrograde,
        Normal,
        AntiNormal,
        Radial,
        AntiRadial,
        Target,
        AntiTarget
    }

    [KRPCEnum (Service = "SpaceCenter")]
    public enum SpeedMode
    {
        Orbit,
        Surface,
        Target
    }

    /// <remarks>
    /// Taken and adapted from MechJeb2/KOS/RemoteTech2/forum discussion; credit goes to the authors of those plugins/posts:
    /// https://github.com/MuMech/MechJeb2
    /// https://github.com/KSP-KOS/KOS
    /// https://github.com/RemoteTechnologiesGroup/RemoteTech
    /// http://forum.kerbalspaceprogram.com/threads/69313-WIP-kRPC-A-language-agnostic-Remote-Procedure-Call-server-for-KSP?p=1021721&amp;viewfull=1#post1021721
    /// </remarks>
    [KRPCClass (Service = "SpaceCenter")]
    public sealed class AutoPilot : Equatable<AutoPilot>
    {
        readonly global::Vessel vessel;
        static HashSet<AutoPilot> engaged = new HashSet<AutoPilot> ();
        ReferenceFrame referenceFrame;
        double pitch;
        double heading;
        double roll;
        bool sasSet;
        int sasUpdate;

        internal AutoPilot (global::Vessel vessel)
        {
            this.vessel = vessel;
        }

        public override bool Equals (AutoPilot obj)
        {
            return vessel == obj.vessel;
        }

        public override int GetHashCode ()
        {
            return vessel.GetHashCode ();
        }

        [KRPCProperty]
        public bool SAS {
            get { return vessel.ActionGroups.groups [BaseAction.GetGroupIndex (KSPActionGroup.SAS)]; }
            set { vessel.ActionGroups.SetGroup (KSPActionGroup.SAS, value); }
        }

        [KRPCProperty]
        public SASMode SASMode {
            get { return vessel.Autopilot.Mode.ToSASMode (); }
            set {
                var mode = value.FromSASMode ();
                if (vessel.Autopilot.CanSetMode (mode)) {
                    vessel.Autopilot.SetMode (mode);
                    // Update the UI buttons
                    var modeIndex = (int)vessel.Autopilot.Mode;
                    var modeButtons = UnityEngine.Object.FindObjectOfType<VesselAutopilotUI> ().modeButtons;
                    modeButtons.ElementAt<RUIToggleButton> (modeIndex).SetTrue (true, true);
                }
            }
        }

        [KRPCProperty]
        public SpeedMode SpeedMode {
            get { return FlightUIController.speedDisplayMode.ToSpeedMode (); }
            set {
                var startMode = FlightUIController.speedDisplayMode;
                var mode = value.FromSpeedMode ();
                while (FlightUIController.speedDisplayMode != mode) {
                    FlightUIController.fetch.cycleSpdModes ();
                    if (FlightUIController.speedDisplayMode == startMode)
                        break;
                }
            }
        }

        double ComputeSASError ()
        {
            //TODO: Rewrite this avoiding the use of the front-end API,
            //      and all the calls to .ToVector() that that entails.
            //      There doesn't appear to be a way to get the direction that
            //      SAS is targeting from the vessel.Autopilot.SAS object, o
            //      from FlightUIController.
            var speedMode = FlightUIController.speedDisplayMode;
            if (speedMode == FlightUIController.SpeedDisplayModes.Surface) {
                throw new NotImplementedException ();
            }
            if (speedMode == FlightUIController.SpeedDisplayModes.Target) {
                throw new NotImplementedException ();
            }

            // Orbital reference frame
            var v = new Vessel (vessel);
            var refFrame = v.OrbitalReferenceFrame;
            var direction = v.Direction (refFrame).ToVector ();
            Vector3 target = Vector3.up;
            switch (SASMode) {
            case SASMode.StabilityAssist:
                return Double.NaN;
            case SASMode.Maneuver:
                if (!v.Control.Nodes.Any ())
                    return Double.NaN;
                target = v.Control.Nodes [0].Direction (refFrame).ToVector ();
                break;
            case SASMode.Prograde:
                target = v.Flight (refFrame).Prograde.ToVector ();
                break;
            case SASMode.Retrograde:
                target = v.Flight (refFrame).Retrograde.ToVector ();
                break;
            case SASMode.Normal:
                target = v.Flight (refFrame).Normal.ToVector ();
                break;
            case SASMode.AntiNormal:
                target = v.Flight (refFrame).AntiNormal.ToVector ();
                break;
            case SASMode.Radial:
                target = v.Flight (refFrame).Radial.ToVector ();
                break;
            case SASMode.AntiRadial:
                target = v.Flight (refFrame).AntiRadial.ToVector ();
                break;
            case SASMode.Target:
            case SASMode.AntiTarget:
                target = v.Target.Position (refFrame).ToVector () - v.Position (refFrame).ToVector ();
                if (SASMode == SASMode.AntiTarget)
                    target *= -1;
                break;
            }
            return Math.Abs (Math.Acos (Vector3.Dot (direction, target))) * (180.0d / Math.PI);
        }

        [KRPCMethod]
        public void SetRotation (double pitch, double heading, double roll = Double.NaN, ReferenceFrame referenceFrame = null)
        {
            if (referenceFrame == null)
                referenceFrame = ReferenceFrame.Orbital (vessel);
            this.referenceFrame = referenceFrame;
            this.pitch = pitch;
            this.heading = heading;
            this.roll = roll;
            Engage ();
        }

        [KRPCMethod]
        public void SetDirection (Tuple3 direction, double roll = Double.NaN, ReferenceFrame referenceFrame = null)
        {
            if (referenceFrame == null)
                referenceFrame = ReferenceFrame.Orbital (vessel);
            this.referenceFrame = referenceFrame;
            QuaternionD rotation = Quaternion.FromToRotation (Vector3d.up, direction.ToVector ());
            var phr = rotation.PitchHeadingRoll ();
            pitch = phr [0];
            heading = phr [1];
            this.roll = roll;
            Engage ();
        }

        void Engage ()
        {
            sasSet = false;
            sasUpdate = 0;
            engaged.Add (this);
        }

        [KRPCMethod]
        public void Disengage ()
        {
            SASMode = SASMode.StabilityAssist;
            SAS = false;
            engaged.Remove (this);
        }

        [KRPCProperty]
        public double Error {
            get {
                if (engaged.Contains (this))
                    return ComputeError (ComputeTarget ());
                else if (SAS)
                    return ComputeSASError ();
                else
                    return Double.NaN;
            }
        }

        public static void Clear ()
        {
            engaged.Clear ();
        }

        public static void Fly (FlightCtrlState state)
        {
            foreach (var autoPilot in engaged) {
                autoPilot.DoAutoPiloting (state);
            }
        }

        void DoAutoPiloting (FlightCtrlState state)
        {
            // Initialize SAS autopilot
            if (!sasSet) {
                SAS = true;
                SASMode = SASMode.StabilityAssist;
                sasSet = true;
            }

            var target = ComputeTarget ();
            if (ComputeError (target) < 3.0f) {
                // Update SAS heading when the SAS heading has large error
                // At most every 5 frames so that the SAS autopilot has a chance to affect the ship heading
                if (sasUpdate > 5) {
                    vessel.Autopilot.SAS.LockHeading (target, false);
                    sasUpdate = 0;
                } else {
                    sasUpdate++;
                }
            } else {
                SteerShipToward (target, state, vessel);
            }
        }

        Quaternion ComputeTarget ()
        {
            // Compute world space target rotation from pitch, heading, roll
            Quaternion rotation = GeometryExtensions.QuaternionFromPitchHeadingRoll (new Vector3d (pitch, heading, Double.IsNaN (roll) ? 0.0f : roll));
            var target = referenceFrame.RotationToWorldSpace (rotation);

            // If roll is not specified, re-compute rotation between direction vectors
            if (Double.IsNaN (roll)) {
                var from = Vector3.up;
                var to = target * Vector3.up;
                target = Quaternion.FromToRotation (from, to);
            }

            return target;
        }

        double ComputeError (Quaternion target)
        {
            return Math.Abs (Quaternion.Angle (vessel.transform.rotation, target));
        }

        static void SteerShipToward (Quaternion target, FlightCtrlState c, global::Vessel vessel)
        {
            target = target * Quaternion.Inverse (Quaternion.Euler (90, 0, 0));

            var centerOfMass = vessel.findWorldCenterOfMass ();
            var momentOfInertia = vessel.findLocalMOI (centerOfMass);

            var vesselRotation = vessel.ReferenceTransform.rotation;

            Quaternion delta = Quaternion.Inverse (Quaternion.Euler (90, 0, 0) * Quaternion.Inverse (vesselRotation) * target);

            Vector3d deltaEuler = ReduceAngles (delta.eulerAngles);
            deltaEuler.y *= -1;

            Vector3d torque = GetTorque (vessel, c.mainThrottle);
            Vector3d inertia = GetEffectiveInertia (vessel, torque);

            Vector3d err = deltaEuler * Math.PI / 180.0F;
            err += new Vector3d (inertia.x, inertia.z, inertia.y);

            Vector3d act = 120.0f * err;

            float precision = Mathf.Clamp ((float)torque.x * 20f / momentOfInertia.magnitude, 0.5f, 10f);
            float driveLimit = Mathf.Clamp01 ((float)(err.magnitude * 380.0f / precision));

            act.x = Mathf.Clamp ((float)act.x, -driveLimit, driveLimit);
            act.y = Mathf.Clamp ((float)act.y, -driveLimit, driveLimit);
            act.z = Mathf.Clamp ((float)act.z, -driveLimit, driveLimit);

            c.roll = Mathf.Clamp ((float)(c.roll + act.z), -driveLimit, driveLimit);
            c.pitch = Mathf.Clamp ((float)(c.pitch + act.x), -driveLimit, driveLimit);
            c.yaw = Mathf.Clamp ((float)(c.yaw + act.y), -driveLimit, driveLimit);

        }

        static Vector3d GetEffectiveInertia (global::Vessel vessel, Vector3d torque)
        {
            var centerOfMass = vessel.findWorldCenterOfMass ();
            var momentOfInertia = vessel.findLocalMOI (centerOfMass);
            var angularVelocity = Quaternion.Inverse (vessel.transform.rotation) * vessel.rigidbody.angularVelocity;
            var angularMomentum = new Vector3d (angularVelocity.x * momentOfInertia.x, angularVelocity.y * momentOfInertia.y, angularVelocity.z * momentOfInertia.z);

            var retVar = Vector3d.Scale
                (
                             Sign (angularMomentum) * 2.0f,
                             Vector3d.Scale (Pow (angularMomentum, 2), Inverse (Vector3d.Scale (torque, momentOfInertia)))
                         );

            retVar.y *= 10;

            return retVar;
        }

        static Vector3d GetTorque (global::Vessel vessel, float thrust)
        {
            var centerOfMass = vessel.findWorldCenterOfMass ();
            var rollaxis = vessel.transform.up;
            rollaxis.Normalize ();
            var pitchaxis = vessel.GetFwdVector ();
            pitchaxis.Normalize ();

            float pitch = 0.0f;
            float yaw = 0.0f;
            float roll = 0.0f;

            foreach (Part part in vessel.parts) {
                var relCoM = part.Rigidbody.worldCenterOfMass - centerOfMass;

                foreach (PartModule module in part.Modules) {
                    var wheel = module as ModuleReactionWheel;
                    if (wheel == null)
                        continue;

                    pitch += wheel.PitchTorque;
                    yaw += wheel.YawTorque;
                    roll += wheel.RollTorque;
                }
                if (vessel.ActionGroups [KSPActionGroup.RCS]) {
                    foreach (PartModule module in part.Modules) {
                        var rcs = module as ModuleRCS;
                        if (rcs == null || !rcs.rcsEnabled)
                            continue;

                        bool enoughfuel = rcs.propellants.All (p => (int)(p.totalResourceAvailable) != 0);
                        if (!enoughfuel)
                            continue;
                        foreach (Transform thrustdir in rcs.thrusterTransforms) {
                            float rcsthrust = rcs.thrusterPower;
                            //just counting positive contributions in one direction. This is incorrect for asymmetric thruster placements.
                            roll += Mathf.Max (rcsthrust * Vector3.Dot (Vector3.Cross (relCoM, thrustdir.up), rollaxis), 0.0f);
                            pitch += Mathf.Max (rcsthrust * Vector3.Dot (Vector3.Cross (Vector3.Cross (relCoM, thrustdir.up), rollaxis), pitchaxis), 0.0f);
                            yaw += Mathf.Max (rcsthrust * Vector3.Dot (Vector3.Cross (Vector3.Cross (relCoM, thrustdir.up), rollaxis), Vector3.Cross (rollaxis, pitchaxis)), 0.0f);
                        }
                    }
                }
                pitch += (float)GetThrustTorque (part, vessel) * thrust;
                yaw += (float)GetThrustTorque (part, vessel) * thrust;
            }

            return new Vector3d (pitch, roll, yaw);
        }

        static double GetThrustTorque (Part p, global::Vessel vessel)
        {
            //TODO: implement gimbalthrust Torque calculation
            return 0;
        }

        static Vector3d Pow (Vector3d vector, float exponent)
        {
            return new Vector3d (Math.Pow (vector.x, exponent), Math.Pow (vector.y, exponent), Math.Pow (vector.z, exponent));
        }

        static Vector3d ReduceAngles (Vector3d input)
        {
            return new Vector3d (
                (input.x > 180f) ? (input.x - 360f) : input.x,
                (input.y > 180f) ? (input.y - 360f) : input.y,
                (input.z > 180f) ? (input.z - 360f) : input.z
            );
        }

        static Vector3d Inverse (Vector3d input)
        {
            return new Vector3d (1 / input.x, 1 / input.y, 1 / input.z);
        }

        static Vector3d Sign (Vector3d vector)
        {
            return new Vector3d (Math.Sign (vector.x), Math.Sign (vector.y), Math.Sign (vector.z));
        }
    }
}
