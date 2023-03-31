using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the NomadicEventArgs event
    /// </summary>
    public class NomadicEventArgs : EventArgs
    {
        /// <summary>
        /// Feature activated
        /// </summary>
        public Boolean FeatureActivated
        {
            get;
            private set;
        }

        /// <summary>
        /// Mode activated
        /// </summary>
        public Boolean ModeActivated
        {
            get;
            private set;
        }

        /// <summary>
        /// make Call Initiator Is Main
        /// </summary>
        public Boolean MakeCallInitiatorIsMain
        {
            get;
            private set;
        }

        /// <summary>
        /// destination
        /// </summary>
        public String Destination
        {
            get;
            private set;
        }


        public NomadicEventArgs(Boolean featureActivated, Boolean modeActivated, Boolean makeCallInitiatorIsMain, String destination)
        {
            FeatureActivated = featureActivated;
            ModeActivated = modeActivated;
            MakeCallInitiatorIsMain = makeCallInitiatorIsMain;
            Destination = destination;
        }

    }
}
