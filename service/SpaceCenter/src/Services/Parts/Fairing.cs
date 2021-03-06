using System;
using KRPC.Service.Attributes;
using KRPC.SpaceCenter.ExtensionMethods;
using KRPC.Utils;

namespace KRPC.SpaceCenter.Services.Parts
{
    /// <summary>
    /// A fairing. Obtained by calling <see cref="Part.Fairing"/>.
    /// </summary>
    [KRPCClass (Service = "SpaceCenter")]
    public class Fairing : Equatable<Fairing>
    {
        readonly ModuleProceduralFairing fairing;

        internal static bool Is (Part part)
        {
            return part.InternalPart.HasModule<ModuleProceduralFairing> ();
        }

        internal Fairing (Part part)
        {
            Part = part;
            fairing = part.InternalPart.Module<ModuleProceduralFairing> ();
            if (fairing == null)
                throw new ArgumentException ("Part is not a fairing");
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        public override bool Equals (Fairing other)
        {
            return !ReferenceEquals (other, null) && Part == other.Part && fairing.Equals (other.fairing);
        }

        /// <summary>
        /// Hash code for the object.
        /// </summary>
        public override int GetHashCode ()
        {
            return Part.GetHashCode () ^ fairing.GetHashCode ();
        }

        /// <summary>
        /// The part object for this fairing.
        /// </summary>
        [KRPCProperty]
        public Part Part { get; private set; }

        /// <summary>
        /// Jettison the fairing. Has no effect if it has already been jettisoned.
        /// </summary>
        [KRPCMethod]
        public void Jettison ()
        {
            if (!Jettisoned)
                fairing.DeployFairing ();
        }

        /// <summary>
        /// Whether the fairing has been jettisoned.
        /// </summary>
        [KRPCProperty]
        public bool Jettisoned {
            get { return !fairing.CanMove; }
        }
    }
}
