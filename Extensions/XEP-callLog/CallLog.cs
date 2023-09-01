using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Xml;

using Microsoft.Extensions.Logging;


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
        public event EventHandler<CallLogItemEventArgs> CallLogItemRetrieved;

        /// <summary>
        /// The event that is raised when a call log entry has been added
        /// </summary>
        public event EventHandler<CallLogItemEventArgs> CallLogItemAdded;

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
            if (message.Type == MessageType.Webrtc)
            {
                //TO DO
                log.LogWarning("[Input] Input in Webrtc context received - To manage ...");
                return true;
            }
            else if ( (message.Type == MessageType.Chat)
                    && (message.Data["deleted_call_log"] != null)
                    && (message.Data["deleted_call_log"].NamespaceURI == "jabber:iq:notification:telephony:call_log"))
            {
                String peerId = message.Data["deleted_call_log"].GetAttribute("peer");
                String callId = message.Data["deleted_call_log"].GetAttribute("call_id");
                log.LogDebug("CallLogItemsDeleted raised - peer:[{0}] - callId:[{1}]", peerId, callId);
                CallLogItemsDeleted.Raise(this, new CallLogItemDeletedEventArgs(peerId, callId));
                return true;
            }
            else if ( (message.Data["read"] != null)
                    && (message.Data["read"].NamespaceURI == "urn:xmpp:telephony:call_log:receipts"))
            {
                String id = message.Data["read"].GetAttribute("call_id");
                log.LogDebug("CallLogRead raised - id:[{0}]", id);
                CallLogRead.Raise(this, new CallLogReadEventArgs(id));
                return true;
            }
            else if ((message.Data["result"] != null)
                && (message.Data["result"].NamespaceURI == "jabber:iq:telephony:call_log"))
            {
                CallLogItemEventArgs evt = GetCallLogItemEventArgs(message.Data["result"]);
                if (evt != null)
                    CallLogItemRetrieved.Raise(this, evt);
                else
                    log.LogWarning("Cannot create CallLogItemEventArgs object ... [using jabber:iq:telephony:call_log namespace]");

                return true;
            }
            else if ((message.Data["updated_call_log"] != null)
                && (message.Data["updated_call_log"].NamespaceURI == "jabber:iq:notification:telephony:call_log"))
            {

                CallLogItemEventArgs evt = GetCallLogItemEventArgs(message.Data["updated_call_log"]);
                if (evt != null)
                    CallLogItemAdded.Raise(this, evt);
                else
                    log.LogWarning("Cannot create CallLogItemEventArgs object ... [using jabber:iq:notification:telephony:call_log]");

                return true;
            }

            // Pass the message to the next handler.
            return false;
        }

        private CallLogItemEventArgs GetCallLogItemEventArgs(XmlElement e)
        {
            CallLogItemEventArgs result = null;

            if ((e["forwarded"] != null)
                    && (e["forwarded"]["call_log"] != null))
            {
                XmlElement callLog = e["forwarded"]["call_log"];

                String id = "";
                String callId = "";
                String callee = "";
                String caller = "";
                String duration = "";
                String state = "";
                String media = "";
                String timeStamp = "";
                String type = "";
                String service = "";
                bool read = false;

                id = e.GetAttribute("id");
                type = callLog.GetAttribute("type");
                service = callLog.GetAttribute("service");
                if (service == "conference")
                    type = service;

                if (callLog["call_id"] != null)
                    callId = callLog["call_id"].InnerText;

                if (callLog["callee"] != null)
                    callee = callLog["callee"].InnerText;

                if (callLog["caller"] != null)
                    caller = callLog["caller"].InnerText;

                if (callLog["duration"] != null)
                    duration = callLog["duration"].InnerText;

                if (callLog["state"] != null)
                    state = callLog["state"].InnerText;

                if (callLog["media"] != null)
                    media = callLog["media"].InnerText;

                if (callLog["ack"] != null)
                    read = (callLog["ack"].GetAttribute("read") == "true");

                if (e["forwarded"]["delay"] != null)
                    timeStamp = e["forwarded"]["delay"].GetAttribute("stamp");

                result = new CallLogItemEventArgs(id, callId, state, callee, caller, media, timeStamp, duration, read, type);
            }
            return result;
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
        public void RequestCustomIqAsync(string queryId, int max, string before = null, string after = null)
        {
            var xml = Xml.Element("query", "jabber:iq:telephony:call_log");
            xml.SetAttribute("queryid", queryId);
            var xmlParam = Xml.Element("set", "http://jabber.org/protocol/rsm");
            if (max > 0) xmlParam.Child(Xml.Element("max").Text(max.ToString()));
            if (before == null)
                xmlParam.Child(Xml.Element("before"));
            else
                xmlParam.Child(Xml.Element("before").Text(before));
            if (after != null)
                xmlParam.Child(Xml.Element("after").Text(after));
            xml.Child(xmlParam);

            //The Request is Async
            im.IqRequestAsync(IqType.Set, null, im.Jid, xml, null, (id, iq) =>
            {
                //For any reply we execute the callback
                if (iq.Type == IqType.Error)
                {
                    CallLogResult.Raise(this, new CallLogResultEventArgs());
                    return;
                }

                if (iq.Type == IqType.Result)
                {
                    string queryid = "";
                    CallLogResult complete = Sharp.Xmpp.Extensions.CallLogResult.Error;
                    int count = 0;
                    string first = "";
                    string last = "";
                    try
                    {
                        if ((iq.Data["query"] != null) && (iq.Data["query"]["set"] != null))
                        {
                            XmlElement e = iq.Data["query"];

                            queryid = e.GetAttribute("queryid");
                            complete = (e.GetAttribute("complete") == "false") ? Sharp.Xmpp.Extensions.CallLogResult.InProgress : Sharp.Xmpp.Extensions.CallLogResult.Complete;

                            if (e["set"]["count"] != null)
                                count = Int16.Parse(e["set"]["count"].InnerText);

                            if (e["set"]["first"] != null)
                                first = e["set"]["first"].InnerText;

                            if (e["set"]["last"] != null)
                                last = e["set"]["last"].InnerText;

                            //log.LogDebug("[Input] call log result received - queryid:[{0}] - complete:[{1}] - count:[{2}] - first:[{3}] - last:[{4}]"
                            //                , queryid, complete, count, first, last);
                            CallLogResult.Raise(this, new CallLogResultEventArgs(queryid, complete, count, first, last));
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        log.LogError("RequestCustomIqAsync - an error occurred ...");
                    }

                    CallLogResult.Raise(this, new CallLogResultEventArgs(queryid, Sharp.Xmpp.Extensions.CallLogResult.Error, count, first, last));
                }
            });
        }

        /// <summary>
        /// Delete the specified call log
        /// </summary>
        /// <param name="callId">Id of the call log</param>
        public void DeleteCallLog(String callId)
        {
            var xml = Xml.Element("delete", "jabber:iq:telephony:call_log");
            xml.SetAttribute("call_id", callId);

            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            im.IqRequestAsync(IqType.Set, jid, jid, xml, null, (id, iq) =>
            {
            });
        }
        /// <summary>
        /// Delete all calls log related to the specified contact
        /// </summary>
        /// <param name="contactJid">Jid of the contact</param>

        public void DeleteCallsLogForContact(String contactJid)
        {
            var xml = Xml.Element("delete", "jabber:iq:telephony:call_log");
            xml.SetAttribute("peer", contactJid);

            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            im.IqRequestAsync(IqType.Set, jid, jid, xml, null, (id, iq) =>
            {
            });
        }

        /// <summary>
        /// Delete all call logs
        /// </summary>
        public void DeleteAllCallsLog()
        {
            var xml = Xml.Element("delete", "jabber:iq:telephony:call_log");

            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            im.IqRequestAsync(IqType.Set, jid, jid, xml, null, (id, iq) =>
            {
            });
        }

        /// <summary>
        /// Mark as read the specified call log
        /// </summary>
        /// <param name="callId">Id of the call log</param>
        public void MarkCallLogAsRead(String callId)
        {
            string jid = im.Jid.Node + "@" + im.Jid.Domain;
            Sharp.Xmpp.Im.Message message = new(jid);

            XmlElement e = message.Data;

            XmlElement read = e.OwnerDocument.CreateElement("read", "urn:xmpp:telephony:call_log:receipts");
            read.SetAttribute("call_id", callId);
            e.AppendChild(read);

            im.SendMessage(message);
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