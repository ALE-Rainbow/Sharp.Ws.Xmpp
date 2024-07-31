using Microsoft.Extensions.Logging;
using Sharp.Xmpp.Core;
using Sharp.Xmpp.Im;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Implements Mechanism for providing MaM support
    /// </summary>
    internal class MessageArchiveManagment : XmppExtension, IInputFilter<Sharp.Xmpp.Im.Message>
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
                return new string[] { "urn:xmpp:mam:1"  };
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
                return Extension.MessageArchiveManagment;
            }
        }

        /// <summary>
        /// Invoked after all extensions have been loaded.
        /// </summary>
        public override void Initialize()
        {
        }

        /// <summary>
        /// The event that is raised when a result is recevied after asking list of messages archive
        /// </summary>
        public event EventHandler<MessageArchiveManagementResultEventArgs> MessageArchiveManagementResult;

        /// <summary>
        /// The event that is raised when a message archive has been found
        /// </summary>
        public event EventHandler<MessageArchiveEventArgs> MessageArchiveRetrieved;

        /// <summary>
        /// The event that is raised when a archived messages using bulk have been found
        /// </summary>
        public event EventHandler<XmlElementEventArgs> MessageArchiveResultsRetrieved;

        /// <summary>
        /// Event raised when all messages in conversations is deleted
        /// </summary>
        public event EventHandler<JidEventArgs> MessagesAllDeleted;

        /// <summary>
        /// Invoked when a message stanza has been received.
        /// </summary>
        /// <param name="stanza">The stanza which has been received.</param>
        /// <returns>true to intercept the stanza or false to pass the stanza
        /// on to the next handler.</returns>
        public bool Input(Sharp.Xmpp.Im.Message message)
        {
            if (message.Data["result", "urn:xmpp:mam:1"] != null)
            {
                String queryId = message.Data["result"].GetAttribute("queryid");
                String resultId = message.Data["result"].GetAttribute("id");

                MessageArchiveRetrieved.Raise(this, new MessageArchiveEventArgs(queryId, resultId, message));
                return true;
            }

            if (message.Data["results", "urn:xmpp:mam:1:bulk"] != null)
            {
                MessageArchiveResultsRetrieved.Raise(this, new XmlElementEventArgs(message.Data));
                return true;
            }

            // Check if we received a message related to "all messages deleted"
            XmlElement deleted = message.Data["deleted", "jabber:iq:notification"];
            if (deleted != null)
            {
                String xmlId = deleted.GetAttribute("id");
                String jid = deleted.GetAttribute("with");
                if ((xmlId == "all") && (!String.IsNullOrEmpty(jid)))
                {
                    MessagesAllDeleted.Raise(this, new JidEventArgs(jid));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Requests archived messages according options specified
        /// </summary>
        /// <param name="toJid">The JID of the XMPP entity to get.</param>
        /// <param name="withJid">Filtering result with this JID</param>
        /// <param name="queryId">The Id related to this query - it will be used to identify this request</param>
        /// <param name="start">Start date
        /// <param name="end">Edn date
        /// <exception cref="ArgumentNullException">The jid parameter
        /// is null.</exception>
        /// <exception cref="NotSupportedException">The XMPP entity with
        /// the specified JID does not support the 'Ping' XMPP extension.</exception>
        /// <exception cref="XmppErrorException">The server returned an XMPP error code.
        /// Use the Error property of the XmppErrorException to obtain the specific
        /// error condition.</exception>
        /// <exception cref="XmppException">The server returned invalid data or another
        /// unspecified XMPP error occurred.</exception>
        public void RequestArchivedMessagesByDate(Jid toJid, Jid fromJid, Jid withJid, string queryId, DateTime start, DateTime end)
        {
            /*
             * 
               <query xmlns='urn:xmpp:mam:1'>
                <x xmlns='jabber:x:data' type='submit'>
                  <field var='FORM_TYPE' type='hidden'>
                    <value>urn:xmpp:mam:1</value>
                  </field>
                  <field var='start'>
                    <value>2010-06-07T00:00:00Z</value>
                  </field>
                  <field var='end'>
                    <value>2010-07-07T13:23:54Z</value>
                  </field>
                </x>
              </query>
             */

            //jid.ThrowIfNull("jid");

            XmlElement rootElement;
            XmlElement subElement;
            XmlElement fieldElement;
            XmlElement valueElement;

            string localQueryId = queryId;

            rootElement = Xml.Element("query", "urn:xmpp:mam:1");
            rootElement.SetAttribute("queryid", queryId);

            subElement = Xml.Element("x", "jabber:x:data");
            subElement.SetAttribute("type", "submit");

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "FORM_TYPE");
            fieldElement.SetAttribute("type", "hidden");

            valueElement = Xml.Element("value");
            valueElement.InnerText = "urn:xmpp:mam:1";
            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            if (withJid != null)
            {
                fieldElement = Xml.Element("field");
                fieldElement.SetAttribute("var", "with");
                valueElement = Xml.Element("value");
                valueElement.InnerText = withJid.ToString();
                fieldElement.Child(valueElement);
                subElement.Child(fieldElement);
            }

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "start");
            valueElement = Xml.Element("value");
            valueElement.InnerText = start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); ;
            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "end");
            valueElement = Xml.Element("value");
            valueElement.InnerText = end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); ;
            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            rootElement.Child(subElement);

            //The Request is Async
            im.IqRequestAsync(IqType.Set, toJid, fromJid, rootElement, null, (id, iq) =>
            {
                MamResult complete = MamResult.Error;
                int count = 0;
                string first = "";
                string last = "";

                if (iq.Type == IqType.Result)
                {
                    try
                    {
                        if ((iq.Data["fin"] != null) && (iq.Data["fin"]["set"] != null))
                        {
                            XmlElement e = iq.Data["fin"];

                            complete = (e.GetAttribute("complete") == "false") ? MamResult.InProgress : MamResult.Complete;

                            if (e["set"]["count"] != null)
                                count = Int16.Parse(e["set"]["count"].InnerText);

                            if (e["set"]["first"] != null)
                                first = e["set"]["first"].InnerText;

                            if (e["set"]["last"] != null)
                                last = e["set"]["last"].InnerText;
                        }
                    }
                    catch (Exception)
                    {
                        log.LogError("RequestCustomIqAsync - an error occurred ...");
                    }
                }

                MessageArchiveManagementResult.Raise(this, new MessageArchiveManagementResultEventArgs(localQueryId, complete, count, first, last, false));
            });
        }

        /// <summary>
        /// Requests archived messages according options specified
        /// </summary>
        /// <param name="jid">The JID of the XMPP entity to get.</param>
        /// <param name="queryId">The Id related to this query - it will be used to identify this request</param>
        /// <param name="max">The maximum number of result expected
        /// <param name="isRoom">To know if we request archive from room
        /// <param name="before">Stanza ID - if not null search message before it
        /// <param name="max">Stanza ID - if not null search message before it
        /// <exception cref="ArgumentNullException">The jid parameter
        /// is null.</exception>
        /// <exception cref="NotSupportedException">The XMPP entity with
        /// the specified JID does not support the 'Ping' XMPP extension.</exception>
        /// <exception cref="XmppErrorException">The server returned an XMPP error code.
        /// Use the Error property of the XmppErrorException to obtain the specific
        /// error condition.</exception>
        /// <exception cref="XmppException">The server returned invalid data or another
        /// unspecified XMPP error occurred.</exception>
        public void RequestArchivedMessages(Jid jid, string queryId, int max, bool isRoom, string before = null, string after = null, Boolean useBulk = false)
        {
            AsyncHelper.RunSync(async () => await RequestArchivedMessagesAsync(jid, queryId, max, isRoom, before, after, useBulk).ConfigureAwait(false));
        }

        public async Task<Boolean> RequestArchivedMessagesAsync(Jid jid, string queryId, int max, bool isRoom, string before = null, string after = null, Boolean useBulk = false)
        {
            jid.ThrowIfNull("jid");

            XmlElement rootElement;
            XmlElement subElement;
            XmlElement fieldElement;
            XmlElement valueElement;

            String ns = $"urn:xmpp:mam:1{(useBulk ? ":bulk" : "")}";
            rootElement = Xml.Element("query", ns);
            rootElement.SetAttribute("queryid", queryId);

            subElement = Xml.Element("x", "jabber:x:data");
            subElement.SetAttribute("type", "submit");

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "FORM_TYPE");
            fieldElement.SetAttribute("type", "hidden");

            valueElement = Xml.Element("value");
            valueElement.InnerText = "urn:xmpp:mam:1";
            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "with");

            valueElement = Xml.Element("value");

            if (isRoom)
                valueElement.InnerText = im.Jid.GetBareJid().ToString();
            else
                valueElement.InnerText = jid.GetBareJid().ToString();

            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            rootElement.Child(subElement);

            subElement = Xml.Element("set", "http://jabber.org/protocol/rsm");
            if (max > 0) subElement.Child(Xml.Element("max").Text(max.ToString()));
            if (before == null)
                subElement.Child(Xml.Element("before"));
            else
                subElement.Child(Xml.Element("before").Text(before));
            if (after != null)
                subElement.Child(Xml.Element("after").Text(after));

            rootElement.Child(subElement);

            Jid to = null;
            if (isRoom)
                to = jid;

            (string id, Iq iq) = await im.IqRequestAsync(IqType.Set, to, null, rootElement, null, 60000);

            string queryid = "";
            MamResult complete = MamResult.Error;
            int count = 0;
            string first = "";
            string last = "";

            if (iq.Type == IqType.Result)
            {
                try
                {
                    if ((iq.Data["fin"] != null) && (iq.Data["fin"]["set"] != null))
                    {
                        XmlElement e = iq.Data["fin"];

                        queryid = e.GetAttribute("queryid");
                        if (useBulk)
                            complete = MamResult.Complete;
                        else
                            complete = (e.GetAttribute("complete") == "false") ? MamResult.InProgress : MamResult.Complete;

                        if (e["set"]["count"] != null)
                            count = Int16.Parse(e["set"]["count"].InnerText);

                        if (e["set"]["first"] != null)
                            first = e["set"]["first"].InnerText;

                        if (e["set"]["last"] != null)
                            last = e["set"]["last"].InnerText;

                        MessageArchiveManagementResult.Raise(this, new MessageArchiveManagementResultEventArgs(queryid, complete, count, first, last, useBulk));
                        return true;
                    }
                }
                catch (Exception)
                {
                    log.LogError("RequestCustomIqAsync - an error occurred ...");
                }
            }

            MessageArchiveManagementResult.Raise(this, new MessageArchiveManagementResultEventArgs(queryid, MamResult.Error, count, first, last, useBulk));
            return false;
        }

        public void DeleteAllArchivedMessages(String with, string queryId, String toJidString, Action<string, Iq> callback = null)
        {
            /*
             * 
              <delete queryid="dd008366-ad0f-4197-a61c-0c34fbc0e75a" xmlns="urn:xmpp:mam:1">
                <x type="submit" xmlns="jabber:x:data">
                  <field type="hidden" var="FORM_TYPE">
                    <value>urn:xmpp:mam:1
                    </value>
                  </field>
                  <field var="with">
                    <value>7ebaaacfa0634f46a903bcdd83ae793b@openrainbow.net
                    </value>
                  </field>
                </x>
                <set xmlns="http://jabber.org/protocol/rsm"/>
              </delete>
             */

            XmlElement rootElement;
            XmlElement subElement;
            XmlElement fieldElement;
            XmlElement valueElement;

            rootElement = Xml.Element("delete", "urn:xmpp:mam:1");
            rootElement.SetAttribute("queryid", queryId);

            subElement = Xml.Element("x", "jabber:x:data");
            subElement.SetAttribute("type", "submit");

            fieldElement = Xml.Element("field");
            fieldElement.SetAttribute("var", "FORM_TYPE");
            fieldElement.SetAttribute("type", "hidden");

            valueElement = Xml.Element("value");
            valueElement.InnerText = "urn:xmpp:mam:1";
            fieldElement.Child(valueElement);
            subElement.Child(fieldElement);

            if (!String.IsNullOrEmpty(with))
            {
                fieldElement = Xml.Element("field");
                fieldElement.SetAttribute("var", "with");
                valueElement = Xml.Element("value");
                valueElement.InnerText = with;
                fieldElement.Child(valueElement);
                subElement.Child(fieldElement);
            }
            rootElement.Child(subElement);

            Jid jidTo = null;
            if (!String.IsNullOrEmpty(toJidString))
                jidTo = new Jid(toJidString);

            //The Request is Async
            im.IqRequestAsync(IqType.Set, jidTo, null, rootElement, null, callback);
        }

        /// <summary>
        /// Initializes a new instance of the CustomIq class.
        /// </summary>
        /// <param name="im">A reference to the XmppIm instance on whose behalf this
        /// instance is created.</param>
        public MessageArchiveManagment(XmppIm im, String loggerPrefix)
            : base(im, loggerPrefix)
        {
            log = LogFactory.CreateLogger<MessageArchiveManagment>(loggerPrefix);
        }
    }
}