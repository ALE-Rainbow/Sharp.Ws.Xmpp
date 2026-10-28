using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements the 'CallLog' extension used in Rainbow Hub
    /// </summary>
    internal class CallLog : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
    {
        private readonly ILogger log;

        /// <summary>
        /// An enumerable collection of XMPP namespaces the extension implements.
        /// </summary>
        /// <remarks>This is used for compiling the list of supported extensions
        /// advertised by the 'Service Discovery' extension.</remarks>
        public override IEnumerable<string> Namespaces
        {
            get
            {
                return new string[] { "jabber:iq:telephony:call_log" };
            }
        }

        /// <summary>
        /// The named constant of the Extension enumeration that corresponds to this
        /// extension.
        /// </summary>
        public override Extension Xep
        {
            get
            {
                return Extension.CallLog;
            }
        }

        /// <summary>
        /// The event that is raised when a call log item has been deleted
        /// </summary>
        public event EventHandler<CallLogItemDeletedEventArgs> CallLogItemsDeleted;

        /// <summary>
        /// The event that is raised when a call log item has been read
        /// </summary>
        public event EventHandler<CallLogReadEventArgs> CallLogRead;

        /// <summary>
        /// The event that is raised when a call log entry has been retrieved
        /// </summary>
        public event EventHandler<XmlElementEventArgs> CallLogItemRetrieved;

        /// <summary>
        /// The event that is raised when a call log entry has been added
        /// </summary>
        public event EventHandler<XmlElementEventArgs> CallLogItemAdded;

        /// <summary>
        /// The event that is raised when the list of call logs entry has been provided
        /// </summary>
        public event EventHandler<CallLogResultEventArgs> CallLogResult;

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Im.Message message)
        {
            if (message.Data["call_log", "jabber:iq:notification:telephony:call_log"] != null)
            {
                // Nothing to do ? An updated_call_log is sent just after ...
                return true;
            }
            else if (message.Data["updated_call_log", "jabber:iq:notification:telephony:call_log"] != null)
            {
                var evt = GetXmlElementEventArgs(message.Data.GetAttribute("id"), message.Data["updated_call_log"]);
                if (evt != null)
                    CallLogItemAdded.Raise(this, evt);
                else
                    log.LogWarning("Cannot create CallLogItemEventArgs object ... [using jabber:iq:notification:telephony:call_log]");

                return true;
            }
            else if (message.Data["result", "jabber:iq:telephony:call_log"] != null)
            {
                var evt = GetXmlElementEventArgs(message.Data.GetAttribute("id"), message.Data["result"]);
                if (evt != null)
                    CallLogItemRetrieved.Raise(this, evt);
                else
                    log.LogWarning("Cannot create CallLogItemEventArgs object ... [using jabber:iq:telephony:call_log namespace]");

                return true;
            }
            else if (message.Data["deleted_call_log", "jabber:iq:notification:telephony:call_log"] != null)
            {
                String peerId = message.Data["deleted_call_log"].GetAttribute("peer");
                String callId = message.Data["deleted_call_log"].GetAttribute("call_id");
                //log.LogDebug("CallLogItemsDeleted raised - peer:[{0}] - callId:[{1}]", peerId, callId);
                CallLogItemsDeleted.Raise(this, new CallLogItemDeletedEventArgs(peerId, callId));
                return true;
            }
            else if (message.Data["read", "urn:xmpp:telephony:call_log:receipts"] != null)
            {
                String id = message.Data["read"].GetAttribute("call_id");
                log.LogDebug("CallLogRead raised - id:[{0}]", id);
                CallLogRead.Raise(this, new CallLogReadEventArgs(id));
                return true;
            }

            // Pass the message to the next handler.
            return false;
        }

        private XmlElementEventArgs GetXmlElementEventArgs(String id, XmlElement e)
        {
            XmlElementEventArgs evt = null;

            if ((e["forwarded"] != null)
                    && (e["forwarded"]["call_log"] != null))
            {
                var callLogXmlElement = e["forwarded"]["call_log"];

                // We have to store "id" of the Xmpp Message
                callLogXmlElement.SetAttribute("id", id);

                // We have to store "stamp" of the delay node
                String stamp = "";
                if (e["forwarded"]["delay"] != null)
                    stamp = e["forwarded"]["delay"].GetAttribute("stamp");
                callLogXmlElement.SetAttribute("stamp", stamp);

                evt = new XmlElementEventArgs(callLogXmlElement);
            }
            return evt;
        }

        /// <summary>
        /// Requests the XMPP entity with the specified JID a GET command.
        /// When the Result is received and it not not an error
        /// if fires the callback function
        /// </summary>
        /// <param name="jid">The JID of the XMPP entity to get.</param>
        /// <param name="queryId">The Id related to this query - it will be used to identify this request</param>
        /// <exception cref="ArgumentNullException">The jid parameter
        /// is null.</exception>
        /// <exception cref="NotSupportedException">The XMPP entity with
        /// the specified JID does not support the 'Ping' XMPP extension.</exception>
        /// <exception cref="XmppErrorException">The server returned an XMPP error code.
        /// Use the Error property of the XmppErrorException to obtain the specific
        /// error condition.</exception>
        /// <exception cref="XmppException">The server returned invalid data or another
        /// unspecified XMPP error occurred.</exception>
        public void RequestCallLogs(string queryId, int max, string before = null, string after = null)
        {
            var _ = RequestCallLogsAsync(queryId, max, before, after);
        }

        static private XmlElement CreateQuery(string queryId, int max, string before = null, string after = null, Dictionary<String, String> fields = null)
        {
            var xml = Xml.Element("query", "jabber:iq:telephony:call_log");
            xml.SetAttribute("queryid", queryId);
            var xmlParam = Xml.Element("set", "http://jabber.org/protocol/rsm");
            if (max > 0) xmlParam.Child(Xml.Element("max").Text(max.ToString()));
            if (before is not null)
                xmlParam.Child(Xml.Element("before").Text(before));
            if (after is not null)
                xmlParam.Child(Xml.Element("after").Text(after));
            xml.Child(xmlParam);

            if (fields?.Count > 0)
            {
                var x = Xml.Element("x", "jabber:x:data");
                x.SetAttribute("type", "submit");
                foreach(var field in fields)
                {
                    var fieldXml = Xml.Element("field");
                    fieldXml.SetAttribute("var", field.Key);
                    fieldXml.Child(Xml.Element("value").Text(field.Value));

                    x.Child(fieldXml);
                }
                xml.Child(x);
            }
            return xml;
        }

        static private (Boolean success, int count, string first, string last) ParseQueryResult(Iq iq, ILogger log)
        {
            Boolean success = false;
            int count = 0;
            string first = "";
            string last = "";

            if (iq?.Type == IqType.Result)
            {
                try
                {
                    if ((iq.Data["query"] != null) && (iq.Data["query"]["set"] != null))
                    {
                        XmlElement e = iq.Data["query"];

                        if (e["set"]["count"] != null)
                            count = Int16.Parse(e["set"]["count"].InnerText);

                        if (e["set"]["first"] != null)
                            first = e["set"]["first"].InnerText;

                        if (e["set"]["last"] != null)
                            last = e["set"]["last"].InnerText;
                    }
                    success = true;
                }
                catch (Exception ex)
                {
                    log?.LogError("ParseQueryResult - an error occurred:{Exception}", ex);
                }
            }
            
            return (success, count, first, last);
        }

        static private int ParseDeleteQueryResult(Iq iq, ILogger log)
        {
            int count = -1;
            
            if (iq?.Type == IqType.Result)
            {
                try
                {
                    if (iq.Data["deleted"] != null)
                    {
                        XmlElement deletedXml = iq.Data["deleted"];
                        if (!Int32.TryParse(deletedXml.GetAttribute("count"), out count))
                            count = -1;
                    }
                }
                catch (Exception ex)
                {
                    log?.LogError("ParseDeleteQueryResult - an error occurred:{Exception}", ex);
                }
            }

            return count;
        }

        public async Task<Boolean> RequestCallLogsAsync(string queryId, int max, string before = null, string after = null)
        {
            var xml = CreateQuery(queryId, max, before, after);
            var result = await im.IqRequestAsync(IqType.Set, null, im.Jid, null, 60000, xml);

            (Boolean success, int count, string first, string last) = ParseQueryResult(result.Iq, log);

            if(success)
                CallLogResult.Raise(this, new CallLogResultEventArgs(queryId, Sharp.Xmpp.Extensions.CallLogResult.Complete, count, first, last));
            return success;
        }

        /// <summary>
        /// Delete the specified call log
        /// </summary>
        /// <param name="callId">Id of the call log</param>
        public void DeleteCallLog(String callId)
        {
            var _ = DeleteCallLogAsync(callId);
        }

        public async Task<int> DeleteCallLogAsync(String callId) => await DeleteCallsLogInternalAsync("call_id", callId);

        /// <summary>
        /// Delete all calls log related to the specified contact
        /// </summary>
        /// <param name="contactJid">Jid of the contact</param>
        public void DeleteCallsLogForContact(String contactJid)
        {
            var _ = DeleteCallsLogForContactAsync(contactJid);
        }

        public async Task<int> DeleteCallsLogForContactAsync(String contactJid) => await DeleteCallsLogInternalAsync("peer", contactJid);

        /// <summary>
        /// Delete all call logs
        /// </summary>
        public void DeleteAllCallsLog()
        {
            var _ = DeleteAllCallsLogAsync();
        }

        public async Task<int> DeleteAllCallsLogAsync() => await DeleteCallsLogInternalAsync(null, null);

        private async Task<int> DeleteCallsLogInternalAsync(String param, String value)
        {
            var xml = Xml.Element("delete", "jabber:iq:telephony:call_log");
            if (!String.IsNullOrEmpty(param))
                xml.SetAttribute(param, value);

            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            var result = await im.IqRequestAsync(IqType.Set, jid, jid, null, 60000, xml);

            return ParseDeleteQueryResult(result.Iq, log);
        }

        /// <summary>
        /// Mark as read the specified call log
        /// </summary>
        /// <param name="callId">Id of the call log</param>
        public void MarkCallLogAsRead(String callId)
        {
            var _ = MarkCallLogAsReadAsync(callId);
        }

        public async Task<Boolean> MarkCallLogAsReadAsync(String callId)
        {
            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            Sharp.Xmpp.Im.Message message = new(jid);

            XmlElement e = message.Data;

            XmlElement read = e.OwnerDocument.CreateElement("read", "urn:xmpp:telephony:call_log:receipts");
            read.SetAttribute("call_id", callId);
            e.AppendChild(read);

            return await im.SendMessageAsync(message);
        }

        public async Task<int> GetNbUnreadCallLogsAsync(string queryId)
        {
            Dictionary<String, String> fields = new()
            {
                { "read", "false" }
            };
            var xml = CreateQuery(queryId, 0, null, null, fields);
            var result = await im.IqRequestAsync(IqType.Set, null, im.Jid, null, 60000, xml);

            (Boolean success, int count, string _1, string _2) = ParseQueryResult(result.Iq, log);

            if (success)
                return count;
            return -1;
        }

        /// <summary>
        /// Initializes a new instance of the CallLog class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public CallLog(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<CallLog>(loggerPrefix);
        }
    }
}