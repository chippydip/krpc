using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using KRPC.Service.Attributes;
using KRPC.SpaceCenter.ExtensionMethods;
using KRPC.Utils;

namespace KRPC.SpaceCenter.Services.Parts
{
    /// <summary>
    /// Obtained by calling <see cref="Part.Experiment"/>.
    /// </summary>
    [KRPCClass (Service = "SpaceCenter")]
    public class Experiment : Equatable<Experiment>
    {
        readonly ModuleScienceExperiment experiment;

        internal static bool Is (Part part)
        {
            return part.InternalPart.HasModule<ModuleScienceExperiment> ();
        }

        internal Experiment (Part part)
        {
            Part = part;
            experiment = part.InternalPart.Module<ModuleScienceExperiment> ();
            if (experiment == null)
                throw new ArgumentException ("Part is not a science experiment");
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        public override bool Equals (Experiment other)
        {
            return !ReferenceEquals (other, null) && Part == other.Part && experiment == other.experiment;
        }

        /// <summary>
        /// Hash code for the object.
        /// </summary>
        public override int GetHashCode ()
        {
            return Part.GetHashCode () ^ experiment.GetHashCode ();
        }

        /// <summary>
        /// The part object for this experiment.
        /// </summary>
        [KRPCProperty]
        public Part Part { get; private set; }

        /// <summary>
        /// Run the experiment.
        /// </summary>
        [KRPCMethod]
        public void Run ()
        {
            if (HasData)
                throw new InvalidOperationException ("Experiment already contains data");
            if (Inoperable)
                throw new InvalidOperationException ("Experiment is inoperable");
            //FIXME: Don't use private API!!!
            var gatherData = experiment.GetType ().GetMethod ("gatherData", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (IEnumerator)gatherData.Invoke (experiment, new object[] { false });
            experiment.StartCoroutine (result);
        }

        /// <summary>
        /// Transmit all experimental data contained by this part.
        /// </summary>
        [KRPCMethod]
        [SuppressMessage ("Gendarme.Rules.Performance", "ReviewLinqMethodRule")]
        public void Transmit ()
        {
            var data = experiment.GetData ();
            if (!data.Any ())
                return;
            var transmitters = experiment.vessel.FindPartModulesImplementing<IScienceDataTransmitter> ();
            if (!transmitters.Any ())
                throw new InvalidOperationException ("No transmitters available to transmit the data");
            transmitters.OrderBy (ScienceUtil.GetTransmitterScore).First ().TransmitData (data.ToList ());
            if (!experiment.IsRerunnable ())
                experiment.SetInoperable ();
            Dump ();
        }

        /// <summary>
        /// Dump the experimental data contained by the experiment.
        /// </summary>
        [KRPCMethod]
        public void Dump ()
        {
            foreach (var data in experiment.GetData ())
                experiment.DumpData (data);
        }

        /// <summary>
        /// Reset the experiment.
        /// </summary>
        [KRPCMethod]
        public void Reset ()
        {
            if (Inoperable)
                throw new InvalidOperationException ("Experiment is inoperable");
            experiment.ResetExperiment ();
        }

        /// <summary>
        /// Whether the experiment is inoperable.
        /// </summary>
        [KRPCProperty]
        public bool Inoperable {
            get { return experiment.Inoperable; }
        }

        /// <summary>
        /// Whether the experiment has been deployed.
        /// </summary>
        [KRPCProperty]
        public bool Deployed {
            get { return experiment.Deployed; }
        }

        /// <summary>
        /// Whether the experiment can be re-run.
        /// </summary>
        [KRPCProperty]
        public bool Rerunnable {
            get { return experiment.IsRerunnable (); }
        }

        /// <summary>
        /// Whether the experiment contains data.
        /// </summary>
        [KRPCProperty]
        public bool HasData {
            get { return experiment.GetData ().Any (); }
        }

        /// <summary>
        /// The data contained in this experiment.
        /// </summary>
        [KRPCProperty]
        public IList<ScienceData> Data {
            get { return experiment.GetData ().Select (data => new ScienceData (experiment, data)).ToList (); }
        }
    }
}
