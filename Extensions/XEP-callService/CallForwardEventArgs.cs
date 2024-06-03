using System;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Provides data for the CallLogItemEventArgs event
    /// </summary>
    public class CallForwardEventArgs : EventArgs
    {
        /// <summary>
        /// Type of the forward: "Activation" / "Deactivation" 
        /// </summary>
        public String Type
        {
            get;
            private set;
        }

        /// <summary>
        /// Forward to: "VOICEMAILBOX" / a phone number
        /// </summary>
        public String To
        {
            get;
            private set;
        }

        /// <summary>
        /// Forward Type: "immediate", "busy", "noreply" (aka No Answer), "busy_or_noreply", "no_forward" (forward deactivation)
        /// "to_voicemail" (Reserved : not yet implemented), "do_not_disturb" (Reserved : not yet implemented)
        /// </summary>
        public String ForwardType
        {
            get;
            private set;
        }


        public CallForwardEventArgs(String type, String to, String forwardType)
        {
            Type = type;
            To = to;
            ForwardType = forwardType;
        }

    }
}
