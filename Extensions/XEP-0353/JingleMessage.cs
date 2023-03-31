using System;
using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    public class JingleMessage
    {
        /// <summary>
        /// Id of the Jingle Message
        /// </summary>
        public String Id { get; set; }

        /// <summary>
        /// Jingle Message Bare Jid Receiver 
        /// </summary>
        public String ToJid { get; set; }

        /// <summary>
        /// Jingle Message Resource Receiver 
        /// </summary>
        public String ToResource { get; set; }

        /// <summary>
        /// Jingle Message Jid Sender
        /// </summary>
        public String FromJid { get; set; }

        /// <summary>
        /// Jingle Message Resource Sender 
        /// </summary>
        public String FromResource { get; set; }

        /// <summary>
        /// Jingle Message Action: propose, retract, reject, accept, proceed
        /// </summary>
        public String Action { get; set; }

        /// <summary>
        /// Display Name if any of the sender
        /// </summary>
        public String DisplayName { get; set; }

        /// <summary>
        /// Subject of the call
        /// </summary>
        public String Subject { get; set; }

        /// <summary>
        /// Media list
        /// </summary>
        public List<String> Media { get; set; }

        /// <summary>
        /// Is Unifiedplan plan used 
        /// </summary>
        public Boolean UnifiedPlan { get; set; }

        /// <summary>
        /// Error Code if an error has raised
        /// </summary>
        public String ErrorCode { get; set; }

        /// <summary>
        /// Error Type if an error has raised
        /// </summary>
        public String ErrorType { get; set; }

        /// <summary>
        /// Serialize this object to string
        /// </summary>
        /// <returns><see cref="String"/> as serialization result</returns>
        public override String ToString()
        {
            String tab = "\r\n\t";
            String media = "";

            if (Media != null)
                media = String.Join(", ", Media);

            return String.Format($"{tab}Id:[{Id}] {tab}FromJid/Resource:[{FromJid} / {FromResource}] {tab}ToJid/Resource:[{ToJid} / {ToResource}] " +
                $"{tab}ErrorCode:[{ErrorCode}] {tab}ErrorType:[{ErrorType}]"  +
                $"{tab}Action:[{Action}] {tab}Media:[{media}] {tab}UnifiedPlan:[{UnifiedPlan}] {tab}DisplayName:[{DisplayName}]");
        }
    }
}
